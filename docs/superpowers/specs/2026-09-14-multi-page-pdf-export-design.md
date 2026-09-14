# Multi-Page Import, Page Management, and PDF Export Design

**Date:** 2026-09-14

**Status:** Approved for implementation planning

**Scope:** Phase 1 web application and .NET Worker, with API contracts reusable by Phase 2 mobile

## Purpose

SuperScanner must treat a document as an ordered collection of independently editable pages. A user can import a multi-page PDF, add more photos or PDFs to the same document, crop and filter each page, reorder or remove pages, and export all currently Ready pages as one PDF.

This replaces the current one-upload/one-page assumption and the current behavior that previews only the first page of a PDF.

## User Workflow

1. The user creates a document by uploading a photo or PDF.
2. A photo becomes one page. A PDF is split into one page for every valid source page.
3. New pages are appended after the document's last active page.
4. Each page shows its own import, crop, and processing status.
5. The user can add more photos or PDFs at any time.
6. The user can open any page to adjust corners and filters.
7. The user can reorder pages by drag and drop, with keyboard-accessible Move controls as an alternative.
8. The user can remove a page after confirmation. Removal is initially soft and does not immediately delete stored assets.
9. The user can export when at least one active page is Ready. The export includes only Ready pages, in the visible order.
10. If page order or page content changes later, the UI marks the previous export as outdated and offers **Generate updated PDF**.

## Success Criteria

- A valid three-page PDF produces three independently selectable and editable pages.
- Adding one photo to that document produces page 4 without replacing or renumbering the existing content incorrectly.
- Adding another PDF appends all of its valid pages in source order.
- Reordering persists across refreshes and determines export order.
- A PDF export contains every active Ready page and no non-ready or removed page.
- An export is a stable snapshot: changes made while it is being generated cannot alter its contents.
- Concurrent reorder requests do not silently overwrite one another.
- Every operation enforces Firebase ownership and Firebase App Check through the existing API security pipeline.
- Original uploads remain immutable and private.

## Recommended Architecture

Use a page-first asynchronous model. `Document` is the parent aggregate, imported files are immutable source assets, and `Page` is the editable unit. Worker jobs expand accepted imports into page records and derived images. Export jobs consume an immutable snapshot of ready page revisions.

```text
photo or PDF
    -> upload intent and private quarantine object
    -> validation and immutable accepted import
    -> asynchronous import expansion
        -> image: one page
        -> PDF: one page per valid PDF page
    -> per-page preview, crop detection, crop/filter processing
    -> ordered Ready-page export snapshot
    -> asynchronous PDF assembly
    -> private PDF download
```

The API exposes provider-neutral document, page, import, reorder, and export contracts. Angular must not depend on R2 object keys, PDF renderer details, or Worker job representations.

## Domain and Persistence Model

### Document

Extend `documents` with:

- `Revision` (`long`): optimistic concurrency value incremented by any change that affects export output, including append, reorder, remove, restore, crop, and filter changes.
- `PageOrderRevision` (`long`): incremented only when active page membership or ordering changes. Reorder commands must supply the expected value.

Document-level status remains a summary for lists. It must not replace page-level status. A document may contain Ready, Processing, and Failed pages at the same time.

### Import

The existing `UploadIntent` remains the upload security boundary but changes from a required single-page target to a document-level import:

- `PageId` becomes nullable for compatibility with existing rows and is not assigned for new imports.
- An accepted import records its immutable `AcceptedObjectKey`.
- Import metadata records original filename, media type, byte size, SHA-256, discovered page count, successfully created page count, failed page count, state, and safe failure code.
- The accepted source object is retained privately so an import can be retried without asking the user to upload it again.

Validation promotes the quarantine object to an immutable `imports/{documentId}/{uploadId}/{sha256}` key. It does not create or mutate a page. Existing rows created under the one-page model remain readable through a compatibility migration.

### Page

Replace `PageNumber` as identity-like state with an explicit mutable `Position`. The API may still call the displayed 1-based value `pageNumber`.

Add or formalize:

