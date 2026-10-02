using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArksScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SignatureAssetPurged : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AssetPurgedAt",
                table: "page_signatures",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AssetPurgedAt",
                table: "page_signatures");
        }
    }
}
