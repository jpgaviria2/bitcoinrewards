#nullable enable
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

/// <summary>
/// DbContextFactory for the BitcoinRewards plugin DbContext.
/// Uses the plugin's own schema.
/// </summary>
public class BitcoinRewardsPluginDbContextFactory : BaseDbContextFactory<BitcoinRewardsPluginDbContext>
{
    public BitcoinRewardsPluginDbContextFactory(IOptions<DatabaseOptions> options, ILoggerFactory loggerFactory) 
        : base(options, "BTCPayServer.Plugins.BitcoinRewards")
    {
        LoggerFactory = loggerFactory;
    }

    public ILoggerFactory LoggerFactory { get; }

    public override BitcoinRewardsPluginDbContext CreateContext(System.Action<Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.NpgsqlDbContextOptionsBuilder>? npgsqlOptionsAction = null)
    {
        var builder = new DbContextOptionsBuilder<BitcoinRewardsPluginDbContext>();
        builder.UseLoggerFactory(LoggerFactory);
        ConfigureBuilder(builder, npgsqlOptionsAction);
        return new BitcoinRewardsPluginDbContext(builder.Options);
    }
}

