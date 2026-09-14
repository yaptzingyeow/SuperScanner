using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SuperScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260912010000_PageCrops")]
public sealed partial class PageCrops : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[] { "CropSourceObjectKey", "CropPointsJson", "CropStatus", "CropSource" })
            migrationBuilder.AddColumn<string>(name: column, table: "pages", type: "text", nullable: true);
        migrationBuilder.AddColumn<double>(name: "CropConfidence", table: "pages", type: "double precision", nullable: true);
        migrationBuilder.AddColumn<int>(name: "CropRevision", table: "pages", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "AppliedCropRevision", table: "pages", type: "integer", nullable: false, defaultValue: 0);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        foreach (var column in new[] { "CropSourceObjectKey", "CropPointsJson", "CropStatus", "CropSource", "CropConfidence", "CropRevision", "AppliedCropRevision" })
            migrationBuilder.DropColumn(name: column, table: "pages");
    }
}
