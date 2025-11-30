#nullable enable
using BTCPayServer.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

public static class BitcoinRewardsDbContextExtensions
{
    public static void AddBitcoinRewardsTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BitcoinRewardRecord>(entity =>
        {
            entity.ToTable("BitcoinRewardRecords");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => new { e.StoreId, e.TransactionId, e.Platform });
            entity.HasIndex(e => e.Status);
        });
    }
}

