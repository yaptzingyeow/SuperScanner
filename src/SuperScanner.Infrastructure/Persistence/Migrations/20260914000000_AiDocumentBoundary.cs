using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SuperScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AiDocumentBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CropDiagnosticsCode",
                table: "pages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CropModelVersion",
                table: "pages",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CropDiagnosticsCode",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "CropModelVersion",
                table: "pages");
        }
    }
}
