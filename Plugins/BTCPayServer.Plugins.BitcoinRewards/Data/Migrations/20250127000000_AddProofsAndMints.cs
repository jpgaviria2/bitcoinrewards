#nullable disable
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProofsAndMints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create Proofs table
            migrationBuilder.CreateTable(
                name: "Proofs",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    ProofId = table.Column<System.Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MintUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Amount = table.Column<ulong>(type: "bigint", nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: false),
                    C = table.Column<string>(type: "text", nullable: false),
                    DLEQ = table.Column<string>(type: "text", nullable: true),
                    Witness = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Proofs", x => x.ProofId);
                });

            // Create indexes for Proofs
            migrationBuilder.CreateIndex(
                name: "IX_Proofs_StoreId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "Proofs",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Proofs_MintUrl",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "Proofs",
                column: "MintUrl");

            migrationBuilder.CreateIndex(
                name: "IX_Proofs_StoreId_MintUrl",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "Proofs",
                columns: new[] { "StoreId", "MintUrl" });

            migrationBuilder.CreateIndex(
                name: "IX_Proofs_Id",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "Proofs",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_Proofs_Amount",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "Proofs",
                column: "Amount");

            // Create Mints table
            migrationBuilder.CreateTable(
                name: "Mints",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Unit = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "sat"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    CreatedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mints", x => x.Id);
                });

            // Create indexes for Mints
            migrationBuilder.CreateIndex(
                name: "IX_Mints_StoreId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "Mints",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_Mints_StoreId_IsActive",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "Mints",
                columns: new[] { "StoreId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Mints_Url",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "Mints",
                column: "Url");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Proofs",
                schema: "BTCPayServer.Plugins.BitcoinRewards");

            migrationBuilder.DropTable(
                name: "Mints",
                schema: "BTCPayServer.Plugins.BitcoinRewards");
        }
    }
}

