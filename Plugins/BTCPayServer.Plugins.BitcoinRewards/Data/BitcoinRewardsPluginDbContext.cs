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
            entity.Property(e => e.AutoConvertToCad).HasDefaultValue(true);
            entity.Property(e => e.TotalRewardedSatoshis).HasDefaultValue(0L);
            entity.Property(e => e.TotalRewardedCadCents).HasDefaultValue(0L);
            entity.Property(e => e.IsActive).HasDefaultValue(true);
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

