# Document Signature Overlays Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users upload or draw a transparent visual signature, place and later edit it on a document page, and include it in exported PDFs.

**Architecture:** Angular owns unsaved image-processing and placement drafts. The .NET API validates and privately stores normalized PNG assets and canonical overlay geometry, while PostgreSQL stores editable per-page records. PDF export snapshots overlay assets and geometry with each page and renders them above the scan.

**Tech Stack:** Angular 22, TypeScript, Canvas 2D, .NET 10, EF Core, PostgreSQL, PdfSharp, ImageMagick, existing private R2-compatible object store.

**Spec:** `docs/superpowers/specs/2026-09-26-document-signature-overlay-design.md`

## Global Constraints

- Signatures belong only to the current document page; do not create an account-wide library.
- No paid background-removal or image-generation API; keep slider processing local to the browser.
- Do not modify imported scans or OCR source data. Saved overlays remain movable, resizable, and deletable after refresh.
- Exported PDFs are immutable snapshots; changing an overlay makes an earlier export stale.
- This is a visual signature tool, not identity-verified or certified electronic signing.
- All assets remain private and owner-authorized; only PNG/JPEG input, not SVG, is accepted.
- Preserve unrelated dirty-worktree changes. Stage and commit only files from the current task.

## Review Focus

- Pale or already-transparent ink: Task 3 tests adjustable removal and unchanged original alpha.
- Huge or malformed image: Task 2 tests size, dimensions, pixel limits, decode failure, and metadata stripping.
- Button clicks versus page dragging on zoomed pages: Task 5 tests both operations together.
- Concurrent edits or a refresh during save: Tasks 2 and 5 test revision conflicts and draft preservation.
- Overlay edit while export is queued: Task 6 tests immutable asset/geometry snapshots and stale-export marking.

## File Structure

- `src/SuperScanner.Domain/Documents/PageSignature.cs`: geometry, revision, lifecycle invariants.
- `src/SuperScanner.Application/Abstractions/IPageSignatureRepository.cs`: active overlay reads for export and API orchestration.
- `src/SuperScanner.Infrastructure/Persistence/Configurations/PageSignatureConfiguration.cs` and a new EF migration: database mapping.
- `src/SuperScanner.Infrastructure/Persistence/EfPageSignatureRepository.cs`: active overlays for export snapshots.
- `src/SuperScanner.Infrastructure/Signatures/SignatureImageNormalizer.cs`: bounded decode and canonical PNG encode.
- `src/SuperScanner.Api/Endpoints/PageSignatureEndpoints.cs`: owner-scoped asset and overlay operations.
- `apps/web/src/app/documents/signature-image.ts`: pure Canvas image processing.
- `apps/web/src/app/documents/signature-creator.component.*`: upload/draw/preview draft UI.
- `apps/web/src/app/documents/page-signature-overlay.component.*`: selectable placement and edit controls.
- Existing page editor, document models/API service, export snapshot, and PDF builder: integration points only.

---

### Task 1: Canonical overlay model and persistence

**Files:**
- Create: `src/SuperScanner.Domain/Documents/PageSignature.cs`
- Create: `src/SuperScanner.Application/Abstractions/IPageSignatureRepository.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Configurations/PageSignatureConfiguration.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/EfPageSignatureRepository.cs`
- Create: a generated `PageSignatures` migration and designer in `src/SuperScanner.Infrastructure/Persistence/Migrations/`
- Modify: `src/SuperScanner.Infrastructure/Persistence/AppDbContext.cs`, `src/SuperScanner.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs`
- Test: `tests/SuperScanner.Domain.Tests/Documents/PageSignatureTests.cs`, `tests/SuperScanner.Infrastructure.IntegrationTests/Persistence/PageSignaturePersistenceTests.cs`

**Interfaces:**
- Produce `SignatureBox(double X, double Y, double Width, double Height)` with positive normalized dimensions fully inside `[0,1]`.
- Produce `PageSignature.Create(Guid id, Guid documentId, Guid pageId, Guid clientRequestId, string assetKey, double imageAspectRatio, SignatureBox box, DateTimeOffset now)`, `MoveResize(SignatureBox box, long expectedRevision, DateTimeOffset now)`, and `Delete(long expectedRevision, DateTimeOffset now)`; expose `Revision`, `DeletedAt`, `AssetKey`, and box.
- Produce `IPageSignatureRepository.GetActiveForDocumentAsync(Guid documentId, CancellationToken ct): Task<IReadOnlyList<PageSignature>>` for Task 6.

