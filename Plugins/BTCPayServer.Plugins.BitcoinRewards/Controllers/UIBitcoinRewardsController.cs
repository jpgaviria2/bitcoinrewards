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
using BTCPayServer.Data;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIBitcoinRewardsController : Controller
{
    private readonly StoreRepository _storeRepository;
    private readonly BitcoinRewardsRepository _rewardsRepository;
    private readonly PayoutProcessorDiscoveryService _payoutProcessorDiscoveryService;
    private readonly ILogger<UIBitcoinRewardsController> _logger;

    public UIBitcoinRewardsController(
        StoreRepository storeRepository,
        BitcoinRewardsRepository rewardsRepository,
        PayoutProcessorDiscoveryService payoutProcessorDiscoveryService,
        ILogger<UIBitcoinRewardsController> logger)
    {
        _storeRepository = storeRepository;
        _rewardsRepository = rewardsRepository;
        _payoutProcessorDiscoveryService = payoutProcessorDiscoveryService;
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
        
        // Get configured payout processors
        vm.AvailablePayoutProcessors = await _payoutProcessorDiscoveryService.GetConfiguredPayoutProcessorsAsync(storeId);
        
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
            // For checkboxes with hidden inputs, Request.Form may contain multiple values
            // Check if "true" is in the collection (checkbox value) vs just "false" (hidden input)
            var enabledValues = Request.Form["Enabled"];
            vm.Enabled = enabledValues.Count > 0 && enabledValues.Contains("true");
            
            var enableShopifyValues = Request.Form["EnableShopify"];
            vm.EnableShopify = enableShopifyValues.Count > 0 && enableShopifyValues.Contains("true");
            
            var enableSquareValues = Request.Form["EnableSquare"];
            vm.EnableSquare = enableSquareValues.Count > 0 && enableSquareValues.Contains("true");

            var enableBtcpayValues = Request.Form["EnableBtcpay"];
            vm.EnableBtcpay = enableBtcpayValues.Count > 0 && enableBtcpayValues.Contains("true");
            
            // Clear ModelState for checkboxes to use our explicitly read values
            ModelState.Remove(nameof(vm.Enabled));
            ModelState.Remove(nameof(vm.EnableShopify));
            ModelState.Remove(nameof(vm.EnableSquare));
            ModelState.Remove(nameof(vm.EnableBtcpay));
            
            // Log what we received from the form for debugging
            var enabledValuesStr = enabledValues.Count > 0 ? string.Join(",", enabledValues.ToArray()) : "none";
            _logger.LogInformation("POST EditSettings for store {StoreId}: Enabled={Enabled} (form values: {EnabledValues}), ExternalPct={ExternalPct}, BtcpayPct={BtcpayPct}, EnableShopify={EnableShopify}, EnableSquare={EnableSquare}, EnableBtcpay={EnableBtcpay}", 
                storeId, vm.Enabled, enabledValuesStr, vm.ExternalRewardPercentage, vm.BtcpayRewardPercentage, vm.EnableShopify, vm.EnableSquare, vm.EnableBtcpay);
            
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

            var settings = vm.ToSettings();
            
            _logger.LogInformation("Saving settings for store {StoreId}: ViewModel.Enabled={VmEnabled}, Settings.Enabled={SettingsEnabled}, ExternalPct={ExternalPct}, BtcpayPct={BtcpayPct}", 
                storeId, vm.Enabled, settings.Enabled, settings.ExternalRewardPercentage, settings.BtcpayRewardPercentage);
            
            await _storeRepository.UpdateSetting(storeId, BitcoinRewardsStoreSettings.SettingsName, settings);
            
            // Verify settings were saved correctly
            var savedSettings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
                storeId, 
                BitcoinRewardsStoreSettings.SettingsName);
            
            _logger.LogInformation("Verified saved settings for store {StoreId}: Enabled={Enabled}, Percentage={Percentage}", 
                storeId, savedSettings?.Enabled ?? false, savedSettings?.RewardPercentage ?? 0);
            
            if (savedSettings == null)
            {
                _logger.LogError("Settings were not saved - savedSettings is null for store {StoreId}", storeId);
            }
            else if (savedSettings.Enabled != settings.Enabled)
            {
                _logger.LogError("Settings persistence issue for store {StoreId}: Expected Enabled={Expected}, Got={Actual}", 
                    storeId, settings.Enabled, savedSettings.Enabled);
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
                PullPaymentId = r.PullPaymentId,
                ClaimLink = r.ClaimLink,
                PayoutProcessor = r.PayoutProcessor,
                PayoutMethod = r.PayoutMethod,
                ErrorMessage = r.ErrorMessage
            }).ToList(),
            TotalCount = totalCount
        };
        
        ViewData.SetActivePage("BitcoinRewards", "Rewards History", "BitcoinRewards");
        return View("RewardsHistory", vm);
    }


    [HttpGet]
    [Route("plugins/bitcoin-rewards/{storeId}/test/create")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> CreateTestReward(string storeId)
    {
        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            storeId, 
            BitcoinRewardsStoreSettings.SettingsName);
        var store = await _storeRepository.FindStore(storeId);
        var storeCurrency = store?.GetStoreBlob().DefaultCurrency ?? StoreBlob.StandardDefaultCurrency;
        
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
        return View("CreateTestReward", new CreateTestRewardViewModel
        {
            StoreId = storeId,
            Currency = storeCurrency
        });
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

        var store = await _storeRepository.FindStore(storeId);
        var storeCurrency = store?.GetStoreBlob().DefaultCurrency ?? StoreBlob.StandardDefaultCurrency;
        var currency = string.IsNullOrWhiteSpace(vm.Currency)
            ? storeCurrency
            : vm.Currency.Trim().ToUpperInvariant();

        // Create test transaction data
        var transaction = new Models.TransactionData
        {
            TransactionId = $"TEST_{Guid.NewGuid():N}",
            OrderId = vm.OrderId,
            Amount = vm.TransactionAmount,
            Currency = currency,
            CustomerEmail = vm.CustomerEmail,
            CustomerPhone = vm.CustomerPhone,
            Platform = vm.Platform,
            TransactionDate = DateTime.UtcNow
        };

        try
        {
            var rewardsService = HttpContext.RequestServices.GetRequiredService<BitcoinRewardsService>();
            _logger.LogInformation("Creating test reward for store {StoreId}: Amount={Amount}, Currency={Currency}, Platform={Platform}, Email={Email}", 
                storeId, transaction.Amount, transaction.Currency, transaction.Platform, transaction.CustomerEmail);
            
            var success = await rewardsService.ProcessRewardAsync(storeId, transaction);

            if (success)
            {
                _logger.LogInformation("Test reward created successfully for store {StoreId}", storeId);
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "Test reward created successfully",
                    Severity = StatusMessageModel.StatusSeverity.Success
                });
            }
            else
            {
                _logger.LogWarning("Test reward creation failed for store {StoreId} - ProcessRewardAsync returned false", storeId);
                TempData.SetStatusMessageModel(new StatusMessageModel
                {
                    Message = "Failed to create test reward. Check logs for details.",
                    Severity = StatusMessageModel.StatusSeverity.Error
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception occurred while creating test reward for store {StoreId}", storeId);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"Failed to create test reward: {ex.Message}. Check logs for details.",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
        }

        return RedirectToAction(nameof(RewardsHistory), new { storeId });
    }
}