- `Position` (`int`): contiguous, 1-based ordering among active pages.
- `SourceUploadId` (`Guid`): the accepted import that produced this page.
- `SourcePageIndex` (`int`): 1 for a photo or the 1-based source PDF page number.
- `State`: `Importing`, `Processing`, `NeedsCrop`, `Ready`, or `Failed`.
- `FailureCode`: safe retryable or terminal page failure identifier.
- `RemovedAt` and `RemovedByFirebaseUid`: null for active pages; populated for soft removal.
- Existing immutable `OriginalObjectKey`, preview, thumbnail, crop, filter, and revision fields.

For imported PDFs, the Worker renders each PDF page into its own lossless or high-quality immutable page-source image and assigns that object as the page's `OriginalObjectKey`. The original PDF remains stored at the import level. Crop and filter output continues to be derived from immutable page sources.

Use a PostgreSQL partial unique index for `(DocumentId, Position)` where `RemovedAt IS NULL`. Page append and reorder run in a database transaction while locking the owning document row. Positions are normalized to contiguous values before commit.

### Export

Add `document_exports`:

- `Id`, `DocumentId`, `OwnerFirebaseUid`
- `State`: `Queued`, `Processing`, `Ready`, or `Failed`
- `DocumentRevision`
- `SnapshotJson`: ordered entries containing page ID, position, applied crop revision, applied filter, and selected source object key
- `ReadyPageCount` and `ExcludedPageCount`
- `OutputObjectKey`, `FailureCode`, `CreatedAt`, `CompletedAt`, and `ExpiresAt`

The snapshot is created transactionally when export is requested. It is the sole input to PDF assembly. A later page edit does not mutate a queued or completed export; it only makes that export outdated relative to the current document revision.

## Import Processing

### Upload and validation

The existing presigned R2 upload flow remains:

1. Create a document-level upload intent.
2. Upload to the private quarantine key.
3. Complete the upload.
4. Verify declared size, actual size, signature, SHA-256, allowed media type, expiry, and malware policy.
5. Promote the accepted file to its immutable import key.
6. Enqueue `ExpandDocumentImport` using the upload ID as the idempotency key component.

Creating the intent checks the maximum file size. PDF page-count and decompression limits are enforced after safe inspection and before page creation.

### Photo expansion

The Worker decodes and auto-orients the accepted photo, creates one Page in `Processing`, appends it at the next position, generates its immutable crop source and previews, and starts existing boundary detection. A retry reuses the same page when it already exists.

### PDF expansion

The Worker uses a sandboxed PDF toolchain to:

1. Reject encrypted/password-protected PDFs.
2. Inspect page count, dimensions, and decompression limits before rendering.
3. Create or reuse one ordered `Importing` Page placeholder per source page in a short database transaction, reserving all required positions.
4. Render source pages independently and in ascending source order.
5. Update the deterministic page identified by `(SourceUploadId, SourcePageIndex)` rather than creating a second record.
6. Generate the page source, thumbnail, preview, and boundary-detection job independently.

The unique source tuple makes retries idempotent. The Worker must never create duplicates after a lease loss or restart.

If the PDF itself is invalid, encrypted, over the page limit, or unsafe to decode, the whole import is rejected and no page is appended. If inspection succeeds but rendering a particular source page fails, successful pages remain appended and the corresponding placeholder becomes a Failed page card at its reserved position. The UI offers a retry for the import. Retrying attempts only failed source pages and preserves source order.

If the remaining document page limit cannot hold the entire inspected PDF, reject the import before creating pages. Do not silently truncate it.

## API Contracts

All routes require Firebase authentication and the configured App Check validation. A resource owned by another user returns `404` to avoid disclosing its existence.

### Document detail

`GET /api/documents/{documentId}` returns:

- document ID, title, summary status, `revision`, and `pageOrderRevision`
- active pages ordered by Position
- per-page state, safe failure message, preview availability, crop/filter revisions, and available actions
- current import activity
- latest export summary and whether it is outdated

Removed pages are omitted unless a future explicit restore view requests them.

### Add pages