- [ ] **Step 1: Write failing domain and persistence tests.** `Create_rejects_out_of_page_box`, `MoveResize_rejects_stale_revision`, `Delete_hides_overlay_without_losing_asset_key`, and `Round_trip_preserves_box_aspect_ratio_and_revision` assert those concrete outcomes.
- [ ] **Step 2: Run RED.** `dotnet test tests/SuperScanner.Domain.Tests/SuperScanner.Domain.Tests.csproj --filter FullyQualifiedName~PageSignature` and `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter FullyQualifiedName~PageSignaturePersistence`; expect missing type/behavior failures.
- [ ] **Step 3: Implement model, repository, EF configuration, and migration.** Use a soft-deleted row and concurrency revision; index `(DocumentId, PageId, DeletedAt)` and uniquely constrain `(DocumentId, ClientRequestId)` for idempotent create. Do not create a reusable user signature table.
- [ ] **Step 4: Run GREEN.** Repeat both filtered commands; expect zero failures.
- [ ] **Step 5: Commit only Task 1 files:** `feat: persist editable page signature overlays`.

### Task 2: Secure signature lifecycle API

**Files:**
- Create: `src/SuperScanner.Infrastructure/Signatures/SignatureImageNormalizer.cs`
- Create: `src/SuperScanner.Api/Endpoints/PageSignatureEndpoints.cs`
- Modify: `src/SuperScanner.Api/Program.cs`, `src/SuperScanner.Domain/Documents/Document.cs`
- Test: `tests/SuperScanner.Api.IntegrationTests/Documents/PageSignatureEndpointsTests.cs`, `tests/SuperScanner.Infrastructure.IntegrationTests/Signatures/SignatureImageNormalizerTests.cs`

**Interfaces:**
- Produce `POST /api/documents/{documentId}/pages/{pageId}/signatures` with multipart PNG, normalized `SignatureBox`, and a UUID `Idempotency-Key` header; return ID, revision, box, aspect ratio, image URL.
- Produce `GET .../signatures`, `GET .../signatures/{signatureId}/image`, `PUT .../signatures/{signatureId}` with `{ box, expectedRevision }`, and `DELETE .../signatures/{signatureId}` with `expectedRevision`.
- Produce `SignatureImageNormalizer.NormalizeAsync(Stream input, CancellationToken ct): Task<NormalizedSignatureImage>` where the result holds canonical PNG bytes, width, height, and aspect ratio.

- [ ] **Step 1: Write failing tests.** Test owner-only operations, unauthorized App Check/Auth, valid PNG creation/read, JPEG rejected at this canonical endpoint, invalid/oversized/empty-transparent images, metadata removal, stale revision `409`, inactive page `404`, an idempotent create retry, and failed create leaving neither an active row nor a live orphaned asset. Use a small valid fixture, not a file-extension-only fake.
- [ ] **Step 2: Run RED.** `dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj --filter FullyQualifiedName~PageSignature` and `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter FullyQualifiedName~SignatureImageNormalizer`; expect missing endpoint/type failures.
- [ ] **Step 3: Implement minimal owner-scoped endpoints and bounded normalizer.** Register `EfPageSignatureRepository` in `Program.cs`. Browser converts JPEG to PNG before POST. Cap input at 5 MiB and decoded image at 12 megapixels; reject an entirely transparent image, strip metadata, write immutable private PNG with `WriteIfAbsentAsync`; use `Document.MarkContentChanged` on create/update/delete. If asset write succeeds but DB commit fails, delete the new asset or queue orphan cleanup. Never expose object keys or storage credentials in JSON.
- [ ] **Step 4: Run GREEN.** Repeat both filtered commands; expect zero failures.
- [ ] **Step 5: Commit only Task 2 files:** `feat: add secure page signature API`.

### Task 3: Local upload processing and drawing

**Files:**
- Create: `apps/web/src/app/documents/signature-image.ts`, `.spec.ts`
- Create: `apps/web/src/app/documents/signature-creator.component.ts`, `.html`, `.scss`, `.spec.ts`

