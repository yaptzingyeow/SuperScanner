using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SuperScanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class QuarantinedUploadIntents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "upload_intents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerFirebaseUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageId = table.Column<Guid>(type: "uuid", nullable: false),
                    QuarantineObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    DeclaredMediaType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    DeclaredSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    DeclaredSha256Hex = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_upload_intents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_upload_intents_documents_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_upload_intents_pages_PageId",
                        column: x => x.PageId,
                        principalTable: "pages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_upload_intents_DocumentId",
                table: "upload_intents",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_upload_intents_IdempotencyKey",
                table: "upload_intents",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_upload_intents_OwnerFirebaseUid_DocumentId",
                table: "upload_intents",
                columns: new[] { "OwnerFirebaseUid", "DocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_upload_intents_PageId",
                table: "upload_intents",
                column: "PageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "upload_intents");
        }
    }
}
