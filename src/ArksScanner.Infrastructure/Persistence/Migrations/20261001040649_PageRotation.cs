using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArksScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageRotation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Rotation",
                table: "pages",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rotation",
                table: "pages");
        }
    }
}
