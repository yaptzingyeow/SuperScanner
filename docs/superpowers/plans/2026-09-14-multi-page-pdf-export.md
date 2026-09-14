# Multi-Page Import, Page Management, and PDF Export Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import photos and complete multi-page PDFs as ordered, independently editable pages, manage those pages, and export all active Ready pages as one secure PDF.

**Architecture:** Convert uploads into immutable document-level imports, then let the Worker expand each import into deterministic Page records. Persist page ordering and revisions in PostgreSQL, expose optimistic-concurrency page-management APIs, and build immutable export snapshots asynchronously. Angular consumes only API DTOs and presents a responsive, accessible page organizer.

**Tech Stack:** Angular 22.1, Angular CDK 22.1, TypeScript 6, .NET 10, ASP.NET Core minimal APIs, Entity Framework Core 10, PostgreSQL, Cloudflare R2, Poppler, Magick.NET, PDFsharp 6.2.4, Firebase Authentication, Firebase App Check, Vitest, xUnit

**Spec:** `docs/superpowers/specs/2026-09-14-multi-page-pdf-export-design.md`

## Global Constraints

- Original uploads, page source images, derived revisions, and export snapshots are immutable.
- Every protected route uses the existing Firebase authentication and App Check pipeline; unowned resources return `404`.
- New pages append after the last active page; active positions are contiguous and 1-based.
- A PDF that exceeds file, page, pixel, rendered-byte, time, or memory limits is rejected before page creation.
- A PDF is never silently truncated to fit the document page limit.
- Export includes all and only active Ready pages in visible order.
- Export output is image-based; OCR text layers, PDF/A, and vector preservation are outside this plan.
- Storage remains private; downloads are authenticated and use `private, no-store` plus `nosniff`.
- Keep the AI boundary model disabled; imported pages continue through the configured boundary detector.
- Upgrade the local Node.js runtime from 22.14.0 to at least 22.22.3 before Task 9; the installed Angular CLI rejects the current runtime.
- Preserve all unrelated working-tree changes and stage only files named by the current task.
- Use red-green-refactor for every behavior change and run focused tests before each commit.

---

### Task 1: Ordered Page and Export Domain Model

**Files:**
- Modify: `src/SuperScanner.Domain/Documents/Document.cs`
- Modify: `src/SuperScanner.Domain/Documents/Page.cs`
- Create: `src/SuperScanner.Domain/Documents/PageState.cs`
- Create: `src/SuperScanner.Domain/Documents/DocumentExport.cs`
- Create: `src/SuperScanner.Domain/Documents/DocumentExportState.cs`
- Test: `tests/SuperScanner.Domain.Tests/Documents/DocumentTests.cs`
- Test: `tests/SuperScanner.Domain.Tests/Documents/PageTests.cs`
- Create: `tests/SuperScanner.Domain.Tests/Documents/DocumentExportTests.cs`

**Interfaces:**
- Consumes: existing `Document.Create`, `Page` crop/filter revisions, and `DocumentStatus`.
- Produces: `Document.AppendImportedPages`, `Document.ReorderPages`, `Document.RemovePage`, `Document.MarkContentChanged`; `PageState`; and `DocumentExport.Create` used by later persistence and application tasks.

- [ ] **Step 1: Write failing aggregate tests**

Add tests proving append assigns positions 1..N, duplicate `(uploadId, sourcePageIndex)` is idempotent, reorder requires every active page exactly once, remove compacts positions, stale revisions fail, and every export-affecting operation increments the correct revision.

```csharp
[Fact]
public void ReorderPages_UsesExpectedRevisionAndPersistsContiguousPositions()
{
    var document = CreateDocumentWithThreePages();
    var ids = document.Pages.OrderBy(x => x.Position).Select(x => x.Id).ToArray();

    document.ReorderPages([ids[2], ids[0], ids[1]], document.PageOrderRevision, Now);

    Assert.Equal([ids[2], ids[0], ids[1]], document.ActivePages.Select(x => x.Id));
    Assert.Equal([1, 2, 3], document.ActivePages.Select(x => x.Position));
    Assert.Equal(1, document.PageOrderRevision);
    Assert.Equal(1, document.Revision);
}
```

- [ ] **Step 2: Run domain tests and confirm the new APIs fail to compile**

Run: `dotnet test tests/SuperScanner.Domain.Tests/SuperScanner.Domain.Tests.csproj --filter "FullyQualifiedName~Documents"`

Expected: FAIL because `Position`, `PageOrderRevision`, and the new aggregate methods do not exist.

- [ ] **Step 3: Add page lifecycle and ordering behavior**

Implement these exact domain signatures:

```csharp
public IReadOnlyCollection<Page> ActivePages => _pages.Where(x => x.RemovedAt is null)
    .OrderBy(x => x.Position).ToArray();
public long Revision { get; private set; }
public long PageOrderRevision { get; private set; }

public IReadOnlyList<Page> AppendImportedPages(
    Guid sourceUploadId, IReadOnlyList<int> sourcePageIndexes, int maxPages, DateTimeOffset now);
public void ReorderPages(IReadOnlyList<Guid> pageIds, long expectedPageOrderRevision, DateTimeOffset now);
public void RemovePage(Guid pageId, string firebaseUid, DateTimeOffset now);
public void MarkContentChanged(DateTimeOffset now);
```

`AppendImportedPages` reuses existing source tuples, rejects a total above `maxPages`, appends missing indexes in input order, and increments both revisions once when membership changes. `ReorderPages` rejects stale revisions, duplicate/missing IDs, removed pages, and foreign IDs before mutation. `RemovePage` sets removal metadata and compacts active positions.

