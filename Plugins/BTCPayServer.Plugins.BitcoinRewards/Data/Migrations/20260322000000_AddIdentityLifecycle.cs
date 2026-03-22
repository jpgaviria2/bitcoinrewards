#nullable enable
using System;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations;

public partial class AddIdentityLifecycle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "ReleasedAt",
            schema: "BTCPayServer.Plugins.BitcoinRewards",
            table: "Nip05Identities",
            type: "timestamp with time zone",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ReleasedAt",
            schema: "BTCPayServer.Plugins.BitcoinRewards",
            table: "Nip05Identities");
    }
}
