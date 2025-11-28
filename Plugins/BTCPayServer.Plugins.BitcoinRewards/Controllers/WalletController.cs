#nullable enable
using System;
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

    public WalletController(
        WalletConfigurationService walletConfigurationService,
        ProofStorageService proofStorageService,
        ICashuService cashuService,
        StoreRepository storeRepository,
        Data.BitcoinRewardsPluginDbContextFactory dbContextFactory)
    {
        _walletConfigurationService = walletConfigurationService;
        _proofStorageService = proofStorageService;
        _cashuService = cashuService as CashuServiceAdapter ?? throw new ArgumentException("CashuService must be CashuServiceAdapter");
        _storeRepository = storeRepository;
        _dbContextFactory = dbContextFactory;
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
            Enabled = config?.Enabled ?? true
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

        if (string.IsNullOrWhiteSpace(viewModel.MintUrl))
        {
            ModelState.AddModelError(nameof(viewModel.MintUrl), "Mint URL is required");
            return View(viewModel);
        }

        var result = await _walletConfigurationService.SetMintUrlAsync(storeId, viewModel.MintUrl.Trim(), "sat", viewModel.Enabled);
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
        
        // Get Lightning balance (if available)
        long lightningBalance = await _cashuService.GetLightningBalanceAsync(storeId);

        // Create available balances list (matching Cashu plugin structure)
        var availableBalances = new List<(string Mint, string Unit, ulong Amount)>
        {
            (config.MintUrl, config.Unit, config.Balance)
        };

        // Get exported tokens
        var exportedTokens = await db.ExportedTokens
            .Where(et => et.StoreId == storeId)
            .OrderByDescending(et => et.CreatedAt)
            .ToListAsync();

        var viewModel = new WalletViewModel
        {
            StoreId = storeId,
            MintUrl = config.MintUrl,
            EcashBalance = config.Balance,
            LightningBalance = lightningBalance,
            Unit = config.Unit,
            AvailableBalances = availableBalances,
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

        // Export token
        var result = await _cashuService.ExportTokenAsync(storeId, config.MintUrl);
        if (result.Success && !string.IsNullOrEmpty(result.Token))
        {
            var viewModel = new ExportedTokenViewModel
            {
                Token = result.Token,
                Amount = result.Amount,
                FormatedAmount = $"{result.Amount} sat",
                MintAddress = config.MintUrl,
                Unit = config.Unit
            };

            return View("ExportedToken", viewModel);
        }
        else
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Severity = StatusMessageModel.StatusSeverity.Error,
                Message = result.ErrorMessage ?? "Failed to export token. Check logs for details."
            });
            return RedirectToAction(nameof(Wallet), new { storeId });
        }
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
}

