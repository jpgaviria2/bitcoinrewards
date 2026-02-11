#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.NTag424;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Services;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NBitcoin.DataEncoders;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Handles Bolt Card reward collection: decrypting NFC tap params, looking up the
/// card's pull payment, and topping up its limit with earned rewards.
/// 
/// Relies on core BTCPay's boltcards table (managed by BoltcardDataExtensions) for
/// the card→pull-payment mapping, and uses the plugin's own BoltCardLinks table to
/// track reward totals and store association.
/// </summary>
public class BoltCardRewardService
{
    private readonly ApplicationDbContextFactory _appDbContextFactory;
    private readonly BitcoinRewardsPluginDbContextFactory _pluginDbContextFactory;
    private readonly SettingsRepository _settingsRepository;
    private readonly BTCPayServerEnvironment _env;
    private readonly ILogger<BoltCardRewardService> _logger;

    // Rate limiting: track last tap per boltcard ID to prevent abuse
    private static readonly Dictionary<string, DateTime> _lastTapTimes = new();
    private static readonly object _tapLock = new();
    private const int MIN_TAP_INTERVAL_SECONDS = 5;

    public BoltCardRewardService(
        ApplicationDbContextFactory appDbContextFactory,
        BitcoinRewardsPluginDbContextFactory pluginDbContextFactory,
        SettingsRepository settingsRepository,
        BTCPayServerEnvironment env,
        ILogger<BoltCardRewardService> logger)
    {
        _appDbContextFactory = appDbContextFactory;
        _pluginDbContextFactory = pluginDbContextFactory;
        _settingsRepository = settingsRepository;
        _env = env;
        _logger = logger;
    }

    /// <summary>
    /// Result of resolving encrypted NFC tap parameters to a card registration.
    /// </summary>
    public record CardLookupResult(
        bool Success,
        string? PullPaymentId = null,
        string? BoltcardId = null,
        byte[]? Uid = null,
        string? Error = null);

