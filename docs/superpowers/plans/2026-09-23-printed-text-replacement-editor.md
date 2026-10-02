# Phase 3D Printed Text Replacement Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let an owner replace contiguous printed English OCR text through a preview-before-apply editor, then render the confirmed change into an immutable, undoable page revision used by PDF export.

**Architecture:** Angular owns transient selection, draft styling, safe-fit preview, and normalized box manipulation. ASP.NET Core validates and persists confirmed edits, while the existing PostgreSQL job queue dispatches a deterministic Magick.NET renderer that reconstructs only the approved glyph region and atomically activates a derived `PageRevision`. Existing pages without revisions continue to resolve their current `PreviewObjectKey`, providing a safe migration path for crop, filter, preview, and export flows.

**Tech Stack:** .NET 10, ASP.NET Core minimal APIs, EF Core 10, PostgreSQL, Magick.NET, Angular 22 signals/reactive forms, Angular CDK, Vitest, xUnit, Testcontainers, PDFsharp, private Cloudflare R2-compatible object storage.

**Spec:** `docs/superpowers/specs/2026-09-23-printed-text-replacement-editor-design.md`

## Global Constraints

- Phase 3D edits printed English OCR words only; handwriting remains read-only.
- Draft changes stay in browser memory until **Apply change**.
- The imported source and every accepted revision remain immutable and recoverable.
- Canonical output is rendered by the server; browser rendering is a labelled approximation.
- Longer text reduces letter spacing first, then font size, and otherwise requires manual box adjustment.
- Phase 3D never automatically shifts neighbouring text and never automatically wraps.
- Only bundled, versioned, redistribution-safe fonts may be rendered.
- No OpenAI, GPT Image, or other generative-AI dependency is introduced.
- No recognized text, replacement text, image bytes, object keys, or signed URLs may enter ordinary logs, metrics, traces, or audit summaries.
- Selection highlights, draft overlays, handles, and warnings must never appear in exported output.
- Existing scan, crop, filter, OCR, page management, and unedited export behavior must continue when text editing is disabled or fails.

## Review Focus

- A page cropped or filtered while an old text editor is open must reject that stale edit and preserve the newer page.
- A replacement crossing a form line or unselected OCR word must fail safe rather than erase surrounding content.
- Duplicate Apply requests or reclaimed worker leases must create exactly one derived revision.
- Undo followed by a new edit must hide the obsolete redo branch without deleting its immutable audit history.
- Preview/export resolution must use the same active revision after browser refresh, worker retry, and application restart.

---

## File Structure

### Domain and persistence

- `src/ArksScanner.Domain/TextEditing/PageRevision.cs` — immutable processed-page asset and parent linkage.
- `src/ArksScanner.Domain/TextEditing/TextEditOperation.cs` — edit state machine, sensitive payload, branch parent, and result linkage.
- `src/ArksScanner.Domain/TextEditing/TextEditStyle.cs` — validated style value object serialized through EF.
- `src/ArksScanner.Domain/TextEditing/NormalizedBox.cs` — finite, page-bounded geometry value object.
- `src/ArksScanner.Domain/TextEditing/FontCatalogueEntry.cs` — stable, versioned licensed-font metadata.
- `src/ArksScanner.Domain/Documents/Page.cs` — optional active revision and one canonical processed-object resolver.
- `src/ArksScanner.Infrastructure/Persistence/Configurations/*TextEdit*.cs` — EF mappings and concurrency indexes.
- `src/ArksScanner.Infrastructure/Persistence/Migrations/<timestamp>_PrintedTextEditing.cs` — schema plus safe existing-page compatibility.

### Application and API

- `src/ArksScanner.Application/TextEditing/TextEditContracts.cs` — repository, renderer, font, and DTO contracts.
- `src/ArksScanner.Application/TextEditing/ProposeTextStyle.cs` — validates a printed selection and produces ranked local style candidates.
- `src/ArksScanner.Application/TextEditing/CreateTextEdit.cs` — validates and queues a confirmed edit atomically.
- `src/ArksScanner.Application/TextEditing/GetTextEdit.cs` — owner-scoped status query.
- `src/ArksScanner.Application/TextEditing/GetPageEditHistory.cs` — revision-aware history query.
- `src/ArksScanner.Application/TextEditing/SwitchPageRevision.cs` — undo/redo command.
- `src/ArksScanner.Api/Endpoints/TextEditingEndpoints.cs` — owner-scoped HTTP contract and safe problem codes.

### Infrastructure and rendering

- `src/ArksScanner.Infrastructure/TextEditing/EfTextEditRepository.cs` — PostgreSQL locking, idempotency, history, and activation.
- `src/ArksScanner.Infrastructure/TextEditing/TextEditingOptions.cs` — feature flag and validated bounds.
- `src/ArksScanner.Infrastructure/TextEditing/BundledFontCatalogue.cs` — allowlisted fonts and pinned hashes.
- `src/ArksScanner.Infrastructure/TextEditing/TextStyleEstimator.cs` — deterministic crop sampling and font ranking.
- `src/ArksScanner.Infrastructure/TextEditing/TextLayoutEngine.cs` — canonical measurements and safe-fit result.
- `src/ArksScanner.Infrastructure/TextEditing/BackgroundReconstructor.cs` — conservative mask refinement and reconstruction.
- `src/ArksScanner.Infrastructure/TextEditing/TextEditRenderer.cs` — canonical output, containment verification, and immutable object write.
- `src/ArksScanner.Infrastructure/TextEditing/TextEditProcessor.cs` — worker orchestration and activation.
- `assets/fonts/` — pinned OFL font files and licence notices.

