#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
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
    private readonly ILogger<CustomerCheckInController> _logger;

    public CustomerCheckInController(
        PendingLightningAddressCheckInService checkInService,
        ILogger<CustomerCheckInController> logger)
    {
        _checkInService = checkInService;
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

            return Ok(new
            {
                status = "checked_in",
                expiresAt = checkIn.ExpiresAt,
                lightningAddress = checkIn.LightningAddress
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
}
