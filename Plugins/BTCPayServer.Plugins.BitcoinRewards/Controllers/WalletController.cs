#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DotNut;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("stores/{storeId}/bitcoin-rewards")]
public class WalletController : Controller
{
    private readonly WalletConfigurationService _walletConfigurationService;
    private readonly ProofStorageService _proofStorageService;
    private readonly CashuServiceAdapter _cashuService;
    private readonly StoreRepository _storeRepository;
    private readonly Data.BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly ILogger<WalletController> _logger;

    public WalletController(
        WalletConfigurationService walletConfigurationService,
        ProofStorageService proofStorageService,
        ICashuService cashuService,
        StoreRepository storeRepository,
        Data.BitcoinRewardsPluginDbContextFactory dbContextFactory,
        ILogger<WalletController> logger)
    {
        _walletConfigurationService = walletConfigurationService;
        _proofStorageService = proofStorageService;
        _cashuService = cashuService as CashuServiceAdapter ?? throw new ArgumentException("CashuService must be CashuServiceAdapter");
        _storeRepository = storeRepository;
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Configure wallet - root route (matching Cashu plugin StoreConfig)
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Configure(string storeId)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
        {
            return NotFound();
        }

        var config = await _walletConfigurationService.GetConfigurationAsync(storeId);
        var viewModel = new WalletConfigurationViewModel
        {
            StoreId = storeId,
            MintUrl = config?.MintUrl ?? string.Empty,
            Enabled = config?.Enabled ?? false
        };

        return View(viewModel);
    }

