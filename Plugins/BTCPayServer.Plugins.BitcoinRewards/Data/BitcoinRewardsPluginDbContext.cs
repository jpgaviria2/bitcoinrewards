using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

#pragma warning disable CS9113 // Parameter 'designTime' is unread (matches Cashu plugin pattern for design-time compatibility)
public class BitcoinRewardsPluginDbContext(DbContextOptions<BitcoinRewardsPluginDbContext> options, bool designTime = false)
    : DbContext(options)
#pragma warning restore CS9113
{
    public static string DefaultPluginSchema = "BTCPayServer.Plugins.BitcoinRewards";

    public DbSet<BitcoinRewardRecord> BitcoinRewardRecords { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(DefaultPluginSchema);

        // Configure BitcoinRewardRecord entity
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

