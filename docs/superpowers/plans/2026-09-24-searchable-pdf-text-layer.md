# Searchable PDF Text Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a deterministic invisible word-level OCR text layer to eligible PDF export pages while preserving image-only fallback and existing visual output.

**Architecture:** Export creation snapshots the exact matching Ready OCR result beside each immutable page asset. `DocumentPdfBuilder` draws the existing page image, then delegates eligible OCR words to a focused PDF text-layer writer; missing or stale OCR degrades only that page to image-only. Export responses expose aggregate searchability without exposing OCR text.

**Tech Stack:** .NET 10, EF Core 10, PostgreSQL, PDFsharp 6.2.4, xUnit, Angular, TypeScript

**Spec:** `docs/superpowers/specs/2026-09-24-searchable-pdf-and-font-catalogue-design.md`

## Global Constraints

- Export must never wait for or invoke OCR.
- OCR must match the exact snapshotted page source; never substitute another result.
- Existing snapshots must deserialize and export as image-only.
- Invalid individual OCR words are skipped; unsafe PDF/font/output failures fail closed.
- OCR text and geometry must never enter logs, metrics, traces, analytics, or safe errors.
- The visible page image remains the source of visual truth; no editor overlay becomes PDF content.
- Continue enforcing current page, pixel, byte, cancellation, private-storage, and immutable-write limits.

## Review Focus

- A newer Ready OCR result exists after export creation: the job must use only the snapshotted result or image-only fallback.
- Legacy snapshot JSON omits OCR fields: it must still build the same image-only PDF.
- One word has malformed/out-of-bounds geometry: valid words and the page must remain searchable/exportable.
- Mixed OCR availability across multiple pages: the export must be `PartiallySearchable` with correct counts.
- Sensitive OCR text appears in an exception: captured logs and API failures must not contain it.

---

### Task 1: Snapshot Exact OCR Eligibility

**Files:**
- Modify: `src/SuperScanner.Domain/Documents/DocumentExport.cs`
- Modify: `src/SuperScanner.Application/Documents/CreateDocumentExport.cs`
- Modify: `src/SuperScanner.Application/Abstractions/IOcrRepository.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/EfOcrRepository.cs`
- Test: `tests/SuperScanner.Domain.Tests/Documents/DocumentExportTests.cs`
- Test: `tests/SuperScanner.Application.Tests/Documents/DocumentExportTests.cs`

**Interfaces:**
- Produces: `DocumentExportSnapshotEntry.OcrResultId : Guid?`, `OcrSourceObjectKey : string?`, `OcrSourceFingerprint : string?`
- Produces: `IOcrRepository.FindReadyByPageIdsAsync(IReadOnlyCollection<Guid> pageIds, CancellationToken ct)`
- Consumes: existing `PageOcrResult` source identity and `DocumentExport.Create`

- [ ] **Step 1: Write failing domain tests** proving a matching Ready OCR ID is recorded, nonmatching/unfinished OCR is omitted, and deserializing old JSON yields null OCR fields.
- [ ] **Step 2: Run the focused tests** with `dotnet test tests/SuperScanner.Domain.Tests/SuperScanner.Domain.Tests.csproj --filter FullyQualifiedName~DocumentExportTests`; expect failures for missing snapshot fields.
- [ ] **Step 3: Extend the snapshot contract** with nullable positional-safe properties and change `DocumentExport.Create` to accept a read-only page-to-OCR map:

```csharp
public sealed record DocumentExportSnapshotEntry(
    Guid PageId, int Position, int AppliedCropRevision, string AppliedFilter,
    string ProcessedObjectKey, Guid? OcrResultId = null,
    string? OcrSourceObjectKey = null, string? OcrSourceFingerprint = null);
```

