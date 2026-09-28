#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BtcpayPayoutState = BTCPayServer.Client.Models.PayoutState;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Data.Payouts.LightningLike;
using BTCPayServer.HostedServices;
using BTCPayServer.Lightning;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Payouts;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards.HostedServices;

/// <summary>
/// Settles Bitcoin Rewards display/pull-payment claims with the store Lightning node directly.
///
/// BTCPay's generic automated pull-payment payout processor can strand tiny 10-40 sat claims
/// in AwaitingPayment even after a wallet reports the LNURL-withdraw was accepted. This worker
/// only touches payouts for pull payments created by this plugin, claims the payout by moving it
/// to InProgress, pays the claimed invoice with the store Lightning client, then marks BTCPay's
/// payout Completed with the normal lightning proof blob.
/// </summary>
public sealed class LegacyPullPaymentDirectSettlementService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private readonly ApplicationDbContextFactory _applicationDbContextFactory;
    private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
    private readonly BitcoinRewardsPluginDbContextFactory _pluginDbContextFactory;
    private readonly PullPaymentHostedService _pullPaymentHostedService;
    private readonly PayoutMethodHandlerDictionary _payoutHandlers;
    private readonly PaymentMethodHandlerDictionary _paymentHandlers;
    private readonly BTCPayNetworkProvider _networkProvider;
    private readonly LightningNetworkOptions _lightningNetworkOptions;
    private readonly LightningClientFactoryService _lightningClientFactory;
    private readonly ILogger<LegacyPullPaymentDirectSettlementService> _logger;

    public LegacyPullPaymentDirectSettlementService(
        ApplicationDbContextFactory applicationDbContextFactory,
        BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
        BitcoinRewardsPluginDbContextFactory pluginDbContextFactory,
        PullPaymentHostedService pullPaymentHostedService,
        PayoutMethodHandlerDictionary payoutHandlers,
        PaymentMethodHandlerDictionary paymentHandlers,
        BTCPayNetworkProvider networkProvider,
        IOptions<LightningNetworkOptions> lightningNetworkOptions,
        LightningClientFactoryService lightningClientFactory,
        ILogger<LegacyPullPaymentDirectSettlementService> logger)
    {
        _applicationDbContextFactory = applicationDbContextFactory;
        _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
        _pluginDbContextFactory = pluginDbContextFactory;
        _pullPaymentHostedService = pullPaymentHostedService;
        _payoutHandlers = payoutHandlers;
        _paymentHandlers = paymentHandlers;
        _networkProvider = networkProvider;
        _lightningNetworkOptions = lightningNetworkOptions.Value;
        _lightningClientFactory = lightningClientFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SettlePendingRewardPayouts(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Bitcoin Rewards display payout settlement loop failed");
            }

            await Task.Delay(PollInterval, stoppingToken);
        }
    }

    private async Task SettlePendingRewardPayouts(CancellationToken cancellationToken)
    {
        await using var pluginDb = _pluginDbContextFactory.CreateContext();
        var pullPaymentIds = await pluginDb.BitcoinRewardRecords
            .Where(r => r.DeliveryMode == RewardDeliveryMode.LegacyPullPayment && r.PullPaymentId != null)
            .Select(r => r.PullPaymentId!)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (pullPaymentIds.Count == 0)
            return;

        await using var appDb = _applicationDbContextFactory.CreateContext();
        var pendingPayouts = await appDb.Payouts
            .Where(p => pullPaymentIds.Contains(p.PullPaymentDataId))
            .Where(p => p.PayoutMethodId == "BTC-LN")
            .Where(p => p.State == BtcpayPayoutState.AwaitingPayment)
            .Where(p => p.Proof == null)
            .OrderBy(p => p.Date)
            .Take(10)
            .ToListAsync(cancellationToken);

        foreach (var payout in pendingPayouts)
        {
            await SettlePayout(payout.Id, cancellationToken);
        }
    }

    private async Task SettlePayout(string payoutId, CancellationToken cancellationToken)
    {
        BitcoinRewardRecord? reward;
        PayoutData? payout;
        PayoutBlob payoutBlob;
        RewardPayoutAttempt attempt;

        await using (var appDb = _applicationDbContextFactory.CreateContext())
        {
            payout = await appDb.Payouts.FirstOrDefaultAsync(p => p.Id == payoutId, cancellationToken);
            if (payout is null || payout.State != BtcpayPayoutState.AwaitingPayment || payout.Proof is not null)
                return;
            payoutBlob = payout.GetBlob(_btcPayNetworkJsonSerializerSettings);
        }

        await using (var pluginDb = _pluginDbContextFactory.CreateContext())
        {
            reward = await pluginDb.BitcoinRewardRecords
                .FirstOrDefaultAsync(r => r.PullPaymentId == payout.PullPaymentDataId, cancellationToken);
            if (reward is null)
                return;

            var existing = await pluginDb.RewardPayoutAttempts
                .Where(a => a.RewardId == reward.Id && a.ProviderReference == payout.Id)
                .OrderByDescending(a => a.CreatedAt)
                .FirstOrDefaultAsync(cancellationToken);
            if (existing?.State == RewardPayoutState.Paid)
                return;
            if (existing?.State == RewardPayoutState.Paying && existing.UpdatedAt > DateTime.UtcNow.AddMinutes(-3))
                return;

            if (existing is null)
            {
                var nextAttempt = await pluginDb.RewardPayoutAttempts
                    .Where(a => a.RewardId == reward.Id)
                    .Select(a => (int?)a.AttemptNumber)
                    .MaxAsync(cancellationToken) ?? 0;
                existing = new RewardPayoutAttempt
                {
                    RewardId = reward.Id,
                    AttemptNumber = nextAttempt + 1,
                    LightningAddressHash = reward.LightningAddressHash ?? string.Empty,
                    AmountSatoshis = reward.RewardAmountSatoshis,
                    ProviderReference = payout.Id,
                    State = RewardPayoutState.Queued,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                pluginDb.RewardPayoutAttempts.Add(existing);
                await pluginDb.SaveChangesAsync(cancellationToken);
            }

            attempt = existing;
        }

        var inProgress = await _pullPaymentHostedService.MarkPaid(new MarkPayoutRequest
        {
            PayoutId = payout.Id,
            State = BtcpayPayoutState.InProgress,
            Proof = null
        });
        if (inProgress != MarkPayoutRequest.PayoutPaidResult.Ok)
        {
            _logger.LogDebug("Skipping reward payout {PayoutId}; could not mark InProgress: {Result}", payout.Id, inProgress);
            return;
        }

        try
        {
            var paymentRequest = await ResolveClaimInvoice(payout, payoutBlob, cancellationToken);
            var lightningClient = await CreateStoreLightningClient(payout.StoreDataId, cancellationToken);
            var pay = await lightningClient.Pay(
                paymentRequest.ToString(),
                new PayInvoiceParams
                {
                    Amount = new LightMoney(reward.RewardAmountSatoshis, LightMoneyUnit.Satoshi),
                    SendTimeout = TimeSpan.FromSeconds(45)
                },
                cancellationToken);

            var paymentHash = paymentRequest.PaymentHash?.ToString();
            string? preimage = null;
            LightMoney? amountSent = null;
            bool? succeeded = null;
            string? error = null;

            if (pay.Result == PayResult.Ok)
            {
                succeeded = true;
                paymentHash = pay.Details?.PaymentHash?.ToString() ?? paymentHash;
                preimage = pay.Details?.Preimage?.ToString();
                amountSent = pay.Details?.TotalAmount;
            }
            else
            {
                error = pay.ErrorDetail ?? pay.Result.ToString();
                var existingPayment = await GetPaymentStatus(lightningClient, paymentHash, cancellationToken);
                if (existingPayment?.Status == LightningPaymentStatus.Complete)
                {
                    succeeded = true;
                    paymentHash = existingPayment.PaymentHash ?? paymentHash;
                    preimage = existingPayment.Preimage;
                    amountSent = existingPayment.AmountSent;
                }
                else if (existingPayment?.Status == LightningPaymentStatus.Failed || pay.Result is PayResult.Error or PayResult.CouldNotFindRoute)
                {
                    succeeded = false;
                }
            }

            if (succeeded is true)
            {
                var proof = JObject.FromObject(new PayoutLightningBlob
                {
                    PaymentHash = paymentHash,
                    Preimage = preimage
                });
                var completed = await _pullPaymentHostedService.MarkPaid(new MarkPayoutRequest
                {
                    PayoutId = payout.Id,
                    State = BtcpayPayoutState.Completed,
                    Proof = proof
                });
                if (completed != MarkPayoutRequest.PayoutPaidResult.Ok)
                    throw new InvalidOperationException($"BTCPay refused to mark payout completed: {completed}");

                await MarkRewardPaid(reward.Id, attempt.Id, payout.Id, paymentHash, cancellationToken);
                _logger.LogInformation(
                    "Paid Bitcoin Rewards display claim {PayoutId} for reward {RewardId} using store Lightning node. Amount {Amount}, payment hash {PaymentHash}",
                    payout.Id,
                    reward.Id,
                    amountSent?.ToDecimal(LightMoneyUnit.Satoshi) ?? reward.RewardAmountSatoshis,
                    paymentHash);
            }
            else
            {
                var permanentFailure = IsPermanentClaimFailure(error);
                await MarkFailed(reward.Id, attempt.Id, error ?? "Lightning payment is still pending or failed", permanentFailure, cancellationToken);
                await _pullPaymentHostedService.MarkPaid(new MarkPayoutRequest
                {
                    PayoutId = payout.Id,
                    State = permanentFailure ? BtcpayPayoutState.Cancelled : BtcpayPayoutState.AwaitingPayment,
                    Proof = null
                });
            }
        }
        catch (Exception ex)
        {
            var permanentFailure = IsPermanentClaimFailure(ex.Message);
            await MarkFailed(reward.Id, attempt.Id, ex.Message, permanentFailure, cancellationToken);
            await _pullPaymentHostedService.MarkPaid(new MarkPayoutRequest
            {
                PayoutId = payout.Id,
                State = permanentFailure ? BtcpayPayoutState.Cancelled : BtcpayPayoutState.AwaitingPayment,
                Proof = null
            });
            _logger.LogWarning(ex, "Direct settlement failed for Bitcoin Rewards display payout {PayoutId}", payout.Id);
        }
    }

    private async Task<BOLT11PaymentRequest> ResolveClaimInvoice(PayoutData payout, PayoutBlob payoutBlob, CancellationToken cancellationToken)
    {
        var payoutMethodId = PayoutMethodId.Parse(payout.PayoutMethodId);
        if (_payoutHandlers.TryGet(payoutMethodId) is not LightningLikePayoutHandler handler)
            throw new InvalidOperationException($"Payout method {payout.PayoutMethodId} is not a Lightning payout handler");

        var (destination, error) = await handler.ParseClaimDestination(payoutBlob.Destination, cancellationToken);
        switch (destination)
        {
            case BoltInvoiceClaimDestination bolt:
                return bolt.PaymentRequest;
            case LNURLPayClaimDestinaton lnurlPay:
                return await ResolveLnurlPayInvoice(handler, payout, lnurlPay, cancellationToken);
            default:
                throw new InvalidOperationException(error ?? "Unsupported Lightning claim destination");
        }
    }

    private async Task<BOLT11PaymentRequest> ResolveLnurlPayInvoice(
        LightningLikePayoutHandler handler,
        PayoutData payout,
        LNURLPayClaimDestinaton lnurlPay,
        CancellationToken cancellationToken)
    {
        var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC")
                      ?? throw new InvalidOperationException("BTC network is not available");
        var endpoint = lnurlPay.LNURL.IsValidEmail()
            ? LNURL.LNURL.ExtractUriFromInternetIdentifier(lnurlPay.LNURL)
            : LNURL.LNURL.Parse(lnurlPay.LNURL, out _);
        var httpClient = handler.CreateClient(endpoint);
        var info = (LNURL.LNURLPayRequest)await LNURL.LNURL.FetchInformation(endpoint, "payRequest", httpClient, cancellationToken);
        var amount = new LightMoney(payout.Amount ?? payout.OriginalAmount, LightMoneyUnit.BTC);
        if (amount > info.MaxSendable || amount < info.MinSendable)
            throw new InvalidOperationException($"LNURL destination will not generate an invoice for {amount.ToDecimal(LightMoneyUnit.Satoshi):0} sats");
        var response = await info.SendRequest(amount, network.NBitcoinNetwork, httpClient, cancellationToken: cancellationToken);
        return response.GetPaymentRequest(network.NBitcoinNetwork);
    }

    private async Task<ILightningClient> CreateStoreLightningClient(string storeId, CancellationToken cancellationToken)
    {
        var network = _networkProvider.GetNetwork<BTCPayNetwork>("BTC")
                      ?? throw new InvalidOperationException("BTC network is not available");
        await using var appDb = _applicationDbContextFactory.CreateContext();
        var store = await appDb.Stores.FirstOrDefaultAsync(s => s.Id == storeId, cancellationToken)
                    ?? throw new InvalidOperationException("Store not found");
        var paymentMethodId = PaymentTypes.LN.GetPaymentMethodId("BTC");
        var lightningConfig = store.GetPaymentMethodConfig<LightningPaymentMethodConfig>(paymentMethodId, _paymentHandlers);
        if (lightningConfig is null || !lightningConfig.IsConfigured(network, _lightningNetworkOptions))
            throw new InvalidOperationException("BTC Lightning node is not configured for this store");
        return lightningConfig.CreateLightningClient(network, _lightningNetworkOptions, _lightningClientFactory);
    }

    private static async Task<LightningPayment?> GetPaymentStatus(ILightningClient lightningClient, string? paymentHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentHash))
            return null;
        try
        {
            return await lightningClient.GetPayment(paymentHash, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task MarkRewardPaid(Guid rewardId, Guid attemptId, string payoutId, string? paymentHash, CancellationToken cancellationToken)
    {
        await using var pluginDb = _pluginDbContextFactory.CreateContext();
        var reward = await pluginDb.BitcoinRewardRecords.FirstOrDefaultAsync(r => r.Id == rewardId, cancellationToken);
        var attempt = await pluginDb.RewardPayoutAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);
        if (reward is not null)
        {
            reward.PayoutId = payoutId;
            reward.DirectPayoutState = RewardPayoutState.Paid;
            reward.Status = RewardStatus.Redeemed;
            reward.RedeemedAt ??= DateTime.UtcNow;
            reward.PaidAt ??= DateTime.UtcNow;
            reward.ErrorMessage = null;
        }
        if (attempt is not null)
        {
            attempt.State = RewardPayoutState.Paid;
            attempt.ProviderReference = payoutId;
            attempt.PaymentHash = paymentHash?.Length <= 64 ? paymentHash : paymentHash?[..64];
            attempt.PaidAt = DateTime.UtcNow;
            attempt.UpdatedAt = DateTime.UtcNow;
            attempt.LastError = null;
        }
        await pluginDb.SaveChangesAsync(cancellationToken);
    }

    private async Task MarkFailed(Guid rewardId, Guid attemptId, string error, bool permanent, CancellationToken cancellationToken)
    {
        await using var pluginDb = _pluginDbContextFactory.CreateContext();
        var reward = await pluginDb.BitcoinRewardRecords.FirstOrDefaultAsync(r => r.Id == rewardId, cancellationToken);
        var attempt = await pluginDb.RewardPayoutAttempts.FirstOrDefaultAsync(a => a.Id == attemptId, cancellationToken);
        if (reward is not null)
        {
            reward.DirectPayoutState = permanent ? RewardPayoutState.PermanentFailure : RewardPayoutState.RetryableFailure;
            reward.ErrorMessage = error;
        }
        if (attempt is not null)
        {
            attempt.State = permanent ? RewardPayoutState.PermanentFailure : RewardPayoutState.RetryableFailure;
            attempt.LastError = error;
            attempt.UpdatedAt = DateTime.UtcNow;
        }
        await pluginDb.SaveChangesAsync(cancellationToken);
    }

    private static bool IsPermanentClaimFailure(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return false;
        return error.Contains("expired", StringComparison.OrdinalIgnoreCase) ||
               error.Contains("self-payments not allowed", StringComparison.OrdinalIgnoreCase);
    }
}
