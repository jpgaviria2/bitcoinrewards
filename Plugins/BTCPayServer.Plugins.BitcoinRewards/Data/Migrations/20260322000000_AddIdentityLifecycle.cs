#nullable enable
using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations;

[DbContext(typeof(BitcoinRewardsPluginDbContext))]
[Migration("20260322_AddIdentityLifecycle")]
public partial class AddIdentityLifecycle : Migration
{
    private const string Schema = "BTCPayServer.Plugins.BitcoinRewards";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ReleasedAt",
            schema: Schema,
            table: "Nip05Identities",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReleasedAt",
            schema: Schema,
            table: "Nip05Identities");
    }
}