- [ ] **Step 4: Write failing application tests** for one batched OCR lookup, exact source matching, no OCR wait/job request, and a newer OCR result created after snapshot not changing the stored JSON.
- [ ] **Step 5: Implement the repository query and create-handler join** using one no-tracking query with elements excluded; select only Ready results whose page and active source identity match.
- [ ] **Step 6: Run domain and application export tests**; expect all focused tests to pass.
- [ ] **Step 7: Commit** with `git add src/SuperScanner.Domain/Documents/DocumentExport.cs src/SuperScanner.Application/Documents/CreateDocumentExport.cs src/SuperScanner.Application/Abstractions/IOcrRepository.cs src/SuperScanner.Infrastructure/Persistence/EfOcrRepository.cs tests/SuperScanner.Domain.Tests/Documents/DocumentExportTests.cs tests/SuperScanner.Application.Tests/Documents/DocumentExportTests.cs && git commit -m "feat: snapshot export OCR eligibility"`.

### Task 2: Isolate and Validate Searchable Words

**Files:**
- Create: `src/SuperScanner.Infrastructure/Processing/PdfTextLayerWord.cs`
- Create: `src/SuperScanner.Infrastructure/Processing/PdfTextLayerProjector.cs`
- Test: `tests/SuperScanner.Application.Tests/Processing/PdfTextLayerProjectorTests.cs`

**Interfaces:**
- Produces: `PdfTextLayerWord(string Text, double X, double Y, double Width, double Height, double AngleDegrees, int ReadingOrder)`
- Produces: `PdfTextLayerProjector.Project(IReadOnlyCollection<OcrElement> elements, double pageWidthPoints, double pageHeightPoints, PdfTextLayerLimits limits) : IReadOnlyList<PdfTextLayerWord>`
- Consumes: `OcrElementKind.Word`, normalized four-point polygons, stored reading order

- [ ] **Step 1: Write failing projection tests** for coordinate-origin conversion, ordered words, mild rotation, horizontal scaling bounds, empty text, malformed geometry, out-of-range points, collapsed boxes, maximum word count, and maximum character count.
- [ ] **Step 2: Run** `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter FullyQualifiedName~PdfTextLayerProjectorTests`; expect compile failure because the projector does not exist.
- [ ] **Step 3: Implement immutable projection types and limits** with finite-number checks, clipping policy from the spec, deterministic ordering, and no logging or exception messages containing `Text`.
- [ ] **Step 4: Run the focused tests**; expect every projection and skip case to pass.
- [ ] **Step 5: Commit** with `git add src/SuperScanner.Infrastructure/Processing/PdfTextLayerWord.cs src/SuperScanner.Infrastructure/Processing/PdfTextLayerProjector.cs tests/SuperScanner.Application.Tests/Processing/PdfTextLayerProjectorTests.cs && git commit -m "feat: project OCR words into PDF coordinates"`.

### Task 3: Write the Invisible PDF Text Layer

**Files:**
- Create: `src/SuperScanner.Infrastructure/Processing/IPdfTextLayerWriter.cs`
- Create: `src/SuperScanner.Infrastructure/Processing/PdfSharpTextLayerWriter.cs`
- Add: `assets/fonts/NotoSans-Regular.ttf` only if the existing pinned asset cannot be reused at publish time
- Modify: `src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj`
- Modify: `tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj` (add a pinned PDF text-extraction test dependency if PDFsharp cannot verify extraction)
- Test: `tests/SuperScanner.Application.Tests/Processing/PdfSharpTextLayerWriterTests.cs`

**Interfaces:**
- Produces: `IPdfTextLayerWriter.Write(PdfPage page, IReadOnlyList<PdfTextLayerWord> words)`
- Consumes: projected PDF-point geometry from Task 2