### Angular

- `apps/web/src/app/documents/text-edit.models.ts` — exact API and draft types.
- `apps/web/src/app/documents/text-fit.ts` — pure safe-fit and box helpers.
- `apps/web/src/app/documents/text-edit.service.ts` — API client and status polling boundary.
- `apps/web/src/app/documents/text-replacement-editor.component.*` — accessible editor panel.
- `apps/web/src/app/documents/text-replacement-overlay.component.*` — preview, move, and resize surface.
- `apps/web/src/app/documents/ocr-text-overlay.component.*` — emits eligible selection instead of owning editor behavior.
- `apps/web/src/app/documents/page-card.component.*` — hosts the editor and refreshes the active page.

### Operations

- `docs/operations/printed-text-editing.md` — configuration, font licensing, queues, safe failures, recovery, and rollout.

---

### Task 1: Revision-aware page assets

**Files:**
- Create: `src/ArksScanner.Domain/TextEditing/PageRevision.cs`
- Create: `src/ArksScanner.Infrastructure/Persistence/Configurations/PageRevisionConfiguration.cs`
- Modify: `src/ArksScanner.Domain/Documents/Page.cs`
- Modify: `src/ArksScanner.Infrastructure/Persistence/AppDbContext.cs`
- Test: `tests/ArksScanner.Domain.Tests/TextEditing/PageRevisionTests.cs`
- Test: `tests/ArksScanner.Infrastructure.IntegrationTests/Persistence/PageRevisionPersistenceTests.cs`

**Interfaces:**
- Consumes: existing `Page.PreviewObjectKey` and `Page.GetExportObjectKey()` behavior.
- Produces: `PageRevision.CreateBase(...)`, `PageRevision.CreateDerived(...)`, `Page.ActiveRevisionId`, `Page.GetProcessedObjectKey()`, `Page.ActivateRevision(...)`.

- [ ] **Step 1: Write failing domain tests for immutable revisions and page activation**

```csharp
[Fact]
public void Activating_a_derived_revision_changes_only_the_processed_asset()
{
    var page = PageTestFactory.Ready(previewKey: "previews/p1.jpg");
    var baseRevision = PageRevision.CreateBase(Guid.NewGuid(), page.Id, "previews/p1.jpg", "sha-base", Now);
    var derived = PageRevision.CreateDerived(Guid.NewGuid(), page.Id, baseRevision.Id,
        Guid.NewGuid(), "page-revisions/p1/r2.jpg", "sha-derived", Now);

    page.ActivateRevision(derived);

    Assert.Equal(derived.Id, page.ActiveRevisionId);
    Assert.Equal("page-revisions/p1/r2.jpg", page.GetProcessedObjectKey());
    Assert.Equal("previews/p1.jpg", page.PreviewObjectKey);
}
```

Also pin rejection of cross-page revisions, mutation of revision fields, and a Ready page with no revision resolving its legacy preview key.

- [ ] **Step 2: Run the focused domain tests and verify the red state**

Run: `dotnet test tests/ArksScanner.Domain.Tests/ArksScanner.Domain.Tests.csproj --filter FullyQualifiedName~PageRevisionTests`

Expected: FAIL because `PageRevision` and revision-aware `Page` members do not exist.

- [ ] **Step 3: Implement the minimum revision aggregate and compatibility resolver**

```csharp
public sealed class PageRevision
{
    public Guid Id { get; private set; }
    public Guid PageId { get; private set; }
    public Guid? ParentRevisionId { get; private set; }
    public Guid? ProducingTextEditId { get; private set; }
    public string ObjectKey { get; private set; } = string.Empty;
    public string MediaType { get; private set; } = "image/jpeg";
    public string Sha256Hex { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
}
```

Add `ActiveRevisionId` to `Page`. `GetProcessedObjectKey()` returns the loaded active revision's object key when present and otherwise the existing `PreviewObjectKey`; keep `GetExportObjectKey()` as a Ready-state guard delegating to this resolver.

- [ ] **Step 4: Add EF mappings and persistence tests**

Map `page_revisions` with immutable columns, a unique object key, indexes on `(PageId, CreatedAt)` and `ParentRevisionId`, restrictive parent deletion, and a nullable `pages.ActiveRevisionId`. Test round-trip, parent linkage, and that two active switches cannot silently overwrite a concurrency token.

- [ ] **Step 5: Run Task 1 tests**

Run: `dotnet test tests/ArksScanner.Domain.Tests/ArksScanner.Domain.Tests.csproj --filter FullyQualifiedName~PageRevisionTests`

Run: `dotnet test tests/ArksScanner.Infrastructure.IntegrationTests/ArksScanner.Infrastructure.IntegrationTests.csproj --filter FullyQualifiedName~PageRevisionPersistenceTests`

Expected: PASS.

- [ ] **Step 6: Commit Task 1**

```powershell
git add src/ArksScanner.Domain/TextEditing src/ArksScanner.Domain/Documents/Page.cs src/ArksScanner.Infrastructure/Persistence tests/ArksScanner.Domain.Tests/TextEditing tests/ArksScanner.Infrastructure.IntegrationTests/Persistence/PageRevisionPersistenceTests.cs
git commit -m "feat: add immutable page revisions"
```

### Task 2: Text edit aggregate, style values, and database migration

