using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArksScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrintedTextEditing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "font_catalogue_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogueId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FamilyName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    AssetSha256Hex = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    LicenseIdentifier = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WebAssetPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    RendererAssetPath = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_font_catalogue_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "text_edit_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorFirebaseUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SourceRevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceOcrResultId = table.Column<Guid>(type: "uuid", nullable: false),
                    SelectedOcrElementIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                    OriginalText = table.Column<string>(type: "text", nullable: false),
                    ReplacementText = table.Column<string>(type: "text", nullable: false),
                    ReplacementBoxJson = table.Column<string>(type: "jsonb", nullable: false),
                    StyleJson = table.Column<string>(type: "jsonb", nullable: false),
                    StyleProvenanceJson = table.Column<string>(type: "jsonb", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    BranchParentEditId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CanonicalRequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    RendererVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LayoutVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ResultRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    QueuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_text_edit_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_text_edit_operations_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_text_edit_operations_page_revisions_ResultRevisionId",
                        column: x => x.ResultRevisionId,
                        principalTable: "page_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_text_edit_operations_page_revisions_SourceRevisionId",
                        column: x => x.SourceRevisionId,
                        principalTable: "page_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_text_edit_operations_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_text_edit_operations_text_edit_operations_BranchParentEditId",
                        column: x => x.BranchParentEditId,
                        principalTable: "text_edit_operations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_page_revisions_ProducingTextEditId",
                table: "page_revisions",
                column: "ProducingTextEditId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_font_catalogue_entries_CatalogueId_Version",
                table: "font_catalogue_entries",
                columns: new[] { "CatalogueId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_text_edit_operations_BranchParentEditId",
                table: "text_edit_operations",
                column: "BranchParentEditId");

            migrationBuilder.CreateIndex(
                name: "IX_text_edit_operations_DocumentId",
                table: "text_edit_operations",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_text_edit_operations_PageId_IdempotencyKey",
                table: "text_edit_operations",
                columns: new[] { "PageId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_text_edit_operations_PageId_Sequence",
                table: "text_edit_operations",
                columns: new[] { "PageId", "Sequence" });

            migrationBuilder.CreateIndex(
                name: "IX_text_edit_operations_ResultRevisionId",
                table: "text_edit_operations",
                column: "ResultRevisionId",
                unique: true,
                filter: "\"ResultRevisionId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_text_edit_operations_SourceRevisionId",
                table: "text_edit_operations",
                column: "SourceRevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_page_revisions_text_edit_operations_ProducingTextEditId",
                table: "page_revisions",
                column: "ProducingTextEditId",
                principalTable: "text_edit_operations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_page_revisions_text_edit_operations_ProducingTextEditId",
                table: "page_revisions");

            migrationBuilder.DropTable(
                name: "font_catalogue_entries");

            migrationBuilder.DropTable(
                name: "text_edit_operations");

            migrationBuilder.DropIndex(
                name: "IX_page_revisions_ProducingTextEditId",
                table: "page_revisions");
        }
    }
}