- [ ] **Step 4: Add Page and DocumentExport state**

```csharp
public enum PageState { Importing, Processing, NeedsCrop, Ready, Failed }

public void MarkImportReady(string originalObjectKey, string originalMediaType);
public string OriginalMediaType { get; private set; }
public void MarkProcessing();
public void MarkReady();
public void MarkFailed(string failureCode);
public void SoftRemove(string firebaseUid, DateTimeOffset now);
public string GetExportObjectKey(); // returns PreviewObjectKey only when State is Ready
```

Create `DocumentExport.Create(Guid id, Document document, string ownerUid, DateTimeOffset now, TimeSpan retention)` to snapshot ordered Ready pages as JSON, reject an empty snapshot, and expose `Queue`, `Start`, `Complete`, and `Fail` transitions. Snapshot entries contain `PageId`, `Position`, `AppliedCropRevision`, `AppliedFilter`, and the exact current processed object key.

- [ ] **Step 5: Run domain tests**

Run: `dotnet test tests/SuperScanner.Domain.Tests/SuperScanner.Domain.Tests.csproj --filter "FullyQualifiedName~Documents"`

Expected: PASS.

- [ ] **Step 6: Commit the domain increment**

```powershell
git add src/SuperScanner.Domain/Documents tests/SuperScanner.Domain.Tests/Documents
git commit -m "feat: model ordered document pages and exports"
```

---

### Task 2: PostgreSQL Schema and Legacy Backfill

**Files:**
- Modify: `src/SuperScanner.Infrastructure/Persistence/AppDbContext.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/Configurations/DocumentConfiguration.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/Configurations/PageConfiguration.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/Configurations/UploadIntentConfiguration.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Configurations/DocumentExportConfiguration.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Migrations/20260914210000_MultiPageDocuments.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs`
- Test: `tests/SuperScanner.Infrastructure.IntegrationTests/Persistence/DocumentPersistenceTests.cs`

**Interfaces:**
- Consumes: domain properties and export entity from Task 1.
- Produces: EF mappings, partial indexes, and a migration compatible with current one-page records.

- [ ] **Step 1: Write failing persistence tests**

Add PostgreSQL integration tests that round-trip revisions, source tuples, page state, soft removal, and exports. Add a test that duplicate active positions and duplicate source tuples fail at the database boundary.

```csharp
[Fact]
public async Task SavesExportSnapshotAndOrderedPageMetadata()
{
    await using var db = CreateDbContext();
    var document = SeedReadyDocument();
    var export = DocumentExport.Create(Guid.NewGuid(), document, document.OwnerFirebaseUid, Now, TimeSpan.FromDays(7));
    db.AddRange(document, export);
    await db.SaveChangesAsync();
    db.ChangeTracker.Clear();

    var stored = await db.DocumentExports.SingleAsync();
    Assert.Equal(document.Revision, stored.DocumentRevision);
    Assert.Equal(2, stored.ReadyPageCount);
}
```

- [ ] **Step 2: Run the focused persistence test and verify failure**

Run: `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter "FullyQualifiedName~DocumentPersistenceTests"`

Expected: FAIL because the new mappings and table do not exist.

- [ ] **Step 3: Configure the model**

Map `Document.Revision` and `PageOrderRevision` as concurrency tokens. Map Page state, source metadata including `OriginalMediaType`, failure/removal metadata, and replace the old unique `(DocumentId, PageNumber)` index with:

```csharp
builder.HasIndex(x => new { x.DocumentId, x.Position })
    .IsUnique()
    .HasFilter("\"RemovedAt\" IS NULL");
builder.HasIndex(x => new { x.SourceUploadId, x.SourcePageIndex }).IsUnique();
```

Add `DbSet<DocumentExport> DocumentExports` and map export ownership, states, timestamps, snapshot JSON, output key, and indexes on `(OwnerFirebaseUid, DocumentId, CreatedAt)`.

- [ ] **Step 4: Add the migration and backfill**

The migration adds new columns nullable first, copies `PageNumber` into `Position`, copies each legacy upload relationship into `SourceUploadId`, sets `SourcePageIndex=1`, derives Page State from existing crop/preview fields, then makes required columns non-null. Make `upload_intents.PageId` nullable and add accepted-import metadata columns. Create `document_exports`, foreign keys, unique source index, and the active-position partial unique index. Preserve `PageNumber` during this migration for rolling compatibility; removal occurs only after all readers use Position.

- [ ] **Step 5: Apply the migration to an empty and a legacy-shaped test database**

Run: `dotnet ef database update --project src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj --startup-project src/SuperScanner.Api/SuperScanner.Api.csproj`

Expected: migration completes without data loss; existing pages have matching Position values.

- [ ] **Step 6: Run persistence tests and commit**

Run: `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter "FullyQualifiedName~DocumentPersistenceTests"`

Expected: PASS.

```powershell
git add src/SuperScanner.Infrastructure/Persistence tests/SuperScanner.Infrastructure.IntegrationTests/Persistence/DocumentPersistenceTests.cs
git commit -m "feat: persist multi-page document state"
```

---

### Task 3: Document-Level Upload Intents and Accepted Imports