**Files:**
- Create: `src/ArksScanner.Domain/TextEditing/NormalizedBox.cs`
- Create: `src/ArksScanner.Domain/TextEditing/TextEditStyle.cs`
- Create: `src/ArksScanner.Domain/TextEditing/TextEditOperation.cs`
- Create: `src/ArksScanner.Domain/TextEditing/FontCatalogueEntry.cs`
- Create: `src/ArksScanner.Infrastructure/Persistence/Configurations/TextEditOperationConfiguration.cs`
- Create: `src/ArksScanner.Infrastructure/Persistence/Configurations/FontCatalogueEntryConfiguration.cs`
- Modify: `src/ArksScanner.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/ArksScanner.Infrastructure/Persistence/Migrations/<generated>_PrintedTextEditing.cs`
- Test: `tests/ArksScanner.Domain.Tests/TextEditing/TextEditOperationTests.cs`
- Test: `tests/ArksScanner.Infrastructure.IntegrationTests/Persistence/TextEditPersistenceTests.cs`

**Interfaces:**
- Consumes: `PageRevision` from Task 1.
- Produces: `TextEditOperation.Queue(...)`, `Start(...)`, `Complete(...)`, `Fail(...)`, `TextEditStyle`, `NormalizedBox`, and EF schema.

- [ ] **Step 1: Write failing aggregate tests**

```csharp
[Fact]
public void Completed_edit_pins_result_revision_and_cannot_complete_twice()
{
    var edit = TextEditFixture.Queued();
    edit.Start(Now);
    var revisionId = Guid.NewGuid();
    edit.Complete(revisionId, Now.AddSeconds(1));

    Assert.Equal(TextEditState.Succeeded, edit.State);
    Assert.Equal(revisionId, edit.ResultRevisionId);
    Assert.Throws<InvalidOperationException>(() => edit.Complete(Guid.NewGuid(), Now));
}
```

Cover empty/oversized replacement, invalid boxes, arbitrary font identifiers, invalid style numbers, illegal transitions, safe failure codes, and canonical-request-hash/idempotency invariants.

- [ ] **Step 2: Verify the tests fail**

Run: `dotnet test tests/ArksScanner.Domain.Tests/ArksScanner.Domain.Tests.csproj --filter FullyQualifiedName~TextEditOperationTests`

Expected: FAIL because the aggregate is absent.

- [ ] **Step 3: Implement validated value objects and state machine**

```csharp
public enum TextEditState { Queued, Processing, Succeeded, Failed }
public enum TextAlignment { Left, Center, Right }

public sealed record TextEditStyle(
    string FontId, string FontVersion, double FontSize,
    int Weight, string ColorHex, double LetterSpacing,
    double Baseline, double AngleDegrees, TextAlignment Alignment);
```

Store sensitive original/replacement strings as PostgreSQL `text` columns but never return them through `ToString()`, exceptions, metrics, or audit payloads. Store selected element IDs, box, style, and property provenance as canonical `jsonb`.

- [ ] **Step 4: Add mappings and generate the migration**

Run: `dotnet ef migrations add PrintedTextEditing --project src/ArksScanner.Infrastructure --startup-project src/ArksScanner.Api`

The migration creates `page_revisions`, `font_catalogue_entries`, and `text_edit_operations`, then adds the nullable active-revision FK and concurrency token. It does not rewrite existing preview keys; legacy pages remain compatible through Task 1's resolver.

- [ ] **Step 5: Test PostgreSQL constraints and concurrency**

Pin unique `(PageId, IdempotencyKey)`, unique result revision, restrictive revision parents, JSON round-trip, and concurrent mutation failure.

- [ ] **Step 6: Run Task 2 tests and commit**

Run: `dotnet test tests/ArksScanner.Domain.Tests/ArksScanner.Domain.Tests.csproj --filter FullyQualifiedName~TextEditing`

Run: `dotnet test tests/ArksScanner.Infrastructure.IntegrationTests/ArksScanner.Infrastructure.IntegrationTests.csproj --filter FullyQualifiedName~TextEditPersistenceTests`

Expected: PASS.

```powershell
git add src/ArksScanner.Domain/TextEditing src/ArksScanner.Infrastructure/Persistence tests/ArksScanner.Domain.Tests/TextEditing tests/ArksScanner.Infrastructure.IntegrationTests/Persistence/TextEditPersistenceTests.cs
git commit -m "feat: persist printed text edits"
```

### Task 3: Validated configuration and bundled font catalogue