Reuse `POST /api/documents/{documentId}/uploads` for both initial and later imports. It creates a document-level upload intent and returns upload ID, presigned PUT URL, expiry, and accepted constraints. Upload completion remains asynchronous and its status reports inspection, expansion progress, created-page count, failed-page count, and a safe error.

### Reorder

`PUT /api/documents/{documentId}/page-order`

```json
{
  "expectedPageOrderRevision": 12,
  "pageIds": ["page-guid-3", "page-guid-1", "page-guid-2"]
}
```

The list must contain every active page exactly once. The server rejects missing, duplicate, removed, foreign, or unowned page IDs. A revision mismatch returns `409 Conflict` with the current revision so the UI can reload instead of overwriting another change.

### Remove page

`DELETE /api/documents/{documentId}/pages/{pageId}` soft-removes an owned active page, compacts positions, and increments both revisions. Removing the final active page is allowed; the document returns to an empty Draft state and cannot be exported.

### Export

`POST /api/documents/{documentId}/exports` creates a snapshot from all active Ready pages in position order. If no page is Ready, return a validation error. The response returns export ID, state, Ready count, excluded count, and status URL.

`GET /api/documents/{documentId}/exports/{exportId}` returns progress, counts, safe failure information, completion time, and `isOutdated`.

`GET /api/documents/{documentId}/exports/{exportId}/download` streams only a Ready, unexpired export after rechecking ownership. Responses use `Cache-Control: private, no-store` and `X-Content-Type-Options: nosniff`.

## PDF Assembly

The Worker leases a `BuildDocumentPdf` job and reads only the recorded export snapshot. For each entry it opens the exact processed page revision selected at snapshot time, normalizes orientation, and writes a PDF page in the same order.

The first version prioritizes visual fidelity and predictable output over searchable text. It produces an image-based PDF, preserving each page's aspect ratio on an appropriately sized PDF page. OCR text layers, PDF/A, compression controls, and selectable text are separate features.

The completed PDF is written to an immutable private key such as `exports/{documentId}/{exportId}/document.pdf`. A failed build leaves the snapshot available for an idempotent retry. Partial PDF files are never exposed.

## Angular User Experience

The document detail screen becomes a responsive page organizer:

- A compact header shows title, document activity, **Add pages**, and **Export PDF**.
- Active pages appear as thumbnail cards in position order.
- Each card shows page number, Ready/Processing/Needs crop/Failed state, selected filter, and actions.
- Selecting a card opens the existing corner and filter editor.
- Drag and drop supports mouse and touch. While dragging, a clear insertion position is shown.
- Keyboard and assistive-technology users receive Move earlier/Move later controls and position announcements.
- Reorder is optimistic in the UI, then persisted with `expectedPageOrderRevision`. On `409`, the UI restores server order and explains that the document changed elsewhere.
- **Add pages** accepts multiple photos and PDFs. Each file has upload and import progress; successful imports remain when another file fails.
- Removing a page requires confirmation and immediately hides the soft-removed card after the API succeeds.
- **Export PDF** shows how many Ready pages will be included and identifies how many non-ready pages will be excluded.
- Export status progresses through Queued, Processing, Ready, or Failed. Ready state provides **Download PDF**.
- After an export-affecting edit, the last export is labeled **Outdated** and the primary action becomes **Generate updated PDF**.

The layout must work first on desktop web and collapse naturally to a single-column touch layout for later mobile reuse. It must not imitate a live camera on desktop; uploaded photos still receive the same automatic paper detection.

## Concurrency and Idempotency

- Append, reorder, and remove operations lock the Document row and update positions in one transaction.
- Upload expansion is idempotent on `(SourceUploadId, SourcePageIndex)`.
- Reorder uses `PageOrderRevision`; stale clients receive `409`.
- Crop and filter writes continue to use page crop revisions and also increment Document Revision after a derived output becomes current.
- Export creation records an immutable ordered snapshot before it queues work.
- Worker jobs use deterministic idempotency keys and must tolerate lease loss after storage writes but before database commit.
- Storage keys are revisioned or immutable; a retry may confirm an existing matching object but must not overwrite a different revision.

## Error Handling

