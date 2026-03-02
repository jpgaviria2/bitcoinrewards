using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingLnurlClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingLnurlClaims",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerWalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    LightningInvoiceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Bolt11 = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    ExpectedSats = table.Column<long>(type: "bigint", nullable: false),
                    K1Prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsCompleted = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    IsFailed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingLnurlClaims", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingLnurlClaims_Status_ExpiresAt",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "PendingLnurlClaims",
                columns: new[] { "IsCompleted", "IsFailed", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PendingLnurlClaims_CustomerWalletId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "PendingLnurlClaims",
                column: "CustomerWalletId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingLnurlClaims",
                schema: "BTCPayServer.Plugins.BitcoinRewards");
        }
    }
}