**Files:**
- Modify: `src/SuperScanner.Domain/Uploads/UploadIntent.cs`
- Modify: `src/SuperScanner.Application/Uploads/CreateUploadIntent.cs`
- Modify: `src/SuperScanner.Application/Uploads/ValidateUpload.cs`
- Modify: `src/SuperScanner.Application/Uploads/GetUploadStatus.cs`
- Modify: `src/SuperScanner.Application/Abstractions/IUploadIntentRepository.cs`
- Modify: `src/SuperScanner.Application/Abstractions/IUploadValidationRepository.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/EfUploadIntentRepository.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/EfUploadValidationRepository.cs`
- Modify: `src/SuperScanner.Api/Endpoints/UploadsEndpoints.cs`
- Test: `tests/SuperScanner.Application.Tests/Uploads/CreateUploadIntentTests.cs`
- Test: `tests/SuperScanner.Application.Tests/Uploads/ValidateUploadTests.cs`
- Test: `tests/SuperScanner.Api.IntegrationTests/Uploads/UploadOwnershipTests.cs`

**Interfaces:**
- Consumes: nullable legacy `PageId` and accepted-import columns from Task 2.
- Produces: new document-level upload contract and immutable `AcceptedObjectKey` for import expansion.

- [ ] **Step 1: Change tests to require no Page at intent creation**

Assert that creating an upload leaves document page membership unchanged, returns no `pageId`, and validation promotes to `imports/{documentId}/{uploadId}/{sha256}`.

```csharp
var result = await handler.HandleAsync(owner, document.Id, request, CancellationToken.None);
Assert.Empty(document.Pages);
Assert.Equal(uploadId: result.UploadId, actual: storedUpload.Id);
Assert.Null(storedUpload.PageId);
```

- [ ] **Step 2: Run upload tests and verify failure**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~Uploads"`

Expected: FAIL because creation still adds a Page and validation writes its original key.

- [ ] **Step 3: Make UploadIntent document-scoped**

Change `PageId` to `Guid?`, add original filename plus accepted import metadata, and expose:

```csharp
public void Accept(string acceptedObjectKey, DateTimeOffset acceptedAt);
public void BeginExpansion(int discoveredPageCount);
public void RecordExpansion(int createdPages, int failedPages, string? failureCode);
```

`CreateUploadIntent` validates the file exactly as today but stores no Page. `UploadIntentDto` becomes `(UploadId, PutUrl, ExpiresAt)`.

- [ ] **Step 4: Promote validation to the immutable import key**

Replace page mutation with:

```csharp
var acceptedKey = $"imports/{upload.DocumentId:N}/{upload.Id:N}/{actualSha256}";
await objectStore.PromoteAsync(upload.QuarantineObjectKey, acceptedKey, cancellationToken);
upload.Accept(acceptedKey, clock.UtcNow);
target.Document.MarkProcessing(clock.UtcNow);
```

Preserve signature, size, SHA-256, expiry, and malware checks. Update audit JSON to contain upload ID only when `PageId` is null.

- [ ] **Step 5: Expose expansion progress in upload status**

Return `state`, `discoveredPageCount`, `createdPageCount`, `failedPageCount`, and safe `errorCode`. Update ownership integration tests to verify another Firebase UID gets `404` for create, complete, and status operations.

- [ ] **Step 6: Run tests and commit**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~Uploads"`

Run: `dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj --filter "FullyQualifiedName~UploadOwnershipTests"`

Expected: PASS.

```powershell
git add src/SuperScanner.Domain/Uploads src/SuperScanner.Application/Uploads src/SuperScanner.Application/Abstractions src/SuperScanner.Infrastructure/Persistence/EfUploadIntentRepository.cs src/SuperScanner.Infrastructure/Persistence/EfUploadValidationRepository.cs src/SuperScanner.Api/Endpoints/UploadsEndpoints.cs tests/SuperScanner.Application.Tests/Uploads tests/SuperScanner.Api.IntegrationTests/Uploads
git commit -m "feat: accept document-level imports"
```

---

### Task 4: Safe Photo and Multi-Page PDF Expansion

**Files:**
- Create: `src/SuperScanner.Infrastructure/Processing/DocumentImportOptions.cs`
- Create: `src/SuperScanner.Infrastructure/Processing/IPdfImportTool.cs`
- Create: `src/SuperScanner.Infrastructure/Processing/PopplerPdfImportTool.cs`
- Create: `src/SuperScanner.Infrastructure/Processing/DocumentImportProcessor.cs`
- Modify: `src/SuperScanner.Infrastructure/Processing/DocumentPreviewProcessor.cs`
- Modify: `src/SuperScanner.Infrastructure/Processing/CropProcessor.cs`
- Modify: `src/SuperScanner.Application/Abstractions/IObjectStore.cs`
- Modify: `src/SuperScanner.Infrastructure/ObjectStorage/R2ObjectStore.cs`
- Modify: `src/SuperScanner.Worker/Program.cs`
- Modify: `src/SuperScanner.Worker/appsettings.json`
- Test: `tests/SuperScanner.Application.Tests/Processing/DocumentImportProcessorTests.cs`
- Create fixture: `tests/fixtures/import/three-pages.pdf`
- Create fixture: `tests/fixtures/import/encrypted.pdf`

**Interfaces:**
- Consumes: accepted import object key, `Document.AppendImportedPages`, R2 store, Magick.NET, and Poppler installed in the Worker image.
- Produces: `DocumentImportProcessor.ProcessAsync(Guid uploadId, CancellationToken)` and deterministic Page sources for existing crop processing.

- [ ] **Step 1: Write processor tests with a fake PDF tool**

Cover one photo -> one page; three-page PDF -> three pages; retry -> no duplicates; encrypted/over-limit PDF -> zero pages; page 2 render failure -> pages 1 and 3 advance while page 2 is Failed; insufficient document capacity -> zero pages.

