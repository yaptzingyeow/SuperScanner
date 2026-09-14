-- Repair only stale Uploading documents with accepted originals.
BEGIN;
UPDATE documents d
SET "Status" = 'Processing', "UpdatedAt" = CURRENT_TIMESTAMP
WHERE d."Status" = 'Uploading'
  AND EXISTS (
    SELECT 1 FROM upload_intents u
    JOIN pages p ON p."Id" = u."PageId" AND p."DocumentId" = d."Id"
    WHERE u."DocumentId" = d."Id" AND u."State" = 'Accepted'
      AND p."OriginalObjectKey" IS NOT NULL
  );
COMMIT;
