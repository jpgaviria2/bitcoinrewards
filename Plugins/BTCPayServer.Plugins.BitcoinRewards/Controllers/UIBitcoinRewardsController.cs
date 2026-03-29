#nullable enable
using ServiceErrorStats = BTCPayServer.Plugins.BitcoinRewards.Services.ErrorStatistics;
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
using QRCoder;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie)]
public class UIBitcoinRewardsController : Controller
{
    private readonly StoreRepository _storeRepository;
    private readonly BitcoinRewardsRepository _rewardsRepository;
    private readonly PayoutProcessorDiscoveryService _payoutProcessorDiscoveryService;
    private readonly PullPaymentStatusService _pullPaymentStatusService;
    private readonly ILogger<UIBitcoinRewardsController> _logger;
    private readonly ErrorTrackingService _errorTracking;

    public UIBitcoinRewardsController(
        StoreRepository storeRepository,
        BitcoinRewardsRepository rewardsRepository,
        PayoutProcessorDiscoveryService payoutProcessorDiscoveryService,
        PullPaymentStatusService pullPaymentStatusService,
        ILogger<UIBitcoinRewardsController> logger,
        ErrorTrackingService errorTracking)
    {
        _storeRepository = storeRepository;
        _rewardsRepository = rewardsRepository;
        _payoutProcessorDiscoveryService = payoutProcessorDiscoveryService;
        _pullPaymentStatusService = pullPaymentStatusService;
        _logger = logger;
        _errorTracking = errorTracking;
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
            var existingSettings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
                storeId,
                BitcoinRewardsStoreSettings.SettingsName);

            // For checkboxes with hidden inputs, Request.Form may contain multiple values
            // Check if "true" is in the collection (checkbox value) vs just "false" (hidden input)
            var enabledValues = Request.Form["Enabled"];
            vm.Enabled = enabledValues.Count > 0 && enabledValues.Contains("true");
            
            // Shopify temporarily disabled
            vm.EnableShopify = false;
            
            var enableSquareValues = Request.Form["EnableSquare"];
            vm.EnableSquare = enableSquareValues.Count > 0 && enableSquareValues.Contains("true");

            var enableBtcpayValues = Request.Form["EnableBtcpay"];
            vm.EnableBtcpay = enableBtcpayValues.Count > 0 && enableBtcpayValues.Contains("true");

            var boltCardValues = Request.Form["BoltCardEnabled"];
            vm.BoltCardEnabled = boltCardValues.Count > 0 && boltCardValues.Contains("true");

            var defaultAutoConvertValues = Request.Form["DefaultAutoConvertToCad"];
            vm.DefaultAutoConvertToCad = defaultAutoConvertValues.Count > 0 && defaultAutoConvertValues.Contains("true");
            var swapEnabledValues = Request.Form["SwapEnabled"];
            vm.SwapEnabled = swapEnabledValues.Count > 0 && swapEnabledValues.Contains("true");
            var cadSpendingValues = Request.Form["CadSpendingEnabled"];
            vm.CadSpendingEnabled = cadSpendingValues.Count > 0 && cadSpendingValues.Contains("true");
            
            // Clear ModelState for checkboxes to use our explicitly read values
            ModelState.Remove(nameof(vm.Enabled));
            ModelState.Remove(nameof(vm.EnableShopify));
            ModelState.Remove(nameof(vm.EnableSquare));
            ModelState.Remove(nameof(vm.EnableBtcpay));
            ModelState.Remove(nameof(vm.BoltCardEnabled));
            ModelState.Remove(nameof(vm.DefaultAutoConvertToCad));
            ModelState.Remove(nameof(vm.SwapEnabled));
            ModelState.Remove(nameof(vm.CadSpendingEnabled));
            
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
                var existingSquare = existingSettings?.Square;
                var hasExistingAppId = !string.IsNullOrWhiteSpace(existingSquare?.ApplicationId);
                var hasExistingAccessToken = !string.IsNullOrWhiteSpace(existingSquare?.AccessToken);
                var hasExistingLocationId = !string.IsNullOrWhiteSpace(existingSquare?.LocationId);
                var hasExistingSigKey = !string.IsNullOrWhiteSpace(existingSquare?.WebhookSignatureKey);

