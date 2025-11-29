using System;
using System.Linq;
using System.Text.Json;
using BTCPayServer.Plugins.BitcoinRewards.Data.Models;
using DotNut;
using DotNut.JsonConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using ISecret = DotNut.ISecret;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

#pragma warning disable CS9113 // Parameter 'designTime' is unread (matches Cashu plugin pattern for design-time compatibility)
public class BitcoinRewardsPluginDbContext(DbContextOptions<BitcoinRewardsPluginDbContext> options, bool designTime = false)
    : DbContext(options)
#pragma warning restore CS9113
{
    public static string DefaultPluginSchema = "BTCPayServer.Plugins.BitcoinRewards";

    public DbSet<BitcoinRewardRecord> BitcoinRewardRecords { get; set; } = null!;
    public DbSet<StoredProof> Proofs { get; set; } = null!;
    public DbSet<Mint> Mints { get; set; } = null!;
    public DbSet<MintKeys> MintKeys { get; set; } = null!;
    public DbSet<FailedTransaction> FailedTransactions { get; set; } = null!;
    public DbSet<ExportedToken> ExportedTokens { get; set; } = null!;

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
            
            // Configure relationship with FailedTransaction (matching Cashu plugin)
            // Note: This is a many-to-many relationship handled through the navigation property
            // No explicit foreign key configuration needed - EF handles it through the navigation
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

        // Configure MintKeys entity (matching Cashu plugin)
        modelBuilder.Entity<MintKeys>(entity =>
        {
            entity.Property(mk => mk.KeysetId).HasConversion(
                kid => kid.ToString(),
                kid => new KeysetId(kid.ToString())
            );
            entity.HasKey(mk => new { mk.MintId, mk.KeysetId });

            entity.HasIndex(mk => mk.MintId);

            entity.HasOne(mk => mk.Mint)
                .WithMany(m => m.Keysets)
                .HasForeignKey(mk => mk.MintId);

            entity.Property(mk => mk.Keyset).HasConversion(
                ks => JsonSerializer.Serialize(ks,
                    new JsonSerializerOptions { Converters = { new KeysetJsonConverter() } }),
                ks => JsonSerializer.Deserialize<Keyset>(ks,
                    new JsonSerializerOptions { Converters = { new KeysetJsonConverter() } }));
        });

        // Configure FailedTransaction entity (matching Cashu plugin)
        modelBuilder.Entity<FailedTransaction>(entity =>
        {
            // Use Id as primary key (matching Cashu plugin exactly)
            entity.HasKey(t => t.Id);
            entity.HasIndex(t => t.InvoiceId);
            entity.OwnsOne(t => t.MeltDetails);
            entity.OwnsOne(t => t.OutputData, fo =>
            {
                // Secrets conversion
                fo.Property(f => f.Secrets)
                    .HasConversion(
                        s => JsonSerializer.Serialize(s, new JsonSerializerOptions
                            { Converters = { new SecretJsonConverter() } }),
                        s => JsonSerializer.Deserialize<ISecret[]>(s, new JsonSerializerOptions
                            { Converters = { new SecretJsonConverter() } })
                    ).Metadata.SetValueComparer(
                        new ValueComparer<ISecret[]>(
                            (c1, c2) => c1.SequenceEqual(c2),
                            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                            c => c.ToArray()));

                // BlindingFactors conversion
                fo.Property(f => f.BlindingFactors)
                    .HasConversion(
                        bf => JsonSerializer.Serialize(bf, new JsonSerializerOptions
                            { Converters = { new PrivKeyJsonConverter() } }),
                        bf => JsonSerializer.Deserialize<PrivKey[]>(bf, new JsonSerializerOptions
                            { Converters = { new PrivKeyJsonConverter() } })
                    ).Metadata.SetValueComparer(
                        new ValueComparer<PrivKey[]>(
                            (c1, c2) => c1.SequenceEqual(c2),
                            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                            c => c.ToArray()));

                // BlindedMessages conversion
                fo.Property(f => f.BlindedMessages)
                    .HasConversion(
                        bm => JsonSerializer.Serialize(bm, (JsonSerializerOptions)null!),
                        bm => JsonSerializer.Deserialize<BlindedMessage[]>(bm, (JsonSerializerOptions)null!)
                    ).Metadata.SetValueComparer(
                        new ValueComparer<BlindedMessage[]>(
                            (c1, c2) => c1.SequenceEqual(c2),
                            c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                            c => c.ToArray()));
            });
        });

        // Configure ExportedToken entity (matching Cashu plugin)
        modelBuilder.Entity<ExportedToken>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.StoreId);
            entity.HasIndex(e => e.Mint);
        });
    }
}

