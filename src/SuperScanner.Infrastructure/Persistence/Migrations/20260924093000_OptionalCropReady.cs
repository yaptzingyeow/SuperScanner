using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace SuperScanner.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260924093000_OptionalCropReady")]
public sealed class OptionalCropReady : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE documents SET "Revision" = "Revision" + 1, "UpdatedAt" = CURRENT_TIMESTAMP
            WHERE "Id" IN (
                SELECT "DocumentId" FROM pages
                WHERE "RemovedAt" IS NULL AND "PreviewObjectKey" IS NOT NULL
                  AND "CropStatus" IN ('Detecting', 'NeedsCrop')
            );
            """);
        migrationBuilder.Sql("""
            UPDATE pages SET "State" = 'Ready', "CropStatus" = 'Ready', "FailureCode" = NULL,
                "CropSourceObjectKey" = COALESCE("CropSourceObjectKey", "PreviewObjectKey"),
                "Filter" = 'Original', "AppliedFilter" = 'Original'
            WHERE "RemovedAt" IS NULL AND "PreviewObjectKey" IS NOT NULL
              AND "CropStatus" IN ('Detecting', 'NeedsCrop');
            """);
        migrationBuilder.Sql("""
            UPDATE documents SET "Status" = CASE
                WHEN EXISTS (SELECT 1 FROM pages p WHERE p."DocumentId" = documents."Id"
                    AND p."RemovedAt" IS NULL AND p."State" IN ('Importing', 'Processing')) THEN 'Processing'
                WHEN EXISTS (SELECT 1 FROM pages p WHERE p."DocumentId" = documents."Id"
                    AND p."RemovedAt" IS NULL AND p."State" = 'NeedsCrop') THEN 'NeedsCrop'
                WHEN EXISTS (SELECT 1 FROM pages p WHERE p."DocumentId" = documents."Id"
                    AND p."RemovedAt" IS NULL AND p."State" = 'Ready') THEN 'Ready'
                ELSE documents."Status" END
            WHERE "Status" IN ('NeedsCrop', 'Processing');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Deliberately retain ready pages; reverting would exclude user documents from export.
    }
}
