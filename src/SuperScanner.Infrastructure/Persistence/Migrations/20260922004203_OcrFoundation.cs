using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SuperScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OcrFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "page_ocr_results",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    SourceFingerprint = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    FullText = table.Column<string>(type: "text", nullable: false),
                    ProviderName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ProviderModelVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FailureRetryable = table.Column<bool>(type: "boolean", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ElementCount = table.Column<int>(type: "integer", nullable: false),
                    AggregateConfidence = table.Column<double>(type: "double precision", nullable: true),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_page_ocr_results", x => x.Id);
                    table.ForeignKey(
                        name: "FK_page_ocr_results_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ocr_elements",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PageOcrResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentElementId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    TextType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ReadingOrder = table.Column<int>(type: "integer", nullable: false),
                    PolygonJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ocr_elements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ocr_elements_ocr_elements_ParentElementId",
                        column: x => x.ParentElementId,
                        principalTable: "ocr_elements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ocr_elements_page_ocr_results_PageOcrResultId",
                        column: x => x.PageOcrResultId,
                        principalTable: "page_ocr_results",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ocr_elements_PageOcrResultId_ParentElementId_ReadingOrder",
                table: "ocr_elements",
                columns: new[] { "PageOcrResultId", "ParentElementId", "ReadingOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ocr_elements_ParentElementId",
                table: "ocr_elements",
                column: "ParentElementId");

            migrationBuilder.CreateIndex(
                name: "IX_page_ocr_results_PageId_SourceFingerprint",
                table: "page_ocr_results",
                columns: new[] { "PageId", "SourceFingerprint" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_page_ocr_results_PageId_State_QueuedAt",
                table: "page_ocr_results",
                columns: new[] { "PageId", "State", "QueuedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ocr_elements");

            migrationBuilder.DropTable(
                name: "page_ocr_results");
        }
    }
}
