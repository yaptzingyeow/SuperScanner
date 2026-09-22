using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SuperScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PageRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ActiveRevisionId",
                table: "pages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "page_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProducingTextEditId = table.Column<Guid>(type: "uuid", nullable: true),
                    ObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    MediaType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Sha256Hex = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_page_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_page_revisions_page_revisions_ParentRevisionId",
                        column: x => x.ParentRevisionId,
                        principalTable: "page_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_page_revisions_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_pages_ActiveRevisionId",
                table: "pages",
                column: "ActiveRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_page_revisions_ObjectKey",
                table: "page_revisions",
                column: "ObjectKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_page_revisions_PageId_CreatedAt",
                table: "page_revisions",
                columns: new[] { "PageId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_page_revisions_ParentRevisionId",
                table: "page_revisions",
                column: "ParentRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_pages_page_revisions_ActiveRevisionId",
                table: "pages",
                column: "ActiveRevisionId",
                principalTable: "page_revisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_pages_page_revisions_ActiveRevisionId",
                table: "pages");

            migrationBuilder.DropTable(
                name: "page_revisions");

            migrationBuilder.DropIndex(
                name: "IX_pages_ActiveRevisionId",
                table: "pages");

            migrationBuilder.DropColumn(
                name: "ActiveRevisionId",
                table: "pages");
        }
    }
}