```csharp
[Fact]
public async Task ThreePagePdf_CreatesOrderedDeterministicPages()
{
    pdfTool.InspectResult = new PdfInspection(3, false, 2_000_000);
    await processor.ProcessAsync(upload.Id, CancellationToken.None);
    Assert.Equal([1, 2, 3], document.ActivePages.Select(x => x.SourcePageIndex));
    Assert.Equal([1, 2, 3], document.ActivePages.Select(x => x.Position));
}
```

- [ ] **Step 2: Run the new processor tests and verify failure**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~DocumentImportProcessorTests"`

Expected: FAIL because the processor and PDF tool do not exist.

- [ ] **Step 3: Implement bounded Poppler inspection and rendering**

Define:

```csharp
public sealed record PdfInspection(int PageCount, bool IsEncrypted, long EstimatedDecodedPixels);
public interface IPdfImportTool
{
    Task<PdfInspection> InspectAsync(string sourcePath, CancellationToken ct);
    Task RenderPageAsync(string sourcePath, int pageIndex, string outputPngPath, CancellationToken ct);
}
```

Invoke `pdfinfo` and `pdftoppm` using `ProcessStartInfo.ArgumentList`, `UseShellExecute=false`, `CreateNoWindow=true`, redirected stderr, and linked timeout tokens. Never concatenate user input into a command. Kill the process tree on timeout. Map encrypted, invalid, timeout, and dimension-limit results to safe codes.

- [ ] **Step 4: Implement deterministic import expansion**

`DocumentImportProcessor` downloads at most configured bytes, inspects before page creation, creates all PDF placeholders in one transaction, then renders each page to an immutable key. A photo page uses the accepted import key and declared image media type; a PDF page uses its rendered PNG key and `image/png`:

```csharp
var sourceKey = $"page-sources/{document.Id:N}/{page.Id:N}/source.png";
if (await store.HeadAsync(sourceKey, ct) is null)
    await store.WriteAsync(sourceKey, "image/png", renderedPage, ct);
page.MarkImportReady(sourceKey, "image/png");
```

Add `IObjectStore.WriteAsync(string objectKey, string mediaType, Stream content, CancellationToken ct)` and implement it with R2 `PutObjectAsync`; retain `WriteImageAsync` only as a temporary forwarding compatibility method. Reuse `DocumentPreviewProcessor` through a page-oriented method `ProcessPageAsync(Guid pageId, CancellationToken)` that decodes according to `Page.OriginalMediaType`, so image and PDF pages share preview creation. Replace `CropProcessor.EnsureDetectionAsync(Guid uploadId, ...)` with `EnsureDetectionForPageAsync(Guid pageId, ...)` and enqueue detection after each preview. When a crop/filter output becomes current, mark the Page Ready and atomically increment the owning Document Revision; detection alone does not change export revision. Record per-page safe failures and aggregate counts on the import.

- [ ] **Step 5: Bind strict default limits**

Add `DocumentImport` settings: `MaxUploadBytes=26214400`, `MaxPagesPerImport=50`, `MaxPagesPerDocument=50`, `MaxDecodedPixels=250000000`, `MaxRenderedBytes=524288000`, `InspectTimeoutSeconds=15`, `PageRenderTimeoutSeconds=45`, and `MaxAttempts=6`. Validate positive values at startup.

- [ ] **Step 6: Run processor tests and commit**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~DocumentImportProcessorTests"`

Expected: PASS.

```powershell
git add src/SuperScanner.Infrastructure/Processing src/SuperScanner.Application/Abstractions/IObjectStore.cs src/SuperScanner.Infrastructure/ObjectStorage/R2ObjectStore.cs src/SuperScanner.Worker tests/SuperScanner.Application.Tests/Processing/DocumentImportProcessorTests.cs tests/fixtures/import
git commit -m "feat: expand photos and multi-page PDFs"
```

---

### Task 5: Worker Routing and Mixed Page Status

**Files:**
- Modify: `src/SuperScanner.Worker/UploadValidationJobRunner.cs`
- Modify: `src/SuperScanner.Infrastructure/Processing/CropDocumentStatus.cs`
- Modify: `src/SuperScanner.Api/Endpoints/DocumentPreviewEndpoints.cs`
- Modify: `src/SuperScanner.Api/Program.cs`
- Test: `tests/SuperScanner.Application.Tests/Processing/UploadValidationJobRunnerTests.cs`
- Test: `tests/SuperScanner.Api.IntegrationTests/Documents/DocumentsEndpointsTests.cs`

**Interfaces:**
- Consumes: `DocumentImportProcessor.ProcessAsync` from Task 4 and page state from Task 1.
- Produces: `ExpandDocumentImport` job routing and a document detail DTO suitable for the organizer.

- [ ] **Step 1: Write failing routing and detail tests**

Assert accepted validation enqueues `ExpandDocumentImport`, the runner dispatches it using an upload GUID payload, and document detail returns mixed page states ordered by Position with both revisions and import progress.

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~UploadValidationJobRunnerTests"`

Run: `dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj --filter "FullyQualifiedName~DocumentsEndpointsTests"`

Expected: FAIL because the runner still queues `ProcessDocument` and detail is PageNumber-based.

- [ ] **Step 3: Route import jobs and bounded retries**

Replace the accepted-upload enqueue with:

```csharp
await queue.EnqueueAsync(
    "ExpandDocumentImport",
    uploadId.ToString(),
    $"upload:{uploadId}:expand:v1",
    workCancellation.Token);
```

Dispatch this type to `DocumentImportProcessor`. Reschedule with `import_failed`; after the configured attempt limit, mark the import and its remaining Importing placeholders Failed.

- [ ] **Step 4: Compute summary status from active page states**

