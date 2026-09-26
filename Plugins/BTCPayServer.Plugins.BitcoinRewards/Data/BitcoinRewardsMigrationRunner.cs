using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

internal class BitcoinRewardsMigrationRunner : IHostedService
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly ILogger<BitcoinRewardsMigrationRunner> _logger;

    public BitcoinRewardsMigrationRunner(
        BitcoinRewardsPluginDbContextFactory dbContextFactory,
        ILogger<BitcoinRewardsMigrationRunner> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Running Bitcoin Rewards plugin migrations...");
            await using var ctx = _dbContextFactory.CreateContext();
            await ctx.Database.MigrateAsync(cancellationToken);
            _logger.LogInformation("Bitcoin Rewards plugin migrations completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run Bitcoin Rewards plugin migrations.");
            // Don't throw - allow plugin to continue loading even if migrations fail
            // Migrations will be retried on next startup
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}




