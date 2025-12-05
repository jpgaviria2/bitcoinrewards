using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations;

public partial class RemoveCashuTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "Proofs",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);

        migrationBuilder.DropTable(
            name: "MintKeys",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);

        migrationBuilder.DropTable(
            name: "FailedTransactions",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);

        migrationBuilder.DropTable(
            name: "ExportedTokens",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);

        migrationBuilder.DropTable(
            name: "Mints",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Mints",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Url = table.Column<string>(type: "text", nullable: false),
                Unit = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Enabled = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Mints", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "FailedTransactions",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InvoiceId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                MeltDetails_Amount = table.Column<long>(type: "bigint", nullable: true),
                MeltDetails_Error = table.Column<string>(type: "text", nullable: true),
                MeltDetails_MaxFee = table.Column<long>(type: "bigint", nullable: true),
                MeltDetails_Satvbyte = table.Column<decimal>(type: "numeric", nullable: true),
                MeltDetails_Status = table.Column<string>(type: "text", nullable: true),
                MeltDetails_Token = table.Column<string>(type: "text", nullable: true),
                OutputData_BlindedMessages = table.Column<string>(type: "text", nullable: true),
                OutputData_BlindingFactors = table.Column<string>(type: "text", nullable: true),
                OutputData_Secrets = table.Column<string>(type: "text", nullable: true),
                OutputData_Signatures = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FailedTransactions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ExportedTokens",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Mint = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Token = table.Column<string>(type: "text", nullable: false),
                Amount = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ExportedTokens", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Proofs",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            columns: table => new
            {
                ProofId = table.Column<Guid>(type: "uuid", nullable: false),
                C = table.Column<string>(type: "text", nullable: false),
                DLEQ = table.Column<string>(type: "text", nullable: true),
                Id = table.Column<string>(type: "text", nullable: false),
                M = table.Column<string>(type: "text", nullable: false),
                MintUrl = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Secret = table.Column<string>(type: "text", nullable: false),
                StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Amount = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Proofs", x => x.ProofId);
            });

        migrationBuilder.CreateTable(
            name: "MintKeys",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            columns: table => new
            {
                MintId = table.Column<Guid>(type: "uuid", nullable: false),
                KeysetId = table.Column<string>(type: "text", nullable: false),
                Keyset = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_MintKeys", x => new { x.MintId, x.KeysetId });
                table.ForeignKey(
                    name: "FK_MintKeys_Mints_MintId",
                    column: x => x.MintId,
                    principalSchema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
                    principalTable: "Mints",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ExportedTokens_Mint",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "ExportedTokens",
            column: "Mint");

        migrationBuilder.CreateIndex(
            name: "IX_ExportedTokens_StoreId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "ExportedTokens",
            column: "StoreId");

        migrationBuilder.CreateIndex(
            name: "IX_FailedTransactions_InvoiceId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "FailedTransactions",
            column: "InvoiceId");

        migrationBuilder.CreateIndex(
            name: "IX_MintKeys_MintId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "MintKeys",
            column: "MintId");

        migrationBuilder.CreateIndex(
            name: "IX_Mints_IsActive",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "Mints",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_Mints_StoreId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "Mints",
            column: "StoreId");

        migrationBuilder.CreateIndex(
            name: "IX_Mints_StoreId_IsActive",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "Mints",
            columns: new[] { "StoreId", "IsActive" });

        migrationBuilder.CreateIndex(
            name: "IX_Mints_Url",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "Mints",
            column: "Url");

        migrationBuilder.CreateIndex(
            name: "IX_Proofs_Amount",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "Proofs",
            column: "Amount");

        migrationBuilder.CreateIndex(
            name: "IX_Proofs_Id",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "Proofs",
            column: "Id");

        migrationBuilder.CreateIndex(
            name: "IX_Proofs_MintUrl",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "Proofs",
            column: "MintUrl");

        migrationBuilder.CreateIndex(
            name: "IX_Proofs_StoreId",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "Proofs",
            column: "StoreId");

        migrationBuilder.CreateIndex(
            name: "IX_Proofs_StoreId_MintUrl",
            schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
            table: "Proofs",
            columns: new[] { "StoreId", "MintUrl" });
    }
}

