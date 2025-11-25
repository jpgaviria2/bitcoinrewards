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
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIBitcoinRewardsController : Controller
{
    private readonly StoreRepository _storeRepository;
    private readonly BitcoinRewardsRepository _rewardsRepository;
    private readonly ICashuService _cashuService;
    private readonly ILogger<UIBitcoinRewardsController> _logger;

    public UIBitcoinRewardsController(
        StoreRepository storeRepository,
        BitcoinRewardsRepository rewardsRepository,
        ICashuService cashuService,
        ILogger<UIBitcoinRewardsController> logger)
    {
        _storeRepository = storeRepository;
        _rewardsRepository = rewardsRepository;
        _cashuService = cashuService;
        _logger = logger;
    }

    [HttpGet]
    [Route("plugins/bitcoin-rewards/{storeId}/settings")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> EditSettings(string storeId)
    {
        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            storeId, 
            BitcoinRewardsStoreSettings.SettingsName);
        
        _logger.LogDebug("Loading settings for store {StoreId}: Enabled={Enabled}", storeId, settings?.Enabled ?? false);
        
        var vm = new BitcoinRewardsSettingsViewModel
        {
            StoreId = storeId
        };
        
        if (settings != null)
        {
            vm.SetFromSettings(settings);
        }
        
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
            // For checkboxes in ASP.NET Core, if unchecked they don't send a value
            // Read checkbox values directly from form to ensure correct binding
            vm.Enabled = Request.Form.ContainsKey("Enabled") && Request.Form["Enabled"].ToString() == "true";
            vm.EnableShopify = Request.Form.ContainsKey("EnableShopify") && Request.Form["EnableShopify"].ToString() == "true";
            vm.EnableSquare = Request.Form.ContainsKey("EnableSquare") && Request.Form["EnableSquare"].ToString() == "true";
            
            // Log what we received from the form
            _logger.LogInformation("POST EditSettings for store {StoreId}: Enabled={Enabled}, Percentage={Percentage}, EnableShopify={EnableShopify}, EnableSquare={EnableSquare}", 
                storeId, vm.Enabled, vm.RewardPercentage, vm.EnableShopify, vm.EnableSquare);
            
            if (!ModelState.IsValid)
            {
                ViewData.SetActivePage("BitcoinRewards", "Bitcoin Rewards Settings", "BitcoinRewards");
                return View("EditSettings", vm);
            }

            // Only validate platform credentials if platforms are enabled
            // Allow plugin to be enabled without platforms configured (for manual testing)
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

            _logger.LogInformation("Saving settings for store {StoreId}: Enabled={Enabled}, Percentage={Percentage}, ViewModel.Enabled={VmEnabled}", 
                storeId, vm.Enabled, vm.RewardPercentage, vm.Enabled);
            
            var settings = vm.ToSettings();
            
            _logger.LogInformation("Converted to settings: Enabled={Enabled}, Percentage={Percentage}", 
                settings.Enabled, settings.RewardPercentage);
            
            await _storeRepository.UpdateSetting(storeId, BitcoinRewardsStoreSettings.SettingsName, settings);
            
            // Verify settings were saved
            var savedSettings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
                storeId, 
                BitcoinRewardsStoreSettings.SettingsName);
            
            _logger.LogInformation("Verified saved settings for store {StoreId}: Enabled={Enabled}", 
                storeId, savedSettings?.Enabled ?? false);
            
            if (savedSettings == null || savedSettings.Enabled != settings.Enabled)
            {
                _logger.LogWarning("Settings persistence issue: Expected Enabled={Expected}, Got={Actual}", 
                    settings.Enabled, savedSettings?.Enabled ?? false);
            }
            
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
        
        System.Collections.Generic.List<BitcoinRewardRecord> rewards;
        int totalCount;
        
        try
        {
            (rewards, totalCount) = await _rewardsRepository.GetRewardsAsync(
                storeId,
                currentPage,
                pageSize,
                status,
                platform,
                dateFrom,
                dateTo);
        }
        catch (Exception)
        {
            // Database table might not exist yet - show empty list
            rewards = new System.Collections.Generic.List<BitcoinRewardRecord>();
            totalCount = 0;
        }

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

    [HttpGet]
    [Route("plugins/bitcoin-rewards/{storeId}/test/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> CreateTestReward(string storeId)
    {
        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            storeId, 
            BitcoinRewardsStoreSettings.SettingsName);
        
        if (settings == null || !settings.Enabled)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Bitcoin Rewards must be enabled to create test rewards",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(EditSettings), new { storeId });
        }

        ViewData.SetActivePage("BitcoinRewards", "Create Test Reward", "BitcoinRewards");
        return View("CreateTestReward", new CreateTestRewardViewModel { StoreId = storeId });
    }

    [HttpPost]
    [Route("plugins/bitcoin-rewards/{storeId}/test/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> CreateTestReward(string storeId, CreateTestRewardViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData.SetActivePage("BitcoinRewards", "Create Test Reward", "BitcoinRewards");
            return View("CreateTestReward", vm);
        }

        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            storeId, 
            BitcoinRewardsStoreSettings.SettingsName);
        
        if (settings == null || !settings.Enabled)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Bitcoin Rewards must be enabled",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
            return RedirectToAction(nameof(EditSettings), new { storeId });
        }

        // Create test transaction data
        var transaction = new Models.TransactionData
        {
            TransactionId = $"TEST_{Guid.NewGuid():N}",
            OrderId = vm.OrderId,
            Amount = vm.TransactionAmount,
            Currency = vm.Currency ?? "USD",
            CustomerEmail = vm.CustomerEmail,
            CustomerPhone = vm.CustomerPhone,
            Platform = vm.Platform,
            TransactionDate = DateTime.UtcNow
        };

        var rewardsService = HttpContext.RequestServices.GetRequiredService<BitcoinRewardsService>();
        var success = await rewardsService.ProcessRewardAsync(storeId, transaction);

        if (success)
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Test reward created successfully",
                Severity = StatusMessageModel.StatusSeverity.Success
            });
        }
        else
        {
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = "Failed to create test reward. Check logs for details.",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
        }

        return RedirectToAction(nameof(RewardsHistory), new { storeId });
    }
}