**Interfaces:**
- Produce `prepareSignature(file: File, removalStrength: number, keepOriginal: boolean): Promise<Blob>` returning a PNG Blob without changing the source file.
- Produce `SignatureCreatorComponent` with `confirmed = output<Blob>()` and `cancelled = output<void>()`.

- [ ] **Step 1: Write failing tests.** A white-backed dark stroke gains transparency; changing strength changes only draft output; pre-existing transparent pixels stay transparent; Keep original preserves opaque background; blank drawing cannot confirm; undo removes the last stroke; clear resets all strokes; pointer cancel does not create an unintended stroke.
- [ ] **Step 2: Run RED.** From `apps/web`, `npm test -- --watch=false --include=src/app/documents/signature-image.spec.ts --include=src/app/documents/signature-creator.component.spec.ts`; expect missing behavior failures.
- [ ] **Step 3: Implement Canvas processing and upload/draw UI.** Accept source PNG/JPEG, reject unsupported file types early, process only in browser memory, show original plus checkerboard preview, expose one strength slider and Keep original. The drawing canvas uses Pointer Events with touch support, Undo stroke, Clear, and explicit Use signature. Do not use an external service or persist a draft.
- [ ] **Step 4: Run GREEN.** Repeat the targeted command; expect zero failures.
- [ ] **Step 5: Commit only Task 3 files:** `feat: create signatures from image or drawing`.

### Task 4: Client API contract and document state

**Files:**
- Create: `apps/web/src/app/documents/page-signature.models.ts`
- Create: `apps/web/src/app/documents/page-signature.service.ts`, `.spec.ts`

**Interfaces:**
- Produce `PageSignatureDto` with ID, page ID, normalized box, aspect ratio, revision, and authorized image URL.
- Produce `PageSignatureService.list(documentId, pageId)`, `.create(documentId, pageId, png, box, idempotencyKey)`, `.update(documentId, pageId, signatureId, box, expectedRevision)`, and `.delete(documentId, pageId, signatureId, expectedRevision)` matching Task 2 routes.

- [ ] **Step 1: Write failing HTTP tests.** Verify route/payload shape, image URL remains same-origin and owner-authorized, a `409` reaches the caller for reloading, and the service never exposes object keys.
- [ ] **Step 2: Run RED.** From `apps/web`, `npm test -- --watch=false --include=src/app/documents/page-signature.service.spec.ts`; expect missing service failures.
- [ ] **Step 3: Implement typed API service and models.** Use the dedicated list endpoint from Task 2 and the existing security interceptor; do not introduce Firebase Storage or a separate credential path.
- [ ] **Step 4: Run GREEN.** Repeat targeted command; expect zero failures.
- [ ] **Step 5: Commit only Task 4 files:** `feat: connect page signature API`.

### Task 5: Editable placement on the page

**Files:**
- Create: `apps/web/src/app/documents/page-signature-overlay.component.ts`, `.html`, `.scss`, `.spec.ts`
- Modify: `apps/web/src/app/documents/page-text-editor.component.ts`, `.html`, `.scss`, `.spec.ts`

**Interfaces:**
- Consume Tasks 3-4 creator output and API service.
- Produce `PageSignatureOverlayComponent` with `signatures`, `selectedId`, `draft` inputs and `boxChange`, `selected` outputs; all geometry remains normalized.

- [ ] **Step 1: Write failing component tests.** Add signature is visible without OCR; create -> preview -> drag/resize -> Save persists; Cancel leaves page unchanged; saved signature remains selectable after reload; delete confirmation removes only selected overlay; on `409` the draft remains visible with a reload message; page pan and Edit/Delete text buttons still respond; keyboard controls move/size a selected overlay; pointer/touch handles meet usable target size.
- [ ] **Step 2: Run RED.** From `apps/web`, `npm test -- --watch=false --include=src/app/documents/page-signature-overlay.component.spec.ts --include=src/app/documents/page-text-editor.component.spec.ts`; expect new behavior failures.
- [ ] **Step 3: Implement placement UI and page integration.** Put Add signature beside Add text, keep selection handles editor-only, preserve aspect ratio on resize, place initial draft near current viewport center, show Save/Cancel for drafts and Move/resize/Delete for saved overlays. Reuse one idempotency key across retries of the same draft. Guard unsaved navigation and do not require OCR.
- [ ] **Step 4: Run GREEN.** Repeat targeted command, then `npm test -- --watch=false`; expect zero failures.
- [ ] **Step 5: Commit only Task 5 files:** `feat: place and edit signatures on scanned pages`.