Ignore removed pages. Use `Processing` when any active page is Importing/Processing, `NeedsCrop` when no work is running and any page needs crop, `Ready` when at least one page is Ready and none is running, `Failed` only when every active page has failed, and `Draft` when none is active.

- [ ] **Step 5: Return organizer detail data**

Return `revision`, `pageOrderRevision`, active pages ordered by Position, Page State, safe failure code, preview/crop/filter revisions, imports, and latest export summary. Remove the first-page-only PDF note from the contract and UI data.

- [ ] **Step 6: Run focused tests and commit**

Run both Step 2 commands. Expected: PASS.

```powershell
git add src/SuperScanner.Worker/UploadValidationJobRunner.cs src/SuperScanner.Infrastructure/Processing/CropDocumentStatus.cs src/SuperScanner.Api/Endpoints/DocumentPreviewEndpoints.cs src/SuperScanner.Api/Program.cs tests/SuperScanner.Application.Tests/Processing/UploadValidationJobRunnerTests.cs tests/SuperScanner.Api.IntegrationTests/Documents/DocumentsEndpointsTests.cs
git commit -m "feat: expose multi-page processing status"
```

---

### Task 6: Reorder and Soft-Remove APIs

**Files:**
- Create: `src/SuperScanner.Application/Documents/ReorderPages.cs`
- Create: `src/SuperScanner.Application/Documents/RemovePage.cs`
- Modify: `src/SuperScanner.Application/Abstractions/IDocumentRepository.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/EfDocumentRepository.cs`
- Create: `src/SuperScanner.Api/Endpoints/PageManagementEndpoints.cs`
- Modify: `src/SuperScanner.Api/Program.cs`
- Create: `tests/SuperScanner.Application.Tests/Documents/ReorderPagesTests.cs`
- Create: `tests/SuperScanner.Application.Tests/Documents/RemovePageTests.cs`
- Create: `tests/SuperScanner.Api.IntegrationTests/Documents/PageManagementEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 1 aggregate ordering operations and Task 2 concurrency mappings.
- Produces: `PUT /api/documents/{id}/page-order` and `DELETE /api/documents/{id}/pages/{pageId}`.

- [ ] **Step 1: Write failing command tests**

Cover success, stale revision, duplicate/missing/foreign/removed page IDs, ownership hiding, final-page removal, audit actions, and transaction rollback.

```csharp
var result = await handler.HandleAsync(owner, document.Id,
    new ReorderPagesRequest(document.PageOrderRevision, [third, first, second]), ct);
Assert.Equal([third, first, second], result.Pages.Select(x => x.Id));
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~ReorderPagesTests|FullyQualifiedName~RemovePageTests"`

Expected: FAIL because the commands do not exist.

- [ ] **Step 3: Implement locked document mutation**

Add repository method:

```csharp
Task<Document?> FindOwnedForUpdateAsync(string ownerUid, Guid documentId, CancellationToken ct);
```

Implement it in a transaction using a parameterized PostgreSQL `SELECT ... FOR UPDATE` for the document row, then load active pages. Commands call domain methods, append audit events (`document.pages_reordered`, `document.page_removed`), save once, and commit.

- [ ] **Step 4: Map precise HTTP results**

Return `200` with new order and revisions, `404` for unowned/missing resources, `409` with current `pageOrderRevision` for stale commands, and `400` validation details for malformed page lists. The delete route returns `204` after success.

- [ ] **Step 5: Run application and API tests**

Run Step 2 plus:

`dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj --filter "FullyQualifiedName~PageManagementEndpointsTests"`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/SuperScanner.Application/Documents src/SuperScanner.Application/Abstractions/IDocumentRepository.cs src/SuperScanner.Infrastructure/Persistence/EfDocumentRepository.cs src/SuperScanner.Api/Endpoints/PageManagementEndpoints.cs src/SuperScanner.Api/Program.cs tests/SuperScanner.Application.Tests/Documents tests/SuperScanner.Api.IntegrationTests/Documents/PageManagementEndpointsTests.cs
git commit -m "feat: reorder and remove document pages"
```

---

### Task 7: Immutable Export Snapshot API

**Files:**
- Create: `src/SuperScanner.Application/Documents/CreateDocumentExport.cs`
- Create: `src/SuperScanner.Application/Documents/GetDocumentExport.cs`
- Create: `src/SuperScanner.Application/Abstractions/IDocumentExportRepository.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/EfDocumentExportRepository.cs`
- Create: `src/SuperScanner.Api/Endpoints/DocumentExportEndpoints.cs`
- Modify: `src/SuperScanner.Api/Program.cs`
- Create: `tests/SuperScanner.Application.Tests/Documents/DocumentExportTests.cs`
- Create: `tests/SuperScanner.Api.IntegrationTests/Documents/DocumentExportEndpointsTests.cs`

**Interfaces:**
- Consumes: `DocumentExport.Create` and persistence from Tasks 1-2 plus processing queue.
- Produces: create/status/download metadata endpoints and `BuildDocumentPdf` jobs.

- [ ] **Step 1: Write failing snapshot and authorization tests**

Prove export excludes Processing/Failed/removed pages, preserves Ready order and revisions, rejects zero Ready pages, remains unchanged after reorder, reports outdated state, and hides another user's export.

```csharp
var result = await create.HandleAsync(owner, document.Id, ct);
Assert.Equal(3, result.ReadyPageCount);
Assert.Equal(2, result.ExcludedPageCount);
Assert.Equal("BuildDocumentPdf", queue.Single().Type);
```

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~DocumentExportTests"`

Expected: FAIL because export handlers do not exist.

- [ ] **Step 3: Implement transactional export creation**

Load the owned document and active pages in one transaction, create the snapshot, persist it, append `document.export_created`, and enqueue:

