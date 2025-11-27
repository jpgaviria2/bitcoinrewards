#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

/// <summary>
/// Hosted service that runs database migrations for the BitcoinRewards plugin on startup.
/// </summary>
public class BitcoinRewardsMigrationRunner : IHostedService
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
            
            await using var context = _dbContextFactory.CreateContext();
            
            // Ensure the schema exists first
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);
            
            var command = connection.CreateCommand();
            command.CommandText = @"CREATE SCHEMA IF NOT EXISTS ""BTCPayServer.Plugins.BitcoinRewards"";";
            await command.ExecuteNonQueryAsync(cancellationToken);
            
            // Run migrations
            await context.Database.MigrateAsync(cancellationToken);
            
            _logger.LogInformation("Bitcoin Rewards plugin migrations completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run Bitcoin Rewards plugin migrations.");
            // Don't throw - allow the app to continue even if migrations fail
            // This prevents the entire BTCPay Server from failing to start if there's a migration issue
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}