- Invalid, encrypted, oversized, over-page-limit, or unsafe PDFs fail before page creation and do not affect existing pages.
- An individual PDF page render failure preserves other successfully imported pages and records a retryable page/import failure.
- A photo decode failure creates no page and leaves existing document pages unchanged.
- Storage or renderer outages reschedule jobs within bounded retry limits, then expose a safe Failed state.
- Reorder validation errors do not change any position.
- A concurrent order conflict reloads current order rather than silently retrying with stale intent.
- Export excludes non-ready pages by design and reports the excluded count before and after submission.
- If a snapshot asset is missing, export fails safely; it does not substitute a newer page revision.
- Logs contain IDs, counts, timing, states, and safe error codes. They exclude file content, OCR text, tokens, credentials, presigned URLs, and raw exception details returned to clients.

## Security and Privacy

- Existing Firebase JWT ownership checks and App Check enforcement apply to every import, page, reorder, remove, export, status, and download operation.
- All R2 buckets and objects remain private. The browser uploads only through short-lived constrained PUT URLs and downloads through authenticated API responses.
- Validate actual file signatures and content, not only extensions or declared MIME types.
- Apply configurable limits for source bytes, PDF pages, decoded pixels, individual page dimensions, total rendered bytes, render time, memory, and Worker retries.
- Run PDF inspection and rendering without shell interpolation, external network access, or arbitrary ImageMagick delegates.
- Originals, accepted imports, and export snapshots are immutable.
- Soft-removed page assets follow a configurable retention policy before permanent cleanup. Cleanup is a separate maintenance job and never runs inline with removal.
- Audit events record import acceptance/rejection, page append/reorder/remove, export creation/completion/download, and ownership-safe failure categories.

## Configuration

Database-backed or environment configuration must provide safe defaults for:

- Maximum upload bytes
- Maximum pages per import and active pages per document
- Maximum PDF dimensions, decoded pixels, and rendered bytes
- PDF inspection and per-page rendering timeouts
- Import and export retry limits
- PDF image quality and maximum output dimensions
- Export retention duration
- Soft-delete retention duration

Limits are returned to the web client where useful for preflight validation, but the server remains authoritative.

## Verification Strategy

Implementation follows red-green-refactor even if extended exploratory testing is delegated later.

Domain tests cover append positions, page limits, contiguous reorder, soft removal, revision increments, and export eligibility. Application and integration tests cover ownership, App Check enforcement, upload compatibility, PDF page splitting, deterministic retry, partial page failure, stale reorder conflicts, excluded-page counts, snapshot stability, ordered assembly, and authenticated downloads.

Angular component tests cover multi-file add progress, page status cards, drag-and-drop ordering, keyboard ordering, conflict recovery, removal confirmation, export counts, outdated export messaging, and download action state. Focused end-to-end coverage verifies importing a three-page PDF, appending a photo, moving page 4 to page 2, and exporting a four-page PDF in the visible order.

## Rollout and Migration

1. Add nullable import-level fields and page/order/export tables without breaking current rows.
2. Backfill existing pages with Position equal to PageNumber, active state, source page index 1, and the existing upload relationship.
3. Deploy readers that understand both legacy and new imports.
4. Enable document-level upload intents and import expansion.
5. Enable organizer, reorder, and soft removal.
6. Enable export generation and authenticated download.
7. Remove legacy first-page-only PDF behavior after migration verification.

Migration and feature activation must be deployable without deleting existing documents or stored originals.

## Non-Goals

- OCR or searchable PDF text layers
- Editing or replacing recognized text
- Handwriting or font imitation
- Live mobile camera capture
- AI boundary-model activation or training-data collection
- Subscription, token, advertising, or watermark-removal rules
- Permanent deletion and restore UI for soft-removed pages
- Combining source PDF vector/text content directly into the export

## Delivery Boundary

This feature is complete when the web application and API can import photos and full multi-page PDFs into an existing or new document; represent every source page as an independently editable ordered Page; append, reorder, and soft-remove pages safely; and asynchronously generate and securely download one immutable PDF containing all active Ready pages in visible order.
