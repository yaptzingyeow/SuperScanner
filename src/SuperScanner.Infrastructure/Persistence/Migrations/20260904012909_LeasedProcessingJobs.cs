using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SuperScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LeasedProcessingJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ValidationErrorCode",
                table: "upload_intents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AttemptCount",
                table: "processing_jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AvailableAt",
                table: "processing_jobs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "ErrorCode",
                table: "processing_jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseExpiresAt",
                table: "processing_jobs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "processing_jobs",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<string>(
                name: "WorkerId",
                table: "processing_jobs",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_processing_jobs_Status_AvailableAt_CreatedAt",
                table: "processing_jobs",
                columns: new[] { "Status", "AvailableAt", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_processing_jobs_Status_AvailableAt_CreatedAt",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "ValidationErrorCode",
                table: "upload_intents");

            migrationBuilder.DropColumn(
                name: "AttemptCount",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "AvailableAt",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "ErrorCode",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresAt",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "processing_jobs");

            migrationBuilder.DropColumn(
                name: "WorkerId",
                table: "processing_jobs");
        }
    }
}
