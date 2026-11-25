using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArksScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AdRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ad_rewards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Pages = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    TransactionId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GrantedBy = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ad_rewards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ad_rewards_accounts_AccountUid",
                        column: x => x.AccountUid,
                        principalTable: "accounts",
                        principalColumn: "FirebaseUid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ad_rewards_AccountUid_Day",
                table: "ad_rewards",
                columns: new[] { "AccountUid", "Day" });

            migrationBuilder.CreateIndex(
                name: "IX_ad_rewards_CreatedAt",
                table: "ad_rewards",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ad_rewards_TransactionId",
                table: "ad_rewards",
                column: "TransactionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ad_rewards");
        }
    }
}
