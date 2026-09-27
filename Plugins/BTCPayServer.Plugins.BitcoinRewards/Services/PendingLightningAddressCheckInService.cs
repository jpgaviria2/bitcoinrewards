#nullable enable
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Services.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public sealed class PendingLightningAddressCheckInService
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly StoreRepository _storeRepository;
    private readonly ILogger<PendingLightningAddressCheckInService> _logger;

    public PendingLightningAddressCheckInService(
        BitcoinRewardsPluginDbContextFactory dbContextFactory,
        StoreRepository storeRepository,
        ILogger<PendingLightningAddressCheckInService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _storeRepository = storeRepository;
        _logger = logger;
    }

    public async Task<PendingLightningAddressCheckIn> CreateAsync(
        string storeId,
        string qrPayload,
        string? registerId,
        string? deviceId,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettings(storeId);
        if (!settings.CustomerLightningCheckInEnabled)
            throw new InvalidOperationException("Customer QR check-in is disabled for this store.");

        var address = NormalizeWalletQrPayload(qrPayload, settings.CustomerLightningCheckInAllowedDomains);
        var now = DateTime.UtcNow;
        var ttl = settings.CustomerLightningCheckInTtlMinutes <= 0 ? 5 : Math.Min(settings.CustomerLightningCheckInTtlMinutes, 60);

        await using var context = _dbContextFactory.CreateContext();
        ExpireOldPendingRows(context, storeId, now);

        var checkIn = new PendingLightningAddressCheckIn
        {
            StoreId = Required(storeId, 50, nameof(storeId)),
            LightningAddress = address,
            LightningAddressHash = AddressHash(address),
            RegisterId = Optional(registerId, 100),
            DeviceId = Optional(deviceId, 100),
            ExpiresAt = now.AddMinutes(ttl),
            CreatedAt = now,
            UpdatedAt = now
        };
        context.PendingLightningAddressCheckIns.Add(checkIn);
        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Saved customer Lightning check-in for store {StoreId}; expires at {ExpiresAt:o}", storeId, checkIn.ExpiresAt);
        return checkIn;
    }

    public async Task<PendingLightningAddressCheckIn?> ConsumeNextForSquarePaymentAsync(
        string storeId,
        string squarePaymentId,
        string? squareOrderId,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettings(storeId);
        if (!settings.CustomerLightningCheckInEnabled)
            return null;

        var now = DateTime.UtcNow;
        await using var context = _dbContextFactory.CreateContext();
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        ExpireOldPendingRows(context, storeId, now);

        var alreadyConsumed = await context.PendingLightningAddressCheckIns
            .Where(c => c.StoreId == storeId && c.SquarePaymentId == squarePaymentId && c.State == PendingLightningAddressCheckInState.Consumed)
            .OrderBy(c => c.ConsumedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (alreadyConsumed is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return alreadyConsumed;
        }

        var checkIn = await context.PendingLightningAddressCheckIns
            .Where(c => c.StoreId == storeId && c.State == PendingLightningAddressCheckInState.Pending && c.ExpiresAt > now)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (checkIn is null)
        {
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        checkIn.State = PendingLightningAddressCheckInState.Consumed;
        checkIn.SquarePaymentId = Required(squarePaymentId, 255, nameof(squarePaymentId));
        checkIn.SquareOrderId = Optional(squareOrderId, 255);
        checkIn.ConsumedAt = now;
        checkIn.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _logger.LogInformation("Consumed customer Lightning check-in {CheckInId} for Square payment {PaymentId}", checkIn.Id, squarePaymentId);
        return checkIn;
    }

    public async Task<PendingLightningAddressCheckIn?> GetLatestPendingAsync(
        string storeId,
        CancellationToken cancellationToken = default)
    {
        var settings = await GetSettings(storeId);
        if (!settings.CustomerLightningCheckInEnabled)
            return null;

        var now = DateTime.UtcNow;
        await using var context = _dbContextFactory.CreateContext();
        ExpireOldPendingRows(context, storeId, now);
        await context.SaveChangesAsync(cancellationToken);

        return await context.PendingLightningAddressCheckIns
            .Where(c => c.StoreId == storeId && c.State == PendingLightningAddressCheckInState.Pending && c.ExpiresAt > now)
            .OrderBy(c => c.CreatedAt)
            .ThenBy(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task MarkConsumedByTestAsync(
        Guid id,
        string testPaymentId,
        CancellationToken cancellationToken = default)
    {
        await using var context = _dbContextFactory.CreateContext();
        var row = await context.PendingLightningAddressCheckIns.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (row is null || row.State != PendingLightningAddressCheckInState.Pending)
            return;
        var now = DateTime.UtcNow;
        row.State = PendingLightningAddressCheckInState.Consumed;
        row.SquarePaymentId = Required(testPaymentId, 255, nameof(testPaymentId));
        row.SquareOrderId = "one-time-scanned-address-test";
        row.ConsumedAt = now;
        row.UpdatedAt = now;
        await context.SaveChangesAsync(cancellationToken);
    }

    private async Task<BitcoinRewardsStoreSettings> GetSettings(string storeId) =>
        await _storeRepository.GetSettingAsync<BitcoinRewardsStoreSettings>(storeId, BitcoinRewardsStoreSettings.SettingsName)
        ?? new BitcoinRewardsStoreSettings();

    private static void ExpireOldPendingRows(BitcoinRewardsPluginDbContext context, string storeId, DateTime now)
    {
        foreach (var row in context.PendingLightningAddressCheckIns
                     .Where(c => c.StoreId == storeId && c.State == PendingLightningAddressCheckInState.Pending && c.ExpiresAt <= now))
        {
            row.State = PendingLightningAddressCheckInState.Expired;
            row.UpdatedAt = now;
        }
    }

    public static string NormalizeWalletQrPayload(string payload, string? allowedDomains)
    {
        var value = (payload ?? string.Empty).Trim();
        if (value.StartsWith("lightning:", StringComparison.OrdinalIgnoreCase))
            value = Uri.UnescapeDataString(value["lightning:".Length..].Trim());
        value = value.Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(value) || value.Length > 128)
            throw new ArgumentException("Lightning address is required.", nameof(payload));
        if (value.StartsWith("lnbc", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("lnurl", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("npub", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("nostr:", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Scan the wallet Lightning address QR, not an invoice, LNURL, link, or Nostr key.", nameof(payload));

        var parts = value.Split('@');
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]) || string.IsNullOrWhiteSpace(parts[1]))
            throw new ArgumentException("A Lightning address like user@example.com is required.", nameof(payload));

        var local = parts[0];
        var domain = parts[1];
        if (!System.Text.RegularExpressions.Regex.IsMatch(local, "^[a-z0-9](?:[a-z0-9._-]{0,62}[a-z0-9])?$") ||
            !System.Text.RegularExpressions.Regex.IsMatch(domain, "^[a-z0-9.-]+\\.[a-z]{2,}$"))
            throw new ArgumentException("A valid Lightning address is required.", nameof(payload));

        var domains = (allowedDomains ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToLowerInvariant())
            .Where(d => d.Length > 0)
            .ToArray();
        if (domains.Length > 0 && !domains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Lightning address must use one of: {string.Join(", ", domains)}.", nameof(payload));

        return value;
    }

    private static string AddressHash(string normalizedAddress)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes($"bitcoin-rewards:pending-check-in\0{normalizedAddress}"))).ToLowerInvariant();
    }

    private static string Required(string? value, int max, string name) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= max
            ? value.Trim()
            : throw new ArgumentException($"{name} is required.", name);

    private static string? Optional(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, max)];
}
