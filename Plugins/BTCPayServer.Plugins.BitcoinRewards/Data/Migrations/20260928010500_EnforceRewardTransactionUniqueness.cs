using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations;

public partial class EnforceRewardTransactionUniqueness : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($@"
WITH ranked AS (
    SELECT ""Id"",
           row_number() OVER (
               PARTITION BY ""StoreId"", ""TransactionId"", ""Platform""
               ORDER BY ""CreatedAt"" DESC, ""Id"" DESC
           ) AS rn
    FROM ""{BitcoinRewardsPluginDbContext.DefaultPluginSchema}"".""BitcoinRewardRecords""
)
DELETE FROM ""{BitcoinRewardsPluginDbContext.DefaultPluginSchema}"".""BitcoinRewardRecords"" r
USING ranked d
WHERE r.""Id"" = d.""Id"" AND d.rn > 1;

CREATE UNIQUE INDEX IF NOT EXISTS ""IX_BitcoinRewardRecords_StoreId_TransactionId_Platform_Unique""
ON ""{BitcoinRewardsPluginDbContext.DefaultPluginSchema}"".""BitcoinRewardRecords"" (""StoreId"", ""TransactionId"", ""Platform"");
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql($@"
DROP INDEX IF EXISTS ""{BitcoinRewardsPluginDbContext.DefaultPluginSchema}"".""IX_BitcoinRewardRecords_StoreId_TransactionId_Platform_Unique"";
");
    }
}
