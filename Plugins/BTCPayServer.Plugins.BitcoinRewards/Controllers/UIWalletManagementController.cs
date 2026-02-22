#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIWalletManagementController : Controller
{
    private readonly CustomerWalletService _walletService;
    private readonly ILogger<UIWalletManagementController> _logger;

    public UIWalletManagementController(
        CustomerWalletService walletService,
        ILogger<UIWalletManagementController> logger)
    {
        _walletService = walletService;
        _logger = logger;
    }

    [HttpGet("plugins/bitcoin-rewards/{storeId}/wallets")]
    [Authorize(Policy = Policies.CanViewStoreSettings)]
    public async Task<IActionResult> ListWallets(string storeId)
    {
        var wallets = await _walletService.GetStoreWalletsAsync(storeId);
        var vm = new CustomerWalletListViewModel
        {
            StoreId = storeId,
            Wallets = new()
        };

        foreach (var w in wallets)
        {
            var balance = await _walletService.GetBalanceAsync(w.Id);
            vm.Wallets.Add(new CustomerWalletListItem
            {
                Id = w.Id,
                CardUid = w.CardUid,
                CadBalanceCents = w.CadBalanceCents,
                SatsBalance = balance?.SatsBalance ?? 0,
                AutoConvertToCad = w.AutoConvertToCad,
                TotalRewardedCadCents = w.TotalRewardedCadCents,
                TotalRewardedSatoshis = w.TotalRewardedSatoshis,
                LastRewardedAt = w.LastRewardedAt,
                IsActive = w.IsActive,
                CreatedAt = w.CreatedAt
            });
        }

        ViewData.SetActivePage("BitcoinRewards", "Customer Wallets", "BitcoinRewards");
        return View("ListWallets", vm);
    }

    [HttpGet("plugins/bitcoin-rewards/{storeId}/wallets/{walletId}")]
    [Authorize(Policy = Policies.CanViewStoreSettings)]
    public async Task<IActionResult> WalletDetail(string storeId, Guid walletId)
    {
        var wallet = await _walletService.FindByIdAsync(walletId);
        if (wallet == null || wallet.StoreId != storeId)
            return NotFound();

        var balance = await _walletService.GetBalanceAsync(walletId);
        var transactions = await _walletService.GetHistoryAsync(walletId, 100);

        var vm = new CustomerWalletDetailViewModel
        {
            StoreId = storeId,
            WalletId = wallet.Id,
            CardUid = wallet.CardUid,
            BoltcardId = wallet.BoltcardId,
            CadBalanceCents = wallet.CadBalanceCents,
            SatsBalance = balance?.SatsBalance ?? 0,
            AutoConvertToCad = wallet.AutoConvertToCad,
            TotalRewardedSatoshis = wallet.TotalRewardedSatoshis,
            TotalRewardedCadCents = wallet.TotalRewardedCadCents,
            IsActive = wallet.IsActive,
            CreatedAt = wallet.CreatedAt,
            LastRewardedAt = wallet.LastRewardedAt,
            ApiTokenHash = wallet.ApiTokenHash,
            PullPaymentId = wallet.PullPaymentId,
            Transactions = transactions.Select(t => new WalletTransactionItem
            {
                Id = t.Id,
                Type = t.Type,
                SatsAmount = t.SatsAmount,
                CadCentsAmount = t.CadCentsAmount,
                ExchangeRate = t.ExchangeRate,
                Reference = t.Reference,
                CreatedAt = t.CreatedAt
            }).ToList()
        };

        ViewData.SetActivePage("BitcoinRewards", "Wallet Detail", "BitcoinRewards");
        return View("WalletDetail", vm);
    }

    [HttpGet("plugins/bitcoin-rewards/{storeId}/wallets/{walletId}/adjust")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> AdjustBalance(string storeId, Guid walletId)
    {
        var wallet = await _walletService.FindByIdAsync(walletId);
        if (wallet == null || wallet.StoreId != storeId)
            return NotFound();

        var balance = await _walletService.GetBalanceAsync(walletId);

        var vm = new AdjustBalanceViewModel
        {
            StoreId = storeId,
            WalletId = walletId,
            CardUid = wallet.CardUid,
            CurrentCadBalanceCents = wallet.CadBalanceCents,
            CurrentSatsBalance = balance?.SatsBalance ?? 0
        };

        ViewData.SetActivePage("BitcoinRewards", "Adjust Balance", "BitcoinRewards");
        return View("AdjustBalance", vm);
    }

    [HttpPost("plugins/bitcoin-rewards/{storeId}/wallets/{walletId}/adjust")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> AdjustBalance(string storeId, Guid walletId, AdjustBalanceViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData.SetActivePage("BitcoinRewards", "Adjust Balance", "BitcoinRewards");
            return View("AdjustBalance", vm);
        }

        var wallet = await _walletService.FindByIdAsync(walletId);
        if (wallet == null || wallet.StoreId != storeId)
            return NotFound();

        var multiplier = vm.Direction == "debit" ? -1L : 1L;
        var satsAmount = vm.BalanceType == "sats" ? vm.Amount * multiplier : 0;
        var cadAmount = vm.BalanceType == "cad" ? vm.Amount * multiplier : 0;

        var success = await _walletService.AdjustAsync(walletId, satsAmount, cadAmount, vm.Reason);

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = success ? "Balance adjusted successfully" : "Failed to adjust balance",
            Severity = success ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Error
        });

        return RedirectToAction(nameof(WalletDetail), new { storeId, walletId });
    }

    [HttpPost("plugins/bitcoin-rewards/{storeId}/wallets/{walletId}/spend")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> SpendCad(string storeId, Guid walletId, SpendCadViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            var wallet = await _walletService.FindByIdAsync(walletId);
            if (wallet == null || wallet.StoreId != storeId)
                return NotFound();
            vm.CurrentCadBalanceCents = wallet.CadBalanceCents;
            ViewData.SetActivePage("BitcoinRewards", "Wallet Detail", "BitcoinRewards");
            // Redirect back to detail with error
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Invalid spend amount",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(WalletDetail), new { storeId, walletId });
        }

        var cadCents = (long)(vm.AmountCad * 100);
        var (success, error) = await _walletService.SpendCadAsync(walletId, cadCents, vm.Reference);

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = success ? $"Spent CA${vm.AmountCad:F2} successfully" : $"Failed: {error}",
            Severity = success ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Error
        });

        return RedirectToAction(nameof(WalletDetail), new { storeId, walletId });
    }

    [HttpPost("plugins/bitcoin-rewards/{storeId}/wallets/{walletId}/toggle-autoconvert")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> ToggleAutoConvert(string storeId, Guid walletId, bool autoConvert)
    {
        var wallet = await _walletService.FindByIdAsync(walletId);
        if (wallet == null || wallet.StoreId != storeId)
            return NotFound();

        await _walletService.SetAutoConvertAsync(walletId, autoConvert);

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = $"Auto-convert {(autoConvert ? "enabled" : "disabled")}",
            Severity = StatusMessageModel.StatusSeverity.Success
        });

        return RedirectToAction(nameof(WalletDetail), new { storeId, walletId });
    }

    [HttpPost("plugins/bitcoin-rewards/{storeId}/wallets/{walletId}/regenerate-token")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> RegenerateToken(string storeId, Guid walletId)
    {
        var wallet = await _walletService.FindByIdAsync(walletId);
        if (wallet == null || wallet.StoreId != storeId)
            return NotFound();

        var token = await _walletService.GenerateWalletTokenAsync(walletId);

        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = token != null ? "Token regenerated. New token (copy now, won't be shown again): " + token : "Failed to regenerate token",
            Severity = token != null ? StatusMessageModel.StatusSeverity.Success : StatusMessageModel.StatusSeverity.Error
        });

        return RedirectToAction(nameof(WalletDetail), new { storeId, walletId });
    }

    [HttpPost("plugins/bitcoin-rewards/{storeId}/wallets/{walletId}/deactivate")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> Deactivate(string storeId, Guid walletId)
    {
        // Simple deactivation - just set IsActive = false via a direct adjust or dedicated method
        // For now, redirect with message - would need a dedicated service method
        TempData.SetStatusMessageModel(new StatusMessageModel
        {
            Message = "Wallet deactivation not yet implemented",
            Severity = StatusMessageModel.StatusSeverity.Warning
        });

        return RedirectToAction(nameof(ListWallets), new { storeId });
    }
}
