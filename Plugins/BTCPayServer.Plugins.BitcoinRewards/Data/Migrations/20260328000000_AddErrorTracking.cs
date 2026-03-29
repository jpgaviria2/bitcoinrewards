using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddErrorTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RewardErrors",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    ErrorType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    StackTrace = table.Column<string>(type: "text", nullable: true),
                    OrderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    StoreId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    RewardId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                    Context = table.Column<string>(type: "text", nullable: true),
                    UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResolvedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ResolutionNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    LastRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardErrors", x => x.Id);
                });

            // Create indexes for efficient querying
            migrationBuilder.CreateIndex(
                name: "IX_RewardErrors_ErrorType",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "RewardErrors",
                column: "ErrorType");

            migrationBuilder.CreateIndex(
                name: "IX_RewardErrors_StoreId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "RewardErrors",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_RewardErrors_OrderId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "RewardErrors",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_RewardErrors_RewardId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "RewardErrors",
                column: "RewardId");

            migrationBuilder.CreateIndex(
                name: "IX_RewardErrors_Timestamp",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "RewardErrors",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_RewardErrors_Resolved_Timestamp",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "RewardErrors",
                columns: new[] { "Resolved", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RewardErrors",
                schema: "BTCPayServer.Plugins.BitcoinRewards");
        }
    }
}
