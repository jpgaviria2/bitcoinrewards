#nullable disable
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BTCPayServer.Plugins.BitcoinRewards.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create schema
            migrationBuilder.Sql(@"CREATE SCHEMA IF NOT EXISTS ""BTCPayServer.Plugins.BitcoinRewards"";");

            // Create table
            migrationBuilder.CreateTable(
                name: "BitcoinRewardRecords",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                columns: table => new
                {
                    Id = table.Column<System.Guid>(type: "uuid", nullable: false),
                    StoreId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    TransactionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    OrderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CustomerEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    CustomerPhone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TransactionAmount = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RewardAmount = table.Column<decimal>(type: "numeric(18,8)", nullable: false),
                    RewardAmountSatoshis = table.Column<long>(type: "bigint", nullable: false),
                    EcashToken = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: true),
                    RedeemedAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<System.DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RetryCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BitcoinRewardRecords", x => x.Id);
                });

            // Create indexes
            migrationBuilder.CreateIndex(
                name: "IX_BitcoinRewardRecords_StoreId",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "BitcoinRewardRecords",
                column: "StoreId");

            migrationBuilder.CreateIndex(
                name: "IX_BitcoinRewardRecords_Status",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "BitcoinRewardRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_BitcoinRewardRecords_StoreId_TransactionId_Platform",
                schema: "BTCPayServer.Plugins.BitcoinRewards",
                table: "BitcoinRewardRecords",
                columns: new[] { "StoreId", "TransactionId", "Platform" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BitcoinRewardRecords",
                schema: "BTCPayServer.Plugins.BitcoinRewards");

            // Optionally drop schema (commented out to preserve data during rollback)
            // migrationBuilder.Sql(@"DROP SCHEMA IF EXISTS ""BTCPayServer.Plugins.BitcoinRewards"" CASCADE;");
        }
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "8.0.0")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("BTCPayServer.Plugins.BitcoinRewards.Data.BitcoinRewardRecord", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<DateTime>("CreatedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("Currency")
                        .IsRequired()
                        .HasMaxLength(10)
                        .HasColumnType("character varying(10)");

                    b.Property<string>("CustomerEmail")
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)");

                    b.Property<string>("CustomerPhone")
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<string>("EcashToken")
                        .HasMaxLength(10000)
                        .HasColumnType("character varying(10000)");

                    b.Property<DateTime?>("ExpiresAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<string>("ErrorMessage")
                        .HasMaxLength(500)
                        .HasColumnType("character varying(500)");

                    b.Property<string>("OrderId")
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)");

                    b.Property<int>("Platform")
                        .HasColumnType("integer");

                    b.Property<decimal>("RewardAmount")
                        .HasColumnType("numeric(18,8)");

                    b.Property<long>("RewardAmountSatoshis")
                        .HasColumnType("bigint");

                    b.Property<DateTime?>("RedeemedAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("RetryCount")
                        .HasColumnType("integer");

                    b.Property<DateTime?>("SentAt")
                        .HasColumnType("timestamp with time zone");

                    b.Property<int>("Status")
                        .HasColumnType("integer");

                    b.Property<string>("StoreId")
                        .IsRequired()
                        .HasMaxLength(50)
                        .HasColumnType("character varying(50)");

                    b.Property<decimal>("TransactionAmount")
                        .HasColumnType("numeric(18,8)");

                    b.Property<string>("TransactionId")
                        .IsRequired()
                        .HasMaxLength(255)
                        .HasColumnType("character varying(255)");

                    b.HasKey("Id");

                    b.HasIndex("StoreId");

                    b.HasIndex(new[] { "StoreId", "TransactionId", "Platform" });

                    b.HasIndex("Status");

                    b.ToTable("BitcoinRewardRecords", "BTCPayServer.Plugins.BitcoinRewards");
                });
#pragma warning restore 612, 618
        }
    }
}

