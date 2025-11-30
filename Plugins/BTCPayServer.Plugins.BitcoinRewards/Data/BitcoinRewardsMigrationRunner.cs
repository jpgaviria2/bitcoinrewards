#nullable enable
using System;
using System.Linq;
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
            var wasOpen = connection.State == System.Data.ConnectionState.Open;
            if (!wasOpen)
            {
                await connection.OpenAsync(cancellationToken);
            }
            
            var command = connection.CreateCommand();
            command.CommandText = @"CREATE SCHEMA IF NOT EXISTS ""BTCPayServer.Plugins.BitcoinRewards"";";
            await command.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogDebug("Schema 'BTCPayServer.Plugins.BitcoinRewards' ensured");
            
            // Check if migrations are pending
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingList = pendingMigrations.ToList();
            if (pendingList.Any())
            {
                _logger.LogInformation("Found {Count} pending migrations: {Migrations}", 
                    pendingList.Count, string.Join(", ", pendingList));
            }
            else
            {
                _logger.LogInformation("No pending migrations found");
            }
            
            // Check current migration
            var appliedMigrations = await context.Database.GetAppliedMigrationsAsync(cancellationToken);
            var appliedList = appliedMigrations.ToList();
            if (appliedList.Any())
            {
                _logger.LogInformation("Applied migrations: {Migrations}", string.Join(", ", appliedList));
            }
            
            // Run migrations
            await context.Database.MigrateAsync(cancellationToken);
            
            // Verify table exists after migration
            var verifyCommand = connection.CreateCommand();
            verifyCommand.CommandText = @"
                SELECT COUNT(*) 
                FROM information_schema.tables 
                WHERE table_schema = 'BTCPayServer.Plugins.BitcoinRewards' 
                AND table_name = 'BitcoinRewardRecords'";
            var tableExists = await verifyCommand.ExecuteScalarAsync(cancellationToken);
            var tableCount = Convert.ToInt32(tableExists);
            
            if (tableCount == 0)
            {
                _logger.LogWarning("Table BitcoinRewardRecords does not exist after migration. Attempting to create manually...");
                
                // Use a transaction to ensure the table creation is committed
                using var transaction = connection.BeginTransaction();
                try
                {
                    // Manually create the table if migration didn't create it
                    var createTableCommand = connection.CreateCommand();
                    createTableCommand.Transaction = transaction;
                    createTableCommand.CommandText = @"
                        CREATE TABLE IF NOT EXISTS ""BTCPayServer.Plugins.BitcoinRewards"".""BitcoinRewardRecords"" (
                            ""Id"" uuid NOT NULL,
                            ""StoreId"" character varying(50) NOT NULL,
                            ""Platform"" integer NOT NULL,
                            ""TransactionId"" character varying(255) NOT NULL,
                            ""OrderId"" character varying(255),
                            ""CustomerEmail"" character varying(255),
                            ""CustomerPhone"" character varying(50),
                            ""TransactionAmount"" numeric(18,8) NOT NULL,
                            ""Currency"" character varying(10) NOT NULL,
                            ""RewardAmount"" numeric(18,8) NOT NULL,
                            ""RewardAmountSatoshis"" bigint NOT NULL,
                            ""EcashToken"" character varying(10000),
                            ""Status"" integer NOT NULL,
                            ""CreatedAt"" timestamp with time zone NOT NULL,
                            ""SentAt"" timestamp with time zone,
                            ""RedeemedAt"" timestamp with time zone,
                            ""ExpiresAt"" timestamp with time zone,
                            ""ErrorMessage"" character varying(500),
                            ""RetryCount"" integer NOT NULL,
                            CONSTRAINT ""PK_BitcoinRewardRecords"" PRIMARY KEY (""Id"")
                        );
                        
                        CREATE INDEX IF NOT EXISTS ""IX_BitcoinRewardRecords_StoreId"" 
                            ON ""BTCPayServer.Plugins.BitcoinRewards"".""BitcoinRewardRecords"" (""StoreId"");
                        
                        CREATE INDEX IF NOT EXISTS ""IX_BitcoinRewardRecords_Status"" 
                            ON ""BTCPayServer.Plugins.BitcoinRewards"".""BitcoinRewardRecords"" (""Status"");
                        
                        CREATE INDEX IF NOT EXISTS ""IX_BitcoinRewardRecords_StoreId_TransactionId_Platform"" 
                            ON ""BTCPayServer.Plugins.BitcoinRewards"".""BitcoinRewardRecords"" (""StoreId"", ""TransactionId"", ""Platform"");";
                    
                    await createTableCommand.ExecuteNonQueryAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    _logger.LogInformation("Table BitcoinRewardRecords created manually and committed.");
                    
                    // Verify it was created
                    var verifyAgainCommand = connection.CreateCommand();
                    verifyAgainCommand.CommandText = @"
                        SELECT COUNT(*) 
                        FROM information_schema.tables 
                        WHERE table_schema = 'BTCPayServer.Plugins.BitcoinRewards' 
                        AND table_name = 'BitcoinRewardRecords'";
                    var verifyAgain = await verifyAgainCommand.ExecuteScalarAsync(cancellationToken);
                    var verifyCount = Convert.ToInt32(verifyAgain);
                    if (verifyCount > 0)
                    {
                        _logger.LogInformation("Table BitcoinRewardRecords verified after manual creation.");
                    }
                    else
                    {
                        _logger.LogError("Table BitcoinRewardRecords still does not exist after manual creation attempt!");
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogError(ex, "Failed to create table BitcoinRewardRecords manually. Error: {Message}", ex.Message);
                    throw;
                }
            }
            else
            {
                _logger.LogInformation("Table BitcoinRewardRecords verified to exist.");
            }
            
            if (!wasOpen)
            {
                await connection.CloseAsync();
            }
            
            _logger.LogInformation("Bitcoin Rewards plugin migrations completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run Bitcoin Rewards plugin migrations. Error: {Message}. StackTrace: {StackTrace}", 
                ex.Message, ex.StackTrace);
            // Don't throw - allow the app to continue even if migrations fail
            // This prevents the entire BTCPay Server from failing to start if there's a migration issue
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}



