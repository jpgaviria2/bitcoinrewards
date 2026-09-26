#nullable disable

using System;
using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations
{
    [DbContext(typeof(BitcoinRewardsPluginDbContext))]
    [Migration("20260926171000_AddPendingLightningAddressCheckIns")]
    public partial class AddPendingLightningAddressCheckIns : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingLightningAddressCheckIns",
                schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LightningAddress = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    LightningAddressHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegisterId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    DeviceId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SquareOrderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    SquarePaymentId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingLightningAddressCheckIns", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingLightningAddressCheckIns_StoreId_State_CreatedAt",
                schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
                table: "PendingLightningAddressCheckIns",
                columns: new[] { "StoreId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingLightningAddressCheckIns_StoreId_ExpiresAt",
                schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
                table: "PendingLightningAddressCheckIns",
                columns: new[] { "StoreId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingLightningAddressCheckIns_StoreId_SquarePaymentId",
                schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
                table: "PendingLightningAddressCheckIns",
                columns: new[] { "StoreId", "SquarePaymentId" },
                unique: true,
                filter: "\"SquarePaymentId\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingLightningAddressCheckIns",
                schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);
        }
    }
}
