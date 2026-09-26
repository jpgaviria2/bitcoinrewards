using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.BitcoinRewards.Data;

public sealed class BitcoinRewardsMigrationRunner : IHostedService
{
    private const string MigrationHistoryTable = "__EFMigrationsHistory";
    private static readonly string PluginSchema = BitcoinRewardsPluginDbContext.DefaultPluginSchema;
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
            await RepairEmptySchemaWithStaleHistoryAsync(ctx, cancellationToken);
            await ctx.Database.MigrateAsync(cancellationToken);
            await ValidateRequiredTablesAsync(ctx, cancellationToken);
            _logger.LogInformation("Bitcoin Rewards plugin migrations completed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run Bitcoin Rewards plugin migrations.");
            throw;
        }
    }

    private async Task RepairEmptySchemaWithStaleHistoryAsync(
        BitcoinRewardsPluginDbContext ctx,
        CancellationToken cancellationToken)
    {
        await ctx.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var countCommand = ctx.Database.GetDbConnection().CreateCommand();
            countCommand.CommandText = """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = @schema
                  AND table_type = 'BASE TABLE'
                  AND table_name <> '__EFMigrationsHistory';
                """;
            AddParameter(countCommand, "schema", PluginSchema);
            var domainTableCount = Convert.ToInt64(await countCommand.ExecuteScalarAsync(cancellationToken));
            if (domainTableCount != 0) return;

            await using var historyCommand = ctx.Database.GetDbConnection().CreateCommand();
            historyCommand.CommandText = """
                SELECT EXISTS (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = @schema
                      AND table_name = @historyTable
                      AND table_type = 'BASE TABLE'
                );
                """;
            AddParameter(historyCommand, "schema", PluginSchema);
            AddParameter(historyCommand, "historyTable", MigrationHistoryTable);
            var historyExists = Convert.ToBoolean(await historyCommand.ExecuteScalarAsync(cancellationToken));
            if (!historyExists) return;

            _logger.LogWarning(
                "Bitcoin Rewards migration history exists but its schema has no plugin tables; clearing only the stale plugin history rows before a clean migration.");
            await using var clearCommand = ctx.Database.GetDbConnection().CreateCommand();
            clearCommand.CommandText =
                "DELETE FROM \"BTCPayServer.Plugins.BitcoinRewards\".\"__EFMigrationsHistory\";";
            await clearCommand.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
    }

    private static async Task ValidateRequiredTablesAsync(
        BitcoinRewardsPluginDbContext ctx,
        CancellationToken cancellationToken)
    {
        await ctx.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = ctx.Database.GetDbConnection().CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM information_schema.tables
                WHERE table_schema = @schema
                  AND table_name IN ('BitcoinRewardRecords', 'PendingLnurlClaims', 'RewardErrors', 'RewardNotificationOutbox');
                """;
            AddParameter(command, "schema", PluginSchema);
            var count = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
            if (count != 4)
                throw new InvalidOperationException(
                    $"Bitcoin Rewards database initialization is incomplete: expected 4 required tables, found {count}.");
        }
        finally
        {
            await ctx.Database.CloseConnectionAsync();
        }
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
