using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations;

[DbContext(typeof(BitcoinRewardsPluginDbContext))]
[Migration("20260321_AddNip05Support")]
public partial class AddNip05Support : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add NIP-05 columns to existing wallets table
        migrationBuilder.AddColumn<string>(
            name: "Pubkey",
            table: "CustomerWallets",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Nip05Username",
            table: "CustomerWallets",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "Nip05Revoked",
            table: "CustomerWallets",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        // Create unique indexes
        migrationBuilder.CreateIndex(
            name: "IX_CustomerWallets_Pubkey",
            table: "CustomerWallets",
            column: "Pubkey",
            unique: true,
            filter: "Pubkey IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_CustomerWallets_Nip05Username",
            table: "CustomerWallets",
            column: "Nip05Username",
            unique: true,
            filter: "Nip05Username IS NOT NULL");

        // Create standalone NIP-05 identities table
        migrationBuilder.CreateTable(
            name: "Nip05Identities",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                Pubkey = table.Column<string>(type: "TEXT", nullable: false),
                Username = table.Column<string>(type: "TEXT", nullable: false),
                Revoked = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP")
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Nip05Identities", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_Nip05Identities_Pubkey",
            table: "Nip05Identities",
            column: "Pubkey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_Nip05Identities_Username",
            table: "Nip05Identities",
            column: "Username",
            unique: true);

        // Pre-seed existing users
        migrationBuilder.InsertData(
            table: "Nip05Identities",
            columns: new[] { "Pubkey", "Username", "Revoked", "CreatedAt" },
            values: new object[,]
            {
                { "4123fb4c449d8a48a954fe25ce6b171bda595ff83fecdd8e2588f8ea00634e05", "manager", false, DateTime.UtcNow },
                { "88ee46231382525f784e607913b7efd5943fc107eb97de505937e802e968e955", "jp", false, DateTime.UtcNow },
                { "e0a59f043d07866991ce3457f39c561009c4ca73f9e697e6c9d920b4b39090e8", "birchy", false, DateTime.UtcNow },
                { "c2c2cda6f2dbc736da8542d1742067de91ae287e96c9695550ff37e0117d61f2", "trails", false, DateTime.UtcNow },
                { "17c122ebefc64979940a1aca3e16612b9c428659c5a246a26e1f432391fc0e62", "pac", false, DateTime.UtcNow },
                { "f4c9457d2a710aec0bab80cc82d2350c964c732570aabc9d80f25390bc53bb4f", "coffeelover635280", false, DateTime.UtcNow },
                { "f3f3a288b9551deed41c8e9241dab89583411d99d3b493abb6b908b08adb9864", "torca", false, DateTime.UtcNow },
                { "3176ffec038ffb0e016818ecb541b382add3b6c6ba148b22a1fd4ddf5d8b94af", "coffeelover339076", false, DateTime.UtcNow }
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Nip05Identities");

        migrationBuilder.DropIndex(name: "IX_CustomerWallets_Pubkey", table: "CustomerWallets");
        migrationBuilder.DropIndex(name: "IX_CustomerWallets_Nip05Username", table: "CustomerWallets");

        migrationBuilder.DropColumn(name: "Pubkey", table: "CustomerWallets");
        migrationBuilder.DropColumn(name: "Nip05Username", table: "CustomerWallets");
        migrationBuilder.DropColumn(name: "Nip05Revoked", table: "CustomerWallets");
    }
}
