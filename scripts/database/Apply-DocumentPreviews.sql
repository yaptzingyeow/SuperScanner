-- Local/manual alternative to applying the EF DocumentPreviews migration.
BEGIN;
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "PreviewObjectKey" text;
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "ThumbnailObjectKey" text;
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260912000000_DocumentPreviews', '10.0.4') ON CONFLICT DO NOTHING;
-- Reconcile previously accepted uploads. Unique keys make reruns safe.
INSERT INTO processing_jobs
 ("Id", "Type", "Payload", "IdempotencyKey", "Status", "CreatedAt", "AvailableAt", "UpdatedAt", "AttemptCount")
SELECT gen_random_uuid(), 'ProcessDocument', u."Id"::text,
 'upload:' || u."Id"::text || ':preview:v1', 'Queued', now(), now(), now(), 0
FROM upload_intents u JOIN pages p ON p."Id" = u."PageId"
WHERE u."State" = 'Accepted' AND p."OriginalObjectKey" IS NOT NULL AND p."PreviewObjectKey" IS NULL
ON CONFLICT ("IdempotencyKey") DO NOTHING;
COMMIT;
