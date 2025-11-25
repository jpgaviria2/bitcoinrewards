#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BTCPayServer.Plugins.BitcoinRewards;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIBitcoinRewardsController : Controller
{
    private readonly StoreRepository _storeRepository;
    private readonly BitcoinRewardsRepository _rewardsRepository;
    private readonly ICashuService _cashuService;

    public UIBitcoinRewardsController(
        StoreRepository storeRepository,
        BitcoinRewardsRepository rewardsRepository,
        ICashuService cashuService)
    {
        _storeRepository = storeRepository;
        _rewardsRepository = rewardsRepository;
        _cashuService = cashuService;
    }

    [HttpGet]
    [Route("plugins/bitcoin-rewards/{storeId}/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> EditSettings(string storeId)
    {
        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            storeId, 
            BitcoinRewardsStoreSettings.SettingsName) ?? new BitcoinRewardsStoreSettings();
        
        var vm = new BitcoinRewardsSettingsViewModel
        {
            StoreId = storeId
        };
        vm.SetFromSettings(settings);
        
        ViewData.SetActivePage("BitcoinRewards", "Bitcoin Rewards Settings", "BitcoinRewards");
        return View("EditSettings", vm);
    }

    [HttpPost]
    [Route("plugins/bitcoin-rewards/{storeId}/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> EditSettings(string storeId, BitcoinRewardsSettingsViewModel vm, [FromForm] string? command = null)
    {
        if (command == "Save")
        {
            if (!ModelState.IsValid)
            {
                ViewData.SetActivePage("BitcoinRewards", "Bitcoin Rewards Settings", "BitcoinRewards");
                return View("EditSettings", vm);
            }

            // Validate that at least one platform is enabled
            if (!vm.EnableShopify && !vm.EnableSquare)
            {
                ModelState.AddModelError("", "Please enable at least one platform (Shopify or Square)");
                ViewData.SetActivePage("BitcoinRewards", "Bitcoin Rewards Settings", "BitcoinRewards");
                return View("EditSettings", vm);
            }

            // Validate platform-specific credentials
            if (vm.EnableShopify && string.IsNullOrWhiteSpace(vm.ShopifyAccessToken))
            {
                ModelState.AddModelError(nameof(vm.ShopifyAccessToken), "Shopify access token is required when Shopify is enabled");
                ViewData.SetActivePage("BitcoinRewards", "Bitcoin Rewards Settings", "BitcoinRewards");
                return View("EditSettings", vm);
            }

            if (vm.EnableSquare)
            {
                if (string.IsNullOrWhiteSpace(vm.SquareApplicationId) || 
                    string.IsNullOrWhiteSpace(vm.SquareAccessToken) ||
                    string.IsNullOrWhiteSpace(vm.SquareLocationId))
                {
                    ModelState.AddModelError("", "Square Application ID, Access Token, and Location ID are required when Square is enabled");
                    ViewData.SetActivePage("BitcoinRewards", "Bitcoin Rewards Settings", "BitcoinRewards");
                    return View("EditSettings", vm);
                }
            }

            var settings = vm.ToSettings();
            await _storeRepository.UpdateSetting(storeId, BitcoinRewardsStoreSettings.SettingsName, settings);
            
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Bitcoin Rewards settings saved successfully",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            
            return RedirectToAction(nameof(EditSettings), new { storeId });
        }
        
        if (command == "Reset")
        {
            await _storeRepository.UpdateSetting<BitcoinRewardsStoreSettings>(
                storeId, 
                BitcoinRewardsStoreSettings.SettingsName, 
                new BitcoinRewardsStoreSettings());
            
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Bitcoin Rewards settings reset",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
            
            return RedirectToAction(nameof(EditSettings), new { storeId });
        }

        // GET fallback
        return await EditSettings(storeId);
    }

    [HttpGet]
    [Route("plugins/bitcoin-rewards/{storeId}/history")]
    [Authorize(Policy = Policies.CanViewStoreSettings)]
    public async Task<IActionResult> RewardsHistory(string storeId, int? page, RewardStatus? status, RewardPlatform? platform, DateTime? dateFrom, DateTime? dateTo)
    {
        var currentPage = page ?? 1;
        var pageSize = 20;
        
        var (rewards, totalCount) = await _rewardsRepository.GetRewardsAsync(
            storeId,
            currentPage,
            pageSize,
            status,
            platform,
            dateFrom,
            dateTo);

        var vm = new BitcoinRewardHistoryViewModel
        {
            StoreId = storeId,
            CurrentPage = currentPage,
            PageSize = pageSize,
            FilterStatus = status,
            FilterPlatform = platform,
            FilterDateFrom = dateFrom,
            FilterDateTo = dateTo,
            Rewards = rewards.Select(r => new BitcoinRewardItem
            {
                Id = r.Id,
                Platform = r.Platform,
                TransactionId = r.TransactionId,
                OrderId = r.OrderId,
                CustomerEmail = r.CustomerEmail,
                CustomerPhone = r.CustomerPhone,
                TransactionAmount = r.TransactionAmount,
                Currency = r.Currency,
                RewardAmount = r.RewardAmount,
                RewardAmountSatoshis = r.RewardAmountSatoshis,
                Status = r.Status,
                CreatedAt = r.CreatedAt,
                SentAt = r.SentAt,
                RedeemedAt = r.RedeemedAt,
                ExpiresAt = r.ExpiresAt,
                ErrorMessage = r.ErrorMessage
            }).ToList(),
            TotalCount = totalCount
        };
        
        ViewData.SetActivePage("BitcoinRewards", "Rewards History", "BitcoinRewards");
        return View("RewardsHistory", vm);
    }

    [HttpPost]
    [Route("plugins/bitcoin-rewards/{storeId}/history/reclaim/{rewardId}")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> ReclaimToken(string storeId, Guid rewardId)
    {
        var reward = await _rewardsRepository.GetRewardAsync(rewardId, storeId);
        if (reward == null)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Reward not found",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(RewardsHistory), new { storeId });
        }

        if (reward.Status != RewardStatus.Expired && reward.Status != RewardStatus.Pending)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Only expired or pending rewards can be reclaimed",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(RewardsHistory), new { storeId });
        }

        if (string.IsNullOrEmpty(reward.EcashToken))
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Reward has no ecash token to reclaim",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(RewardsHistory), new { storeId });
        }

        var reclaimed = await _cashuService.ReclaimTokenAsync(reward.EcashToken, storeId);
        if (reclaimed)
        {
            reward.Status = RewardStatus.Reclaimed;
            await _rewardsRepository.UpdateRewardAsync(reward);
            
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Token reclaimed successfully",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Failed to reclaim token",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
        }
        
        return RedirectToAction(nameof(RewardsHistory), new { storeId });
    }
}

