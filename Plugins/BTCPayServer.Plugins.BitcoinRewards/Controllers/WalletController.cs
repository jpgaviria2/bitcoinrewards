#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
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
        // Get config using registered payment handler
        WalletPaymentMethodConfig? walletPaymentMethodConfig = null;
        
        if (_handlers.TryGetValue(BitcoinRewardsPlugin.WalletPmid, out var handler) && 
            handler is PaymentHandlers.WalletPaymentMethodHandler walletHandler)
        {
            var configToken = StoreData.GetPaymentMethodConfig(BitcoinRewardsPlugin.WalletPmid);
            if (configToken != null)
            {
                walletPaymentMethodConfig = walletHandler.ParsePaymentMethodConfig(configToken) as WalletPaymentMethodConfig;
            }
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

        // Use registered payment handler to set config (matching Cashu plugin exactly)
        StoreData.SetPaymentMethodConfig(_handlers[paymentMethodId], paymentMethodConfig);
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

        // Get config using registered payment handler
        WalletPaymentMethodConfig? walletPaymentMethodConfig = null;
        if (_handlers.TryGetValue(BitcoinRewardsPlugin.WalletPmid, out var handler) && 
            handler is PaymentHandlers.WalletPaymentMethodHandler walletHandler)
        {
            var configToken = StoreData.GetPaymentMethodConfig(BitcoinRewardsPlugin.WalletPmid);
            if (configToken != null)
            {
                walletPaymentMethodConfig = walletHandler.ParsePaymentMethodConfig(configToken) as WalletPaymentMethodConfig;
            }
        }
        
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
        
        // Query ExportedTokens with error handling in case table doesn't exist yet (during migration)
        var exportedTokens = new List<ExportedToken>();
        try
        {
            exportedTokens = await db.ExportedTokens.Where(et => et.StoreId == StoreData.Id).ToListAsync();
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "42P01")
        {
            // If ExportedTokens table doesn't exist yet (during migration), return empty list
            // This allows the page to load even if migrations haven't completed
        }
        catch (Exception ex) when (ex.Message.Contains("does not exist") || 
                                   ex.Message.Contains("relation") || 
                                   ex.Message.Contains("ExportedTokens"))
        {
            // Fallback for other exception types that might indicate missing table
        }
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

    /// <summary>
    /// GET action for wallet top-up page
    /// </summary>
    [HttpGet("TopUp")]
    public async Task<IActionResult> TopUp()
    {
        await using var db = _dbContextFactory.CreateContext();
        
        // Get wallet config
        WalletPaymentMethodConfig? walletPaymentMethodConfig = null;
        if (_handlers.TryGetValue(BitcoinRewardsPlugin.WalletPmid, out var handler) && 
            handler is PaymentHandlers.WalletPaymentMethodHandler walletHandler)
        {
            var configToken = StoreData.GetPaymentMethodConfig(BitcoinRewardsPlugin.WalletPmid);
            if (configToken != null)
            {
                walletPaymentMethodConfig = walletHandler.ParsePaymentMethodConfig(configToken) as WalletPaymentMethodConfig;
            }
        }

        // Get current balance from proofs
        var mints = walletPaymentMethodConfig?.TrustedMintsUrls ?? new List<string>();
        ulong totalBalance = 0;
        string? firstMintUrl = mints.FirstOrDefault();
        
        if (!string.IsNullOrEmpty(firstMintUrl))
        {
            var proofs = await db.Proofs
                .Where(p => p.StoreId == StoreData.Id && p.MintUrl == firstMintUrl)
                .ToListAsync();
            totalBalance = proofs.Aggregate(0UL, (sum, p) => sum + p.Amount);
        }

        var model = new TopUpViewModel
        {
            StoreId = StoreData.Id,
            MintUrl = firstMintUrl ?? "",
            CurrentBalance = totalBalance
        };

        return View(model);
    }

    /// <summary>
    /// POST action for top-up from Lightning
    /// </summary>
    [HttpPost("TopUpFromLightning")]
    public async Task<IActionResult> TopUpFromLightning([FromForm] ulong amountSatoshis, [FromForm] string mintUrl)
    {
        if (amountSatoshis == 0)
        {
            TempData[WellKnownTempData.ErrorMessage] = "Amount must be greater than 0";
            return RedirectToAction("TopUp");
        }

        if (string.IsNullOrWhiteSpace(mintUrl))
        {
            TempData[WellKnownTempData.ErrorMessage] = "Mint URL is required";
            return RedirectToAction("TopUp");
        }

        try
        {
            // Get CashuServiceAdapter to use MintFromLightningAsync
            var cashuService = HttpContext.RequestServices.GetRequiredService<Services.ICashuService>();
            
            // MintFromLightningAsync is internal, so we'll use reflection or create a public method
            // For now, let's use the CashuServiceAdapter's internal method via reflection
            var adapterType = typeof(Services.CashuServiceAdapter);
            var mintMethod = adapterType.GetMethod("MintFromLightningAsync", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            
            if (mintMethod == null)
            {
                // Fallback: use InternalCashuWallet directly
                var lightningClientObj = await GetLightningClientForStore(StoreData.Id);
                if (lightningClientObj == null || !(lightningClientObj is BTCPayServer.Lightning.ILightningClient lightningClient))
                {
                    TempData[WellKnownTempData.ErrorMessage] = "Lightning client not available. Please configure Lightning for this store.";
                    return RedirectToAction("TopUp");
                }

                var walletLogger = HttpContext.RequestServices.GetService<Microsoft.Extensions.Logging.ILogger<Services.InternalCashuWallet>>();
                var wallet = new Services.InternalCashuWallet(lightningClient, mintUrl, "sat", walletLogger);

                // Create mint quote
                var mintQuote = await wallet.CreateMintQuote(amountSatoshis, "sat");
                if (mintQuote == null || string.IsNullOrEmpty(mintQuote.Request))
                {
                    TempData[WellKnownTempData.ErrorMessage] = "Failed to create mint quote";
                    return RedirectToAction("TopUp");
                }

                // Pay invoice
                var payResult = await lightningClient.Pay(mintQuote.Request, CancellationToken.None);
                if (payResult.Result != BTCPayServer.Lightning.PayResult.Ok)
                {
                    TempData[WellKnownTempData.ErrorMessage] = $"Lightning payment failed: {payResult.Result}";
                    return RedirectToAction("TopUp");
                }

                // Poll for quote completion
                var quoteId = mintQuote.Quote;
                if (string.IsNullOrEmpty(quoteId))
                {
                    TempData[WellKnownTempData.ErrorMessage] = "Mint quote ID is empty";
                    return RedirectToAction("TopUp");
                }

                for (int i = 0; i < 30; i++)
                {
                    await Task.Delay(1000);
                    var checkResult = await wallet.CheckMintQuote(quoteId, CancellationToken.None);
                    
                    var paidProperty = checkResult.GetType().GetProperty("Paid");
                    var paid = paidProperty?.GetValue(checkResult) as bool?;
                    
                    if (paid == true)
                    {
                        var proofsProperty = checkResult.GetType().GetProperty("Proofs");
                        var proofs = proofsProperty?.GetValue(checkResult) as Array;
                        
                        if (proofs != null && proofs.Length > 0)
                        {
                            var proofsToStore = new List<DotNut.Proof>();
                            foreach (var proof in proofs)
                            {
                                if (proof is DotNut.Proof proofObj)
                                {
                                    proofsToStore.Add(proofObj);
                                }
                            }

                            if (proofsToStore.Count > 0)
                            {
                                var proofStorageService = HttpContext.RequestServices.GetRequiredService<Services.ProofStorageService>();
                                await proofStorageService.AddProofsAsync(proofsToStore, StoreData.Id, mintUrl);
                                
                                TempData[WellKnownTempData.SuccessMessage] = $"Successfully minted {proofsToStore.Count} proofs ({amountSatoshis} sat)";
                                return RedirectToAction("Wallet");
                            }
                        }
                        break;
                    }
                }

                TempData[WellKnownTempData.ErrorMessage] = "Mint quote not paid after polling";
                return RedirectToAction("TopUp");
            }
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Error during Lightning top-up: {ex.Message}";
            return RedirectToAction("TopUp");
        }

        return RedirectToAction("TopUp");
    }

    /// <summary>
    /// POST action for top-up from Cashu token
    /// </summary>
    [HttpPost("TopUpFromToken")]
    public async Task<IActionResult> TopUpFromToken([FromForm] string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            TempData[WellKnownTempData.ErrorMessage] = "Token cannot be empty";
            return RedirectToAction("TopUp");
        }

        try
        {
            var cashuService = HttpContext.RequestServices.GetRequiredService<Services.ICashuService>();
            var result = await cashuService.ReceiveTokenAsync(token, StoreData.Id);
            
            if (result.Success)
            {
                TempData[WellKnownTempData.SuccessMessage] = $"Successfully received token with {result.Amount} sat";
                return RedirectToAction("Wallet");
            }
            else
            {
                TempData[WellKnownTempData.ErrorMessage] = result.ErrorMessage ?? "Failed to receive token";
                return RedirectToAction("TopUp");
            }
        }
        catch (Exception ex)
        {
            TempData[WellKnownTempData.ErrorMessage] = $"Error receiving token: {ex.Message}";
            return RedirectToAction("TopUp");
        }
    }

    private async Task<object?> GetLightningClientForStore(string storeId)
    {
        // Use reflection to get Lightning client (similar to CashuServiceAdapter)
        try
        {
            var store = await _storeRepository.FindStore(storeId);
            if (store == null) return null;

            var lnPaymentType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "LightningPaymentType" && t.Namespace?.Contains("BTCPayServer.Payments") == true);

            if (lnPaymentType == null) return null;

            var getPaymentMethodIdMethod = lnPaymentType.GetMethod("GetPaymentMethodId", new[] { typeof(string) });
            if (getPaymentMethodIdMethod == null) return null;

            var lightningPmi = getPaymentMethodIdMethod.Invoke(null, new object[] { "BTC" });
            if (lightningPmi == null) return null;

            var getPaymentMethodConfigMethod = typeof(StoreData).GetMethod("GetPaymentMethodConfig", 
                new[] { typeof(PaymentMethodId), typeof(PaymentMethodHandlerDictionary) });
            if (getPaymentMethodConfigMethod == null) return null;

            var lightningConfig = getPaymentMethodConfigMethod.Invoke(store, new[] { lightningPmi, _handlers });
            if (lightningConfig == null) return null;

            var lightningClientFactoryServiceType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "LightningClientFactoryService");
            
            var lightningClientFactoryService = lightningClientFactoryServiceType != null
                ? HttpContext.RequestServices.GetService(lightningClientFactoryServiceType)
                : null;

            if (lightningClientFactoryService == null) return null;

            var createLightningClientMethod = lightningConfig.GetType().GetMethod("CreateLightningClient");
            if (createLightningClientMethod == null) return null;

            var network = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "BTCPayNetworkProvider")
                ?.GetMethod("GetNetwork")?.Invoke(null, new object[] { "BTC" });

            var lightningNetworkOptions = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "LightningNetworkOptions");

            var lightningNetworkOptionsInstance = lightningNetworkOptions != null
                ? HttpContext.RequestServices.GetService(lightningNetworkOptions)
                : null;

            return createLightningClientMethod.Invoke(lightningConfig,
                new[] { network, lightningNetworkOptionsInstance, lightningClientFactoryService });
        }
        catch
        {
            return null;
        }
    }
}
