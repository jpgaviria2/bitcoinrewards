using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations;

[DbContext(typeof(BitcoinRewardsPluginDbContext))]
[Migration("20260321_AddNip05Support")]
public partial class AddNip05Support : Migration
{
    private const string Schema = "BTCPayServer.Plugins.BitcoinRewards";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add NIP-05 columns to existing wallets table
        migrationBuilder.AddColumn<string>(
            name: "Pubkey",
            schema: Schema,
            table: "CustomerWallets",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Nip05Username",
            schema: Schema,
            table: "CustomerWallets",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "Nip05Revoked",
            schema: Schema,
            table: "CustomerWallets",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        // Create unique indexes
        migrationBuilder.CreateIndex(
            name: "IX_CustomerWallets_Pubkey",
            schema: Schema,
            table: "CustomerWallets",
            column: "Pubkey",
            unique: true,
            filter: "\"Pubkey\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_CustomerWallets_Nip05Username",
            schema: Schema,
            table: "CustomerWallets",
            column: "Nip05Username",
            unique: true,
            filter: "\"Nip05Username\" IS NOT NULL");

        // Create standalone NIP-05 identities table
        migrationBuilder.CreateTable(
            name: "Nip05Identities",
            schema: Schema,
            columns: table => new
            {
                Id = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                Pubkey = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Username = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                Revoked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Nip05Identities", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Nip05Identities_Pubkey",
            schema: Schema,
            table: "Nip05Identities",
            column: "Pubkey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Nip05Identities_Username",
            schema: Schema,
            table: "Nip05Identities",
            column: "Username",
            unique: true);

        // Pre-seed existing users via raw SQL (InsertData doesn't work when model snapshot is stale)
        migrationBuilder.Sql($@"
INSERT INTO ""{Schema}"".""Nip05Identities"" (""Pubkey"", ""Username"", ""Revoked"", ""CreatedAt"") VALUES
('4123fb4c449d8a48a954fe25ce6b171bda595ff83fecdd8e2588f8ea00634e05', 'manager', false, NOW()),
('88ee46231382525f784e607913b7efd5943fc107eb97de505937e802e968e955', 'jp', false, NOW()),
('e0a59f043d07866991ce3457f39c561009c4ca73f9e697e6c9d920b4b39090e8', 'birchy', false, NOW()),
('c2c2cda6f2dbc736da8542d1742067de91ae287e96c9695550ff37e0117d61f2', 'trails', false, NOW()),
('17c122ebefc64979940a1aca3e16612b9c428659c5a246a26e1f432391fc0e62', 'pac', false, NOW()),
('f4c9457d2a710aec0bab80cc82d2350c964c732570aabc9d80f25390bc53bb4f', 'coffeelover635280', false, NOW()),
('f3f3a288b9551deed41c8e9241dab89583411d99d3b493abb6b908b08adb9864', 'torca', false, NOW()),
('3176ffec038ffb0e016818ecb541b382add3b6c6ba148b22a1fd4ddf5d8b94af', 'coffeelover339076', false, NOW())
ON CONFLICT DO NOTHING;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Nip05Identities", schema: Schema);

        migrationBuilder.DropIndex(name: "IX_CustomerWallets_Pubkey", schema: Schema, table: "CustomerWallets");
        migrationBuilder.DropIndex(name: "IX_CustomerWallets_Nip05Username", schema: Schema, table: "CustomerWallets");

        migrationBuilder.DropColumn(name: "Pubkey", schema: Schema, table: "CustomerWallets");
        migrationBuilder.DropColumn(name: "Nip05Username", schema: Schema, table: "CustomerWallets");
        migrationBuilder.DropColumn(name: "Nip05Revoked", schema: Schema, table: "CustomerWallets");
    }
}