```csharp
await queue.EnqueueAsync("BuildDocumentPdf", export.Id.ToString(),
    $"export:{export.Id}:build:v1", ct);
```

Return export ID, state, Ready/excluded counts, status URL, and `isOutdated` computed as `export.DocumentRevision != document.Revision`.

- [ ] **Step 4: Map create and status endpoints**

Map `POST /api/documents/{id}/exports` and `GET /api/documents/{id}/exports/{exportId}`. Return `422` when no Ready page exists, `404` for ownership mismatch, `202` for a queued export, and `200` for status.

- [ ] **Step 5: Run tests and commit**

Run Step 2 plus:

`dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj --filter "FullyQualifiedName~DocumentExportEndpointsTests"`

Expected: PASS.

```powershell
git add src/SuperScanner.Application/Documents src/SuperScanner.Application/Abstractions/IDocumentExportRepository.cs src/SuperScanner.Infrastructure/Persistence/EfDocumentExportRepository.cs src/SuperScanner.Api/Endpoints/DocumentExportEndpoints.cs src/SuperScanner.Api/Program.cs tests/SuperScanner.Application.Tests/Documents/DocumentExportTests.cs tests/SuperScanner.Api.IntegrationTests/Documents/DocumentExportEndpointsTests.cs
git commit -m "feat: create immutable document exports"
```

---

### Task 8: PDF Assembly, Worker Job, and Secure Download