- [ ] **Step 1: Write a failing PDF extraction test** that creates a one-page image PDF, writes `Yap Tzing Yeow` as three invisible words, saves/reopens it, and verifies extraction order while rendered pixels remain equal to the image-only control.
- [ ] **Step 2: Run the focused test**; expect failure because no writer exists.
- [ ] **Step 3: Implement the writer** using PDF invisible text rendering mode (`Tr 3`), an embedded pinned Noto Sans font, bounded font size, baseline rotation, and horizontal text scaling. Keep low-level PDF operator handling inside this class.
- [ ] **Step 4: Add failure tests** for unsupported glyphs, zero dimensions, oversized word lists, and a corrupt/missing font; assert safe codes contain no OCR text.
- [ ] **Step 5: Run the writer tests** and render the produced PDF to an image for pixel comparison; expect extraction to pass and no visible glyphs.
- [ ] **Step 6: Commit** with `git add src/SuperScanner.Infrastructure/Processing/IPdfTextLayerWriter.cs src/SuperScanner.Infrastructure/Processing/PdfSharpTextLayerWriter.cs src/SuperScanner.Infrastructure/SuperScanner.Infrastructure.csproj tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj tests/SuperScanner.Application.Tests/Processing/PdfSharpTextLayerWriterTests.cs assets/fonts/NotoSans-Regular.ttf && git commit -m "feat: write invisible PDF text layers"` (omit the font path if reused unchanged).

### Task 4: Integrate OCR into PDF Assembly with Image-Only Fallback

**Files:**
- Modify: `src/SuperScanner.Infrastructure/Processing/DocumentPdfBuilder.cs`
- Modify: `src/SuperScanner.Infrastructure/Processing/PdfTextLayerProjector.cs`
- Modify: `src/SuperScanner.Api/Program.cs`
- Modify: `src/SuperScanner.Worker/Program.cs`
- Test: `tests/SuperScanner.Application.Tests/Processing/DocumentPdfBuilderTests.cs`
- Test: `tests/SuperScanner.Infrastructure.IntegrationTests/Persistence/OcrPersistenceTests.cs`

**Interfaces:**
- Consumes: nullable OCR snapshot identity from Task 1
- Consumes: projector and writer from Tasks 2–3
- Produces: per-build `SearchablePageCount` and `SkippedWordCount` without text-bearing telemetry

- [ ] **Step 1: Add failing builder tests** for fully searchable, partially searchable, no OCR, stale source identity, missing row, invalid single word, newer-result substitution prevention, cancellation, and legacy snapshot JSON.
- [ ] **Step 2: Run** `dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter FullyQualifiedName~DocumentPdfBuilderTests`; expect new cases to fail.
- [ ] **Step 3: Refactor `DocumentPdfBuilder` dependencies** to accept the focused projector/writer, and load only the exact snapshotted OCR IDs with `Elements` in a bounded query.
- [ ] **Step 4: Draw image first, then write eligible invisible words**; downgrade eligibility/data problems to image-only and retain hard failure for corrupt output, font, resource-limit, or storage failures.
- [ ] **Step 5: Add a log-capture assertion** using a distinctive sensitive phrase and verify it appears in neither success nor failure logs.
- [ ] **Step 6: Run focused application and infrastructure tests**; expect all cases to pass.
- [ ] **Step 7: Commit** with `git add src/SuperScanner.Infrastructure/Processing/DocumentPdfBuilder.cs src/SuperScanner.Infrastructure/Processing/PdfTextLayerProjector.cs src/SuperScanner.Api/Program.cs src/SuperScanner.Worker/Program.cs tests/SuperScanner.Application.Tests/Processing/DocumentPdfBuilderTests.cs tests/SuperScanner.Infrastructure.IntegrationTests/Persistence/OcrPersistenceTests.cs && git commit -m "feat: build searchable document PDFs"`.

### Task 5: Persist and Expose Searchability Classification

**Files:**
- Modify: `src/SuperScanner.Domain/Documents/DocumentExport.cs`
- Modify: `src/SuperScanner.Application/Documents/GetDocumentExport.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/Configurations/DocumentExportConfiguration.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Migrations/<timestamp>_SearchableDocumentExports.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs`
- Test: `tests/SuperScanner.Domain.Tests/Documents/DocumentExportTests.cs`
- Test: `tests/SuperScanner.Api.IntegrationTests/Documents/DocumentExportEndpointsTests.cs`

**Interfaces:**
- Produces: `SearchablePageCount : int`
- Produces: `Searchability : "ImageOnly" | "PartiallySearchable" | "Searchable"`
- Produces: API fields `searchablePageCount` and `searchability`

