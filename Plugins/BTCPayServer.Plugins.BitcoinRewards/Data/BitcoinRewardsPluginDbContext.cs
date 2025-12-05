using System;
using System.Linq;
using BTCPayServer.Plugins.BitcoinRewards.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

#pragma warning disable CS9113 // Parameter 'designTime' is unread (matches Cashu plugin pattern for design-time compatibility)
public class BitcoinRewardsPluginDbContext(DbContextOptions<BitcoinRewardsPluginDbContext> options, bool designTime = false)
    : DbContext(options)
#pragma warning restore CS9113
{
    public static string DefaultPluginSchema = "BTCPayServer.Plugins.BitcoinRewards";

    public DbSet<BitcoinRewardRecord> BitcoinRewardRecords { get; set; } = null!;
    public DbSet<RewardsConfig> RewardsConfigs { get; set; } = null!;
    public DbSet<RewardIssue> RewardIssues { get; set; } = null!;
    public DbSet<RewardFundingTx> RewardFundingTxs { get; set; } = null!;

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

        // CDK-based redesign entities
        modelBuilder.Entity<RewardsConfig>(entity =>
        {
            entity.ToTable("RewardsConfigs");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId).IsUnique();
        });

        modelBuilder.Entity<RewardIssue>(entity =>
        {
            entity.ToTable("RewardIssues");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => e.OrderId);
            entity.HasIndex(e => new { e.StoreId, e.OrderId, e.InvoiceId });
            entity.Property(e => e.Status).HasMaxLength(32);
        });

        modelBuilder.Entity<RewardFundingTx>(entity =>
        {
            entity.ToTable("RewardFundingTxs");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.RewardIssueId);
            entity.Property(e => e.FundingSource).HasMaxLength(32);
        });
    }
}

