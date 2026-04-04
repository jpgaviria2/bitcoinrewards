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

        // Ensure RewardErrors table exists regardless of EF migration state.
        // EF Core migrations can silently fail or get out of sync with the actual
        // database schema. This raw SQL is idempotent and completely isolated.
        try
        {
            await EnsureRewardErrorsTableAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure RewardErrors table exists.");
        }
    }

    private async Task EnsureRewardErrorsTableAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS "BTCPayServer.Plugins.BitcoinRewards"."RewardErrors" (
                "Id" character varying(36) NOT NULL,
                "ErrorType" character varying(100) NOT NULL,
                "Message" character varying(2000) NOT NULL,
                "StackTrace" text,
                "OrderId" character varying(255),
                "StoreId" character varying(255),
                "RewardId" character varying(36),
                "Context" text,
                "UserId" character varying(255),
                "Timestamp" timestamp with time zone NOT NULL,
                "Resolved" boolean NOT NULL DEFAULT false,
                "ResolvedAt" timestamp with time zone,
                "ResolvedBy" character varying(255),
                "ResolutionNotes" character varying(1000),
                "RetryCount" integer NOT NULL DEFAULT 0,
                "LastRetryAt" timestamp with time zone,
                CONSTRAINT "PK_RewardErrors" PRIMARY KEY ("Id")
            );
            CREATE INDEX IF NOT EXISTS "IX_RewardErrors_ErrorType"
                ON "BTCPayServer.Plugins.BitcoinRewards"."RewardErrors" ("ErrorType");
            CREATE INDEX IF NOT EXISTS "IX_RewardErrors_StoreId"
                ON "BTCPayServer.Plugins.BitcoinRewards"."RewardErrors" ("StoreId");
            CREATE INDEX IF NOT EXISTS "IX_RewardErrors_OrderId"
                ON "BTCPayServer.Plugins.BitcoinRewards"."RewardErrors" ("OrderId");
            CREATE INDEX IF NOT EXISTS "IX_RewardErrors_RewardId"
                ON "BTCPayServer.Plugins.BitcoinRewards"."RewardErrors" ("RewardId");
            CREATE INDEX IF NOT EXISTS "IX_RewardErrors_Timestamp"
                ON "BTCPayServer.Plugins.BitcoinRewards"."RewardErrors" ("Timestamp");
            CREATE INDEX IF NOT EXISTS "IX_RewardErrors_Resolved_Timestamp"
                ON "BTCPayServer.Plugins.BitcoinRewards"."RewardErrors" ("Resolved", "Timestamp");
            """;

        await using var ctx = _dbContextFactory.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        _logger.LogInformation("RewardErrors table ensured.");
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}




