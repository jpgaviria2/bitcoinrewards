using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations;

public partial class AddRewardNotificationOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "RewardNotificationOutbox",
            schema: "BTCPayServer.Plugins.BitcoinRewards",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                RewardId = table.Column<Guid>(type: "uuid", nullable: false),
                EventId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CustomerProfileId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                AmountSatoshis = table.Column<long>(type: "bigint", nullable: false),
                OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                State = table.Column<int>(type: "integer", nullable: false),
                AttemptCount = table.Column<int>(type: "integer", nullable: false),
                NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DeliveredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastError = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_RewardNotificationOutbox", x => x.Id);
                table.ForeignKey(
                    name: "FK_RewardNotificationOutbox_BitcoinRewardRecords_RewardId",
                    column: x => x.RewardId,
                    principalSchema: "BTCPayServer.Plugins.BitcoinRewards",
                    principalTable: "BitcoinRewardRecords",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_RewardNotificationOutbox_EventId",
            schema: "BTCPayServer.Plugins.BitcoinRewards",
            table: "RewardNotificationOutbox",
            column: "EventId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_RewardNotificationOutbox_RewardId",
            schema: "BTCPayServer.Plugins.BitcoinRewards",
            table: "RewardNotificationOutbox",
            column: "RewardId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_RewardNotificationOutbox_State_NextAttemptAt",
            schema: "BTCPayServer.Plugins.BitcoinRewards",
            table: "RewardNotificationOutbox",
            columns: new[] { "State", "NextAttemptAt" });
        migrationBuilder.CreateIndex(
            name: "IX_RewardNotificationOutbox_StoreId_State",
            schema: "BTCPayServer.Plugins.BitcoinRewards",
            table: "RewardNotificationOutbox",
            columns: new[] { "StoreId", "State" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "RewardNotificationOutbox",
            schema: "BTCPayServer.Plugins.BitcoinRewards");
    }
}
