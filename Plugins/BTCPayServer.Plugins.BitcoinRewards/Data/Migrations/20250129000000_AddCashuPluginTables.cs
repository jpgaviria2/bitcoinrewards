#nullable disable
using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCashuPluginTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create MintKeys table
            migrationBuilder.CreateTable(
                name: "MintKeys",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    MintId = table.Column<Guid>(type: "uuid", nullable: false),
                    KeysetId = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Keyset = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MintKeys", x => new { x.MintId, x.KeysetId });
                    table.ForeignKey(
                        name: "FK_MintKeys_Mints_MintId",
                        column: x => x.MintId,
                        principalSchema: "BTCPayServer.Plugins.BitcoinRewards",
                        principalTable: "Mints",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MintKeys_MintId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "MintKeys",
                column: "MintId");

            // Create FailedTransactions table
            migrationBuilder.CreateTable(
                name: "FailedTransactions",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<string>(type: "text", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: false),
                    MintUrl = table.Column<string>(type: "text", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    OperationType = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    LastRetried = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Details = table.Column<string>(type: "text", nullable: false),
                    Resolved = table.Column<bool>(type: "boolean", nullable: false),
                    MeltDetails_MeltQuoteId = table.Column<string>(type: "text", nullable: true),
                    MeltDetails_Expiry = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MeltDetails_LightningInvoiceId = table.Column<string>(type: "text", nullable: true),
                    MeltDetails_Status = table.Column<string>(type: "text", nullable: true),
                    OutputData_Secrets = table.Column<string>(type: "text", nullable: false),
                    OutputData_BlindingFactors = table.Column<string>(type: "text", nullable: false),
                    OutputData_BlindedMessages = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FailedTransactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FailedTransactions_InvoiceId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "FailedTransactions",
                column: "InvoiceId");

            // Create ExportedTokens table
            migrationBuilder.CreateTable(
                name: "ExportedTokens",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SerializedToken = table.Column<string>(type: "text", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    Unit = table.Column<string>(type: "text", nullable: false),
                    Mint = table.Column<string>(type: "text", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StoreId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExportedTokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExportedTokens_StoreId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "ExportedTokens",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_ExportedTokens_Mint",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "ExportedTokens",
                column: "Mint");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExportedTokens",
                schema: "BTCPayServer.Plugins.BitcoinRewards");

            migrationBuilder.DropTable(
                name: "FailedTransactions",
                schema: "BTCPayServer.Plugins.BitcoinRewards");

            migrationBuilder.DropTable(
                name: "MintKeys",
                schema: "BTCPayServer.Plugins.BitcoinRewards");
        }
    }
}

