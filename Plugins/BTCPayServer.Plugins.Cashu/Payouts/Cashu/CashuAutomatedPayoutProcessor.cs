#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.PayoutProcessors;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.Cashu.CashuAbstractions;
using BTCPayServer.Plugins.Cashu.Data;
using BTCPayServer.Plugins.Cashu.PaymentHandlers;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin;
using DotNut;
using PayoutData = BTCPayServer.Data.PayoutData;
using PayoutProcessorData = BTCPayServer.Data.PayoutProcessorData;

namespace BTCPayServer.Plugins.Cashu.Payouts.Cashu;

public class CashuAutomatedPayoutProcessor : BaseAutomatedPayoutProcessor<CashuAutomatedPayoutBlob>
{
    private readonly CashuDbContextFactory _cashuDbContextFactory;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;

    public CashuAutomatedPayoutProcessor(
        PayoutProcessorData payoutProcessorSettings,
        ILoggerFactory logger,
        StoreRepository storeRepository,
        ApplicationDbContextFactory applicationDbContextFactory,
        PaymentMethodHandlerDictionary paymentHandlers,
        IPluginHookService pluginHookService,
        EventAggregator eventAggregator,
        CashuDbContextFactory cashuDbContextFactory,
        BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings) :
        base(
            CashuPlugin.CashuPmid,
            logger,
            storeRepository,
            payoutProcessorSettings,
            applicationDbContextFactory,
            paymentHandlers,
            pluginHookService,
            eventAggregator)
    {
        _cashuDbContextFactory = cashuDbContextFactory;
        _paymentHandlers = paymentHandlers;
        _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
    }

    protected override async Task Process(object paymentMethodConfig, List<PayoutData> payouts)
    {
        if (paymentMethodConfig is not CashuPaymentMethodConfig cashuConfig)
        {
            Logs.PayServer.LogWarning("Cashu payment method not configured for store {StoreId}", PayoutProcessorSettings.StoreId);
            DisableProcessor(payouts);
            return;
        }

        if (cashuConfig.TrustedMintsUrls == null || cashuConfig.TrustedMintsUrls.Count == 0)
        {
            Logs.PayServer.LogWarning("No trusted mints configured for store {StoreId}", PayoutProcessorSettings.StoreId);
            DisableProcessor(payouts);
            return;
        }

        // Use the first trusted mint for payouts
        var mintUrl = cashuConfig.TrustedMintsUrls.First();
        var wallet = new CashuWallet(mintUrl, "sat", _cashuDbContextFactory);

        await using var dbContext = _cashuDbContextFactory.CreateContext();

        foreach (var payout in payouts)
        {
            try
            {
                if (payout.State != PayoutState.AwaitingPayment)
                    continue;

                var blob = payout.GetBlob(_btcPayNetworkJsonSerializerSettings);
                var amountSatoshis = (ulong)Money.Coins(payout.Amount.Value).Satoshi;

                // Get stored proofs from the store's Cashu wallet
                var storedProofs = await dbContext.Proofs
                    .Where(p => p.StoreId == PayoutProcessorSettings.StoreId)
                    .OrderByDescending(p => p.Amount)
                    .ToListAsync();

                var availableAmount = storedProofs.Sum(p => p.Amount);
                if (availableAmount < amountSatoshis)
                {
                    Logs.PayServer.LogWarning(
                        "Insufficient Cashu proofs for payout {PayoutId}. Available: {Available}, Required: {Required}",
                        payout.Id,
                        availableAmount,
                        amountSatoshis);
                    continue;
                }

                // Select proofs to use for the swap - track both the stored proof and the dotnut proof
                var proofsToUse = new List<Proof>();
                var storedProofsToUse = new List<BTCPayServer.Plugins.Cashu.Data.Models.StoredProof>();
                ulong selectedAmount = 0;
                foreach (var storedProof in storedProofs)
                {
                    if (selectedAmount >= amountSatoshis)
                        break;

                    proofsToUse.Add(storedProof.ToDotNutProof());
                    storedProofsToUse.Add(storedProof);
                    selectedAmount += storedProof.Amount;
                }

                // Split the amount into proof amounts using CashuUtils
                var keysets = await wallet.GetKeysets();
                var activeKeyset = await wallet.GetActiveKeyset();
                var keys = await wallet.GetKeys(activeKeyset.Id);

                if (keys == null)
                {
                    Logs.PayServer.LogError("Could not get keys for keyset {KeysetId}", activeKeyset.Id);
                    continue;
                }

                var outputAmounts = CashuUtils.SplitToProofsAmounts(amountSatoshis, keys);

                // Perform swap to create new proofs
                var swapResult = await wallet.Swap(proofsToUse, outputAmounts, activeKeyset.Id, keys);

                if (!swapResult.Success || swapResult.ResultProofs == null)
                {
                    Logs.PayServer.LogError(
                        "Failed to swap Cashu proofs for payout {PayoutId}. Error: {Error}",
                        payout.Id,
                        swapResult.Error?.Message ?? "Unknown error");
                    continue;
                }

                // Create ecash token from the new proofs (only the proofs for the payout amount, not change)
                var payoutProofs = swapResult.ResultProofs.Take(outputAmounts.Count).ToList();
                var createdToken = new CashuToken()
                {
                    Tokens =
                    [
                        new CashuToken.Token
                        {
                            Mint = mintUrl,
                            Proofs = payoutProofs,
                        }
                    ],
                    Memo = $"Cashu payout {payout.Id}",
                    Unit = "sat"
                };
                var serializedToken = createdToken.Encode();

                // Remove used proofs from database - use the ProofId from stored proofs we tracked
                var proofIdsToRemove = storedProofsToUse.Select(p => p.ProofId).ToList();
                var proofsToRemove = await dbContext.Proofs
                    .Where(p => proofIdsToRemove.Contains(p.ProofId))
                    .ToListAsync();
                
                dbContext.Proofs.RemoveRange(proofsToRemove);

                // Store new proofs (change if any) in database
                if (swapResult.ResultProofs.Length > outputAmounts.Count)
                {
                    // There's change - store it
                    var changeProofs = swapResult.ResultProofs.Skip(outputAmounts.Count).ToList();
                    var storedChangeProofs = changeProofs.Select(p => 
                        new BTCPayServer.Plugins.Cashu.Data.Models.StoredProof(p, PayoutProcessorSettings.StoreId));
                    await dbContext.Proofs.AddRangeAsync(storedChangeProofs);
                }

                await dbContext.SaveChangesAsync();

                // Mark payout as in progress first
                payout.State = PayoutState.InProgress;
                
                // Store the token as proof
                var proofBlob = new CashuPayoutBlob
                {
                    Token = serializedToken,
                    Mint = mintUrl,
                    Amount = amountSatoshis
                };
                
                // Set the proof blob using the payout data extension method
                payout.SetProofBlob(proofBlob, _btcPayNetworkJsonSerializerSettings.GetSerializer(PayoutMethodId));
                
                // Update payout state to completed after proof is set
                payout.State = PayoutState.Completed;
                
                Logs.PayServer.LogInformation(
                    "Successfully created Cashu payout {PayoutId} with token worth {Amount} sats",
                    payout.Id,
                    amountSatoshis);
            }
            catch (Exception ex)
            {
                Logs.PayServer.LogError(ex,
                    "Error processing Cashu payout {PayoutId}",
                    payout.Id);
            }
        }
    }
}
