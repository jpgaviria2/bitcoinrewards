#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[Authorize(Policy = Policies.CanModifyStoreSettings, AuthenticationSchemes = AuthenticationSchemes.Cookie)]
[Route("stores/{storeId}/bitcoin-rewards/wallet")]
public class WalletController : Controller
{
    private readonly WalletConfigurationService _walletConfigurationService;
    private readonly ProofStorageService _proofStorageService;
    private readonly CashuServiceAdapter _cashuService;
    private readonly StoreRepository _storeRepository;

    public WalletController(
        WalletConfigurationService walletConfigurationService,
        ProofStorageService proofStorageService,
        ICashuService cashuService,
        StoreRepository storeRepository)
    {
        _walletConfigurationService = walletConfigurationService;
        _proofStorageService = proofStorageService;
        _cashuService = cashuService as CashuServiceAdapter ?? throw new ArgumentException("CashuService must be CashuServiceAdapter");
        _storeRepository = storeRepository;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string storeId)
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
            return RedirectToAction(nameof(Configure), new { storeId });
        }

        // Get Lightning balance (if available)
        long lightningBalance = await _cashuService.GetLightningBalanceAsync(storeId);

        var viewModel = new WalletViewModel
        {
            StoreId = storeId,
            MintUrl = config.MintUrl,
            EcashBalance = config.Balance,
            LightningBalance = lightningBalance,
            Unit = config.Unit
        };

        return View(viewModel);
    }

    [HttpGet("configure")]
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

    [HttpPost("configure")]
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
            return RedirectToAction(nameof(Index), new { storeId });
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

        return RedirectToAction(nameof(Index), new { storeId });
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

        return RedirectToAction(nameof(Index), new { storeId });
    }
}

