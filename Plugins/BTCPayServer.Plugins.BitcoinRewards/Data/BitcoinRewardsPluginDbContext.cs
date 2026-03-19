using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

#pragma warning disable CS9113 // Parameter 'designTime' is unread (matches Cashu plugin pattern for design-time compatibility)
public class BitcoinRewardsPluginDbContext(DbContextOptions<BitcoinRewardsPluginDbContext> options, bool designTime = false)
    : DbContext(options)
#pragma warning restore CS9113
{
    public static string DefaultPluginSchema = "BTCPayServer.Plugins.BitcoinRewards";

    public DbSet<BitcoinRewardRecord> BitcoinRewardRecords { get; set; } = null!;
    public DbSet<BoltCardLink> BoltCardLinks { get; set; } = null!;
    public DbSet<CustomerWallet> CustomerWallets { get; set; } = null!;
    public DbSet<WalletTransaction> WalletTransactions { get; set; } = null!;
    public DbSet<PendingLnurlClaim> PendingLnurlClaims { get; set; } = null!;

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
        });

        // Configure BoltCardLink entity
        modelBuilder.Entity<BoltCardLink>(entity =>
        {
            entity.ToTable("BoltCardLinks");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => e.PullPaymentId);
            entity.HasIndex(e => e.BoltcardId);
            entity.HasIndex(e => new { e.StoreId, e.PullPaymentId })
                .IsUnique()
                .HasDatabaseName("IX_BoltCardLinks_StoreId_PullPaymentId_Unique");
        });

        // Configure CustomerWallet entity
        modelBuilder.Entity<CustomerWallet>(entity =>
        {
            entity.ToTable("CustomerWallets");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => e.PullPaymentId);
            entity.HasIndex(e => e.BoltcardId);
            entity.HasIndex(e => e.CardUid);
            entity.HasIndex(e => e.ApiTokenHash);
            entity.HasIndex(e => new { e.StoreId, e.PullPaymentId })
                .IsUnique()
                .HasDatabaseName("IX_CustomerWallets_StoreId_PullPaymentId_Unique");
            entity.Property(e => e.CadBalanceCents).HasDefaultValue(0L);
            entity.Property(e => e.SatsBalanceSatoshis).HasDefaultValue(0L);
            entity.Property(e => e.AutoConvertToCad).HasDefaultValue(true);
            entity.Property(e => e.TotalRewardedSatoshis).HasDefaultValue(0L);
            entity.Property(e => e.TotalRewardedCadCents).HasDefaultValue(0L);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
        });

        // Configure PendingLnurlClaim entity
        modelBuilder.Entity<PendingLnurlClaim>(entity =>
        {
            entity.ToTable("PendingLnurlClaims");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.IsCompleted, e.IsFailed, e.ExpiresAt })
                .HasDatabaseName("IX_PendingLnurlClaims_Status_ExpiresAt");
            entity.HasIndex(e => e.CustomerWalletId);
            entity.Property(e => e.StoreId).HasMaxLength(50);
            entity.Property(e => e.LightningInvoiceId).HasMaxLength(255);
            entity.Property(e => e.Bolt11).HasMaxLength(2000);
            entity.Property(e => e.K1Prefix).HasMaxLength(20);
            entity.Property(e => e.IsCompleted).HasDefaultValue(false);
            entity.Property(e => e.IsFailed).HasDefaultValue(false);
        });

        // Configure WalletTransaction entity
        modelBuilder.Entity<WalletTransaction>(entity =>
        {
            entity.ToTable("WalletTransactions");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.CustomerWalletId);
            entity.HasIndex(e => e.CreatedAt);
            entity.Property(e => e.SatsAmount).HasDefaultValue(0L);
            entity.Property(e => e.CadCentsAmount).HasDefaultValue(0L);
            entity.HasOne<CustomerWallet>()
                .WithMany()
                .HasForeignKey(e => e.CustomerWalletId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}

