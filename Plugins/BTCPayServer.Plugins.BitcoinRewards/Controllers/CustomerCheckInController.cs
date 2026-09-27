#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Models;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

[AllowAnonymous]
[Route("plugins/bitcoin-rewards/{storeId}/check-in")]
public class CustomerCheckInController : Controller
{
    public sealed record CustomerCheckInApiRequest(string LightningAddress, string? RegisterId, string? DeviceId);

    private readonly PendingLightningAddressCheckInService _checkInService;
    private readonly BitcoinRewardsService _rewardsService;
    private readonly StoreRepository _storeRepository;
    private readonly ILogger<CustomerCheckInController> _logger;

    public CustomerCheckInController(
        PendingLightningAddressCheckInService checkInService,
        BitcoinRewardsService rewardsService,
        StoreRepository storeRepository,
        ILogger<CustomerCheckInController> logger)
    {
        _checkInService = checkInService;
        _rewardsService = rewardsService;
        _storeRepository = storeRepository;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Index(string storeId, string? registerId = null, string? deviceId = null)
    {
        return View(new CustomerCheckInViewModel
        {
            StoreId = storeId,
            RegisterId = registerId,
            DeviceId = deviceId
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(string storeId, CustomerCheckInViewModel model)
    {
        model.StoreId = storeId;
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var checkIn = await _checkInService.CreateAsync(
                storeId,
                model.LightningAddress,
                model.RegisterId,
                model.DeviceId,
                HttpContext.RequestAborted);
            await ProcessOneTimeScanTestIfArmed(storeId, checkIn, model.RegisterId, model.DeviceId);
            return View(new CustomerCheckInViewModel
            {
                StoreId = storeId,
                RegisterId = model.RegisterId,
                DeviceId = model.DeviceId,
                LightningAddress = checkIn.LightningAddress,
                Saved = true,
                ExpiresAt = checkIn.ExpiresAt,
                Message = "You are checked in. The next Square order will use this Lightning address for rewards."
            });
        }
        catch (ArgumentException ex)
        {
            ModelState.AddModelError(nameof(model.LightningAddress), ex.Message);
            return View(model);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Customer Lightning check-in rejected for store {StoreId}", storeId);
            ModelState.AddModelError(string.Empty, ex.Message);
            return View(model);
        }
    }

    [HttpPost("api")]
    [IgnoreAntiforgeryToken]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Api(string storeId, [FromBody] CustomerCheckInApiRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.LightningAddress))
            return BadRequest(new { error = "Lightning address is required." });

        try
        {
            var checkIn = await _checkInService.CreateAsync(
                storeId,
                request.LightningAddress,
                request.RegisterId,
                request.DeviceId,
                HttpContext.RequestAborted);
            var testProcessed = await ProcessOneTimeScanTestIfArmed(storeId, checkIn, request.RegisterId, request.DeviceId);

            return Ok(new
            {
                status = "checked_in",
                expiresAt = checkIn.ExpiresAt,
                lightningAddress = checkIn.LightningAddress,
                testRewardProcessed = testProcessed
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Customer Lightning check-in API rejected for store {StoreId}", storeId);
            return StatusCode(503, new { error = ex.Message });
        }
    }

    private async Task<bool> ProcessOneTimeScanTestIfArmed(string storeId, Data.PendingLightningAddressCheckIn checkIn, string? registerId, string? deviceId)
    {
        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(storeId, BitcoinRewardsStoreSettings.SettingsName);
        if (settings?.OneTimeScannedAddressTestEnabled != true)
            return false;

        settings.OneTimeScannedAddressTestEnabled = false;
        await _storeRepository.UpdateSetting(storeId, BitcoinRewardsStoreSettings.SettingsName, settings);

        var testSats = settings.TestRewardSatoshis > 0 ? settings.TestRewardSatoshis : 10;

        var transactionId = $"SCAN_TEST_{Guid.NewGuid():N}";
        var transaction = new TransactionData
        {
            TransactionId = transactionId,
            OrderId = $"SCAN_TEST_{DateTime.UtcNow:yyyyMMddHHmmss}",
            Amount = 0.01m,
            Currency = "CAD",
            Platform = TransactionPlatform.Square,
            TransactionDate = DateTime.UtcNow,
            LightningAddress = checkIn.LightningAddress,
            LightningAddressHash = checkIn.LightningAddressHash,
            Metadata =
            {
                ["rewardSatoshisOverride"] = testSats.ToString(),
                ["rewardPercentageOverride"] = "100",
                ["forceDirectLightning"] = "true",
                ["source"] = "one-time-scanned-address-test"
            }
        };

        var ok = await _rewardsService.ProcessRewardAsync(storeId, transaction);
        if (ok)
            await _checkInService.MarkConsumedByTestAsync(checkIn.Id, transactionId);
        _logger.LogInformation("One-time scanned address test reward processed for store {StoreId}, check-in {CheckInId}, sats={Sats}, success={Success}", storeId, checkIn.Id, testSats, ok);
        return ok;
    }
}
