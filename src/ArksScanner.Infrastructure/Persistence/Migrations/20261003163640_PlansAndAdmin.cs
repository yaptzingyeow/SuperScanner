using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArksScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlansAndAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RemovedAt",
                table: "documents",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RemovedReason",
                table: "documents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "accounts",
                columns: table => new
                {
                    FirebaseUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    SignInProvider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsGuest = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RetentionGraceFrom = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastPlan = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_accounts", x => x.FirebaseUid);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubscriptionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_payments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "plan_settings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Phase = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EnforceFromUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    FreeOcrPagesPerDay = table.Column<int>(type: "integer", nullable: false),
                    FreeWatermarkExportsPerDay = table.Column<int>(type: "integer", nullable: false),
                    FreeMaxDocuments = table.Column<int>(type: "integer", nullable: false),
                    FreeRetentionDays = table.Column<int>(type: "integer", nullable: false),
                    UsageTimeZone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OcrCostPerThousandPages = table.Column<decimal>(type: "numeric(10,4)", precision: 10, scale: 4, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_settings", x => x.Id);
                    table.CheckConstraint("CK_plan_settings_singleton", "\"Id\" = 1");
                });

            migrationBuilder.CreateTable(
                name: "usage_days",
                columns: table => new
                {
                    AccountUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    OcrPages = table.Column<int>(type: "integer", nullable: false),
                    WatermarkExports = table.Column<int>(type: "integer", nullable: false),
                    BonusOcrPages = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_usage_days", x => new { x.AccountUid, x.Day });
                });

            migrationBuilder.CreateTable(
                name: "admins",
                columns: table => new
                {
                    AccountUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AddedByUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_admins", x => x.AccountUid);
                    table.ForeignKey(
                        name: "FK_admins_accounts_AccountUid",
                        column: x => x.AccountUid,
                        principalTable: "accounts",
                        principalColumn: "FirebaseUid",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "subscriptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    GrantedByUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedByUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_subscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_subscriptions_accounts_AccountUid",
                        column: x => x.AccountUid,
                        principalTable: "accounts",
                        principalColumn: "FirebaseUid",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_accounts_CreatedAt",
                table: "accounts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_Email",
                table: "accounts",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_accounts_LastSeenAt",
                table: "accounts",
                column: "LastSeenAt");

            migrationBuilder.CreateIndex(
                name: "IX_payments_CreatedAt",
                table: "payments",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_payments_Provider_ProviderReference",
                table: "payments",
                columns: new[] { "Provider", "ProviderReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_AccountUid_Status",
                table: "subscriptions",
                columns: new[] { "AccountUid", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_usage_days_Day",
                table: "usage_days",
                column: "Day");

            // Everyone who already owns documents becomes an account, so admin counts start complete.
            migrationBuilder.Sql("""
                INSERT INTO accounts ("FirebaseUid", "SignInProvider", "IsGuest", "CreatedAt", "LastSeenAt")
                SELECT "OwnerFirebaseUid", 'unknown', false, min("CreatedAt"), max("UpdatedAt")
                FROM documents
                GROUP BY "OwnerFirebaseUid"
                ON CONFLICT DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admins");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "plan_settings");

            migrationBuilder.DropTable(
                name: "subscriptions");

            migrationBuilder.DropTable(
                name: "usage_days");

            migrationBuilder.DropTable(
                name: "accounts");

            migrationBuilder.DropColumn(
                name: "RemovedAt",
                table: "documents");

            migrationBuilder.DropColumn(
                name: "RemovedReason",
                table: "documents");
        }
    }
}