- [ ] **Step 1: Write failing tests** for 0-of-N, partial, and N-of-N classifications, plus completion idempotency and legacy database defaults.
- [ ] **Step 2: Run the domain and endpoint tests**; expect missing-property failures.
- [ ] **Step 3: Add the persisted count and derived classification**; require `0 <= SearchablePageCount <= ReadyPageCount` and set the final count atomically on successful completion.
- [ ] **Step 4: Generate and inspect the EF migration** so existing rows default to zero and no document/export data is rewritten.
- [ ] **Step 5: Extend `DocumentExportResult` and endpoint JSON** without exposing OCR IDs or text.
- [ ] **Step 6: Run focused tests and apply the migration to the disposable PostgreSQL test database**; expect all assertions to pass.
- [ ] **Step 7: Commit** with `git add src/SuperScanner.Domain/Documents/DocumentExport.cs src/SuperScanner.Application/Documents/GetDocumentExport.cs src/SuperScanner.Infrastructure/Persistence/Configurations/DocumentExportConfiguration.cs src/SuperScanner.Infrastructure/Persistence/Migrations tests/SuperScanner.Domain.Tests/Documents/DocumentExportTests.cs tests/SuperScanner.Api.IntegrationTests/Documents/DocumentExportEndpointsTests.cs && git commit -m "feat: report PDF searchability"`.

### Task 6: Present Searchability in Angular

**Files:**
- Modify: `apps/web/src/app/documents/document.models.ts`
- Modify: `apps/web/src/app/documents/export-status.component.ts`
- Modify: `apps/web/src/app/documents/export-status.component.html`
- Modify: `apps/web/src/app/documents/export-status.component.scss`
- Test: `apps/web/src/app/documents/export-status.component.spec.ts`

**Interfaces:**
- Consumes: `DocumentExport.searchablePageCount` and `searchability`
- Produces: pre-export page/searchable count and completed-export status copy

- [ ] **Step 1: Write failing component tests** for `4 pages · 3 searchable`, image-only warning, each completed classification, polling updates, and screen-reader announcement.
- [ ] **Step 2: Run** `npm test -- --watch=false --include src/app/documents/export-status.component.spec.ts` from `apps/web`; expect failures for absent fields/copy.
- [ ] **Step 3: Extend TypeScript models and component presentation** with no OCR text and no new blocking step.
- [ ] **Step 4: Run the focused Angular tests**; expect pass.
- [ ] **Step 5: Commit** with `git add apps/web/src/app/documents/document.models.ts apps/web/src/app/documents/export-status.component.* && git commit -m "feat: show PDF searchability status"`.

### Task 7: End-to-End Verification and Operations

**Files:**
- Create: `tests/e2e/searchable-pdf.spec.ts` or extend the repository's current Playwright export suite
- Modify: `docs/operations/pdf-export.md` (create if absent)
- Modify: `src/SuperScanner.Api/appsettings.json`
- Modify: `src/SuperScanner.Worker/appsettings.json`

**Interfaces:**
- Produces: `PdfExport:SearchableTextEnabled` and bounded text-layer settings validated at startup

- [ ] **Step 1: Add an E2E case** that uploads a sanitized page, completes OCR, exports, downloads, extracts `Yap Tzing Yeow`, verifies word order/location, and pixel-compares a rendered PDF page against the processed image.
- [ ] **Step 2: Add a mixed-page E2E case** with one Ready OCR page and one image-only page; assert `PartiallySearchable` and successful download.
- [ ] **Step 3: Add validated configuration and an operational disable/rollback procedure**; disabling the feature must preserve image-only export.
- [ ] **Step 4: Run all .NET test projects, Angular tests, production builds, and the opt-in E2E**; record exact pass/skip counts in the task ledger.
- [ ] **Step 5: Inspect `git diff`, verify no OCR text fixture contains production data, and commit** with `git add tests/e2e docs/operations/pdf-export.md src/SuperScanner.Api/appsettings.json src/SuperScanner.Worker/appsettings.json && git commit -m "test: verify searchable PDF export"`.

