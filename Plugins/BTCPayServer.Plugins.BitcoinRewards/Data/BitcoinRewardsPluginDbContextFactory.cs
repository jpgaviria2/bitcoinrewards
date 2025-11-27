using System;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

/// <summary>
/// DbContextFactory for the BitcoinRewards plugin DbContext.
/// Uses the plugin's own schema.
/// Matches the pattern used by Cashu plugin's CashuDbContextFactory.
/// </summary>
public class BitcoinRewardsPluginDbContextFactory : BaseDbContextFactory<BitcoinRewardsPluginDbContext>
{
    public BitcoinRewardsPluginDbContextFactory(IOptions<DatabaseOptions> options)
        : base(options, BitcoinRewardsPluginDbContext.DefaultPluginSchema)
    {
    }

    public override BitcoinRewardsPluginDbContext CreateContext(
        Action<NpgsqlDbContextOptionsBuilder> npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<BitcoinRewardsPluginDbContext>();
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new BitcoinRewardsPluginDbContext(builder.Options);
    }
}

