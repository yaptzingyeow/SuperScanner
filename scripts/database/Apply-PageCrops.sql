-- Local/manual alternative to the PageCrops EF migration. Run as database owner.
BEGIN;
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "CropSourceObjectKey" text;
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "CropPointsJson" text;
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "CropStatus" text;
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "CropSource" text;
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "CropConfidence" double precision;
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "CropRevision" integer NOT NULL DEFAULT 0;
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "AppliedCropRevision" integer NOT NULL DEFAULT 0;
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260912010000_PageCrops', '10.0.4') ON CONFLICT DO NOTHING;
UPDATE pages p SET "CropSourceObjectKey" = p."PreviewObjectKey", "CropStatus" = 'Detecting',
 "CropSource" = 'Automatic', "CropRevision" = 1
WHERE p."CropSourceObjectKey" IS NULL AND p."PreviewObjectKey" IS NOT NULL
 AND EXISTS (SELECT 1 FROM upload_intents u WHERE u."PageId"=p."Id" AND u."State"='Accepted'
 AND u."DeclaredMediaType" IN ('image/jpeg','image/png'));
INSERT INTO processing_jobs
 ("Id", "Type", "Payload", "IdempotencyKey", "Status", "CreatedAt", "AvailableAt", "UpdatedAt", "AttemptCount")
SELECT gen_random_uuid(), 'DetectDocumentEdges', p."Id"::text || ':' || p."CropRevision"::text,
 'page:' || p."Id"::text || ':crop:' || p."CropRevision"::text, 'Queued', now(), now(), now(), 0
FROM pages p WHERE p."CropStatus" = 'Detecting'
ON CONFLICT ("IdempotencyKey") DO NOTHING;
UPDATE documents d SET "Status"='Processing', "UpdatedAt"=now()
WHERE EXISTS (SELECT 1 FROM pages p WHERE p."DocumentId"=d."Id" AND p."CropStatus"='Detecting');
COMMIT;
