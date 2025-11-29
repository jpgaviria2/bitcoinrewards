#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Data.Models;
using BTCPayServer.Plugins.BitcoinRewards.PaymentHandlers;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using BTCPayServer.Plugins.BitcoinRewards.CashuAbstractions;
using BTCPayServer.Plugins.BitcoinRewards.Data.enums;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DotNut;
using DotNut.ApiModels;
using Newtonsoft.Json.Linq;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[Route("stores/{storeId}/bitcoin-rewards")]
[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class WalletController : Controller
{
    public WalletController(InvoiceRepository invoiceRepository,
        StoreRepository storeRepository,
        PaymentMethodHandlerDictionary handlers,
        WalletStatusProvider walletStatusProvider,
        Data.BitcoinRewardsPluginDbContextFactory dbContextFactory)
    {
        _invoiceRepository = invoiceRepository;
        _storeRepository = storeRepository;
        _dbContextFactory = dbContextFactory;
        _walletStatusProvider = walletStatusProvider;
        _handlers = handlers;
    }
    
    private StoreData StoreData => HttpContext.GetStoreData();
    
    private readonly InvoiceRepository _invoiceRepository;
    private readonly StoreRepository _storeRepository;
    private readonly PaymentMethodHandlerDictionary _handlers;
    private readonly WalletStatusProvider _walletStatusProvider;
    private readonly Data.BitcoinRewardsPluginDbContextFactory _dbContextFactory;

    
    /// <summary>
    /// Api route for fetching current plugin configuration for this store
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> StoreConfig()
    {
        // Get config directly from JToken since we don't have a registered payment handler
        var configToken = StoreData.GetPaymentMethodConfig(BitcoinRewardsPlugin.WalletPmid);
        WalletPaymentMethodConfig? walletPaymentMethodConfig = null;
        
        if (configToken != null)
        {
            walletPaymentMethodConfig = configToken.ToObject<WalletPaymentMethodConfig>();
        }

        WalletStoreViewModel model = new WalletStoreViewModel();

        if (walletPaymentMethodConfig == null)
        {
            model.Enabled = await _walletStatusProvider.WalletEnabled(StoreData.Id);
            model.PaymentAcceptanceModel = CashuPaymentModel.SwapAndHodl;
            model.TrustedMintsUrls = "";
            model.CustomerFeeAdvance = 0;
            // 2% of fee can be a lot, but it's usually returned. 
            model.MaxLightningFee = 2;
            //I wouldn't advise to set more than 1% of keyset fee... since they're denominated in ppk (1/1000 * unit * input_amount)in tokens unit
            model.MaxKeysetFee = 1;
        }
        else
        {
            model.Enabled = await _walletStatusProvider.WalletEnabled(StoreData.Id);
            model.PaymentAcceptanceModel = walletPaymentMethodConfig.PaymentModel;
            model.TrustedMintsUrls = String.Join("\n", walletPaymentMethodConfig.TrustedMintsUrls ?? [""]);
            model.CustomerFeeAdvance = walletPaymentMethodConfig.FeeConfing.CustomerFeeAdvance;
            model.MaxLightningFee = walletPaymentMethodConfig.FeeConfing.MaxLightningFee;
            model.MaxKeysetFee = walletPaymentMethodConfig.FeeConfing.MaxKeysetFee;
        }

        return View(model);
    }

    
    /// <summary>
    /// Api route for setting plugin configuration for this store
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> StoreConfig(WalletStoreViewModel viewModel)
    {
        var store = StoreData;
        var blob = StoreData.GetStoreBlob();
        var paymentMethodId = BitcoinRewardsPlugin.WalletPmid;

        viewModel.TrustedMintsUrls ??= "";

        //trimming trailing slash 
        var parsedTrustedMintsUrls = viewModel.TrustedMintsUrls
            .Split(["\r\n", "\r", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().TrimEnd('/'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        var lightningEnabled = StoreData.IsLightningEnabled("BTC");

        //If lighting isn't configured - don't allow user to set meltImmediately.
        var paymentMethodConfig = new WalletPaymentMethodConfig()
        {
            PaymentModel = lightningEnabled ? viewModel.PaymentAcceptanceModel : CashuPaymentModel.SwapAndHodl,
            TrustedMintsUrls = parsedTrustedMintsUrls,
            FeeConfing = new WalletFeeConfig
            {
                CustomerFeeAdvance = viewModel.CustomerFeeAdvance,
                MaxLightningFee = viewModel.MaxLightningFee,
                MaxKeysetFee = viewModel.MaxKeysetFee,
            }
        };

        blob.SetExcluded(paymentMethodId, !viewModel.Enabled);

        // Use JToken overload since we don't have a registered payment handler
        var configJson = Newtonsoft.Json.Linq.JToken.FromObject(paymentMethodConfig);
        StoreData.SetPaymentMethodConfig(paymentMethodId, configJson);
        store.SetStoreBlob(blob);
        await _storeRepository.UpdateStore(store);
        if (viewModel.PaymentAcceptanceModel == CashuPaymentModel.MeltImmediately && !lightningEnabled)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Can't use this payment model. Lightning wallet is disabled.";
        }
        else
        {
            TempData[WellKnownTempData.SuccessMessage] = "Config Saved Successfully";
        }

        return RedirectToAction("StoreConfig", new { storeId = store.Id });
    }

    /// <summary>
    /// Api route for fetching current store Cashu Wallet view - All stored proofs grouped by mint and unit which can be exported.
    /// </summary>
    /// <returns></returns>
    [HttpGet("wallet")]
    public async Task<IActionResult> Wallet()
    {
        await using var db = _dbContextFactory.CreateContext();

        // Get config directly from JToken since we don't have a registered payment handler
        var configToken = StoreData.GetPaymentMethodConfig(BitcoinRewardsPlugin.WalletPmid);
        var walletPaymentMethodConfig = configToken?.ToObject<WalletPaymentMethodConfig>();
        
        var mints = walletPaymentMethodConfig?.TrustedMintsUrls ?? new List<string>();
        var proofsWithUnits = new List<(string Mint, string Unit, ulong Amount)>();
        
        var unavailableMints = new List<string>();
        
        foreach (var mint in mints)
        {
            try
            { 
                var cashuHttpClient = CashuUtils.GetCashuHttpClient(mint); 
                var keysets = await cashuHttpClient.GetKeysets();

               var localProofs = await db.Proofs
                   .Where(p => keysets.Keysets.Select(k => k.Id).Contains(p.Id) &&
                               p.StoreId == StoreData.Id &&
                                 !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)
                                     )).ToListAsync();
               
                foreach (var proof in localProofs)
                {
                    var matchingKeyset = keysets.Keysets.FirstOrDefault(k => k.Id == proof.Id);
                    if (matchingKeyset != null)
                    {
                        proofsWithUnits.Add((Mint: mint, matchingKeyset.Unit, proof.Amount));
                    }
                }
            }
            catch (Exception)
            {
                unavailableMints.Add(mint);
            }
        }

        var groupedProofs = proofsWithUnits
            .GroupBy(p => new { p.Mint, p.Unit })
            .Select(group => new
                {
                 group.Key.Mint,
                 group.Key.Unit,
                 Amount = group.Aggregate(0UL, (sum, x) => sum + x.Amount)
                }
            )
            .OrderByDescending(x => x.Amount)
            .Select(x => (x.Mint, x.Unit, x.Amount))
            .ToList();
        
        var exportedTokens = db.ExportedTokens.Where(et=>et.StoreId == StoreData.Id).ToList();
        if (unavailableMints.Any())
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Couldn't load {unavailableMints.Count} mints: {String.Join(", ", unavailableMints)}";
        }
        var viewModel = new WalletViewModel {AvaibleBalances = groupedProofs, ExportedTokens = exportedTokens};
        
        return View("Index", viewModel);
    }
    
    /// <summary>
    /// Api route for exporting stored balance for chosen mint and unit
    /// </summary>
    /// <param name="mintUrl">Chosen mint url, form which proofs we want to export</param>
    /// <param name="unit">Chosen unit of token</param>
    [HttpPost("ExportMintBalance")]
    public async Task<IActionResult> ExportMintBalance(string mintUrl, string unit)
    {
        if (string.IsNullOrWhiteSpace(mintUrl)|| string.IsNullOrWhiteSpace(unit))
        {
            TempData[WellKnownTempData.ErrorMessage] = "Invalid mint or unit provided!";
            return RedirectToAction("Wallet", new { storeId = StoreData.Id});
        }
        
        await using var db = _dbContextFactory.CreateContext();
        List<GetKeysetsResponse.KeysetItemResponse> keysets;
        try
        {
            var cashuWallet = new InternalCashuWallet(mintUrl, unit);
            keysets = await cashuWallet.GetKeysets();
            if (keysets == null || keysets.Count == 0)
            {
                throw new Exception("No keysets were found.");
            }
        }
        catch (Exception)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Couldn't get keysets!";
            return RedirectToAction("Wallet", new { storeId = StoreData.Id});
        }
 
        var selectedProofs = db.Proofs.Where(p=>
            p.StoreId == StoreData.Id 
            && keysets.Select(k => k.Id).Contains(p.Id) 
            //ensure that proof is free and spendable (yeah!)
            && !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p))
            ).ToList();
    
        var createdToken = new CashuToken()
        {
            Tokens =
            [
                new CashuToken.Token
                {
                    Mint = mintUrl,
                    Proofs = selectedProofs.Select(p => p.ToDotNutProof()).ToList(),
                }
            ],
            Memo = "Cashu Token withdrawn from Bitcoin Rewards Wallet",
            Unit = unit
        };
        
        var tokenAmount = selectedProofs.Aggregate(0UL, (sum, p) => sum + p.Amount);
        var serializedToken = createdToken.Encode();
    
        var proofsToRemove = await db.Proofs
            .Where(p => p.StoreId == StoreData.Id && 
                        keysets.Select(k => k.Id).Contains(p.Id))
            .ToListAsync();
        
        var exportedTokenEntity = new ExportedToken
        {
            SerializedToken = serializedToken,
            Amount = tokenAmount,
            Unit = unit,
            Mint = mintUrl,
            StoreId = StoreData.Id,
            IsUsed = false,
        };
        var strategy = db.Database.CreateExecutionStrategy();
        
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            try
            {
                db.Proofs.RemoveRange(proofsToRemove);
                db.ExportedTokens.Add(exportedTokenEntity);
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                ViewData[WellKnownTempData.ErrorMessage] = $"Couldn't export";
                RedirectToAction(nameof(Wallet), new { storeId = StoreData.Id });
            }
        });
        return RedirectToAction(nameof(ExportedToken), new { tokenId = exportedTokenEntity.Id });
    }

    /// <summary>
    /// Api route for fetching exported token data
    /// </summary>
    /// <param name="tokenId">Stored Token GUID</param>
    [HttpGet("/Token")]
    public async Task<IActionResult> ExportedToken(Guid tokenId)
    {
        
        var db = _dbContextFactory.CreateContext();
       
        var exportedToken = db.ExportedTokens.SingleOrDefault(e => e.Id == tokenId);
        if (exportedToken == null)
        {
            return BadRequest("Can't find token with provided GUID");
        }

        if (!exportedToken.IsUsed)
        { 
            try
            {
                var wallet = new InternalCashuWallet(exportedToken.Mint, exportedToken.Unit);
                var proofs = CashuTokenHelper.Decode(exportedToken.SerializedToken, out _)
                    .Tokens.SelectMany(t => t.Proofs)
                    .Distinct()
                    .ToList();
                var state = await wallet.CheckTokenState(proofs);
                if (state == StateResponseItem.TokenState.SPENT)
                {
                    exportedToken.IsUsed = true;
                    db.ExportedTokens.Update(exportedToken);
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception)
            {
                //honestly, there's nothing to do. maybe it'll work next time
            }
        }
        
        var model = new ExportedTokenViewModel()
        {
            Amount = exportedToken.Amount,
            Unit = exportedToken.Unit,
            MintAddress = exportedToken.Mint,
            Token = exportedToken.SerializedToken,
        };
        
        return View(model);
    }

    /// <summary>
    /// Api route for fetching failed transactions list
    /// </summary>
    /// <returns></returns>
    [HttpGet("FailedTransactions")]
    public async Task<IActionResult> FailedTransactions()
    {
        await using var db = _dbContextFactory.CreateContext();
        //fetch recently failed transactions 
        var failedTransactions = db.FailedTransactions
            .Where(ft => ft.StoreId == StoreData.Id)
            .Include(ft=>ft.UsedProofs)
            .ToList();
            
        return View(failedTransactions);
    }
}
