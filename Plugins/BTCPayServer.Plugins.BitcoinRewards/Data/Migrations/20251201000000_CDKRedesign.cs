using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations;

public partial class CDKRedesign : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Ensure schema exists
        migrationBuilder.EnsureSchema(
            name: BitcoinRewardsPluginDbContext.DefaultPluginSchema);

        migrationBuilder.CreateTable(
            name: "RewardsConfigs",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                FundingSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                RewardsPercentage = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                MaxRewardSats = table.Column<long>(type: "bigint", nullable: true),
                MintUrl = table.Column<string>(type: "text", nullable: false),
                Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                EmailSubjectTemplate = table.Column<string>(type: "text", nullable: false),
                EmailBodyTemplate = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RewardsConfigs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RewardsConfigs_StoreId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "RewardsConfigs",
            column: "StoreId",
            unique: true);

        migrationBuilder.CreateTable(
            name: "RewardIssues",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                OrderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                InvoiceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CustomerEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                AmountSats = table.Column<long>(type: "bigint", nullable: false),
                Token = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Error = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ClaimedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RewardIssues", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RewardIssues_StoreId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "RewardIssues",
            column: "StoreId");

        migrationBuilder.CreateIndex(
            name: "IX_RewardIssues_OrderId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "RewardIssues",
            column: "OrderId");

        migrationBuilder.CreateIndex(
            name: "IX_RewardIssues_Store_Order_Invoice",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "RewardIssues",
            columns: new[] { "StoreId", "OrderId", "InvoiceId" });

        migrationBuilder.CreateTable(
            name: "RewardFundingTxs",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RewardIssueId = table.Column<Guid>(type: "uuid", nullable: false),
                FundingSource = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Reference = table.Column<string>(type: "text", nullable: false),
                DetailsJson = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RewardFundingTxs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RewardFundingTxs_RewardIssueId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "RewardFundingTxs",
            column: "RewardIssueId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RewardFundingTxs",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);

        migrationBuilder.DropTable(
            name: "RewardIssues",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);

        migrationBuilder.DropTable(
            name: "RewardsConfigs",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);
    }
}


