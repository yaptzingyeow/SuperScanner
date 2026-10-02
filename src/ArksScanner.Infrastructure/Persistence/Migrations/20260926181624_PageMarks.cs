using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArksScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "page_marks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    X = table.Column<double>(type: "double precision", nullable: false),
                    Y = table.Column<double>(type: "double precision", nullable: false),
                    Width = table.Column<double>(type: "double precision", nullable: false),
                    Height = table.Column<double>(type: "double precision", nullable: false),
                    Color = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    StrokeWidth = table.Column<double>(type: "double precision", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_page_marks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_page_marks_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_page_marks_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_page_marks_DocumentId_ClientRequestId",
                table: "page_marks",
                columns: new[] { "DocumentId", "ClientRequestId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_page_marks_DocumentId_PageId_DeletedAt",
                table: "page_marks",
                columns: new[] { "DocumentId", "PageId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_page_marks_PageId",
                table: "page_marks",
                column: "PageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page_marks");
        }
    }
}
