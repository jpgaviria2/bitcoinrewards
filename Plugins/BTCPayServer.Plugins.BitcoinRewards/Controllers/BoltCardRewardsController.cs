#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Client;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Services.Stores;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Controllers;

/// <summary>
/// Handles Bolt Card NFC tap reward collection and admin card management.
/// </summary>
public class BoltCardRewardsController : Controller
{
    private readonly BoltCardRewardService _boltCardService;
    private readonly BitcoinRewardsRepository _rewardsRepository;
    private readonly StoreRepository _storeRepository;
    private readonly ILogger<BoltCardRewardsController> _logger;

    public BoltCardRewardsController(
        BoltCardRewardService boltCardService,
        BitcoinRewardsRepository rewardsRepository,
        StoreRepository storeRepository,
        ILogger<BoltCardRewardsController> logger)
    {
        _boltCardService = boltCardService;
        _rewardsRepository = rewardsRepository;
        _storeRepository = storeRepository;
        _logger = logger;
    }

    /// <summary>
    /// NFC tap endpoint: customer taps their bolt card on the rewards display page.
    /// Decrypts card params, finds the pull payment, tops up with the reward amount.
    /// No authentication required — the card's CMAC provides authentication.
    /// </summary>
    [HttpPost("plugins/bitcoin-rewards/boltcard/tap")]
    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> TapCard([FromBody] BoltCardTapRequest request)
    {
        if (request is null ||
            string.IsNullOrWhiteSpace(request.P) ||
            string.IsNullOrWhiteSpace(request.C) ||
            string.IsNullOrWhiteSpace(request.RewardId))
        {
            return BadRequest(new BoltCardTapResponse
            {
                Success = false,
                Error = "Missing required parameters (p, c, rewardId)"
            });
        }

        // Look up the reward
        if (!Guid.TryParse(request.RewardId, out var rewardGuid))
        {
            return BadRequest(new BoltCardTapResponse
            {
                Success = false,
                Error = "Invalid reward ID"
            });
        }

        // We need to find the reward across stores since this is an anonymous endpoint.
        // The reward ID is a GUID which is unguessable, so this is safe.
        BitcoinRewardRecord? reward = null;
        try
        {
            // Search for the reward by iterating — in production this should use a
            // direct query. For now, use the reward ID as the lookup key.
            await using var ctx = GetPluginDbContext();
            reward = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions
                .FirstOrDefaultAsync(ctx.BitcoinRewardRecords, r => r.Id == rewardGuid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BoltCard tap: error looking up reward {RewardId}", request.RewardId);
            return StatusCode(500, new BoltCardTapResponse
            {
                Success = false,
                Error = "Internal error"
            });
        }

        if (reward is null)
        {
            return NotFound(new BoltCardTapResponse
            {
                Success = false,
                Error = "Reward not found"
            });
        }

        // Check if reward has already been claimed via bolt card
        if (reward.Status == RewardStatus.Redeemed)
        {
            return BadRequest(new BoltCardTapResponse
            {
                Success = false,
                Error = "Reward already claimed"
            });
        }

        // Check bolt card settings
        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            reward.StoreId, BitcoinRewardsStoreSettings.SettingsName);

        if (settings is null || !settings.BoltCardEnabled)
        {
            return BadRequest(new BoltCardTapResponse
            {
                Success = false,
                Error = "Bolt Card rewards not enabled for this store"
            });
        }

        // Decrypt and verify the card
        var cardResult = await _boltCardService.FindCardByEncryptedParamsAsync(request.P, request.C);
        if (!cardResult.Success)
        {
            return BadRequest(new BoltCardTapResponse
            {
                Success = false,
                Error = cardResult.Error ?? "Card verification failed"
            });
        }

        // Top up the card's pull payment
        var (topUpSuccess, newLimitSats, topUpError) = await _boltCardService.TopUpPullPaymentAsync(
            cardResult.PullPaymentId!,
            reward.RewardAmountSatoshis,
            reward.StoreId,
            cardResult.BoltcardId);

        if (!topUpSuccess)
        {
            return StatusCode(500, new BoltCardTapResponse
            {
                Success = false,
                Error = topUpError ?? "Failed to add reward to card"
            });
        }

        // Mark the reward as redeemed (claimed via bolt card)
        reward.Status = RewardStatus.Redeemed;
        reward.RedeemedAt = DateTime.UtcNow;
        reward.PullPaymentId = cardResult.PullPaymentId;
        try
        {
            await _rewardsRepository.UpdateRewardAsync(reward);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BoltCard tap: reward topped up but failed to update reward record {RewardId}",
                reward.Id);
            // Don't fail the response — the top-up succeeded
        }

        // Get current balance
        var balance = await _boltCardService.GetCardBalanceAsync(cardResult.PullPaymentId!);

        _logger.LogInformation(
            "BoltCard tap: reward {RewardId} ({Sats} sats) collected on card {BoltcardId} → PP {PpId}. New balance: {Balance} sats",
            reward.Id, reward.RewardAmountSatoshis, cardResult.BoltcardId, cardResult.PullPaymentId,
            balance?.BalanceSats);

        return Ok(new BoltCardTapResponse
        {
            Success = true,
            RewardSats = reward.RewardAmountSatoshis,
            NewBalanceSats = balance?.BalanceSats ?? newLimitSats,
            TotalRewardedSats = balance?.TotalRewardedSats ?? reward.RewardAmountSatoshis,
            PullPaymentId = cardResult.PullPaymentId
        });
    }

    /// <summary>
    /// Admin endpoint: list all bolt cards for a store with balance URLs for printing QR codes.
    /// </summary>
    [HttpGet("plugins/bitcoin-rewards/{storeId}/boltcard/cards")]
    [Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewStoreSettings)]
    public async Task<IActionResult> ListCards(string storeId)
    {
        var settings = await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(
            storeId, BitcoinRewardsStoreSettings.SettingsName);

        if (settings is null || !settings.BoltCardEnabled)
        {
            return BadRequest(new { error = "Bolt Card rewards not enabled" });
        }

        var baseUrl = $"{Request.Scheme}://{Request.Host}";
        var cards = await _boltCardService.GetAllCardInfoAsync(storeId, baseUrl);
        return Ok(cards);
    }

    /// <summary>
    /// Balance redirect: redirects to the pull payment page for balance viewing.
    /// </summary>
    [HttpGet("plugins/bitcoin-rewards/boltcard/balance/{pullPaymentId}")]
    [AllowAnonymous]
    public IActionResult ViewBalance(string pullPaymentId)
    {
        return Redirect($"/pull-payments/{Uri.EscapeDataString(pullPaymentId)}");
    }

    private BitcoinRewardsPluginDbContext GetPluginDbContext()
    {
        var factory = HttpContext.RequestServices
            .GetService(typeof(BitcoinRewardsPluginDbContextFactory)) as BitcoinRewardsPluginDbContextFactory;
        return factory!.CreateContext();
    }

    // ── Request/Response DTOs ──

    public class BoltCardTapRequest
    {
        /// <summary>Encrypted PICC data from the card's NDEF URL.</summary>
        public string P { get; set; } = string.Empty;

        /// <summary>CMAC from the card's NDEF URL.</summary>
        public string C { get; set; } = string.Empty;

        /// <summary>The reward record ID to collect.</summary>
        public string RewardId { get; set; } = string.Empty;
    }

    public class BoltCardTapResponse
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public long RewardSats { get; set; }
        public long NewBalanceSats { get; set; }
        public long TotalRewardedSats { get; set; }
        public string? PullPaymentId { get; set; }
    }
}