    /// <summary>
    /// Decrypt the p (PICC data) and c (CMAC) parameters from an NFC tap,
    /// verify authenticity, and look up the associated pull payment.
    /// 
    /// This replicates the flow in UIBoltcardController.GetWithdrawRequest()
    /// but does NOT update the counter (we're reading, not spending).
    /// </summary>
    public async Task<CardLookupResult> FindCardByEncryptedParamsAsync(string p, string c)
    {
        try
        {
            var issuerKey = await _settingsRepository.GetIssuerKey(_env);

            // Decrypt PICC data from p parameter
            var piccData = issuerKey.TryDecrypt(p);
            if (piccData?.Uid is null)
            {
                _logger.LogWarning("BoltCard tap: failed to decrypt p parameter");
                return new CardLookupResult(false, Error: "Invalid card data");
            }

            // Look up card registration WITHOUT updating counter
            // (reward collection doesn't consume a counter slot)
            var registration = await _appDbContextFactory.GetBoltcardRegistration(
                issuerKey, piccData, updateCounter: false);

            if (registration is null)
            {
                _logger.LogWarning("BoltCard tap: no registration found for UID {Uid}",
                    Encoders.Hex.EncodeData(piccData.Uid));
                return new CardLookupResult(false, Error: "Card not registered");
            }

            // Verify CMAC to prevent spoofing
            if (registration.PullPaymentId is not null)
            {
                var cardKey = issuerKey.CreatePullPaymentCardKey(
                    piccData.Uid, registration.Version, registration.PullPaymentId);
                if (!cardKey.CheckSunMac(c, piccData))
                {
                    _logger.LogWarning("BoltCard tap: CMAC verification failed for card {BoltcardId}",
                        registration.Id);
                    return new CardLookupResult(false, Error: "Card verification failed");
                }
            }

            if (string.IsNullOrEmpty(registration.PullPaymentId))
            {
                _logger.LogWarning("BoltCard tap: card {BoltcardId} has no pull payment linked",
                    registration.Id);
                return new CardLookupResult(false, Error: "Card not linked to a pull payment");
            }

            // Rate limiting check
            lock (_tapLock)
            {
                if (_lastTapTimes.TryGetValue(registration.Id, out var lastTap) &&
                    (DateTime.UtcNow - lastTap).TotalSeconds < MIN_TAP_INTERVAL_SECONDS)
                {
                    _logger.LogWarning("BoltCard tap: rate limited for card {BoltcardId}", registration.Id);
                    return new CardLookupResult(false, Error: "Too many taps, please wait");
                }
                _lastTapTimes[registration.Id] = DateTime.UtcNow;
            }

            _logger.LogInformation("BoltCard tap: verified card {BoltcardId} → PP {PullPaymentId}",
                registration.Id, registration.PullPaymentId);

            return new CardLookupResult(
                true,
                PullPaymentId: registration.PullPaymentId,
                BoltcardId: registration.Id,
                Uid: piccData.Uid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BoltCard tap: error resolving encrypted params");
            return new CardLookupResult(false, Error: "Internal error");
        }
    }

    /// <summary>
    /// Increase the pull payment's Limit by the given reward amount.
    /// Uses a direct SQL UPDATE for atomicity (no read-modify-write race).
    /// Also updates the plugin's BoltCardLinks tracking table.
    /// </summary>
    public async Task<(bool Success, long NewLimitSats, string? Error)> TopUpPullPaymentAsync(
        string pullPaymentId, long rewardSatoshis, string storeId, string? boltcardId = null)
    {
        if (rewardSatoshis <= 0)
            return (false, 0, "Reward amount must be positive");

        try
        {
            // Atomically increase the pull payment limit in the core BTCPay database
            await using var appCtx = _appDbContextFactory.CreateContext();
            var conn = appCtx.Database.GetDbConnection();

            // Verify pull payment exists and is active
            var pp = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT \"Id\", \"Limit\", \"Currency\", \"StoreId\" FROM \"PullPayments\" WHERE \"Id\" = @id AND \"Archived\" = false",
                new { id = pullPaymentId });

            if (pp is null)
                return (false, 0, "Pull payment not found or archived");

            // Convert sats to the pull payment's currency unit
            string currency = pp.Currency;
            decimal addAmount = currency switch
            {
                "SATS" => rewardSatoshis,
                "BTC" => rewardSatoshis / 100_000_000m,
                _ => 0m
            };

            if (addAmount <= 0)
                return (false, 0, $"Unsupported pull payment currency: {currency}");

            // Atomic UPDATE — no race condition
            var newLimit = await conn.QueryFirstOrDefaultAsync<decimal>(
                "UPDATE \"PullPayments\" SET \"Limit\" = \"Limit\" + @amount WHERE \"Id\" = @id RETURNING \"Limit\"",
                new { id = pullPaymentId, amount = addAmount });

            long newLimitSats = currency == "SATS"
                ? (long)newLimit
                : (long)(newLimit * 100_000_000m);

            // Update the plugin's tracking table
            await EnsureBoltCardLinkAsync(storeId, pullPaymentId, boltcardId, rewardSatoshis);

            _logger.LogInformation(
                "BoltCard top-up: PP {PullPaymentId} += {Sats} sats → new limit {NewLimit} {Currency}",
                pullPaymentId, rewardSatoshis, newLimit, currency);

            return (true, newLimitSats, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BoltCard top-up failed for PP {PullPaymentId}", pullPaymentId);
            return (false, 0, "Database error during top-up");
        }
    }

    /// <summary>
    /// Get the current balance (limit minus payouts) for a pull payment.
    /// </summary>
    public async Task<(long BalanceSats, long TotalRewardedSats)?> GetCardBalanceAsync(string pullPaymentId)
    {
        try
        {
            await using var appCtx = _appDbContextFactory.CreateContext();
            var conn = appCtx.Database.GetDbConnection();

            var pp = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT \"Limit\", \"Currency\" FROM \"PullPayments\" WHERE \"Id\" = @id",
                new { id = pullPaymentId });
            if (pp is null) return null;

            string currency = pp.Currency;
            decimal limit = pp.Limit;

            // Sum completed/in-progress payouts
            var totalPaid = await conn.QueryFirstOrDefaultAsync<decimal?>(
                "SELECT SUM(\"OriginalAmount\") FROM \"Payouts\" WHERE \"PullPaymentDataId\" = @id AND \"State\" != @cancelled",
                new { id = pullPaymentId, cancelled = (int)BTCPayServer.Client.Models.PayoutState.Cancelled }) ?? 0m;

            var balance = limit - totalPaid;
            long balanceSats = currency == "SATS" ? (long)balance : (long)(balance * 100_000_000m);
            long limitSats = currency == "SATS" ? (long)limit : (long)(limit * 100_000_000m);

            // Get total rewarded from our tracking table
            await using var pluginCtx = _pluginDbContextFactory.CreateContext();
            var link = await pluginCtx.BoltCardLinks
                .FirstOrDefaultAsync(l => l.PullPaymentId == pullPaymentId);

            return (balanceSats, link?.TotalRewardedSatoshis ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting balance for PP {PullPaymentId}", pullPaymentId);
            return null;
        }
    }

    /// <summary>
    /// Generate a balance check URL for a card's pull payment.
    /// Points to the boltcards-plugin balance page.
    /// </summary>
    public string GetBalanceUrl(string pullPaymentId, string baseUrl)
    {
        // The boltcards balance plugin uses: /boltcards/balance?p=...&c=...
        // But for a QR on the physical card, we link to the pull payment page directly
        // since the customer doesn't have the encrypted p/c params for a static QR.
        var uri = new Uri(baseUrl.TrimEnd('/') + $"/pull-payments/{Uri.EscapeDataString(pullPaymentId)}");
        return uri.ToString();
    }

    /// <summary>
    /// Get all bolt card links for a store with their balance URLs (for printing QR codes on cards).
    /// </summary>
    public async Task<List<BoltCardInfo>> GetAllCardInfoAsync(string storeId, string baseUrl)
    {
        await using var pluginCtx = _pluginDbContextFactory.CreateContext();
        var links = await pluginCtx.BoltCardLinks
            .Where(l => l.StoreId == storeId && l.IsActive)
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();

        var results = new List<BoltCardInfo>();
        foreach (var link in links)
        {
            var balance = await GetCardBalanceAsync(link.PullPaymentId);
            results.Add(new BoltCardInfo
            {
                Id = link.Id,
                PullPaymentId = link.PullPaymentId,
                CardUid = link.CardUid,
                BoltcardId = link.BoltcardId,
                BalanceUrl = GetBalanceUrl(link.PullPaymentId, baseUrl),
                BalanceSats = balance?.BalanceSats ?? 0,
                TotalRewardedSats = link.TotalRewardedSatoshis,
                CreatedAt = link.CreatedAt,
                IsActive = link.IsActive
            });
        }

        return results;
    }

    /// <summary>
    /// Find a BoltCardLink by pull payment ID.
    /// </summary>
    public async Task<BoltCardLink?> FindByPullPaymentIdAsync(string pullPaymentId)
    {
        await using var ctx = _pluginDbContextFactory.CreateContext();
        return await ctx.BoltCardLinks
            .FirstOrDefaultAsync(l => l.PullPaymentId == pullPaymentId);
    }

    /// <summary>
    /// Ensure a BoltCardLink record exists and update reward tracking.
    /// </summary>
    private async Task EnsureBoltCardLinkAsync(
        string storeId, string pullPaymentId, string? boltcardId, long rewardSatoshis)
    {
        await using var ctx = _pluginDbContextFactory.CreateContext();
        var link = await ctx.BoltCardLinks
            .FirstOrDefaultAsync(l => l.StoreId == storeId && l.PullPaymentId == pullPaymentId);

        if (link is null)
        {
            link = new BoltCardLink
            {
                StoreId = storeId,
                PullPaymentId = pullPaymentId,
                BoltcardId = boltcardId,
                TotalRewardedSatoshis = rewardSatoshis,
                LastRewardedAt = DateTime.UtcNow
            };
            ctx.BoltCardLinks.Add(link);
        }
        else
        {
            link.TotalRewardedSatoshis += rewardSatoshis;
            link.LastRewardedAt = DateTime.UtcNow;
            if (boltcardId is not null && link.BoltcardId is null)
                link.BoltcardId = boltcardId;
        }

        await ctx.SaveChangesAsync();
    }

    public record BoltCardInfo
    {
        public Guid Id { get; init; }
        public string PullPaymentId { get; init; } = string.Empty;
        public string? CardUid { get; init; }
        public string? BoltcardId { get; init; }
        public string BalanceUrl { get; init; } = string.Empty;
        public long BalanceSats { get; init; }
        public long TotalRewardedSats { get; init; }
        public DateTime CreatedAt { get; init; }
        public bool IsActive { get; init; }
    }
}
