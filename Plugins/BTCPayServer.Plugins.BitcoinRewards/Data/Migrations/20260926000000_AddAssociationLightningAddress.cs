#nullable disable

using BTCPayServer.Plugins.BitcoinRewards.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations
{
    [DbContext(typeof(BitcoinRewardsPluginDbContext))]
    [Migration("20260926000000_AddAssociationLightningAddress")]
    public partial class AddAssociationLightningAddress : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LightningAddress",
                schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
                table: "CustomerOrderAssociations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LightningAddress",
                schema: BitcoinRewardsPluginDbContext.DefaultPluginSchema,
                table: "CustomerOrderAssociations");
        }
    }
}
