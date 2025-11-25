#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public class BitcoinRewardsRepository
{
    private readonly ApplicationDbContextFactory _dbContextFactory;

    public BitcoinRewardsRepository(ApplicationDbContextFactory dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<BitcoinRewardRecord?> GetRewardAsync(Guid id, string storeId)
    {
        await using var context = _dbContextFactory.CreateContext();
        return await context.Set<BitcoinRewardRecord>()
            .FirstOrDefaultAsync(r => r.Id == id && r.StoreId == storeId);
    }

    public async Task AddRewardAsync(BitcoinRewardRecord reward)
    {
        try
        {
            await using var context = _dbContextFactory.CreateContext();
            await context.Set<BitcoinRewardRecord>().AddAsync(reward);
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
        context.Set<BitcoinRewardRecord>().Update(reward);
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
        var query = context.Set<BitcoinRewardRecord>()
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
        return await context.Set<BitcoinRewardRecord>()
            .Where(r => r.StoreId == storeId && 
                       (r.Status == RewardStatus.Pending || r.Status == RewardStatus.Expired))
            .ToListAsync();
    }

    public async Task<List<BitcoinRewardRecord>> GetExpiredRewardsAsync(string storeId, DateTime before)
    {
        await using var context = _dbContextFactory.CreateContext();
        return await context.Set<BitcoinRewardRecord>()
            .Where(r => r.StoreId == storeId && 
                       r.Status == RewardStatus.Expired &&
                       (r.ExpiresAt == null || r.ExpiresAt < before))
            .ToListAsync();
    }

    public async Task<bool> TransactionExistsAsync(string storeId, string transactionId, RewardPlatform platform)
    {
        await using var context = _dbContextFactory.CreateContext();
        return await context.Set<BitcoinRewardRecord>()
            .AnyAsync(r => r.StoreId == storeId && 
                          r.TransactionId == transactionId && 
                          r.Platform == platform);
    }
}

