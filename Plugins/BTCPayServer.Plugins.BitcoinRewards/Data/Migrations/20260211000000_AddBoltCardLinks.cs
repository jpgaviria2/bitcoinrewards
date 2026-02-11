using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBoltCardLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoltCardLinks",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PullPaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CardUid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BoltcardId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TotalRewardedSatoshis = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    LastRewardedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoltCardLinks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BoltCardLinks_StoreId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "BoltCardLinks",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_BoltCardLinks_PullPaymentId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "BoltCardLinks",
                column: "PullPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_BoltCardLinks_BoltcardId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "BoltCardLinks",
                column: "BoltcardId");

            migrationBuilder.CreateIndex(
                name: "IX_BoltCardLinks_StoreId_PullPaymentId_Unique",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "BoltCardLinks",
                columns: new[] { "StoreId", "PullPaymentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoltCardLinks",
                schema: "BTCPayServer.Plugins.BitcoinRewards");
        }
    }
}