**Files:**
- Create: `src/ArksScanner.Infrastructure/TextEditing/TextEditingOptions.cs`
- Create: `src/ArksScanner.Infrastructure/TextEditing/BundledFontCatalogue.cs`
- Create: `assets/fonts/OFL.txt`
- Create: `assets/fonts/NotoSans-Regular.ttf`
- Create: `assets/fonts/NotoSans-Bold.ttf`
- Create: `assets/fonts/NotoSerif-Regular.ttf`
- Create: `assets/fonts/NotoSerif-Bold.ttf`
- Modify: `src/ArksScanner.Infrastructure/ArksScanner.Infrastructure.csproj`
- Modify: `src/ArksScanner.Api/Program.cs`
- Modify: `src/ArksScanner.Worker/Program.cs`
- Modify: `src/ArksScanner.Api/appsettings.json`
- Modify: `src/ArksScanner.Worker/appsettings.json`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/TextEditingOptionsTests.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/BundledFontCatalogueTests.cs`

**Interfaces:**
- Consumes: persisted `FontCatalogueEntry`.
- Produces: `IFontCatalogue.Get(string id, string version)` and `TextEditingOptions` shared by API and worker.

- [ ] **Step 1: Write failing configuration and hash-verification tests**

```csharp
[Theory]
[InlineData(0, 100, 0.7, false)]
[InlineData(50, 0, 0.7, false)]
[InlineData(50, 100, 0.7, true)]
public void Options_require_positive_limits(int words, int chars, double minimumScale, bool valid)
{
    var options = ValidOptions() with
    {
        MaxSelectionWords = words,
        MaxReplacementCharacters = chars,
        MinimumFontScale = minimumScale
    };
    Assert.Equal(valid, options.IsValid());
}
```

Also verify every manifest hash matches its embedded file and every enabled face has an OFL licence entry.

- [ ] **Step 2: Add the four pinned Noto faces and licence**

Download the exact release files from the official Noto repository, record SHA-256 values in a checked-in manifest, and embed/copy the same assets into API web-font output and worker runtime output. Do not depend on operating-system fonts.

- [ ] **Step 3: Implement startup validation and catalogue lookup**

Use `TextEditing:Enabled=false` by default. Validate maximum selected words, replacement characters, box area, queue depth, minimum letter spacing, minimum font scale, mask dilation, containment tolerance, retry count, and manifest hashes with `ValidateOnStart()`.

- [ ] **Step 4: Run tests and commit**

Run: `dotnet test tests/ArksScanner.Application.Tests/ArksScanner.Application.Tests.csproj --filter "FullyQualifiedName~TextEditingOptionsTests|FullyQualifiedName~BundledFontCatalogueTests"`

Expected: PASS.

```powershell
git add assets/fonts src/ArksScanner.Infrastructure/TextEditing src/ArksScanner.Infrastructure/ArksScanner.Infrastructure.csproj src/ArksScanner.Api src/ArksScanner.Worker tests/ArksScanner.Application.Tests/TextEditing
git commit -m "feat: add licensed text editing font catalogue"
```

### Task 4: Selection validation and deterministic style proposal

**Files:**
- Create: `src/ArksScanner.Application/TextEditing/TextEditContracts.cs`
- Create: `src/ArksScanner.Application/TextEditing/ProposeTextStyle.cs`
- Create: `src/ArksScanner.Infrastructure/TextEditing/TextStyleEstimator.cs`
- Create: `src/ArksScanner.Infrastructure/TextEditing/EfTextEditRepository.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/ProposeTextStyleTests.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/TextStyleEstimatorTests.cs`

**Interfaces:**
- Consumes: owner UID, document/page/OCR IDs, ordered OCR word IDs, page source, and `IFontCatalogue`.
- Produces: `TextStyleProposalDto` with union box, original phrase, ranked candidates, confidence, colour, size, weight, spacing, baseline, angle, and alignment.

- [ ] **Step 1: Write failing selection-validation tests**

```csharp
[Fact]
public async Task Handwritten_word_is_rejected_before_image_access()
{
    repository.Selection = OcrSelectionFixture.Handwritten();
    await Assert.ThrowsAsync<UnsupportedTextSelectionException>(() =>
        sut.HandleAsync(Owner, DocumentId, PageId, OcrResultId,
            [repository.Selection.WordIds[0]], default));
    Assert.Equal(0, estimator.CallCount);
}
```

Cover ownership, current OCR result, words only, all printed, unique IDs, contiguous reading order, maximum count, valid polygons, same line/contiguous region, stale revision, and unselected-word overlap.

- [ ] **Step 2: Implement repository selection projection and application validation**

Define `OwnedTextSelection` so application code receives only the owned page source, active revision token, OCR result, and requested word records. Sort on server reading order; never trust browser order or browser-supplied original text.

- [ ] **Step 3: Write failing estimator tests with synthetic crops**

Create sanitized generated images for black sans text, dark-blue serif text, rotated baseline, and low-contrast text. Assert bounded colour, size, angle, ranked font IDs, and a low-confidence fallback rather than false certainty.

- [ ] **Step 4: Implement deterministic estimation**

Sample foreground colour from the OCR polygon against its border background, derive size/angle from normalized geometry and image dimensions, compare Magick.NET-rendered candidate proportions, and return ranked catalogue IDs. Never include crop bytes or text in logs.

- [ ] **Step 5: Run focused tests and commit**

Run: `dotnet test tests/ArksScanner.Application.Tests/ArksScanner.Application.Tests.csproj --filter "FullyQualifiedName~ProposeTextStyleTests|FullyQualifiedName~TextStyleEstimatorTests"`

Expected: PASS.

```powershell
git add src/ArksScanner.Application/TextEditing src/ArksScanner.Infrastructure/TextEditing tests/ArksScanner.Application.Tests/TextEditing
git commit -m "feat: propose deterministic printed text styles"
```

### Task 5: Canonical layout and browser-safe fitting contract

**Files:**
- Create: `src/ArksScanner.Infrastructure/TextEditing/TextLayoutEngine.cs`
- Create: `tests/ArksScanner.Application.Tests/TextEditing/TextLayoutEngineTests.cs`
- Create: `apps/web/src/app/documents/text-fit.ts`
- Create: `apps/web/src/app/documents/text-fit.spec.ts`
- Create: `apps/web/src/assets/fonts/text-fonts.css`
- Modify: `apps/web/angular.json`

**Interfaces:**
- Consumes: replacement string, normalized box, image dimensions, chosen catalogue face, size, and letter spacing.
- Produces: `TextLayoutResult(Fits, FontSize, LetterSpacing, Width, Height, Overflow)` with matching JSON field semantics in TypeScript.

- [ ] **Step 1: Write red .NET and TypeScript safe-fit tests**

```typescript
it('reduces spacing before font size and reports overflow at both minima', () => {
  const result = fitSingleLine(measureStub, draft({ text: 'A much longer replacement' }));
  expect(result.steps[0].kind).toBe('letterSpacing');
  expect(result.fontScale).toBeGreaterThanOrEqual(0.7);
  expect(result.overflow).toBe(true);
});
```

Pin empty text rejection, exact fit, Unicode English punctuation, minimum spacing, minimum scale, finite values, box bounds, and server/browser tolerance on shared fixtures.

- [ ] **Step 2: Implement one ordered safe-fit algorithm in both runtimes**

The .NET implementation is authoritative. TypeScript mirrors its ordering and clamping for preview. Persist a `layoutVersion` with every edit so renderer upgrades cannot silently change old results.

- [ ] **Step 3: Add web-font declarations from the same pinned assets**

Expose only stable catalogue family names such as `ArksScanner Noto Sans v1`; do not accept arbitrary CSS font-family text from the API response or user input.

- [ ] **Step 4: Run focused tests and commit**

Run: `dotnet test tests/ArksScanner.Application.Tests/ArksScanner.Application.Tests.csproj --filter FullyQualifiedName~TextLayoutEngineTests`

Run: `npm test -- --run --include src/app/documents/text-fit.spec.ts`

Workdir for npm command: `apps/web`

Expected: PASS.

```powershell
git add src/ArksScanner.Infrastructure/TextEditing tests/ArksScanner.Application.Tests/TextEditing apps/web
git commit -m "feat: add deterministic replacement text fitting"
```

### Task 6: Apply/status/history API with idempotent queueing

**Files:**
- Create: `src/ArksScanner.Application/TextEditing/CreateTextEdit.cs`
- Create: `src/ArksScanner.Application/TextEditing/GetTextEdit.cs`
- Create: `src/ArksScanner.Application/TextEditing/GetPageEditHistory.cs`
- Create: `src/ArksScanner.Api/Endpoints/TextEditingEndpoints.cs`
- Modify: `src/ArksScanner.Api/Program.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/CreateTextEditTests.cs`
- Test: `tests/ArksScanner.Api.IntegrationTests/Documents/TextEditingEndpointsTests.cs`

**Interfaces:**
- Consumes: Task 4 selection validation and Task 5 confirmed layout/style.
- Produces: `POST .../text-edits/style-proposal`, `POST .../text-edits`, `GET .../text-edits/{editId}`, and `GET .../text-edits/history`.

- [ ] **Step 1: Write failing command tests for atomic edit/job creation**

```csharp
[Fact]
public async Task Duplicate_identical_apply_returns_existing_edit_and_one_job()
{
    var first = await sut.HandleAsync(Command(idempotencyKey: "apply-1"), default);
    var second = await sut.HandleAsync(Command(idempotencyKey: "apply-1"), default);
    Assert.Equal(first.EditId, second.EditId);
    Assert.Single(queue.Enqueued);
}
```

Pin different-payload key reuse, stale revision/OCR, overflow, collision, disabled feature, queue-depth limit, ownership, and sensitive-data-free audit payload.

- [ ] **Step 2: Implement the application transaction**

Lock the owned page, resolve its current processed key/revision token, re-read selected OCR words, validate the confirmed style against the catalogue, calculate the canonical request hash, add `TextEditOperation`, append audit metadata containing only edit/page IDs, enqueue `RenderTextEdit` with payload equal to the edit ID, and commit once.

- [ ] **Step 3: Implement minimal API mappings and stable safe codes**

Return `202 Accepted` for queued/existing identical work, `404` across ownership boundaries, `409` for stale/concurrent/idempotency conflicts, `422` for handwriting/selection/overflow/collision/style problems, and `503` when disabled. Add `Cache-Control: private, no-store`.

- [ ] **Step 4: Run API and application tests and commit**

Run: `dotnet test tests/ArksScanner.Application.Tests/ArksScanner.Application.Tests.csproj --filter FullyQualifiedName~CreateTextEditTests`

Run: `dotnet test tests/ArksScanner.Api.IntegrationTests/ArksScanner.Api.IntegrationTests.csproj --filter FullyQualifiedName~TextEditingEndpointsTests`

Expected: PASS, including unauthenticated, cross-owner, App Check, stale revision, and no-sensitive-log cases.

```powershell
git add src/ArksScanner.Application/TextEditing src/ArksScanner.Api tests/ArksScanner.Application.Tests/TextEditing tests/ArksScanner.Api.IntegrationTests/Documents/TextEditingEndpointsTests.cs
git commit -m "feat: add printed text edit API"
```

### Task 7: Conservative background reconstruction and canonical renderer

**Files:**
- Create: `src/ArksScanner.Infrastructure/TextEditing/BackgroundReconstructor.cs`
- Create: `src/ArksScanner.Infrastructure/TextEditing/TextEditRenderer.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/BackgroundReconstructorTests.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/TextEditRendererTests.cs`
- Create: `tests/ArksScanner.Application.Tests/Fixtures/TextEditing/` sanitized generated fixture descriptions.

**Interfaces:**
- Consumes: immutable source stream, server-resolved selection polygons, approved region, style, layout/font version.
- Produces: JPEG render stream, SHA-256 hash, changed-pixel mask, and safe result/error code.

- [ ] **Step 1: Write failing reconstruction and containment tests**

Generate test images in memory rather than committing private scans. Cover plain paper, gradient paper, light compression, form-line intersection, nearby unselected text, rotated text, mask dilation at region edge, and forced outside-mask mutation.

```csharp
[Fact]
public async Task Renderer_rejects_any_change_outside_the_approved_mask()
{
    var renderer = Fixture.RendererThatMutatesPixelOutsideMask();
    var result = await renderer.RenderAsync(Fixture.Request, default);
    Assert.Equal("text_edit_containment_failed", result.FailureCode);
    Assert.Null(result.Output);
}
```

- [ ] **Step 2: Implement conservative mask refinement and reconstruction**

Build the mask from selected quadrilaterals, classify foreground against border samples, dilate only inside the approved box, and protect pixels belonging to unselected OCR polygons. Reconstruct simple backgrounds from opposite border samples. Return `text_edit_unsafe_background` when variance, protected overlap, or line continuity cannot be preserved.

- [ ] **Step 3: Implement exact deterministic text rendering**

Load the pinned font by catalogue path, apply Task 5 layout, render the exact replacement string, composite only through the approved mask/box, strip metadata, encode JPEG with fixed settings, and calculate SHA-256. Compare source/output outside the permitted mask with configured tolerance before returning output.

- [ ] **Step 4: Run golden and property tests**

Run: `dotnet test tests/ArksScanner.Application.Tests/ArksScanner.Application.Tests.csproj --filter "FullyQualifiedName~BackgroundReconstructorTests|FullyQualifiedName~TextEditRendererTests"`

Expected: PASS with byte-identical repeated output for the same renderer/font/layout version and exact OCR-readable replacement fixture text.

- [ ] **Step 5: Commit Task 7**

```powershell
git add src/ArksScanner.Infrastructure/TextEditing tests/ArksScanner.Application.Tests/TextEditing tests/ArksScanner.Application.Tests/Fixtures/TextEditing
git commit -m "feat: render contained printed text replacements"
```

### Task 8: Worker processing, retry, and atomic revision activation

**Files:**
- Create: `src/ArksScanner.Infrastructure/TextEditing/TextEditProcessor.cs`
- Modify: `src/ArksScanner.Worker/UploadValidationJobRunner.cs`
- Modify: `src/ArksScanner.Worker/Program.cs`
- Modify: `src/ArksScanner.Infrastructure/Processing/PostgresJobQueue.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/TextEditProcessorTests.cs`
- Test: `tests/ArksScanner.Application.Tests/Processing/UploadValidationJobRunnerTests.cs`

**Interfaces:**
- Consumes: `RenderTextEdit` job whose payload is exactly one edit GUID.
- Produces: immutable `page-revisions/{documentId:N}/{pageId:N}/{editId:N}.jpg`, one derived revision, terminal edit state, and conditional active-revision switch.

- [ ] **Step 1: Write failing processor tests**

Pin success, reclaimed lease, existing complete object after lost acknowledgement, stale active revision during render, cancellation, transient object-store failure, unsafe-background terminal failure, maximum retry, and sensitive-data-free logger state.

- [ ] **Step 2: Implement processing with a two-phase object/database boundary**

Read/validate state under lock, render outside a long database transaction, write the immutable deterministic key with `WriteIfAbsentAsync`, then re-lock. Create/complete exactly one revision and activate it only when the expected source token is still active. Otherwise fail with `text_edit_stale_revision` and leave the stored output unreachable for lifecycle cleanup.

- [ ] **Step 3: Route and recover `RenderTextEdit` jobs**

Teach `UploadValidationJobRunner` to parse exactly one GUID, dispatch `TextEditProcessor`, reschedule retryable infrastructure errors, and reconcile terminal edit/job state in a fresh scope. Do not log original or replacement text.

- [ ] **Step 4: Run worker tests and commit**

Run: `dotnet test tests/ArksScanner.Application.Tests/ArksScanner.Application.Tests.csproj --filter "FullyQualifiedName~TextEditProcessorTests|FullyQualifiedName~UploadValidationJobRunnerTests"`

Expected: PASS.

```powershell
git add src/ArksScanner.Infrastructure/TextEditing src/ArksScanner.Infrastructure/Processing/PostgresJobQueue.cs src/ArksScanner.Worker tests/ArksScanner.Application.Tests
git commit -m "feat: process printed text edit jobs"
```

### Task 9: Persistent undo and redo

**Files:**
- Create: `src/ArksScanner.Application/TextEditing/SwitchPageRevision.cs`
- Modify: `src/ArksScanner.Infrastructure/TextEditing/EfTextEditRepository.cs`
- Modify: `src/ArksScanner.Api/Endpoints/TextEditingEndpoints.cs`
- Test: `tests/ArksScanner.Application.Tests/TextEditing/SwitchPageRevisionTests.cs`
- Test: `tests/ArksScanner.Api.IntegrationTests/Documents/TextEditingHistoryEndpointsTests.cs`

**Interfaces:**
- Consumes: active revision plus accepted edit branch.
- Produces: `POST .../text-edits/undo`, `POST .../text-edits/redo`, and `PageEditHistoryDto(CanUndo, CanRedo, ActiveRevisionId, Entries)`.

- [ ] **Step 1: Write failing branch-history tests**

```csharp
[Fact]
public async Task New_edit_after_undo_removes_old_branch_from_redo_navigation()
{
    await fixture.Apply("A");
    await fixture.Apply("B");
    await fixture.Undo();
    await fixture.Apply("C");
    var history = await fixture.GetHistory();
    Assert.False(history.CanRedo);
    Assert.Equal("C", fixture.ActiveLabel);
    Assert.Equal(3, fixture.ImmutableEditCount);
}
```

Also cover base revision undo boundary, redo boundary, refresh persistence, concurrent switch/edit, cross-owner access, and disabled controls during queued work.

- [ ] **Step 2: Implement transactional revision switching**

Lock the owned page and ensure no queued/processing mutation exists. Move to the current branch parent for undo or the unique current branch child for redo, increment the page concurrency token, mark document content changed, and append text-free audit metadata.

- [ ] **Step 3: Add endpoints and tests**

Return `200` with refreshed history, `409` for concurrent/busy state, and `422` at the branch boundary. No undo/redo request accepts an arbitrary target revision ID.

- [ ] **Step 4: Run tests and commit**

Run: `dotnet test tests/ArksScanner.Application.Tests/ArksScanner.Application.Tests.csproj --filter FullyQualifiedName~SwitchPageRevisionTests`

Run: `dotnet test tests/ArksScanner.Api.IntegrationTests/ArksScanner.Api.IntegrationTests.csproj --filter FullyQualifiedName~TextEditingHistoryEndpointsTests`

Expected: PASS.

```powershell
git add src/ArksScanner.Application/TextEditing src/ArksScanner.Infrastructure/TextEditing src/ArksScanner.Api/Endpoints/TextEditingEndpoints.cs tests
git commit -m "feat: add persistent text edit undo and redo"
```

### Task 10: Angular replacement editor and adjustable preview box

**Files:**
- Create: `apps/web/src/app/documents/text-edit.models.ts`
- Create: `apps/web/src/app/documents/text-edit.service.ts`
- Create: `apps/web/src/app/documents/text-edit.service.spec.ts`
- Create: `apps/web/src/app/documents/text-replacement-editor.component.ts`
- Create: `apps/web/src/app/documents/text-replacement-editor.component.html`
- Create: `apps/web/src/app/documents/text-replacement-editor.component.scss`
- Create: `apps/web/src/app/documents/text-replacement-editor.component.spec.ts`
- Create: `apps/web/src/app/documents/text-replacement-overlay.component.ts`
- Create: `apps/web/src/app/documents/text-replacement-overlay.component.html`
- Create: `apps/web/src/app/documents/text-replacement-overlay.component.scss`
- Create: `apps/web/src/app/documents/text-replacement-overlay.component.spec.ts`
- Modify: `apps/web/src/app/documents/ocr-text-overlay.component.ts`
- Modify: `apps/web/src/app/documents/ocr-text-overlay.component.html`
- Modify: `apps/web/src/app/documents/page-card.component.ts`
- Modify: `apps/web/src/app/documents/page-card.component.html`
- Modify: `apps/web/src/app/documents/document-detail.component.ts`

**Interfaces:**
- Consumes: Phase 3C `SelectionSummary` plus Tasks 4, 6, and 9 endpoints.
- Produces: browser-memory `TextEditDraft`, `Apply` event/status flow, box/style controls, and refresh event after canonical completion.

- [ ] **Step 1: Make OCR overlay emit a copied selection payload**

Add an output containing page ID, OCR result ID, sorted word IDs, text type, phrase, and union polygon. Copy arrays/sets so later overlay selection changes cannot mutate an open draft. Unit-test printed eligibility and the exact handwriting message.

- [ ] **Step 2: Write failing editor interaction tests**

Pin opening, initial recognized text, draft-only typing, proposal loading, safe-fit warnings, low-confidence alternatives, cancel, explicit apply, duplicate-submit lock, polling completion/failure, refresh restoration, and no sensitive console logging.

```typescript
it('does not persist until Apply change is pressed', async () => {
  fixture.openPrintedSelection();
  fixture.typeReplacement('Tan BB');
  expect(api.createEdit).not.toHaveBeenCalled();
  fixture.clickApply();
  expect(api.createEdit).toHaveBeenCalledTimes(1);
});
```

- [ ] **Step 3: Implement the accessible editor panel**

Use Angular reactive forms/signals with explicit labels. Offer only catalogue choices returned by the server. Announce loading, warning, overflow, queued, success, and failure states with `aria-live`; restore focus on cancel/apply/failure.

- [ ] **Step 4: Write failing overlay geometry tests**

Test pointer/touch move, eight resize handles, page-bound clamping, minimum size, normalized coordinate persistence, collision warning, browser resize, zoom, Escape cancellation, arrow-key move, and Shift+arrow resize.

- [ ] **Step 5: Implement the SVG/HTML replacement preview**

Keep the source image untouched. Draw a local background-colour approximation and exact replacement overlay using catalogue CSS families. Mark it **Preview** and remove the Phase 3C highlight while the editor is open. Handles are UI-only and cannot enter exports.

- [ ] **Step 6: Run Angular tests and commit**

Run: `npm test -- --run`

Workdir: `apps/web`

Expected: PASS.

```powershell
git add apps/web/src/app/documents apps/web/src/assets/fonts apps/web/angular.json
git commit -m "feat: add printed text replacement editor"
```

### Task 11: Active revision preview and PDF export integration

**Files:**
- Modify: `src/ArksScanner.Api/Endpoints/DocumentPreviewEndpoints.cs`
- Modify: `src/ArksScanner.Domain/Documents/DocumentExport.cs`
- Modify: `src/ArksScanner.Infrastructure/Processing/DocumentPdfBuilder.cs`
- Modify: `apps/web/src/app/documents/document.models.ts`
- Modify: `apps/web/src/app/documents/documents-api.service.ts`
- Test: `tests/ArksScanner.Api.IntegrationTests/Documents/DocumentPreviewEndpointsTests.cs`
- Test: `tests/ArksScanner.Application.Tests/Processing/DocumentPdfBuilderTests.cs`
- Test: `tests/ArksScanner.Api.IntegrationTests/Documents/DocumentExportEndpointsTests.cs`

**Interfaces:**
- Consumes: `Page.GetProcessedObjectKey()` and active revision token.
- Produces: preview cache invalidation/version response and exports snapshotting the active revision object key/hash.

- [ ] **Step 1: Write failing preview/export tests**

Test legacy page fallback, edited page preview, undo preview, edit-completes-after-export snapshot stability, export-created-after-edit inclusion, and absence of SVG highlight/editor markup or PDF annotations.

- [ ] **Step 2: Resolve active processed objects everywhere**

Update preview and export snapshot creation to use the revision-aware resolver. Include a non-sensitive revision token in preview URLs/responses so the browser cannot retain the old image after activation or undo.

- [ ] **Step 3: Run focused tests and commit**

Run: `dotnet test tests/ArksScanner.Application.Tests/ArksScanner.Application.Tests.csproj --filter FullyQualifiedName~DocumentPdfBuilderTests`

Run: `dotnet test tests/ArksScanner.Api.IntegrationTests/ArksScanner.Api.IntegrationTests.csproj --filter "FullyQualifiedName~DocumentPreviewEndpointsTests|FullyQualifiedName~DocumentExportEndpointsTests"`

Expected: PASS.

```powershell
git add src/ArksScanner.Api/Endpoints/DocumentPreviewEndpoints.cs src/ArksScanner.Domain/Documents src/ArksScanner.Infrastructure/Processing/DocumentPdfBuilder.cs apps/web/src/app/documents tests
git commit -m "feat: export active edited page revisions"
```

### Task 12: Operations, full verification, and production-disabled delivery

**Files:**
- Create: `docs/operations/printed-text-editing.md`
- Modify: `README.md`
- Modify: `src/ArksScanner.Api/appsettings.json`
- Modify: `src/ArksScanner.Worker/appsettings.json`
- Modify: `.github/workflows/ci.yml`
- Create: `apps/web/e2e/printed-text-replacement.spec.ts`

**Interfaces:**
- Consumes: complete Phase 3D feature.
- Produces: feature-flagged, documented, migration-ready build with benchmark and rollback instructions.

- [ ] **Step 1: Add an opt-in E2E scenario**

The scenario uploads a generated printed form, runs fake/deterministic OCR, selects `Yap Tzing Yeow`, previews `Tan BB`, applies, waits for success, reloads, verifies the edited preview, exports a PDF, verifies no selection highlight, undoes, reloads, and verifies the original active page.

- [ ] **Step 2: Write the operational runbook**

Document every `TextEditing__*` key, default-disabled production state, migration order, font files/licences/hashes, object-key layout, queue/job safe codes, metrics, containment alerts, failed-edit recovery, worker rollback, revision restoration, cleanup of unreachable deterministic outputs, and the production enablement checklist.

- [ ] **Step 3: Run schema and backend verification**

Run: `dotnet restore ArksScanner.slnx --locked-mode`

Run: `dotnet build ArksScanner.slnx --no-restore`

Run: `dotnet test ArksScanner.slnx --no-build`

Run the existing fresh-PostgreSQL migration command from `docs/operations/foundation-runbook.md`, then verify the `PrintedTextEditing` migration applies and rolls the application forward with existing documents intact.

Expected: build and tests pass; legacy pages still preview/export through `PreviewObjectKey`.

- [ ] **Step 4: Run frontend verification**

Run: `npm ci`

Run: `npm test -- --run`

Run: `npm run build`

Run: `npm run e2e -- --grep "printed text replacement"`

Workdir: `apps/web`

Expected: all commands pass; pre-existing documented bundle warnings may remain, but Phase 3D introduces no new warning.

- [ ] **Step 5: Perform privacy and repository audits**

Run: `rg -n "ReplacementText|OriginalText" src tests apps/web | rg "Log|Metric|Activity|Audit"`

Expected: no logging, metrics, trace, or audit payload includes the sensitive values.

Run: `rg -n "gpt|openai|image generation" src/ArksScanner.* apps/web/src -g '!**/obj/**' -g '!**/bin/**'`

Expected: no Phase 3D dependency or provider call.

Run: `git diff --check`

Expected: no whitespace errors.

- [ ] **Step 6: Keep production disabled and commit operations work**

Keep `TextEditing:Enabled=false` in checked-in defaults and omit any production Railway override. Enablement occurs only after manual benchmark review confirms style quality, exact spelling, containment, accessibility, and rollback behavior.

```powershell
git add docs README.md src/ArksScanner.Api/appsettings.json src/ArksScanner.Worker/appsettings.json .github apps/web/e2e/printed-text-replacement.spec.ts
git commit -m "docs: prepare printed text editing rollout"
```

- [ ] **Step 7: Request whole-branch review**

Use `superpowers:requesting-code-review` against the Phase 3D spec and the complete Phase 3D commit range. Resolve validated Critical and Important findings with reproducing tests, rerun Steps 3–5, and record deferred Minor findings in the runbook.

---

## Implementation Order and Gates

1. Tasks 1–3 establish immutable revisions, persistence, configuration, and licensed fonts.
2. Tasks 4–5 prove the risky style-estimation and layout assumptions before exposing mutations.
3. Tasks 6–9 deliver secure persistence, rendering, processing, and history.
4. Task 10 exposes the user workflow only after the canonical path exists.
5. Task 11 makes previews and exports revision-aware.
6. Task 12 validates the complete product while leaving production disabled.

Do not expose the Apply action when Tasks 6–9 are incomplete. Do not enable production until Task 12 and the whole-branch review pass.

