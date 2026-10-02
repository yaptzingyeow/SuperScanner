using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SuperScanner.Infrastructure.Persistence.Migrations;

public partial class PageRepairOperations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "page_repair_operations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                PageId = table.Column<Guid>(type: "uuid", nullable: false),
                SourceRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                SourceObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                RectanglesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                StrokesJson = table.Column<string>(type: "character varying(16384)", maxLength: 16384, nullable: false),
                Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                CandidatesJson = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                PreviewObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                AppliedRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_page_repair_operations", x => x.Id);
                table.ForeignKey("FK_page_repair_operations_pages_PageId", x => x.PageId,
                    "pages", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_page_repair_operations_PageId_CreatedAt",
            table: "page_repair_operations", columns: new[] { "PageId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "page_repair_operations");
}
