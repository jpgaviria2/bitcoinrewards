#nullable enable
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

/// <summary>
/// Plugin-specific DbContext for BitcoinRewards entities.
/// This uses a separate schema to avoid conflicts with the main ApplicationDbContext.
/// </summary>
public class BitcoinRewardsPluginDbContext : DbContext
{
    public BitcoinRewardsPluginDbContext(DbContextOptions<BitcoinRewardsPluginDbContext> options)
        : base(options)
    {
    }

    public DbSet<BitcoinRewardRecord> BitcoinRewardRecords { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        
        // Configure entity
        builder.Entity<BitcoinRewardRecord>(entity =>
        {
            entity.ToTable("BitcoinRewardRecords", schema: "BTCPayServer.Plugins.BitcoinRewards");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => new { e.StoreId, e.TransactionId, e.Platform });
            entity.HasIndex(e => e.Status);
        });
    }
}

