#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using BTCPayServer.Plugins.BitcoinRewards.Services;
using BTCPayServer.Plugins.BitcoinRewards.ViewModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace BTCPayServer.Plugins.BitcoinRewards.Tests;

public sealed class RewardsPartBFoundationTests
{
    [Theory]
    [InlineData("alice@pay.example.com", "alice@pay.example.com")]
    [InlineData(" ALICE@PAY.EXAMPLE.COM ", "alice@pay.example.com")]
    [InlineData("alice@example.org", "alice@example.org")]
    public void Lightning_address_normalization_accepts_valid_lightning_addresses(string input, string expected) =>
        Assert.Equal(expected, CustomerProfileClient.NormalizeLightningAddress(input));

    [Theory]
    [InlineData("npub1qqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqqq")]
    [InlineData("https://pay.example.com/alice")]
    [InlineData("")]
    public void Lightning_address_normalization_rejects_non_lightning_addresses(string input) =>
        Assert.Throws<ArgumentException>(() => CustomerProfileClient.NormalizeLightningAddress(input));

    [Fact]
    public void Profile_api_base_url_rejects_unsafe_origins()
    {
        Assert.Equal("https://profiles.example.com/", CustomerProfileClient.ValidateBaseUri("https://profiles.example.com/path").ToString());
        Assert.Throws<InvalidOperationException>(() => CustomerProfileClient.ValidateBaseUri("http://profiles.example.com"));
        Assert.Throws<InvalidOperationException>(() => CustomerProfileClient.ValidateBaseUri("https://user:pass@profiles.example.com"));
        Assert.Throws<InvalidOperationException>(() => CustomerProfileClient.ValidateBaseUri("https://profiles.example.com?token=secret"));
    }

    [Fact]
    public void Settings_preserve_hidden_token_and_cannot_enable_direct_payout()
    {
        var existing = new BitcoinRewardsStoreSettings
        {
            CustomerProfileApiToken = "stored-secret",
            DirectLightningPayoutEnabled = true
        };
        var model = new BitcoinRewardsSettingsViewModel
        {
            CustomerProfileAssociationEnabled = true,
            CustomerProfileApiBaseUrl = "https://profiles.example.com",
            CustomerProfileApiToken = null,
            LegacyPullPaymentFallbackEnabled = true
        };
        var updated = model.ToSettings(existing);
        Assert.Equal("stored-secret", updated.CustomerProfileApiToken);
        Assert.True(updated.CustomerProfileAssociationEnabled);
        Assert.True(updated.LegacyPullPaymentFallbackEnabled);
        Assert.False(updated.DirectLightningPayoutEnabled);
    }

    [Fact]
    public void Association_and_payout_models_enforce_exact_uniqueness()
    {
        var options = new DbContextOptionsBuilder<BitcoinRewardsPluginDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var context = new BitcoinRewardsPluginDbContext(options);
        var association = context.Model.FindEntityType(typeof(CustomerOrderAssociation))!;
        Assert.Contains(association.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { "StoreId", "SquareOrderId" }));
        Assert.Contains(association.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { "StoreId", "SquarePaymentId" }));
        Assert.True(association.FindProperty(nameof(CustomerOrderAssociation.UpdatedAt))!.IsConcurrencyToken);

        var payout = context.Model.FindEntityType(typeof(RewardPayoutAttempt))!;
        Assert.Contains(payout.GetIndexes(), index => index.IsUnique &&
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { "RewardId", "AttemptNumber" }));
    }

    [Fact]
    public void Part_b_migration_has_a_stable_discoverable_identifier()
    {
        var migration = typeof(Data.Migrations.AddRewardsPartBFoundation);
        var attribute = (MigrationAttribute?)Attribute.GetCustomAttribute(migration, typeof(MigrationAttribute));
        Assert.Equal("20260924000000_AddRewardsPartBFoundation", attribute?.Id);
    }

    [Fact]
    public async Task Part_b_migration_applies_reverts_and_reapplies_on_postgres()
    {
        var connectionString = Environment.GetEnvironmentVariable("BITCOIN_REWARDS_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        var options = new DbContextOptionsBuilder<BitcoinRewardsPluginDbContext>()
            .UseNpgsql(connectionString, builder => builder.MigrationsHistoryTable(
                "__EFMigrationsHistory", BitcoinRewardsPluginDbContext.DefaultPluginSchema))
            .Options;
        await using var context = new BitcoinRewardsPluginDbContext(options);
        var migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync("20260924000000_AddRewardsPartBFoundation");
        Assert.Contains("20260924000000_AddRewardsPartBFoundation", await context.Database.GetAppliedMigrationsAsync());

        await migrator.MigrateAsync(Migration.InitialDatabase);
        Assert.DoesNotContain("20260924000000_AddRewardsPartBFoundation", await context.Database.GetAppliedMigrationsAsync());

        await migrator.MigrateAsync("20260924000000_AddRewardsPartBFoundation");
        Assert.Contains("20260924000000_AddRewardsPartBFoundation", await context.Database.GetAppliedMigrationsAsync());
    }
}
