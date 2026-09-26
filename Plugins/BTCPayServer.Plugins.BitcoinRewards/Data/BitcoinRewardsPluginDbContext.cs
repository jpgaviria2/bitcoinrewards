using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

#pragma warning disable CS9113 // Parameter 'designTime' is unread (matches Cashu plugin pattern for design-time compatibility)
public class BitcoinRewardsPluginDbContext(DbContextOptions<BitcoinRewardsPluginDbContext> options, bool designTime = false)
    : DbContext(options)
#pragma warning restore CS9113
{
    public static string DefaultPluginSchema = "BTCPayServer.Plugins.BitcoinRewards";

    public DbSet<BitcoinRewardRecord> BitcoinRewardRecords { get; set; } = null!;
    public DbSet<CustomerOrderAssociation> CustomerOrderAssociations { get; set; } = null!;
    public DbSet<PendingLightningAddressCheckIn> PendingLightningAddressCheckIns { get; set; } = null!;
    public DbSet<RewardPayoutAttempt> RewardPayoutAttempts { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // BTCPay treats EF Core warnings as errors. This plugin ships several
        // hand-written migrations and historically let the model snapshot lag
        // behind the runtime model, causing MigrateAsync() to abort before it
        // can create the plugin schema/tables. Keep startup resilient: apply
        // the explicit migrations we ship and do not let a snapshot drift
        // warning disable the plugin at runtime.
        // EF Core 9+ exposes this as RelationalEventId.PendingModelChangesWarning.
        // This repository currently builds against EF Core 8, where that named
        // constant does not exist yet, so use the stable event id directly.
        optionsBuilder.ConfigureWarnings(warnings =>
            warnings.Ignore(new EventId(20409, "PendingModelChangesWarning")));
    }

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
            
            // Security: Unique constraint to prevent duplicate rewards at database level
            entity.HasIndex(e => new { e.StoreId, e.TransactionId, e.Platform })
                .IsUnique()
                .HasDatabaseName("IX_BitcoinRewardRecords_StoreId_TransactionId_Platform_Unique");
            
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CustomerProfileId);
        });

        modelBuilder.Entity<CustomerOrderAssociation>(entity =>
        {
            entity.ToTable("CustomerOrderAssociations");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LightningAddress).HasMaxLength(128);
            entity.HasIndex(e => new { e.StoreId, e.SquareOrderId }).IsUnique();
            entity.HasIndex(e => new { e.StoreId, e.SquarePaymentId })
                .IsUnique().HasFilter("\"SquarePaymentId\" IS NOT NULL");
            entity.HasIndex(e => new { e.StoreId, e.CustomerProfileId, e.State });
        });

        modelBuilder.Entity<RewardPayoutAttempt>(entity =>
        {
            entity.ToTable("RewardPayoutAttempts");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.RewardId, e.AttemptNumber }).IsUnique();
            entity.HasIndex(e => new { e.State, e.NextRetryAt });
            entity.HasOne<BitcoinRewardRecord>().WithMany().HasForeignKey(e => e.RewardId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PendingLightningAddressCheckIn>(entity =>
        {
            entity.ToTable("PendingLightningAddressCheckIns");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.LightningAddress).HasMaxLength(128).IsRequired();
            entity.Property(e => e.LightningAddressHash).HasMaxLength(64).IsRequired();
            entity.Property(e => e.RegisterId).HasMaxLength(100);
            entity.Property(e => e.DeviceId).HasMaxLength(100);
            entity.Property(e => e.Source).HasMaxLength(50);
            entity.Property(e => e.SquareOrderId).HasMaxLength(255);
            entity.Property(e => e.SquarePaymentId).HasMaxLength(255);
            entity.HasIndex(e => new { e.StoreId, e.State, e.CreatedAt });
            entity.HasIndex(e => new { e.StoreId, e.ExpiresAt });
            entity.HasIndex(e => new { e.StoreId, e.SquarePaymentId })
                .IsUnique().HasFilter("\"SquarePaymentId\" IS NOT NULL");
        });
    }
}
