# Multi-page import and export operations

## Runtime requirements

Run the API and Worker against the same migrated PostgreSQL database and private R2-compatible bucket. The Worker host must provide `pdfinfo` and `pdftoppm` from Poppler on `PATH`. ImageMagick native dependencies must also be available. Apply EF Core migrations before starting either process; deploy the API and Worker from the same commit.

## Configuration

Set the `DocumentImport__*` limits listed in `.env.example` on both API and Worker. Set `DocumentExport__MaxOutputBytes` on the Worker and `DocumentExport__RetentionDays` on the API. `Pages__SoftDeleteRetentionDays` reserves the cleanup window; do not physically delete page assets sooner than this value. Recommended initial values are 25 MiB per upload, 50 pages per import/document, 250 million decoded pixels, 500 MiB rendered output, 100 MiB final PDF, seven-day exports, and 30-day soft-deleted pages.

Railway variables use the double-underscore names verbatim. Keep R2 secrets, the database connection string, audit signing key, and Firebase credentials private. Never expose R2 credentials in Angular configuration.

## Storage contract

The bucket is private. Expected immutable prefixes are:

- `imports/{owner}/{document}/{upload}/...` for accepted originals.
- `page-sources/{document}/{page}/...` for extracted source pages.
- revisioned preview and crop assets for processed pages.
- `exports/{documentId}/{exportId}/document.pdf` for one final immutable export.

Only a database export in `Ready` state may return a download. A partial or failed object must never be downloadable or overwrite an existing immutable export.

## Failure handling and observability

Safe retry codes include `import_inspection_failed`, `import_page_failed`, `import_pixel_limit`, `import_page_limit`, `export_build_failed`, and `export_size_limit`. Logs may contain document, upload, page, export and job IDs; counts; state transitions; durations; and safe codes. Do not log file contents, extracted text, signed URLs, tokens, object-store credentials, or exception payloads containing those values.

Imports reserve page positions before rendering so failed source pages remain visible and retryable. Job retries are bounded. Operators should investigate terminal jobs and storage/database availability instead of manually changing aggregate state.

## Verification

1. Apply migrations and start PostgreSQL, R2/MinIO, API, and Worker.
2. Confirm `pdfinfo -v` and `pdftoppm -v` succeed on the Worker host.
3. Provide `apps/web/e2e/fixtures/three-page.pdf` and `append-photo.jpg`.
4. Set `E2E_IMPORT_EXPORT_READY=1` and run the `multi-page document` Playwright scenario.
5. Confirm four ordered cards, a four-page downloaded PDF, one final export object, and no downloadable partial object.

## Cleanup and rollback

Expire export downloads after seven days and delete their objects only through the retention job. Keep removed-page assets for at least 30 days. To roll back safely, disable new import/export entry points at the deployment or feature-routing layer, stop the Worker after its current lease finishes, and retain database rows and bucket objects. Do not reverse migrations or bulk-delete prefixes during rollback. The previous single-page read/download path can remain online while queued jobs are inspected.