**Files:**
- Modify: `src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj`
- Modify: `src/SuperScanner.Infrastructure/packages.lock.json`
- Create: `src/SuperScanner.Infrastructure/Processing/DocumentPdfBuilder.cs`
- Modify: `src/SuperScanner.Worker/UploadValidationJobRunner.cs`
- Modify: `src/SuperScanner.Worker/Program.cs`
- Modify: `src/SuperScanner.Api/Endpoints/DocumentExportEndpoints.cs`
- Create: `tests/SuperScanner.Application.Tests/Processing/DocumentPdfBuilderTests.cs`
- Modify: `tests/SuperScanner.Api.IntegrationTests/Documents/DocumentExportEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 7 export snapshot and queue; `IObjectStore.OpenReadAsync`; private R2 storage.
- Produces: immutable image-based PDF and authenticated download endpoint.

- [ ] **Step 1: Add PDFsharp 6.2.4 and write failing builder tests**

Run: `dotnet add src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj package PDFsharp --version 6.2.4`

Expected: project and lock file contain the cross-platform MIT-licensed stable package, not GDI or WPF variants.

Use three distinct small JPEG fixtures. Assert the output has three pages in snapshot order, dimensions follow image aspect ratios, retry does not replace a completed export, a missing exact revision fails with `export_asset_missing`, and no partial output key becomes Ready.

```csharp
await builder.BuildAsync(export.Id, CancellationToken.None);
var saved = await store.OpenReadAsync(export.OutputObjectKey!, CancellationToken.None);
using var pdf = PdfReader.Open(saved, PdfDocumentOpenMode.Import);
Assert.Equal(3, pdf.PageCount);
```

- [ ] **Step 2: Run builder tests and verify failure**

Run: `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter "FullyQualifiedName~DocumentPdfBuilderTests"`

Expected: FAIL because the builder does not exist.

- [ ] **Step 3: Build from the exact snapshot**

Open snapshot keys only. For each image, create a PDF page using pixel dimensions at 96 DPI, draw the image edge-to-edge, save to a bounded temporary stream, and call `IObjectStore.WriteAsync(outputKey, "application/pdf", stream, ct)` once for `exports/{documentId}/{exportId}/document.pdf`. Mark Ready only after storage succeeds. On terminal failure mark `DocumentExportState.Failed` with `export_asset_missing`, `export_decode_failed`, `export_size_limit`, or `export_build_failed`.

- [ ] **Step 4: Dispatch BuildDocumentPdf and add download**

Route the job type to `DocumentPdfBuilder.BuildAsync`. Map `GET /api/documents/{id}/exports/{exportId}/download`; recheck owner, Ready state, and expiry, append `document.export_downloaded`, set security headers, and stream with filename `{sanitized-document-title}.pdf`.

- [ ] **Step 5: Run tests and commit**

Run Step 2 and the export endpoint test from Task 7. Expected: PASS.

```powershell
git add src/SuperScanner.Infrastructure src/SuperScanner.Worker src/SuperScanner.Api/Endpoints/DocumentExportEndpoints.cs tests/SuperScanner.Application.Tests/Processing/DocumentPdfBuilderTests.cs tests/SuperScanner.Api.IntegrationTests/Documents/DocumentExportEndpointsTests.cs
git commit -m "feat: build and download document PDFs"
```

---

### Task 9: Angular Import and Organizer API Layer

**Files:**
- Modify: `apps/web/package.json`
- Modify: `apps/web/package-lock.json`
- Create: `apps/web/src/app/documents/document.models.ts`
- Modify: `apps/web/src/app/documents/documents-api.service.ts`
- Modify: `apps/web/src/app/documents/upload.service.ts`
- Modify: `apps/web/src/app/documents/upload.service.spec.ts`
- Create: `apps/web/src/app/documents/documents-api.service.spec.ts`

**Interfaces:**
- Consumes: Tasks 3, 5, 6, and 7 HTTP contracts.
- Produces: typed Angular methods and independent progress for adding multiple files.

- [ ] **Step 1: Install Angular CDK compatible with Angular 22.1**

Run from `apps/web`: `npm install @angular/cdk@^22.1.0`

Expected: `package.json` and lock file contain `@angular/cdk` 22.1-compatible dependency.

- [ ] **Step 2: Write failing service tests**

Test initial create + upload, adding multiple files to an existing document, independent failure retention, upload expansion polling, reorder payload, remove, export creation/status, and binary download.

```typescript
await service.addFiles('doc-1', [photo, pdf]);
expect(api.createUploadIntent).toHaveBeenCalledTimes(2);
expect(service.items().map(x => x.fileName)).toEqual(['photo.jpg', 'three-pages.pdf']);
```

- [ ] **Step 3: Run Angular service tests and verify failure**

Run from `apps/web`: `npm test -- --run --include src/app/documents/upload.service.spec.ts --include src/app/documents/documents-api.service.spec.ts`

Expected: FAIL because batch APIs and DTOs do not exist.

- [ ] **Step 4: Centralize exact DTOs**

Define `DocumentDetail`, `DocumentPage`, `DocumentImport`, `DocumentExport`, `ReorderPagesRequest`, and `UploadItemProgress` in `document.models.ts`. Remove duplicate inline detail interfaces. Add API methods:

```typescript
getDocument(id: string): Promise<DocumentDetail>;
reorderPages(id: string, request: ReorderPagesRequest): Promise<DocumentDetail>;
removePage(id: string, pageId: string): Promise<void>;
createExport(id: string): Promise<DocumentExport>;
getExport(id: string, exportId: string): Promise<DocumentExport>;
downloadExport(id: string, exportId: string): Promise<Blob>;
```

- [ ] **Step 5: Split upload-one from batch orchestration**

Keep SHA-256 and presigned PUT behavior in `uploadOne(documentId, file)`. `addFiles` validates every file, starts sequentially with per-file signals to avoid saturating the browser, preserves successful results when another file fails, and polls until expansion is complete/rejected/failed. Initial `upload(title,file)` creates the document then delegates to `addFiles`.

- [ ] **Step 6: Run tests and commit**

Run Step 3. Expected: PASS.

```powershell
git add apps/web/package.json apps/web/package-lock.json apps/web/src/app/documents/document.models.ts apps/web/src/app/documents/documents-api.service.ts apps/web/src/app/documents/documents-api.service.spec.ts apps/web/src/app/documents/upload.service.ts apps/web/src/app/documents/upload.service.spec.ts
git commit -m "feat: add multi-page organizer client APIs"
```

---

### Task 10: Responsive Accessible Page Organizer

**Files:**
- Refactor: `apps/web/src/app/documents/document-detail.component.ts`
- Create: `apps/web/src/app/documents/document-detail.component.html`
- Create: `apps/web/src/app/documents/document-detail.component.scss`
- Create: `apps/web/src/app/documents/page-card.component.ts`
- Create: `apps/web/src/app/documents/page-card.component.html`
- Create: `apps/web/src/app/documents/page-card.component.scss`
- Create: `apps/web/src/app/documents/add-pages-dialog.component.ts`
- Create: `apps/web/src/app/documents/add-pages-dialog.component.html`
- Create: `apps/web/src/app/documents/add-pages-dialog.component.scss`
- Create: `apps/web/src/app/documents/document-detail.component.spec.ts`
- Create: `apps/web/src/app/documents/page-card.component.spec.ts`

**Interfaces:**
- Consumes: Task 9 models/services and Angular CDK drag-drop.
- Produces: visual organizer, add-pages flow, persistent reorder, keyboard moves, and confirmed removal.

- [ ] **Step 1: Write failing organizer tests**

Test ordered page cards, all page states, thumbnail labels, Add pages progress, drag drop, Move earlier/later, `409` reload behavior, removal confirmation, and empty document state.

```typescript
component.drop({ previousIndex: 3, currentIndex: 1 } as CdkDragDrop<DocumentPage[]>);
expect(api.reorderPages).toHaveBeenCalledWith('doc-1', {
  expectedPageOrderRevision: 7,
  pageIds: ['p1', 'p4', 'p2', 'p3'],
});
```

- [ ] **Step 2: Run organizer tests and verify failure**

Run from `apps/web`: `npm test -- --run --include src/app/documents/document-detail.component.spec.ts --include src/app/documents/page-card.component.spec.ts`

Expected: FAIL because organizer components do not exist.

- [ ] **Step 3: Build the page grid and cards**

Use `CdkDropList`/`CdkDrag`, responsive CSS grid, visible insertion feedback, stable page IDs, and object-URL cleanup. Each card displays position, state, filter, retry/crop availability, and thumbnail. Route card selection to the existing crop editor.

- [ ] **Step 4: Persist pointer and keyboard reorder**

Apply optimistic order locally, send the full active ID list with the current revision, and replace local state with the server response. On `409`, reload and announce `Page order changed in another session. The latest order has been restored.` through an `aria-live` region. Move controls call the same reorder method.

- [ ] **Step 5: Add pages and confirmed soft removal**

The dialog accepts multiple `.pdf,.jpg,.jpeg,.png,.heic` files and displays each file's stage and safe error. Removal uses an explicit confirmation containing the page number; after API success, remove the card and renumber from the returned/refetched document.

- [ ] **Step 6: Run component tests and commit**

Run Step 2. Expected: PASS.

```powershell
git add apps/web/src/app/documents/document-detail.component.* apps/web/src/app/documents/page-card.component.* apps/web/src/app/documents/add-pages-dialog.component.*
git commit -m "feat: organize document pages"
```

---

### Task 11: Export Progress and Download UI

**Files:**
- Modify: `apps/web/src/app/documents/document-detail.component.ts`
- Modify: `apps/web/src/app/documents/document-detail.component.html`
- Modify: `apps/web/src/app/documents/document-detail.component.scss`
- Create: `apps/web/src/app/documents/export-status.component.ts`
- Create: `apps/web/src/app/documents/export-status.component.html`
- Create: `apps/web/src/app/documents/export-status.component.scss`
- Modify: `apps/web/src/app/documents/document-detail.component.spec.ts`
- Create: `apps/web/src/app/documents/export-status.component.spec.ts`

**Interfaces:**
- Consumes: export APIs and `DocumentExport` DTO from Task 9.
- Produces: Ready/excluded confirmation, status polling, outdated messaging, and browser download.

- [ ] **Step 1: Write failing export UI tests**

Cover disabled export with zero Ready pages, confirmation counts, Queued/Processing polling, Failed retry, Ready download, and Outdated -> Generate updated PDF.

```typescript
expect(fixture.nativeElement.querySelector('[data-testid="export-summary"]').textContent)
  .toContain('3 ready pages will be included. 2 pages will be excluded.');
