#nullable enable
using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations;

[DbContext(typeof(BitcoinRewardsPluginDbContext))]
[Migration("20260924000000_AddRewardsPartBFoundation")]
public partial class AddRewardsPartBFoundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CustomerProfileId", schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(
            name: "LightningAddressHash", schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<int>(
            name: "DeliveryMode", schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(
            name: "DirectPayoutState", schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "BitcoinRewardRecords", type: "integer", nullable: true);

        migrationBuilder.CreateTable(
            name: "CustomerOrderAssociations",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SquareOrderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SquarePaymentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CustomerProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LightningAddressHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                RegisterId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                StaffActorId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                State = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                BoundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_CustomerOrderAssociations", x => x.Id));

        migrationBuilder.CreateTable(
            name: "RewardPayoutAttempts",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RewardId = table.Column<Guid>(type: "uuid", nullable: false),
                AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                LightningAddressHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                State = table.Column<int>(type: "integer", nullable: false),
                AmountSatoshis = table.Column<long>(type: "bigint", nullable: false),
                PaymentHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ProviderReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RewardPayoutAttempts", x => x.Id);
                table.ForeignKey("FK_RewardPayoutAttempts_BitcoinRewardRecords_RewardId", x => x.RewardId,
                    principalSchema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
                    principalTable: "BitcoinRewardRecords", principalColumn: "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(name: "IX_BitcoinRewardRecords_CustomerProfileId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema, table: "BitcoinRewardRecords", column: "CustomerProfileId");
        migrationBuilder.CreateIndex(name: "IX_CustomerOrderAssociations_StoreId_SquareOrderId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema, table: "CustomerOrderAssociations",
            columns: new[] { "StoreId", "SquareOrderId" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_CustomerOrderAssociations_StoreId_SquarePaymentId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema, table: "CustomerOrderAssociations",
            columns: new[] { "StoreId", "SquarePaymentId" }, unique: true,
            filter: "\"SquarePaymentId\" IS NOT NULL");
        migrationBuilder.CreateIndex(name: "IX_CustomerOrderAssociations_StoreId_CustomerProfileId_State",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema, table: "CustomerOrderAssociations",
            columns: new[] { "StoreId", "CustomerProfileId", "State" });
        migrationBuilder.CreateIndex(name: "IX_RewardPayoutAttempts_RewardId_AttemptNumber",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema, table: "RewardPayoutAttempts",
            columns: new[] { "RewardId", "AttemptNumber" }, unique: true);
        migrationBuilder.CreateIndex(name: "IX_RewardPayoutAttempts_State_NextRetryAt",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema, table: "RewardPayoutAttempts",
            columns: new[] { "State", "NextRetryAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("CustomerOrderAssociations", BitcoinRewardsPluginDbContext.DefaultPluginSchema);
        migrationBuilder.DropTable("RewardPayoutAttempts", BitcoinRewardsPluginDbContext.DefaultPluginSchema);
        migrationBuilder.DropIndex(name: "IX_BitcoinRewardRecords_CustomerProfileId",
            table: "BitcoinRewardRecords", schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);
        migrationBuilder.DropColumn(name: "CustomerProfileId", table: "BitcoinRewardRecords",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);
        migrationBuilder.DropColumn(name: "LightningAddressHash", table: "BitcoinRewardRecords",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);
        migrationBuilder.DropColumn(name: "DeliveryMode", table: "BitcoinRewardRecords",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);
        migrationBuilder.DropColumn(name: "DirectPayoutState", table: "BitcoinRewardRecords",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);
    }
}
