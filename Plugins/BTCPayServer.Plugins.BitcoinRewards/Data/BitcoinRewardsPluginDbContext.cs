using System.Text.Json;
using BTCPayServer.Plugins.BitcoinRewards.Data.Models;
using DotNut;
using DotNut.JsonConverters;
using Microsoft.EntityFrameworkCore;
using ISecret = DotNut.ISecret;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

/// <summary>
/// Plugin-specific DbContext for BitcoinRewards entities.
/// This uses a separate schema to avoid conflicts with the main ApplicationDbContext.
/// Matches the pattern used by Cashu plugin's CashuDbContext.
/// </summary>
public class BitcoinRewardsPluginDbContext : DbContext
{
    public static string DefaultPluginSchema = "BTCPayServer.Plugins.BitcoinRewards";
    
    public BitcoinRewardsPluginDbContext(DbContextOptions<BitcoinRewardsPluginDbContext> options)
        : base(options)
    {
    }

    public DbSet<BitcoinRewardRecord> BitcoinRewardRecords { get; set; } = null!;
    public DbSet<StoredProof> Proofs { get; set; } = null!;
    public DbSet<Mint> Mints { get; set; } = null!;

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

        // Configure StoredProof entity - exactly like Cashu plugin
        modelBuilder.Entity<StoredProof>(entity =>
        {
            entity.ToTable("Proofs");
            entity.HasKey(sk => sk.ProofId);
            entity.HasIndex(sk => sk.Id);
            entity.HasIndex(sk => sk.StoreId);
            entity.HasIndex(sk => sk.Amount);
            entity.HasIndex(sk => sk.MintUrl);
            entity.HasIndex(sk => new { sk.StoreId, sk.MintUrl });

            entity.Property(p => p.C)
                .HasConversion(
                    pk => pk.ToString(),
                    pk => new PubKey(pk, false)
                );
            entity.Property(p => p.Id)
                .HasConversion(
                    ki => ki.ToString(),
                    ki => new KeysetId(ki.ToString())
                );
            entity.Property(p => p.Secret)
                .HasConversion(
                    s => JsonSerializer.Serialize(s,
                        new JsonSerializerOptions { Converters = { new SecretJsonConverter() } }),
                    s => JsonSerializer.Deserialize<ISecret>(s,
                        new JsonSerializerOptions { Converters = { new SecretJsonConverter() } })
                );
            entity.Property(p => p.DLEQ)
                .HasConversion(
                    d => d == null ? null : JsonSerializer.Serialize(d, (JsonSerializerOptions)null!),
                    d => d == null ? null : JsonSerializer.Deserialize<DLEQProof>(d, (JsonSerializerOptions)null!)
                );
        });

        // Configure Mint entity
        modelBuilder.Entity<Mint>(entity =>
        {
            entity.ToTable("Mints");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => new { e.StoreId, e.IsActive });
            entity.HasIndex(e => e.Url);
        });
    }
}

