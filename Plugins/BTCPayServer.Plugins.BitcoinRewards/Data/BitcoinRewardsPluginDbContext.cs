#nullable enable
using System.Text.Json;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Data.Models;
using DotNut;
using DotNut.JsonConverters;
using Microsoft.EntityFrameworkCore;
using ISecret = DotNut.ISecret;

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
    public DbSet<StoredProof> Proofs { get; set; } = null!;
    public DbSet<Mint> Mints { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        
        // Configure BitcoinRewardRecord entity
        builder.Entity<BitcoinRewardRecord>(entity =>
        {
            entity.ToTable("BitcoinRewardRecords", schema: "BTCPayServer.Plugins.BitcoinRewards");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => new { e.StoreId, e.TransactionId, e.Platform });
            entity.HasIndex(e => e.Status);
        });

        // Configure StoredProof entity
        builder.Entity<StoredProof>(entity =>
        {
            entity.ToTable("Proofs", schema: "BTCPayServer.Plugins.BitcoinRewards");
            entity.HasKey(e => e.ProofId);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => e.MintUrl);
            entity.HasIndex(e => new { e.StoreId, e.MintUrl });
            entity.HasIndex(e => e.Id); // Keyset ID
            entity.HasIndex(e => e.Amount);

            // Configure value converters for DotNut types (same as Cashu plugin)
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
        builder.Entity<Mint>(entity =>
        {
            entity.ToTable("Mints", schema: "BTCPayServer.Plugins.BitcoinRewards");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => new { e.StoreId, e.IsActive });
            entity.HasIndex(e => e.Url);
        });
    }
}