### Task 6: Immutable PDF signature snapshots

**Files:**
- Modify: `src/SuperScanner.Domain/Documents/DocumentExport.cs`, `src/SuperScanner.Application/Documents/CreateDocumentExport.cs`, `src/SuperScanner.Infrastructure/Processing/DocumentPdfBuilder.cs`
- Test: `tests/SuperScanner.Domain.Tests/Documents/DocumentExportTests.cs`, `tests/SuperScanner.Application.Tests/Documents/DocumentExportTests.cs`, `tests/SuperScanner.Application.Tests/Processing/DocumentPdfBuilderTests.cs`

**Interfaces:**
- Produce `SignatureOverlaySnapshot(Guid SignatureId, string AssetKey, SignatureBox Box, double ImageAspectRatio)`.
- Extend `DocumentExport.Create` with `IReadOnlyDictionary<Guid, IReadOnlyList<SignatureOverlaySnapshot>> signaturesByPage` and `DocumentExportSnapshotEntry` with a per-page immutable signature list, defaulting to empty for older exports.
- Consume `IPageSignatureRepository.GetActiveForDocumentAsync` from Task 1 when creating an export.

- [ ] **Step 1: Write failing tests.** Export creation snapshots exact signature asset key and box; updating/deleting later does not alter that snapshot; no-signature exports remain readable; PDF draws transparent ink at normalized coordinates over the page; missing/corrupt snapshotted image fails export rather than silently omitting it; a signature mutation marks the prior export outdated.
- [ ] **Step 2: Run RED.** `dotnet test tests/SuperScanner.Domain.Tests/SuperScanner.Domain.Tests.csproj --filter FullyQualifiedName~DocumentExport` and `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter 'FullyQualifiedName~DocumentExport|FullyQualifiedName~DocumentPdfBuilder'`; expect snapshot/render failures.
- [ ] **Step 3: Implement export snapshot and PDF drawing.** Serialize immutable overlay entries at export creation. Draw each PNG in snapshot order using PdfSharp after page JPEG and before searchable text layer; calculate bounds from normalized geometry and guard source/total byte limits. Keep older snapshot JSON compatible.
- [ ] **Step 4: Run GREEN.** Repeat the targeted commands; expect zero failures.
- [ ] **Step 5: Commit only Task 6 files:** `feat: render signature overlays in PDF exports`.

### Task 7: Retired asset cleanup and whole-flow verification

**Files:**
- Create: `src/SuperScanner.Infrastructure/Signatures/SignatureAssetCleanup.cs`
- Create: `src/SuperScanner.Worker/SignatureCleanupWorker.cs`
- Modify: `src/SuperScanner.Worker/Program.cs`
- Test: `tests/SuperScanner.Infrastructure.IntegrationTests/Signatures/SignatureAssetCleanupTests.cs`

**Interfaces:**
- Produce `SignatureAssetCleanup.RunAsync(CancellationToken ct): Task` for a scheduled maintenance job.
- Consume active signature rows, immutable export snapshots, and their `ExpiresAt` values before deleting any private object.

- [ ] **Step 1: Write failing tests.** A deleted signature asset referenced by a queued or unexpired export is retained; an asset with no active signature and no unexpired export reference is deleted; a cleanup retry after object deletion is safe; an active signature asset is never deleted.
- [ ] **Step 2: Run RED.** `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter FullyQualifiedName~SignatureAssetCleanup`; expect missing service/behavior failures.
- [ ] **Step 3: Implement cleanup and register a periodic hosted service in `Program.cs`.** Delete only private objects whose DB references are provably no longer live; use bounded batches and preserve retryability.
- [ ] **Step 4: Run GREEN and broad verification.** Repeat targeted command; then run `dotnet test SuperScanner.slnx` and, from `apps/web`, `npm test -- --watch=false`. Expect zero failures and no skipped signature tests.
- [ ] **Step 5: Commit only Task 7 files:** `feat: clean up retired signature assets`.

## Execution handoff

Implementation starts only after the user reviews this plan and chooses an execution method. Do not add signature code while this plan is pending review. When work starts, check five-hour usage periodically and stop before remaining usage falls below the user-requested 20% reserve.
