#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Manages CustomerWallet dual-balance operations: CAD + sats.
/// Sats balance lives in the pull payment (managed by BoltCardRewardService).
/// CAD balance lives in CustomerWallet.CadBalanceCents.
/// </summary>
public class CustomerWalletService
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbFactory;
    private readonly BoltCardRewardService _boltCardService;
    private readonly ExchangeRateService _exchangeRateService;
    private readonly ILogger<CustomerWalletService> _logger;

    public CustomerWalletService(
        BitcoinRewardsPluginDbContextFactory dbFactory,
        BoltCardRewardService boltCardService,
        ExchangeRateService exchangeRateService,
        ILogger<CustomerWalletService> logger)
    {
        _dbFactory = dbFactory;
        _boltCardService = boltCardService;
        _exchangeRateService = exchangeRateService;
        _logger = logger;
    }

    public async Task<CustomerWallet> GetOrCreateWalletAsync(
        string storeId, string pullPaymentId, string? cardUid = null, string? boltcardId = null)
    {
        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets
            .FirstOrDefaultAsync(w => w.StoreId == storeId && w.PullPaymentId == pullPaymentId);

        if (wallet != null)
        {
            // Update card info if newly available
            var changed = false;
            if (cardUid != null && wallet.CardUid == null) { wallet.CardUid = cardUid; changed = true; }
            if (boltcardId != null && wallet.BoltcardId == null) { wallet.BoltcardId = boltcardId; changed = true; }
            if (changed) await ctx.SaveChangesAsync();
            return wallet;
        }

        wallet = new CustomerWallet
        {
            StoreId = storeId,
            PullPaymentId = pullPaymentId,
            CardUid = cardUid,
            BoltcardId = boltcardId
        };
        ctx.CustomerWallets.Add(wallet);
        await ctx.SaveChangesAsync();

        _logger.LogInformation("Created CustomerWallet {WalletId} for store {StoreId}, PP {PullPaymentId}",
            wallet.Id, storeId, pullPaymentId);
        return wallet;
    }

    public async Task<CustomerWallet?> FindByIdAsync(Guid walletId)
    {
        await using var ctx = _dbFactory.CreateContext();
        return await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
    }

    public async Task<CustomerWallet?> FindByTokenHashAsync(string tokenHash)
    {
        await using var ctx = _dbFactory.CreateContext();
        return await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.ApiTokenHash == tokenHash);
    }

    public async Task<CustomerWallet?> FindByPullPaymentIdAsync(string storeId, string pullPaymentId)
    {
        await using var ctx = _dbFactory.CreateContext();
        return await ctx.CustomerWallets
            .FirstOrDefaultAsync(w => w.StoreId == storeId && w.PullPaymentId == pullPaymentId);
    }

    public record WalletBalance(long SatsBalance, long CadBalanceCents, bool AutoConvertToCad,
        long TotalRewardedSatoshis, long TotalRewardedCadCents);

    public async Task<WalletBalance?> GetBalanceAsync(Guid walletId)
    {
        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return null;

        return new WalletBalance(wallet.SatsBalanceSatoshis, wallet.CadBalanceCents, wallet.AutoConvertToCad,
            wallet.TotalRewardedSatoshis, wallet.TotalRewardedCadCents);
    }

    public async Task<bool> CreditCadAsync(Guid walletId, long cadCents, long sats, decimal exchangeRate, string? reference = null)
    {
        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return false;

        wallet.CadBalanceCents += cadCents;
        wallet.TotalRewardedCadCents += cadCents;
        wallet.LastRewardedAt = DateTime.UtcNow;

        ctx.WalletTransactions.Add(new WalletTransaction
        {
            CustomerWalletId = walletId,
            Type = WalletTransactionType.RewardEarned,
            SatsAmount = sats,
            CadCentsAmount = cadCents,
            ExchangeRate = exchangeRate,
            Reference = reference
        });

        await ctx.SaveChangesAsync();
        _logger.LogInformation("Credited {CadCents} CAD cents to wallet {WalletId} (from {Sats} sats @ {Rate})",
            cadCents, walletId, sats, exchangeRate);
        return true;
    }

    public async Task<bool> CreditSatsAsync(Guid walletId, long sats, string? reference = null)
    {
        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return false;

        // Credit sats by topping up the pull payment limit
        var (success, _, error) = await _boltCardService.TopUpPullPaymentAsync(
            wallet.PullPaymentId, sats, wallet.StoreId, wallet.BoltcardId);

        if (!success)
        {
            _logger.LogWarning("CreditSatsAsync failed for wallet {WalletId}: {Error}", walletId, error);
            return false;
        }

        wallet.SatsBalanceSatoshis += sats;  // Current balance
        wallet.TotalRewardedSatoshis += sats;  // Lifetime total
        wallet.LastRewardedAt = DateTime.UtcNow;

        ctx.WalletTransactions.Add(new WalletTransaction
        {
            CustomerWalletId = walletId,
            Type = WalletTransactionType.RewardEarned,
            SatsAmount = sats,
            CadCentsAmount = 0,
            ExchangeRate = 0,
            Reference = reference
        });

        await ctx.SaveChangesAsync();
        _logger.LogInformation("Credited {Sats} sats to wallet {WalletId} (kept as sats, no CAD conversion)",
            sats, walletId);
        return true;
    }

    public async Task<(bool Success, string? Error)> SwapToCadAsync(Guid walletId, long satsAmount, string storeId)
    {
        if (satsAmount <= 0) return (false, "Amount must be positive");

        // Check sats balance
        var balance = await GetBalanceAsync(walletId);
        if (balance == null) return (false, "Wallet not found");
        if (balance.SatsBalance < satsAmount) return (false, "Insufficient sats balance");

        // Convert
        var (cadCents, rate) = await _exchangeRateService.SatsToCadCentsAsync(satsAmount, storeId);
        if (cadCents <= 0) return (false, "Conversion resulted in zero CAD");

        // Get wallet for pull payment ID
        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return (false, "Wallet not found");

        // Debit sats from pull payment (reduce limit)
        var (success, _, error) = await _boltCardService.DebitPullPaymentAsync(
            wallet.PullPaymentId, satsAmount, wallet.StoreId);
        if (!success) return (false, error ?? "Failed to debit sats from pull payment");

        // Update balances
        wallet.SatsBalanceSatoshis -= satsAmount;
        wallet.CadBalanceCents += cadCents;

        ctx.WalletTransactions.Add(new WalletTransaction
        {
            CustomerWalletId = walletId,
            Type = WalletTransactionType.SwapToCad,
            SatsAmount = -satsAmount,
            CadCentsAmount = cadCents,
            ExchangeRate = rate,
            Reference = $"swap-to-cad-{DateTime.UtcNow:yyyyMMddHHmmss}"
        });

        await ctx.SaveChangesAsync();
        _logger.LogInformation("Swapped {Sats} sats → {CadCents} CAD cents for wallet {WalletId}", satsAmount, cadCents, walletId);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SwapToSatsAsync(Guid walletId, long cadCentsAmount, string storeId)
    {
        if (cadCentsAmount <= 0) return (false, "Amount must be positive");

        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return (false, "Wallet not found");
        if (wallet.CadBalanceCents < cadCentsAmount) return (false, "Insufficient CAD balance");

        var (sats, rate) = await _exchangeRateService.CadCentsToSatsAsync(cadCentsAmount, storeId);
        if (sats <= 0) return (false, "Conversion resulted in zero sats");

        // Credit sats to pull payment
        var (success, _, error) = await _boltCardService.TopUpPullPaymentAsync(
            wallet.PullPaymentId, sats, wallet.StoreId);
        if (!success) return (false, error ?? "Failed to credit sats to pull payment");

        // Update balances
        wallet.SatsBalanceSatoshis += sats;
        wallet.CadBalanceCents -= cadCentsAmount;

        ctx.WalletTransactions.Add(new WalletTransaction
        {
            CustomerWalletId = walletId,
            Type = WalletTransactionType.SwapToSats,
            SatsAmount = sats,
            CadCentsAmount = -cadCentsAmount,
            ExchangeRate = rate,
            Reference = $"swap-to-sats-{DateTime.UtcNow:yyyyMMddHHmmss}"
        });

        await ctx.SaveChangesAsync();
        _logger.LogInformation("Swapped {CadCents} CAD cents → {Sats} sats for wallet {WalletId}", cadCentsAmount, sats, walletId);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SpendSatsAsync(Guid walletId, long sats, string? reference = null)
    {
        if (sats <= 0) return (false, "Amount must be positive");

        var balance = await GetBalanceAsync(walletId);
        if (balance == null) return (false, "Wallet not found");
        if (balance.SatsBalance < sats) return (false, "Insufficient sats balance");

        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return (false, "Wallet not found");

        // Debit sats from pull payment (reduce limit)
        var (success, _, error) = await _boltCardService.DebitPullPaymentAsync(
            wallet.PullPaymentId, sats, wallet.StoreId);
        if (!success) return (false, error ?? "Failed to debit sats from pull payment");

        wallet.SatsBalanceSatoshis -= sats;

        ctx.WalletTransactions.Add(new WalletTransaction
        {
            CustomerWalletId = walletId,
            Type = WalletTransactionType.SatsSpent,
            SatsAmount = -sats,
            CadCentsAmount = 0,
            ExchangeRate = 0,
            Reference = reference
        });

        await ctx.SaveChangesAsync();
        _logger.LogInformation("Spent {Sats} sats from wallet {WalletId}", sats, walletId);
        return (true, null);
    }

    public async Task<(bool Success, string? Error)> SpendCadAsync(Guid walletId, long cadCents, string? reference = null)
    {
        if (cadCents <= 0) return (false, "Amount must be positive");

        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return (false, "Wallet not found");
        if (wallet.CadBalanceCents < cadCents) return (false, "Insufficient CAD balance");

        wallet.CadBalanceCents -= cadCents;

        ctx.WalletTransactions.Add(new WalletTransaction
        {
            CustomerWalletId = walletId,
            Type = WalletTransactionType.CadSpent,
            CadCentsAmount = -cadCents,
            ExchangeRate = 0, // no conversion involved
            Reference = reference
        });

        await ctx.SaveChangesAsync();
        _logger.LogInformation("Spent {CadCents} CAD cents from wallet {WalletId}", cadCents, walletId);
        return (true, null);
    }

    public async Task<bool> SetAutoConvertAsync(Guid walletId, bool autoConvert)
    {
        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return false;

        wallet.AutoConvertToCad = autoConvert;
        await ctx.SaveChangesAsync();
        return true;
    }

    public async Task<List<WalletTransaction>> GetHistoryAsync(Guid walletId, int limit = 50)
    {
        await using var ctx = _dbFactory.CreateContext();
        return await ctx.WalletTransactions
            .Where(t => t.CustomerWalletId == walletId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit)
            .ToListAsync();
    }

    /// <summary>
    /// Generate a bearer token for PWA auth. Returns the raw token (base64).
    /// Stores SHA-256 hash in the wallet record.
    /// </summary>
    public async Task<string?> GenerateWalletTokenAsync(Guid walletId)
    {
        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return null;

        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes);
        var hash = Convert.ToHexString(SHA256.HashData(tokenBytes)).ToLowerInvariant();

        wallet.ApiTokenHash = hash;
        await ctx.SaveChangesAsync();

        _logger.LogInformation("Generated new API token for wallet {WalletId}", walletId);
        return token;
    }

    /// <summary>Hash a raw token for lookup.</summary>
    public static string HashToken(string rawToken)
    {
        var bytes = Convert.FromBase64String(rawToken);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>Admin: adjust balances manually.</summary>
    public async Task<bool> AdjustAsync(Guid walletId, long satsAmount, long cadCentsAmount, string? reason)
    {
        await using var ctx = _dbFactory.CreateContext();
        var wallet = await ctx.CustomerWallets.FirstOrDefaultAsync(w => w.Id == walletId);
        if (wallet == null) return false;

        if (cadCentsAmount != 0)
            wallet.CadBalanceCents += cadCentsAmount;

        // sats adjustment would need TopUpPullPaymentAsync — skip for now, log it
        ctx.WalletTransactions.Add(new WalletTransaction
        {
            CustomerWalletId = walletId,
            Type = WalletTransactionType.ManualAdjust,
            SatsAmount = satsAmount,
            CadCentsAmount = cadCentsAmount,
            ExchangeRate = 0,
            Reference = reason
        });

        await ctx.SaveChangesAsync();
        _logger.LogInformation("Admin adjust wallet {WalletId}: sats={Sats}, cadCents={CadCents}, reason={Reason}",
            walletId, satsAmount, cadCentsAmount, reason);
        return true;
    }

    /// <summary>Count wallets created today for a store (for rate limiting).</summary>
    public async Task<int> CountWalletsCreatedTodayAsync(string storeId)
    {
        await using var ctx = _dbFactory.CreateContext();
        var todayUtc = DateTime.UtcNow.Date;
        return await ctx.CustomerWallets
            .CountAsync(w => w.StoreId == storeId && w.CreatedAt >= todayUtc);
    }

    /// <summary>List all wallets for a store.</summary>
    public async Task<List<CustomerWallet>> GetStoreWalletsAsync(string storeId)
    {
        await using var ctx = _dbFactory.CreateContext();
        return await ctx.CustomerWallets
            .Where(w => w.StoreId == storeId)
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync();
    }
}
