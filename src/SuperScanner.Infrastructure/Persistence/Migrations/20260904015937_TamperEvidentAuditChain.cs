using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SuperScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TamperEvidentAuditChain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    ActorUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegionJson = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PreviousHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    EventHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    Signature = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    SigningKeyId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_events", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_events_TargetId_Sequence",
                table: "audit_events",
                columns: new[] { "TargetId", "Sequence" },
                unique: true);

            migrationBuilder.Sql("""
                DO $permissions$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'superscanner_app') THEN
                        GRANT USAGE ON SCHEMA public TO superscanner_app;
                        GRANT SELECT, INSERT ON TABLE audit_events TO superscanner_app;
                        REVOKE UPDATE, DELETE, TRUNCATE ON TABLE audit_events FROM superscanner_app;
                    END IF;
                END
                $permissions$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events");
        }
    }
}
