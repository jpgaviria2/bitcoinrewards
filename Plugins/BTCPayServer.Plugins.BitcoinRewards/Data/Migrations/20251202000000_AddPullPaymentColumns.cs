using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations;

public partial class AddPullPaymentColumns : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ClaimLink",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "ClaimedAt",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "PaidAt",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PayoutId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PayoutMethod",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PayoutProcessor",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "PullPaymentId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ClaimLink",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords");

        migrationBuilder.DropColumn(
            name: "ClaimedAt",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords");

        migrationBuilder.DropColumn(
            name: "PaidAt",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords");

        migrationBuilder.DropColumn(
            name: "PayoutId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords");

        migrationBuilder.DropColumn(
            name: "PayoutMethod",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords");

        migrationBuilder.DropColumn(
            name: "PayoutProcessor",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords");

        migrationBuilder.DropColumn(
            name: "PullPaymentId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords");
    }
}

