using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SuperScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260913000000_PageFilters")]
public sealed partial class PageFilters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "Filter", table: "pages", type: "text", nullable: false, defaultValue: "Document");
        migrationBuilder.AddColumn<string>(name: "AppliedFilter", table: "pages", type: "text", nullable: false, defaultValue: "Document");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "Filter", table: "pages");
        migrationBuilder.DropColumn(name: "AppliedFilter", table: "pages");
    }
}
