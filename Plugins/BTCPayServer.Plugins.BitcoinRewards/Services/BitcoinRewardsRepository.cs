#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public class BitcoinRewardsRepository
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;

    public BitcoinRewardsRepository(BitcoinRewardsPluginDbContextFactory dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<BitcoinRewardRecord?> GetRewardAsync(Guid id, string storeId)
    {
        await using var context = _dbContextFactory.CreateContext();
        return await context.BitcoinRewardRecords
            .FirstOrDefaultAsync(r => r.Id == id && r.StoreId == storeId);
    }

    public async Task AddRewardAsync(BitcoinRewardRecord reward)
    {
        try
        {
            await using var context = _dbContextFactory.CreateContext();
            await context.BitcoinRewardRecords.AddAsync(reward);
            await context.SaveChangesAsync();
        }
        catch
        {
            // Database table might not exist yet - log error but don't crash
            throw;
        }
    }

    public async Task UpdateRewardAsync(BitcoinRewardRecord reward)
    {
        await using var context = _dbContextFactory.CreateContext();
        context.BitcoinRewardRecords.Update(reward);
        await context.SaveChangesAsync();
    }

    public async Task<(List<BitcoinRewardRecord> Rewards, int TotalCount)> GetRewardsAsync(
        string storeId,
        int page,
        int pageSize,
        RewardStatus? status = null,
        RewardPlatform? platform = null,
        DateTime? dateFrom = null,
        DateTime? dateTo = null)
    {
        await using var context = _dbContextFactory.CreateContext();
        var query = context.BitcoinRewardRecords
            .Where(r => r.StoreId == storeId);

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        if (platform.HasValue)
            query = query.Where(r => r.Platform == platform.Value);

        if (dateFrom.HasValue)
            query = query.Where(r => r.CreatedAt >= dateFrom.Value);

        if (dateTo.HasValue)
            query = query.Where(r => r.CreatedAt <= dateTo.Value);

        var totalCount = await query.CountAsync();

        var rewards = await query
            .OrderByDescending(r => r.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (rewards, totalCount);
    }

    public async Task<List<BitcoinRewardRecord>> GetUnclaimedRewardsAsync(string storeId)
    {
        await using var context = _dbContextFactory.CreateContext();
        return await context.BitcoinRewardRecords
            .Where(r => r.StoreId == storeId && 
                       (r.Status == RewardStatus.Pending || r.Status == RewardStatus.Expired))
            .ToListAsync();
    }

    public async Task<List<BitcoinRewardRecord>> GetExpiredRewardsAsync(string storeId, DateTime before)
    {
        await using var context = _dbContextFactory.CreateContext();
        return await context.BitcoinRewardRecords
            .Where(r => r.StoreId == storeId && 
                       r.Status == RewardStatus.Expired &&
                       (r.ExpiresAt == null || r.ExpiresAt < before))
            .ToListAsync();
    }

    public async Task<bool> TransactionExistsAsync(string storeId, string transactionId, RewardPlatform platform)
    {
        await using var context = _dbContextFactory.CreateContext();
        return await context.BitcoinRewardRecords
            .AnyAsync(r => r.StoreId == storeId && 
                          r.TransactionId == transactionId && 
                          r.Platform == platform);
    }
    
    /// <summary>
    /// Returns orphaned reward records that have an OrderId but are missing ClaimLink/PullPaymentId.
    /// These are records where the DB insert succeeded but the pull payment info was never persisted.
    /// </summary>
    public async Task<List<BitcoinRewardRecord>> GetOrphanedRewardsAsync(string storeId)
    {
        await using var context = _dbContextFactory.CreateContext();
        return await context.BitcoinRewardRecords
            .Where(r => r.StoreId == storeId &&
                       r.Status == RewardStatus.Pending &&
                       !string.IsNullOrEmpty(r.OrderId) &&
                       (r.ClaimLink == null || r.PullPaymentId == null))
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    /// <summary>
    /// Get a reward record by transaction ID and platform (for duplicate/recovery lookup).
    /// </summary>
    public async Task<BitcoinRewardRecord?> GetRewardByTransactionAsync(string storeId, string transactionId, RewardPlatform platform)
    {
        await using var context = _dbContextFactory.CreateContext();
        return await context.BitcoinRewardRecords
            .FirstOrDefaultAsync(r => r.StoreId == storeId &&
                                     r.TransactionId == transactionId &&
                                     r.Platform == platform);
    }

    public async Task<BitcoinRewardRecord?> GetLatestUnclaimedRewardAsync(string storeId, int timeframeMinutes, int displayTimeoutSeconds)
    {
        await using var context = _dbContextFactory.CreateContext();
        var cutoffTime = DateTime.UtcNow.AddMinutes(-timeframeMinutes);
        var timeoutCutoff = DateTime.UtcNow.AddSeconds(-displayTimeoutSeconds);
        
        return await context.BitcoinRewardRecords
            .Where(r => r.StoreId == storeId && 
                       (r.Status == RewardStatus.Pending || r.Status == RewardStatus.Sent) &&
                       r.CreatedAt >= cutoffTime &&
                       r.CreatedAt >= timeoutCutoff && // Only show rewards that haven't timed out
                       !string.IsNullOrEmpty(r.ClaimLink))
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync();
    }
}

