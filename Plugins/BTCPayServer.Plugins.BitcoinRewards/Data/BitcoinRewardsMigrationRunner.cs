using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

/// <summary>
/// Hosted service that runs database migrations for the BitcoinRewards plugin on startup.
/// Matches the pattern used by Cashu plugin's MigrationRunner.
/// </summary>
internal class BitcoinRewardsMigrationRunner : IHostedService
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;

    public BitcoinRewardsMigrationRunner(BitcoinRewardsPluginDbContextFactory dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var ctx = _dbContextFactory.CreateContext();
        await ctx.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}




