using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SuperScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260914210000_MultiPageDocuments")]
public sealed partial class MultiPageDocuments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "Revision",
            table: "documents",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<long>(
            name: "PageOrderRevision",
            table: "documents",
            type: "bigint",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.DropIndex(
            name: "IX_pages_DocumentId_PageNumber",
            table: "pages");

        migrationBuilder.AddColumn<int>(name: "Position", table: "pages", type: "integer", nullable: true);
        migrationBuilder.AddColumn<Guid>(name: "SourceUploadId", table: "pages", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<int>(name: "SourcePageIndex", table: "pages", type: "integer", nullable: true);
        migrationBuilder.AddColumn<string>(name: "State", table: "pages", type: "character varying(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<string>(name: "FailureCode", table: "pages", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddColumn<string>(name: "OriginalMediaType", table: "pages", type: "character varying(128)", maxLength: 128, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "RemovedAt", table: "pages", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "RemovedByFirebaseUid", table: "pages", type: "character varying(128)", maxLength: 128, nullable: true);

        migrationBuilder.Sql(
            """
            UPDATE "pages" AS p
            SET "Position" = p."PageNumber",
                "SourceUploadId" = COALESCE(
                    (SELECT u."Id" FROM "upload_intents" AS u WHERE u."PageId" = p."Id" ORDER BY u."Id" LIMIT 1),
                    p."Id"),
                "SourcePageIndex" = 1,
                "OriginalMediaType" = COALESCE(
                    (SELECT NULLIF(u."DeclaredMediaType", '') FROM "upload_intents" AS u WHERE u."PageId" = p."Id" ORDER BY u."Id" LIMIT 1),
                    'application/octet-stream'),
                "State" = CASE
                    WHEN p."CropStatus" = 'NeedsCrop' THEN 'NeedsCrop'
                    WHEN p."CropStatus" IN ('Detecting', 'Processing') THEN 'Processing'
                    WHEN p."CropStatus" = 'Failed' THEN 'Failed'
                    WHEN p."PreviewObjectKey" IS NOT NULL THEN 'Ready'
                    WHEN p."CropSourceObjectKey" IS NOT NULL OR p."CropStatus" IS NOT NULL THEN 'NeedsCrop'
                    WHEN p."OriginalObjectKey" IS NOT NULL THEN 'Processing'
                    ELSE 'Importing'
                END
            """);

        migrationBuilder.AlterColumn<int>(
            name: "Position",
            table: "pages",
            type: "integer",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);
        migrationBuilder.AlterColumn<Guid>(
            name: "SourceUploadId",
            table: "pages",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
        migrationBuilder.AlterColumn<int>(
            name: "SourcePageIndex",
            table: "pages",
            type: "integer",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "integer",
            oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "State",
            table: "pages",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(32)",
            oldMaxLength: 32,
            oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "OriginalMediaType",
            table: "pages",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(128)",
            oldMaxLength: 128,
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_pages_DocumentId_Position",
            table: "pages",
            columns: new[] { "DocumentId", "Position" },
            unique: true,
            filter: "\"RemovedAt\" IS NULL");
        migrationBuilder.CreateIndex(
            name: "IX_pages_SourceUploadId_SourcePageIndex",
            table: "pages",
            columns: new[] { "SourceUploadId", "SourcePageIndex" },
            unique: true);

        migrationBuilder.DropForeignKey(
            name: "FK_upload_intents_pages_PageId",
            table: "upload_intents");
        migrationBuilder.AlterColumn<Guid>(
            name: "PageId",
            table: "upload_intents",
            type: "uuid",
            nullable: true,
            oldClrType: typeof(Guid),
            oldType: "uuid");
        migrationBuilder.AddColumn<string>(name: "OriginalFileName", table: "upload_intents", type: "character varying(255)", maxLength: 255, nullable: true);
        migrationBuilder.AddColumn<string>(name: "AcceptedObjectKey", table: "upload_intents", type: "character varying(1024)", maxLength: 1024, nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "AcceptedAt", table: "upload_intents", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<int>(name: "DiscoveredPageCount", table: "upload_intents", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "CreatedPageCount", table: "upload_intents", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<int>(name: "FailedPageCount", table: "upload_intents", type: "integer", nullable: false, defaultValue: 0);
        migrationBuilder.AddColumn<string>(name: "ExpansionErrorCode", table: "upload_intents", type: "character varying(64)", maxLength: 64, nullable: true);
        migrationBuilder.AddForeignKey(
            name: "FK_upload_intents_pages_PageId",
            table: "upload_intents",
            column: "PageId",
            principalTable: "pages",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTable(
            name: "document_exports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                OwnerFirebaseUid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                DocumentRevision = table.Column<long>(type: "bigint", nullable: false),
                SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                ReadyPageCount = table.Column<int>(type: "integer", nullable: false),
                ExcludedPageCount = table.Column<int>(type: "integer", nullable: false),
                OutputObjectKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_document_exports", x => x.Id);
                table.ForeignKey(
                    name: "FK_document_exports_documents_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_document_exports_DocumentId",
            table: "document_exports",
            column: "DocumentId");
        migrationBuilder.CreateIndex(
            name: "IX_document_exports_OwnerFirebaseUid_DocumentId_CreatedAt",
            table: "document_exports",
            columns: new[] { "OwnerFirebaseUid", "DocumentId", "CreatedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "document_exports");
        migrationBuilder.DropForeignKey(name: "FK_upload_intents_pages_PageId", table: "upload_intents");
        migrationBuilder.DropColumn(name: "OriginalFileName", table: "upload_intents");
        migrationBuilder.DropColumn(name: "AcceptedObjectKey", table: "upload_intents");
        migrationBuilder.DropColumn(name: "AcceptedAt", table: "upload_intents");
        migrationBuilder.DropColumn(name: "DiscoveredPageCount", table: "upload_intents");
        migrationBuilder.DropColumn(name: "CreatedPageCount", table: "upload_intents");
        migrationBuilder.DropColumn(name: "FailedPageCount", table: "upload_intents");
        migrationBuilder.DropColumn(name: "ExpansionErrorCode", table: "upload_intents");
        migrationBuilder.AlterColumn<Guid>(
            name: "PageId",
            table: "upload_intents",
            type: "uuid",
            nullable: false,
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true);
        migrationBuilder.AddForeignKey(
            name: "FK_upload_intents_pages_PageId",
            table: "upload_intents",
            column: "PageId",
            principalTable: "pages",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.DropIndex(name: "IX_pages_DocumentId_Position", table: "pages");
        migrationBuilder.DropIndex(name: "IX_pages_SourceUploadId_SourcePageIndex", table: "pages");
        migrationBuilder.DropColumn(name: "Position", table: "pages");
        migrationBuilder.DropColumn(name: "SourceUploadId", table: "pages");
        migrationBuilder.DropColumn(name: "SourcePageIndex", table: "pages");
        migrationBuilder.DropColumn(name: "State", table: "pages");
        migrationBuilder.DropColumn(name: "FailureCode", table: "pages");
        migrationBuilder.DropColumn(name: "OriginalMediaType", table: "pages");
        migrationBuilder.DropColumn(name: "RemovedAt", table: "pages");
        migrationBuilder.DropColumn(name: "RemovedByFirebaseUid", table: "pages");
        migrationBuilder.CreateIndex(
            name: "IX_pages_DocumentId_PageNumber",
            table: "pages",
            columns: new[] { "DocumentId", "PageNumber" },
            unique: true);

        migrationBuilder.DropColumn(name: "Revision", table: "documents");
        migrationBuilder.DropColumn(name: "PageOrderRevision", table: "documents");
    }
}