    [HttpPost]
    public async Task<IActionResult> Configure(string storeId, WalletConfigurationViewModel viewModel)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(viewModel);
        }

        // Get existing configuration to preserve MintUrl when disabling
        var existingConfig = await _walletConfigurationService.GetConfigurationAsync(storeId);
        
        // If wallet is enabled, MintUrl is required
        if (viewModel.Enabled && string.IsNullOrWhiteSpace(viewModel.MintUrl))
        {
            ModelState.AddModelError(nameof(viewModel.MintUrl), "Mint URL is required when wallet is enabled");
            return View(viewModel);
        }

        // If wallet is being disabled but we have an existing MintUrl, preserve it
        // If wallet is enabled, use the provided MintUrl
        string mintUrlToSave;
        if (viewModel.Enabled)
        {
            mintUrlToSave = viewModel.MintUrl?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(mintUrlToSave))
            {
                ModelState.AddModelError(nameof(viewModel.MintUrl), "Mint URL is required when wallet is enabled");
                return View(viewModel);
            }
        }
        else
        {
            // Wallet is disabled - preserve existing MintUrl if available
            mintUrlToSave = existingConfig?.MintUrl ?? viewModel.MintUrl?.Trim() ?? string.Empty;
            // Allow saving disabled state even without MintUrl (user might disable before configuring)
            if (string.IsNullOrWhiteSpace(mintUrlToSave))
            {
                // Only update the Enabled flag without requiring MintUrl
                var updateResult = await _walletConfigurationService.UpdateEnabledAsync(storeId, viewModel.Enabled);
                if (updateResult.Success)
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Success,
                        Message = "Wallet configuration updated successfully"
                    });
                    return RedirectToAction(nameof(Configure), new { storeId });
                }
                else
                {
                    TempData.SetStatusMessageModel(new StatusMessageModel
                    {
                        Severity = StatusMessageModel.StatusSeverity.Error,
                        Message = updateResult.ErrorMessage ?? "Failed to update wallet configuration"
                    });
                    return View(viewModel);
                }
            }
        }

        var result = await _walletConfigurationService.SetMintUrlAsync(storeId, mintUrlToSave, "sat", viewModel.Enabled);
        if (result.Success)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = "Mint URL configured successfully"
            });
            return RedirectToAction(nameof(Configure), new { storeId });
        }
        else
        {
            var errorMessage = result.ErrorMessage ?? "Failed to save mint URL configuration";
            // Add to both TempData (for status message) and ModelState (for validation summary)
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = errorMessage
            });
            ModelState.AddModelError("", errorMessage);
            return View(viewModel);
        }
    }

    /// <summary>
    /// Wallet view - shows balances and export options (matching Cashu plugin CashuWallet)
    /// </summary>
    [HttpGet("wallet")]
    public async Task<IActionResult> Wallet(string storeId)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
        {
            return NotFound();
        }

        var config = await _walletConfigurationService.GetConfigurationAsync(storeId);
        if (config == null)
        {
            // No configuration yet - redirect to configure
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Message = "Please configure mint URL first"
            });
            return RedirectToAction(nameof(Configure), new { storeId });
        }

        await using var db = _dbContextFactory.CreateContext();

        // Get all mints for this store (matching Cashu plugin pattern)
        var mints = await db.Mints
            .Where(m => m.StoreId == storeId && m.IsActive)
            .Select(m => m.Url)
            .Distinct()
            .ToListAsync();
            
        var proofsWithUnits = new List<(string Mint, string Unit, ulong Amount)>();
        var unavailableMints = new List<string>();
        
        // For each mint, get keysets and validate proofs (matching Cashu plugin)
        foreach (var mint in mints)
        {
            try
            { 
                var cashuHttpClient = CashuAbstractions.CashuUtils.GetCashuHttpClient(mint); 
                var keysetsResponse = await cashuHttpClient.GetKeysets();
                var keysets = keysetsResponse.Keysets;

               var localProofs = await db.Proofs
                   .Where(p => keysets.Select(k => k.Id).Contains(p.Id) &&
                               p.StoreId == storeId &&
                               p.MintUrl == mint &&
                                 !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)
                                     )).ToListAsync();
               
                foreach (var proof in localProofs)
                {
                    var matchingKeyset = keysets.FirstOrDefault(k => k.Id == proof.Id);
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
        
        var exportedTokens = await db.ExportedTokens
            .Where(et => et.StoreId == storeId)
            .OrderByDescending(et => et.CreatedAt)
            .ToListAsync();
            
        if (unavailableMints.Any())
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = $"Couldn't load {unavailableMints.Count} mints: {string.Join(", ", unavailableMints)}"
            });
        }
        
        var viewModel = new WalletViewModel
        {
            StoreId = storeId,
            MintUrl = config.MintUrl,
            EcashBalance = config.Balance,
            LightningBalance = 0, // We don't show Lightning balance in wallet view like Cashu
            Unit = config.Unit,
            AvailableBalances = groupedProofs,
            ExportedTokens = exportedTokens
        };

        return View("Index", viewModel);
    }

    [HttpGet("topup")]
    public async Task<IActionResult> TopUp(string storeId)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
        {
            return NotFound();
        }

        var config = await _walletConfigurationService.GetConfigurationAsync(storeId);
        if (config == null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Message = "Please configure mint URL first"
            });
            return RedirectToAction(nameof(Configure), new { storeId });
        }

        var viewModel = new TopUpViewModel
        {
            StoreId = storeId,
            MintUrl = config.MintUrl,
            CurrentBalance = config.Balance
        };

        return View(viewModel);
    }

    [HttpPost("topup/lightning")]
    public async Task<IActionResult> TopUpFromLightning(string storeId, [FromForm] ulong amount)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
        {
            return NotFound();
        }

        if (amount == 0)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = "Amount must be greater than 0"
            });
            return RedirectToAction(nameof(TopUp), new { storeId });
        }

        var config = await _walletConfigurationService.GetConfigurationAsync(storeId);
        if (config == null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Message = "Please configure mint URL first"
            });
            return RedirectToAction(nameof(Configure), new { storeId });
        }

        // Mint from Lightning
        var token = await _cashuService.MintTokenAsync((long)amount, storeId);
        if (token != null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = $"Successfully minted {amount} sat from Lightning"
            });
        }
        else
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = "Failed to mint from Lightning. Check logs for details."
            });
        }

        return RedirectToAction(nameof(Wallet), new { storeId });
    }

    [HttpPost("topup/token")]
    public async Task<IActionResult> TopUpFromToken(string storeId, [FromForm] string token)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = "Token cannot be empty"
            });
            return RedirectToAction(nameof(TopUp), new { storeId });
        }

        var config = await _walletConfigurationService.GetConfigurationAsync(storeId);
        if (config == null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Message = "Please configure mint URL first"
            });
            return RedirectToAction(nameof(Configure), new { storeId });
        }

        // Receive the token
        var result = await _cashuService.ReceiveTokenAsync(token.Trim(), storeId);
        if (result.Success)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Success,
                Message = $"Successfully received Cashu token! Added {result.Amount} sat to your wallet."
            });
        }
        else
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = result.ErrorMessage ?? "Failed to receive token. Check logs for details."
            });
        }

        return RedirectToAction(nameof(Wallet), new { storeId });
    }

    /// <summary>
    /// Export token by specific mint and unit (matching Cashu plugin ExportMintBalance)
    /// </summary>
    [HttpPost("ExportMintBalance")]
    public async Task<IActionResult> ExportMintBalance(string storeId, string mintUrl, string unit)
    {
        if (string.IsNullOrWhiteSpace(mintUrl) || string.IsNullOrWhiteSpace(unit))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = "Invalid mint or unit provided!"
            });
            return RedirectToAction(nameof(Wallet), new { storeId });
        }

        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
        {
            return NotFound();
        }

        await using var db = _dbContextFactory.CreateContext();
        
        // Get keysets from mint (matching Cashu plugin)
        List<DotNut.ApiModels.GetKeysetsResponse.KeysetItemResponse> keysets;
        try
        {
            var cashuHttpClient = CashuAbstractions.CashuUtils.GetCashuHttpClient(mintUrl);
            var keysetsResponse = await cashuHttpClient.GetKeysets();
            keysets = keysetsResponse.Keysets.ToList();
            if (keysets == null || keysets.Count == 0)
            {
                throw new Exception("No keysets were found.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Couldn't get keysets for mint {MintUrl}", mintUrl);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = "Couldn't get keysets!"
            });
            return RedirectToAction(nameof(Wallet), new { storeId });
        }

        // Select proofs that match the keysets and are not in failed transactions
        var selectedProofs = await db.Proofs
            .Where(p => p.StoreId == storeId 
                && p.MintUrl == mintUrl
                && keysets.Select(k => k.Id).Contains(p.Id) 
                && !db.FailedTransactions.Any(ft => ft.UsedProofs.Contains(p)))
            .ToListAsync();

        if (selectedProofs.Count == 0)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Message = "No proofs available to export for this mint and unit"
            });
            return RedirectToAction(nameof(Wallet), new { storeId });
        }

        // Create token from selected proofs
        var createdToken = new DotNut.CashuToken()
        {
            Tokens =
            [
                new DotNut.CashuToken.Token
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

        // Get all proofs for this mint to remove (not just selected ones)
        var proofsToRemove = await db.Proofs
            .Where(p => p.StoreId == storeId 
                && p.MintUrl == mintUrl
                && keysets.Select(k => k.Id).Contains(p.Id))
            .ToListAsync();

        var exportedTokenEntity = new Data.Models.ExportedToken
        {
            SerializedToken = serializedToken,
            Amount = tokenAmount,
            Unit = unit,
            Mint = mintUrl,
            StoreId = storeId,
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
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Failed to export token for store {StoreId}", storeId);
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Severity = StatusMessageModel.StatusSeverity.Error,
                    Message = "Couldn't export token"
                });
            }
        });
        
        return RedirectToAction(nameof(ExportedToken), new { tokenId = exportedTokenEntity.Id });
    }

    [HttpPost("export")]
    public async Task<IActionResult> ExportToken(string storeId)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
        {
            return NotFound();
        }

        var config = await _walletConfigurationService.GetConfigurationAsync(storeId);
        if (config == null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Message = "Please configure mint URL first"
            });
            return RedirectToAction(nameof(Configure), new { storeId });
        }

        if (config.Balance == 0)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Warning,
                Message = "No balance available to export"
            });
            return RedirectToAction(nameof(Wallet), new { storeId });
        }

        // Export token by mint and unit (use ExportMintBalance)
        return await ExportMintBalance(storeId, config.MintUrl, config.Unit);
    }

    /// <summary>
    /// View exported token by ID (matching Cashu plugin)
    /// </summary>
    [HttpGet("/Token")]
    public async Task<IActionResult> ExportedToken(Guid tokenId)
    {
        await using var db = _dbContextFactory.CreateContext();
        
        var exportedToken = db.ExportedTokens.SingleOrDefault(e => e.Id == tokenId);
        if (exportedToken == null)
        {
            return BadRequest("Can't find token with provided GUID");
        }

        // Note: We could check token state here like Cashu plugin does, but for now just display
        var model = new ExportedTokenViewModel
        {
            Amount = exportedToken.Amount,
            Unit = exportedToken.Unit,
            MintAddress = exportedToken.Mint,
            Token = exportedToken.SerializedToken,
            FormatedAmount = $"{exportedToken.Amount} {exportedToken.Unit}"
        };
        
        return View(model);
    }

    /// <summary>
    /// Failed Transactions page (matching Cashu plugin)
    /// </summary>
    [HttpGet("FailedTransactions")]
    public async Task<IActionResult> FailedTransactions(string storeId)
    {
        var store = await _storeRepository.FindStore(storeId);
        if (store == null)
        {
            return NotFound();
        }

        await using var db = _dbContextFactory.CreateContext();
        //fetch recently failed transactions 
        var failedTransactions = await db.FailedTransactions
            .Where(ft => ft.StoreId == storeId)
            .Include(ft => ft.UsedProofs)
            .ToListAsync();
            
        return View(failedTransactions);
    }
}

