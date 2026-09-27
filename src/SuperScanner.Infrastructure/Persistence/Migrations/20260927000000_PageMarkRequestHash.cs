using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SuperScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260927000000_PageMarkRequestHash")]
public sealed class PageMarkRequestHash : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "CreateRequestHash", table: "page_marks", type: "character varying(64)",
            maxLength: 64, nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "CreateRequestHash", table: "page_marks");
}
