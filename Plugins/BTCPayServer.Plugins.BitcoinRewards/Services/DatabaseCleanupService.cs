#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

/// <summary>
/// Service to handle database cleanup when the plugin is uninstalled.
/// Note: BTCPay Server does not provide an automatic uninstall hook,
/// so this service provides manual cleanup methods.
/// </summary>
public class DatabaseCleanupService
{
    private readonly BitcoinRewardsPluginDbContextFactory _dbContextFactory;
    private readonly ILogger<DatabaseCleanupService> _logger;

    public DatabaseCleanupService(
        BitcoinRewardsPluginDbContextFactory dbContextFactory,
        ILogger<DatabaseCleanupService> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    /// <summary>
    /// Drops the entire plugin database schema.
    /// WARNING: This permanently deletes all plugin data!
    /// </summary>
    public async Task DropSchemaAsync()
    {
        try
        {
            await using var context = _dbContextFactory.CreateContext();
            var connection = context.Database.GetDbConnection();
            
            await connection.OpenAsync();
            
            // Drop the schema and all its objects
            var command = connection.CreateCommand();
            command.CommandText = @"DROP SCHEMA IF EXISTS ""BTCPayServer.Plugins.BitcoinRewards"" CASCADE;";
            
            await command.ExecuteNonQueryAsync();
            
            _logger.LogInformation("Bitcoin Rewards plugin schema dropped successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to drop Bitcoin Rewards plugin schema.");
            throw;
        }
    }

    /// <summary>
    /// Checks if the plugin schema exists in the database.
    /// </summary>
    public async Task<bool> SchemaExistsAsync()
    {
        try
        {
            await using var context = _dbContextFactory.CreateContext();
            var connection = context.Database.GetDbConnection();
            
            await connection.OpenAsync();
            
            var command = connection.CreateCommand();
            command.CommandText = @"
                SELECT COUNT(*) 
                FROM information_schema.schemata 
                WHERE schema_name = 'BTCPayServer.Plugins.BitcoinRewards';";
            
            var result = await command.ExecuteScalarAsync();
            var count = Convert.ToInt32(result);
            
            return count > 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if Bitcoin Rewards plugin schema exists.");
            return false;
        }
    }
}

