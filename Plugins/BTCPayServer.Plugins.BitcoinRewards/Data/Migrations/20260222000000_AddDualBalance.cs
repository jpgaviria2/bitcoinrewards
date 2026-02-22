using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDualBalance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerWallets",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    PullPaymentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    CardUid = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    BoltcardId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CadBalanceCents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    AutoConvertToCad = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    TotalRewardedSatoshis = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    TotalRewardedCadCents = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastRewardedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ApiTokenHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerWallets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletTransactions",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerWalletId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    SatsAmount = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    CadCentsAmount = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L),
                    ExchangeRate = table.Column<decimal>(type: "numeric", nullable: false),
                    Reference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletTransactions_CustomerWallets_CustomerWalletId",
                        column: x => x.CustomerWalletId,
                        principalSchema: "BTCPayServer.Plugins.BitcoinRewards",
                        principalTable: "CustomerWallets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWallets_StoreId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "CustomerWallets",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWallets_PullPaymentId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "CustomerWallets",
                column: "PullPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWallets_BoltcardId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "CustomerWallets",
                column: "BoltcardId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWallets_CardUid",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "CustomerWallets",
                column: "CardUid");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWallets_ApiTokenHash",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "CustomerWallets",
                column: "ApiTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerWallets_StoreId_PullPaymentId_Unique",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "CustomerWallets",
                columns: new[] { "StoreId", "PullPaymentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_CustomerWalletId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "WalletTransactions",
                column: "CustomerWalletId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletTransactions_CreatedAt",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "WalletTransactions",
                column: "CreatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalletTransactions",
                schema: "BTCPayServer.Plugins.BitcoinRewards");

            migrationBuilder.DropTable(
                name: "CustomerWallets",
                schema: "BTCPayServer.Plugins.BitcoinRewards");
        }
    }
}
