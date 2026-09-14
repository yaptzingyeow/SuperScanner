using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SuperScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260912000000_DocumentPreviews")]
public sealed partial class DocumentPreviews : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "PreviewObjectKey", table: "pages", type: "text", nullable: true);
        migrationBuilder.AddColumn<string>(name: "ThumbnailObjectKey", table: "pages", type: "text", nullable: true);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PreviewObjectKey", table: "pages");
        migrationBuilder.DropColumn(name: "ThumbnailObjectKey", table: "pages");
    }
}
