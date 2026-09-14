-- Apply before starting the API/Worker version that supports page filters.
BEGIN;
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "Filter" text NOT NULL DEFAULT 'Document';
ALTER TABLE pages ADD COLUMN IF NOT EXISTS "AppliedFilter" text NOT NULL DEFAULT 'Document';
INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260913000000_PageFilters', '10.0.4') ON CONFLICT DO NOTHING;
COMMIT;