```

- [ ] **Step 2: Run export UI tests and verify failure**

Run from `apps/web`: `npm test -- --run --include src/app/documents/document-detail.component.spec.ts --include src/app/documents/export-status.component.spec.ts`

Expected: FAIL because export status UI does not exist.

- [ ] **Step 3: Add export confirmation and polling**

Compute Ready/excluded counts from active pages, require confirmation, call `createExport`, and poll every three seconds only while Queued/Processing. Stop polling on destroy, Ready, or Failed. Announce state changes through `aria-live`.

- [ ] **Step 4: Add authenticated download and outdated state**

Fetch the Blob through `DocumentsApiService`, create an object URL, click a temporary anchor named from the document title, and revoke the URL. When `isOutdated` is true, retain download access to the old export and show **Generate updated PDF** as the primary action.

- [ ] **Step 5: Run tests and commit**

Run Step 2. Expected: PASS.

```powershell
git add apps/web/src/app/documents/document-detail.component.* apps/web/src/app/documents/export-status.component.*
git commit -m "feat: export and download document PDFs"
```

---

### Task 12: Operational Configuration and End-to-End Acceptance

**Files:**
- Modify: `.env.example`
- Modify: `docker-compose.yml`
- Modify: `src/SuperScanner.Worker/appsettings.Development.json`
- Create: `docs/operations/multi-page-import-export.md`
- Create: `apps/web/e2e/multi-page-document.spec.ts`
- Modify: `tests/SuperScanner.Api.IntegrationTests/Auth/AuthenticationBoundaryTests.cs`

**Interfaces:**
- Consumes: the complete backend, Worker, storage, and Angular flow.
- Produces: deployable configuration, security regression coverage, and executable acceptance evidence.

- [ ] **Step 1: Write the failing end-to-end scenario**

Use the three-page fixture, append one photo, wait for four page cards, drag page 4 into position 2, export, download, and assert four PDF pages appear in the visible order. Add auth boundary cases for reorder, removal, export status, and download with missing/invalid App Check.

- [ ] **Step 2: Run focused security and web acceptance tests**

Run: `dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj --filter "FullyQualifiedName~AuthenticationBoundaryTests"`

Run from `apps/web`: `npm run e2e -- --grep "multi-page document"`

Expected before configuration completion: FAIL with missing import/export settings or unavailable fixture processing.

- [ ] **Step 3: Document and wire exact settings**

Add environment mappings for every `DocumentImport` limit plus `DocumentExport__MaxOutputBytes=104857600`, `DocumentExport__RetentionDays=7`, and `Pages__SoftDeleteRetentionDays=30`. The operations guide documents Poppler requirements, migration order, R2 prefixes, retry codes, Railway variables, cleanup retention, and a rollback that disables new imports/exports without deleting stored data.

- [ ] **Step 4: Run full verification**

Run: `dotnet test SuperScanner.slnx --no-restore`

Run from `apps/web`: `npm test -- --run`

Run from `apps/web`: `npm run build`

Run from `apps/web`: `npm run e2e -- --grep "multi-page document"`

Expected: every command exits 0; the acceptance test downloads a four-page PDF in the requested order.

- [ ] **Step 5: Inspect logs and storage safety**

Verify logs contain IDs, counts, states, durations, and safe codes only. Verify R2 has immutable `imports/`, `page-sources/`, revisioned preview/crop assets, and one final `exports/{documentId}/{exportId}/document.pdf`; no partial export is downloadable.

- [ ] **Step 6: Commit the operational increment**

```powershell
git add .env.example docker-compose.yml src/SuperScanner.Worker/appsettings.Development.json docs/operations/multi-page-import-export.md apps/web/e2e/multi-page-document.spec.ts tests/SuperScanner.Api.IntegrationTests/Auth/AuthenticationBoundaryTests.cs
git commit -m "docs: operate multi-page import and export"
```

## Completion Checklist

- [ ] Three-page PDF produces three ordered editable pages.
- [ ] Photo appended to that document becomes page 4.
- [ ] Additional PDFs append every valid source page without duplication.
- [ ] Failed PDF source pages remain visible and retryable at reserved positions.
- [ ] Drag, touch, and keyboard reorder persist and reject stale revisions.
- [ ] Soft removal compacts active positions without immediate asset deletion.
- [ ] Export snapshots all Ready pages, excludes non-ready pages, and preserves visible order.
- [ ] Later edits mark old exports outdated without mutating them.
- [ ] Authenticated private download succeeds; unowned access returns `404`.
- [ ] Database migration preserves all existing documents and originals.
- [ ] Focused, full, build, and end-to-end verification commands pass.