                if (string.IsNullOrWhiteSpace(vm.SquareApplicationId) && !hasExistingAppId)
                {
                    ModelState.AddModelError("", "Square Application ID is required when Square is enabled");
                }
                if (string.IsNullOrWhiteSpace(vm.SquareAccessToken) && !hasExistingAccessToken)
                {
                    ModelState.AddModelError("", "Square Access Token is required when Square is enabled");
                }
                if (string.IsNullOrWhiteSpace(vm.SquareLocationId) && !hasExistingLocationId)
                {
                    ModelState.AddModelError("", "Square Location ID is required when Square is enabled");
                }
                if (string.IsNullOrWhiteSpace(vm.SquareWebhookSignatureKey) && !hasExistingSigKey)
                {
                    ModelState.AddModelError("", "Square Webhook Signature Key is required when Square is enabled");
                }

                if (!ModelState.IsValid)
                {
                    ViewData.SetActivePage("BitcoinRewards", "Bitcoin Rewards Settings", "BitcoinRewards");
                    return View("EditSettings", vm);
                }
            }

            var settings = vm.ToSettings(existingSettings);
            
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

    [HttpPost]
    [Route("plugins/bitcoin-rewards/{storeId}/recover-orphaned")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> RecoverOrphanedRewards(string storeId)
    {
        try
        {
            var rewardsService = HttpContext.RequestServices.GetRequiredService<BitcoinRewardsService>();
            var recovered = await rewardsService.RecoverOrphanedRewardsAsync(storeId);

            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = recovered > 0
                    ? $"Recovered {recovered} orphaned reward(s)"
                    : "No orphaned rewards found to recover",
                Severity = recovered > 0
                    ? StatusMessageModel.StatusSeverity.Success
                    : StatusMessageModel.StatusSeverity.Info
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recovering orphaned rewards for store {StoreId}", storeId);
            TempData.SetStatusMessageModel(new StatusMessageModel
            {
                Message = $"Error recovering rewards: {ex.Message}",
                Severity = StatusMessageModel.StatusSeverity.Error
            });
        }

        return RedirectToAction(nameof(RewardsHistory), new { storeId });
    }

    [HttpGet]
    [Route("plugins/bitcoin-rewards/{storeId}/display")]
    [Authorize(Policy = Policies.CanViewStoreSettings)]
    public async Task<IActionResult> DisplayRewards(string storeId)
    {
        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            storeId, 
            BitcoinRewardsStoreSettings.SettingsName);
        
        if (settings == null || !settings.Enabled)
        {
            var store = await _storeRepository.FindStore(storeId);
            return View("DisplayRewards", new DisplayRewardsViewModel
            {
                StoreId = storeId,
                HasReward = false,
                AutoRefreshSeconds = 10,
                TimeframeMinutes = 60,
                StoreName = store?.StoreName ?? "Our Store"
            });
        }
        
        var timeframeMinutes = settings.DisplayTimeframeMinutes;
        var autoRefreshSeconds = settings.DisplayAutoRefreshSeconds;
        var displayTimeoutSeconds = settings.DisplayTimeoutSeconds;
        
        _logger.LogInformation("DisplayRewards: Fetching latest unclaimed reward for store {StoreId} with timeframe {TimeframeMinutes} minutes and timeout {TimeoutSeconds} seconds", 
            storeId, timeframeMinutes, displayTimeoutSeconds);
        
        // Get latest unclaimed reward within timeframe and display timeout
        var latestReward = await _rewardsRepository.GetLatestUnclaimedRewardAsync(storeId, timeframeMinutes, displayTimeoutSeconds);
        
        _logger.LogInformation("DisplayRewards: Latest reward found: {HasReward}, ClaimLink: {HasClaimLink}, OrderId: {OrderId}", 
            latestReward != null, 
            latestReward?.ClaimLink != null,
            latestReward?.OrderId);
        
        if (latestReward == null || string.IsNullOrEmpty(latestReward.ClaimLink))
        {
            var store = await _storeRepository.FindStore(storeId);
            return View("DisplayRewards", new DisplayRewardsViewModel
            {
                StoreId = storeId,
                HasReward = false,
                AutoRefreshSeconds = autoRefreshSeconds,
                TimeframeMinutes = timeframeMinutes,
                DisplayTimeoutSeconds = displayTimeoutSeconds,
                WaitingTemplate = settings.WaitingTemplateOverride,
                StoreName = store?.StoreName ?? "Our Store",
                PrimaryColor = settings.PrimaryColor,
                SecondaryColor = settings.SecondaryColor,
                AccentColor = settings.AccentColor,
                LogoUrl = settings.LogoUrl
            });
        }
        
        // Check if the pull payment has been claimed
        if (!string.IsNullOrEmpty(latestReward.PullPaymentId))
        {
            var isClaimed = await _pullPaymentStatusService.IsPullPaymentClaimedAsync(latestReward.PullPaymentId);
            if (isClaimed)
            {
                _logger.LogInformation("DisplayRewards: Pull payment {PullPaymentId} has been claimed, hiding reward", latestReward.PullPaymentId);
                var store = await _storeRepository.FindStore(storeId);
                return View("DisplayRewards", new DisplayRewardsViewModel
                {
                    StoreId = storeId,
                    HasReward = false,
                    AutoRefreshSeconds = autoRefreshSeconds,
                    TimeframeMinutes = timeframeMinutes,
                    DisplayTimeoutSeconds = displayTimeoutSeconds,
                    WaitingTemplate = settings.WaitingTemplateOverride,
                    StoreName = store?.StoreName ?? "Our Store",
                    PrimaryColor = settings.PrimaryColor,
                    SecondaryColor = settings.SecondaryColor,
                    AccentColor = settings.AccentColor,
                    LogoUrl = settings.LogoUrl
                });
            }
        }
        
        // Extract LNURL from claim link if present
        string? lnurlQrDataUri = null;
        string? lnurlBech32 = null;
        var claimLink = latestReward.ClaimLink;
        
        _logger.LogInformation("DisplayRewards: ClaimLink = {ClaimLink}", claimLink);
        
        // Extract LNURL from the pull payment link
        if (!string.IsNullOrEmpty(claimLink))
        {
            lnurlBech32 = GetLnurlBech32FromClaimLink(claimLink);
            _logger.LogInformation("DisplayRewards: Extracted LNURL Bech32 = {LnurlBech32}", lnurlBech32);
            
            if (!string.IsNullOrEmpty(lnurlBech32))
            {
                lnurlQrDataUri = BuildQrDataUri(lnurlBech32);
                _logger.LogInformation("DisplayRewards: Generated QR code data URI (length: {Length})", lnurlQrDataUri?.Length ?? 0);
            }
        }
        
        // Calculate remaining seconds
        var rewardAge = DateTime.UtcNow - latestReward.CreatedAt;
        var remainingSeconds = Math.Max(0, displayTimeoutSeconds - (int)rewardAge.TotalSeconds);
        
        var vm = new DisplayRewardsViewModel
        {
            StoreId = storeId,
            HasReward = true,
            LnurlQrDataUri = lnurlQrDataUri,
            ClaimLink = claimLink,
            RewardAmountSatoshis = latestReward.RewardAmountSatoshis,
            RewardAmountBtc = latestReward.RewardAmount,
            OrderId = latestReward.OrderId,
            CreatedAt = latestReward.CreatedAt,
            AutoRefreshSeconds = autoRefreshSeconds,
            TimeframeMinutes = timeframeMinutes,
            DisplayTimeoutSeconds = displayTimeoutSeconds,
            RemainingSeconds = remainingSeconds,
            PullPaymentId = latestReward.PullPaymentId,
            CustomTemplate = settings.DisplayTemplateOverride,
            LnurlString = lnurlBech32,
            BoltCardEnabled = settings.BoltCardEnabled,
            RewardId = latestReward.Id.ToString()
        };
        
        ViewData.SetActivePage("BitcoinRewards", "Display", "BitcoinRewards");
        return View("DisplayRewards", vm);
    }
    
    private static string? GetLnurlBech32FromClaimLink(string? claimLink)
    {
        if (string.IsNullOrEmpty(claimLink))
            return null;

        if (!Uri.TryCreate(claimLink, UriKind.Absolute, out var uri))
            return null;

        // Extract pull payment ID from URL like: https://anmore.cash/pull-payments/{ppId}
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var ppIndex = Array.FindIndex(segments, s => s.Equals("pull-payments", StringComparison.OrdinalIgnoreCase));
        if (ppIndex < 0 || ppIndex + 1 >= segments.Length)
            return null;

        var ppId = segments[ppIndex + 1];
        
        // Build LNURL withdraw endpoint: /BTC/lnurl/withdraw/pp/{ppId}
        var builder = new UriBuilder(uri.Scheme, uri.Host, uri.Port);
        builder.Path = $"/BTC/lnurl/withdraw/pp/{System.Web.HttpUtility.UrlEncode(ppId)}";
        var endpoint = builder.Uri;

        try
        {
            return LNURL.LNURL.EncodeUri(endpoint, "withdrawRequest", true).ToString();
        }
        catch
        {
            return null;
        }
    }
    
    private static string BuildQrDataUri(string content)
    {
        using var generator = new QRCodeGenerator();
        var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.Q);
        var pngQr = new PngByteQRCode(data);
        var bytes = pngQr.GetGraphic(10); // Scale factor 10 for larger QR code
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }
    
    /// <summary>
    /// Admin dashboard for viewing and managing error logs
    /// </summary>
    [HttpGet]
    [Route("plugins/bitcoin-rewards/{storeId}/errors")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> ErrorDashboard(string storeId, int? days = 7, bool? resolved = null)
    {
        var errors = await _errorTracking.GetRecentErrorsAsync(
            limit: 100, 
            filterType: null, 
            storeId: storeId, 
            resolvedOnly: resolved);
        var stats = await _errorTracking.GetErrorStatisticsAsync(
            storeId: storeId, 
            since: days.HasValue ? DateTime.UtcNow.AddDays(-days.Value) : null);
        
        // Map service statistics to view model statistics
        var vmStats = new ViewModels.ErrorStatistics
        {
            TotalErrors = stats.TotalErrors,
            UnresolvedErrors = stats.UnresolvedErrors,
            RetryableErrors = errors.Count(e => e.IsRetryable),
            ErrorsByType = stats.ErrorsByType,
            ErrorsByOperation = errors
                .Where(e => !string.IsNullOrEmpty(e.Operation))
                .GroupBy(e => e.Operation!)
                .ToDictionary(g => g.Key, g => g.Count())
        };
        
        var vm = new ErrorDashboardViewModel
        {
            StoreId = storeId,
            Errors = errors,
            Statistics = vmStats,
            DaysFilter = days ?? 7,
            ResolvedFilter = resolved
        };
        
        ViewData.SetActivePage("BitcoinRewards", "Error Dashboard", "BitcoinRewardsErrors");
        return View("ErrorDashboard", vm);
    }
    
    /// <summary>
    /// Mark an error as resolved
    /// </summary>
    [HttpPost]
    [Route("plugins/bitcoin-rewards/{storeId}/errors/{errorId}/resolve")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> ResolveError(string storeId, string errorId)
    {
        await _errorTracking.ResolveErrorAsync(errorId, User.Identity?.Name ?? "system");
        TempData[WellKnownTempData.SuccessMessage] = "Error marked as resolved";
        return RedirectToAction(nameof(ErrorDashboard), new { storeId });
    }
    
    /// <summary>
    /// Admin interface for rate limit configuration
    /// </summary>
    [HttpGet]
    [Route("plugins/bitcoin-rewards/{storeId}/rate-limits")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    public async Task<IActionResult> RateLimitSettings(string storeId)
    {
        var config = await _storeRepository.GetSettingAsync<Models.RateLimitConfiguration>(
            storeId,
            "BitcoinRewardsRateLimitConfig") ?? new Models.RateLimitConfiguration();
        
        var vm = new RateLimitSettingsViewModel { StoreId = storeId };
        vm.SetFromConfiguration(config);
        
        ViewData.SetActivePage("BitcoinRewards", "Rate Limit Settings", "BitcoinRewardsRateLimits");
        return View("RateLimitSettings", vm);
    }
    
    /// <summary>
    /// Save rate limit configuration
    /// </summary>
    [HttpPost]
    [Route("plugins/bitcoin-rewards/{storeId}/rate-limits")]
    [Authorize(Policy = Policies.CanModifyStoreSettings)]
    [AutoValidateAntiforgeryToken]
    public async Task<IActionResult> RateLimitSettings(string storeId, RateLimitSettingsViewModel vm)
    {
        if (!ModelState.IsValid)
        {
            ViewData.SetActivePage("BitcoinRewards", "Rate Limit Settings", "BitcoinRewardsRateLimits");
            return View("RateLimitSettings", vm);
        }
        
        var config = vm.ToConfiguration();
        await _storeRepository.UpdateSetting(storeId, "BitcoinRewardsRateLimitConfig", config);
        
        TempData[WellKnownTempData.SuccessMessage] = "Rate limit settings saved successfully";
        return RedirectToAction(nameof(RateLimitSettings), new { storeId });
    }
}

