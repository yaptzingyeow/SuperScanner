# Import Review and Document Workspace Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single-file "New scan → upload status → document detail" flow with a multi-file Import pages review screen and a document workspace with View / Edit / Export tabs, each with its own toolbar.

**Architecture:** Files upload as soon as they are chosen; the Import pages screen polls the document and shows the server-rendered result of each page, and every look/rotation change goes through the existing crop endpoint. Page rotation is new: a `Rotation` column on `Page`, carried by `CropRequest` and applied by `crop_image.py` after flattening. The workspace replaces `DocumentDetailComponent` at `/documents/:documentId`; the Edit tab opens the existing crop, clean and text editors rather than rewriting them.

**Tech Stack:** Angular 22 (standalone components, signals, Vitest), ASP.NET Core 10 + EF Core 10 (PostgreSQL), Python 3 OpenCV worker (`crop_image.py`), xUnit.

**Spec:** Approved design canvas https://claude.ai/artifact/Gg3truEb83xab9MSix8ACz (artboards "1 · Import pages", "2 · Document workspace" and the notes beneath them). Decisions taken with the user on 2026-10-01: page management stays in the left page rail (no Page tab); files upload immediately; mobile layout comes later; image (JPG/PNG) export is a separate follow-up plan.

## Global Constraints

- UI copy is English and uses these exact labels: tabs `View`, `Edit`, `Export`; import buttons `Upload originals without changes`, `Confirm`; checkbox `Apply this look to all pages`; rotation buttons `Rotate left`, `Rotate right`; `Adjust corners`.
- Look labels → `ScanFilter` ids, in this order: `Magic scan`→`Magic`, `Original`→`Original`, `Document`→`Document`, `Bright`→`Bright`, `No shadow`→`RemoveShadows`, `Smart clean`→`CleanDocument` (strengths `Gentle`→`CleanDocumentGentle`, `Balanced`→`CleanDocument`, `Strong`→`CleanDocumentStrong`), `Grayscale`→`Grayscale`, `Black & white`→`BlackAndWhite`, `Clean content`→`ContentClean`.
- Rotation values are exactly 0, 90, 180, 270 (degrees clockwise); anything else is a validation error.
- Accent `#1E6B50`, touch targets ≥ 44 px, every control is a real `<button>`/`<a>`/`<input>` with a label.
- Do not commit, push or deploy unless the user asks; preserve unrelated uncommitted work in the tree.
- Angular commands use Node ≥ 22.22.3: `C:\Users\tzingyeow.yap\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe node_modules/@angular/cli/bin/ng.js test --watch=false` from `apps/web`.
- .NET tests run with `--artifacts-path .task-tools/test-artifacts` because the local API/Worker lock `bin/`.
- After each task that changes the API or Worker: restart both with `.task-tools/start-local-api-secure.ps1 -Detach -UseStoredPassword` and `.task-tools/start-local-worker-secure.ps1 -Detach -UseStoredPassword` (network access needed for Firebase App Check), then confirm `GET /api/documents` returns 200 through the signed-in web app.

## Review Focus

- A look or rotation chosen while a page is still `Detecting`/`Processing` → the crop endpoint returns 409; the screen must reload that page's crop state and retry once, never show a raw error or apply the wrong revision. (Task 4 test `retries once after a crop revision conflict`.)
- `Apply this look to all pages` with a PDF page (`canCrop === false`) or a page with text edits (409 "has text edits") → those pages are skipped and listed in a notice; the others still update. (Task 4 test `skips pages that cannot be re-cropped and reports them`.)
- Rapid repeated `Rotate right` clicks → requests for one page are serialized, final rotation equals the number of clicks × 90 mod 360. (Task 4 test `serializes rotation clicks per page`.)
- Leaving the Import pages screen or the workspace mid-poll → polling stops (no requests after destroy). (Task 4 and Task 7 tests `stops polling on destroy`.)
- Searching text on pages without Ready OCR → those pages are reported as "not recognized yet", not silently treated as "no match". (Task 9 test `reports pages without recognized text`.)

---

### Task 1: Page rotation in domain, API and worker

**Files:**
- Modify: `src/ArksScanner.Domain/Documents/Page.cs` (add `Rotation`, `SetRotation`)
- Modify: `src/ArksScanner.Infrastructure/Persistence/Configurations/PageConfiguration.cs` (column `Rotation`, int, not null, default 0)
- Create: migration `PageRotation` under `src/ArksScanner.Infrastructure/Persistence/Migrations/`
- Modify: `src/ArksScanner.Api/Endpoints/CropEndpoints.cs` (`CropRequest`, `ToDto`)
- Modify: `src/ArksScanner.Infrastructure/Processing/CropProcessor.cs` (pass rotation to the subprocess)
- Modify: `src/ArksScanner.Worker/processing/crop_image.py` (`apply` honours rotation)
- Test: `tests/ArksScanner.Domain.Tests/Documents/PageRotationTests.cs`, `src/ArksScanner.Worker/processing/test_crop_cli.py`, `tests/ArksScanner.Api.IntegrationTests/Documents/CropEndpointsRotationTests.cs`

**Interfaces:**
- Produces: `Page.Rotation : int`; `Page.SetRotation(int degrees)` throws `ArgumentException` unless 0/90/180/270; `CropRequest(int Revision, CropPoint[]? Points, string? Filter = null, int? Rotation = null)`; crop DTO gains `rotation`; CLI `apply <source> <points> <preview> <thumb> <filter> <rotation>` (rotation optional, default 0).

- [ ] **Step 1: Write failing tests**
  - Domain `Rotation_accepts_quarter_turns_and_rejects_others`: `SetRotation(90)` → `Rotation == 90`; `SetRotation(45)` and `SetRotation(360)` throw `ArgumentException`.
  - Python `test_apply_rotates_output_clockwise`: 400×200 source, full-image corners, filter `Original`, rotation `90` → printed size `{"width": 200, "height": 400}`-proportioned (height > width) and the top-left source pixel colour appears at the output's top-right.
  - API `Apply_with_rotation_persists_and_returns_it`: POST `/crop/apply` with `rotation: 270` → 202, then GET `/crop` returns `rotation: 270`; `rotation: 45` → 400 with key `rotation`.
- [ ] **Step 2: Run them, expect failures** (`dotnet test tests/ArksScanner.Domain.Tests --artifacts-path .task-tools/test-artifacts --filter PageRotation`; `python -m unittest test_crop_cli` in `src/ArksScanner.Worker/processing`; API filter `CropEndpointsRotation`).
- [ ] **Step 3: Implement.** `SubmitAsync` validates `request.Rotation` with the same rule and calls `page.SetRotation` before queuing; `CropProcessor` appends `page.Rotation.ToString(CultureInfo.InvariantCulture)` after the filter argument; `crop_image.py` applies `cv2.rotate` (`ROTATE_90_CLOCKWISE`, `ROTATE_180`, `ROTATE_90_COUNTERCLOCKWISE`) to the filtered image in `write_outputs`'s caller for both the Magic and legacy paths. Generate the migration with `dotnet tool restore` then `dotnet ef migrations add PageRotation --project src/ArksScanner.Infrastructure --startup-project src/ArksScanner.Infrastructure`.
- [ ] **Step 4: Run all three suites, expect pass;** also the full Python suite (77 tests) and `CropEndpoints` API tests.
- [ ] **Step 5: Apply the migration locally** — ask the user to run `scripts/database/Apply-DatabaseMigrations.ps1` (needs the PostgreSQL admin password; it also applies the pending `PageRepairOperations` migration). Restart API/Worker and verify `/api/documents` 200.
- [ ] **Step 6: Commit** (only if the user asked for commits).

### Task 2: Frontend crop/preview API helpers

**Files:**
- Modify: `apps/web/src/app/documents/documents-api.service.ts`, `apps/web/src/app/documents/document.models.ts`
- Test: `apps/web/src/app/documents/documents-api.service.spec.ts`

**Interfaces:**
- Produces: `interface CropState { revision: number; appliedRevision: number; status: string; filter: string | null; appliedFilter: string | null; rotation: number; points: CropPoint[] | null; }`; `interface CropPoint { x: number; y: number }`; `type ScanFilterId` (the 13 ids in `crop-editor.component.ts`); methods `getCrop(documentId, pageId): Promise<CropState>`, `applyCrop(documentId, pageId, body: { revision: number; points: CropPoint[]; filter: ScanFilterId; rotation: number }): Promise<CropState>`, `getPagePreview(documentId, pageId): Promise<Blob>` (GET `/pages/{id}/preview`, blob).
- Consumes: Task 1's `rotation` field.

- [ ] **Step 1: Failing specs** with `HttpTestingController`: `applyCrop posts revision, points, filter and rotation to /crop/apply`; `getPagePreview requests a blob`.
- [ ] **Step 2: Run, expect fail.** **Step 3: Implement.** Move `ScanFilter` type out of `crop-editor.component.ts` into `document.models.ts` as `ScanFilterId` and import it there. **Step 4: Run, expect pass.** **Step 5: Commit.**

### Task 3: Multi-file New scan that opens Import pages

**Files:**
- Modify: `apps/web/src/app/documents/new-document.component.{ts,html}`, `apps/web/src/app/documents/upload.service.ts`
- Test: `new-document.component.spec.ts`, `upload.service.spec.ts`

**Interfaces:**
- Produces: `UploadService.startDocument(title: string, files: readonly File[]): Promise<{ documentId: string; uploadIds: string[] }>` (creates the document, then `addFilesInternal`; rejects with `UploadFlowError('no_files')` for an empty list); route navigation `['/documents', documentId, 'import'], { queryParams: { uploads: uploadIds.join(',') } }`.

- [ ] **Step 1: Failing specs:** `title defaults to the first file name without extension when left empty`; `selecting three files uploads all three and navigates to import with their upload ids`; `a rejected file is reported while the others continue`.
- [ ] **Step 2–4:** run (fail), implement (`<input type="file" multiple accept=".pdf,.jpg,.jpeg,.png,.heic">`; drag-and-drop keeps working), run (pass). Keep `upload()` for existing callers.
- [ ] **Step 5: Commit.**

### Task 4: Import pages review screen

**Files:**
- Create: `apps/web/src/app/documents/import-review.component.{ts,html,scss}`, `apps/web/src/app/documents/scan-looks.ts`
- Modify: `apps/web/src/app/app.routes.base.ts` (route `documents/:documentId/import`), `apps/web/src/app/documents/crop-editor.component.ts` (honour `?returnTo=import`)
- Test: `import-review.component.spec.ts`, `scan-looks.spec.ts`

**Interfaces:**
- Consumes: Task 2 API; `DocumentsApiService.getDocument`.
- Produces: `SCAN_LOOKS: readonly { id: ScanFilterId; label: string; help: string }[]` (9 entries in Global Constraints order, Smart clean's id `CleanDocument`) and `SMART_CLEAN_STRENGTHS` in `scan-looks.ts`, reused by Task 8.

Layout and behaviour follow artboard "1 · Import pages": page rail (status chip `Ready` / `Finding edges…` / `Check corners` when `cropSource` guidance is `manual` or `verify`), centre preview from `getPagePreview` refreshed when `appliedCropRevision` changes, zoom − / + (25–400 %), `Compare with original` (only when the page's original is an image), right panel with rotate/adjust corners, look grid, strengths when Smart clean is chosen, `Apply this look to all pages` (default checked), footer with document name, `Upload originals without changes`, `Confirm`. Only pages whose `sourceUploadId` is in `?uploads=` are shown; without the parameter, all pages.

- [ ] **Step 1: Failing specs:**
  - `polls the document every 1500 ms until no shown page is Importing, Detecting or Processing`, and `stops polling on destroy`.
  - `choosing a look with apply-to-all posts applyCrop for every croppable shown page with its current points, revision and rotation`.
  - `choosing a look without apply-to-all only updates the selected page`.
  - `serializes rotation clicks per page` (three quick `Rotate right` → three sequential calls, last rotation 270).
  - `retries once after a crop revision conflict` (first 409 → `getCrop` → second call succeeds).
  - `skips pages that cannot be re-cropped and reports them` (`canCrop: false` and a 409 "text edits" page listed in `role="status"`).
  - `upload originals applies Original with full-image corners and rotation 0, then navigates to the workspace`.
  - `confirm navigates to /documents/:id` and `Adjust corners navigates to the crop editor with returnTo=import`.
- [ ] **Step 2: Run, expect fail.**
- [ ] **Step 3: Implement `ImportReviewComponent`.** Full-image corners are `[{x:0,y:0},{x:1,y:0},{x:1,y:1},{x:0,y:1}]`. Per-page request queue: a `Map<string, Promise<void>>` chain. Revoke preview object URLs on change and destroy. Crop editor: when `returnTo=import`, its Done/back link goes to `/documents/:id/import` keeping `uploads`.
- [ ] **Step 4: Run, expect pass.** **Step 5: Commit.**

### Task 5: Page rail component

**Files:**
- Create: `apps/web/src/app/documents/page-rail.component.{ts,html,scss}`
- Modify: `apps/web/src/app/documents/document-detail.component.ts` (source of the reorder/remove/add-pages logic being moved)
- Test: `page-rail.component.spec.ts`

**Interfaces:**
- Produces: `<app-page-rail [documentId] [pages]="DocumentPage[]" [selectedPageId] [pageOrderRevision] (selectPage)="string" (pagesChanged)="DocumentDetail" (pagesAdded)="string[] /* uploadIds */">`. Thumbnails via `/pages/{id}/thumbnail` (existing endpoint used by `PageCardComponent.thumbnailUrl`); CDK drag-drop reorder calling `reorderPages` with `expectedPageOrderRevision`; per-thumbnail `Move earlier`/`Move later` buttons for keyboard users; `Remove page` with confirmation; `Add pages` opens the existing `AddPagesDialogComponent`.

- [ ] **Step 1: Failing specs:** `dropping page 3 before page 1 sends the new order with the expected revision`; `a 409 reorder restores the server order and shows the conflict message`; `remove asks for confirmation before calling removePage`; `adding pages emits the new upload ids`.
- [ ] **Step 2–4:** run, implement (move logic; `document-detail` keeps working until Task 7 removes its route), run.
- [ ] **Step 5: Commit.**

### Task 6: Page viewer with layouts and zoom

**Files:**
- Create: `apps/web/src/app/documents/page-viewer.component.{ts,html,scss}`
- Test: `page-viewer.component.spec.ts`

**Interfaces:**
- Produces: `<app-page-viewer [documentId] [pages] [selectedPageId] [layout]="'continuous' | 'single' | 'double'" [zoom]="number" [highlights]="Record<string, string[]>" (selectPage)="string">`; `type ViewerLayout = 'continuous' | 'single' | 'double'`; zoom is a percentage clamped to 25–400, `Fit width` sets it from the container width.

- [ ] **Step 1: Failing specs:** `single shows only the selected page`; `double shows the selected page and the next one (last page shows alone)`; `continuous shows every page in order`; `zoom is clamped to 25–400`; `highlights are passed to the OCR overlay of the matching page`.
- [ ] **Step 2–4:** run, implement (previews via `getPagePreview`, object URLs revoked; reuse `OcrTextOverlayComponent` for selection and highlights — add input `highlightWordIds: readonly string[]` there with its own spec `renders highlighted words with the search style`), run.
- [ ] **Step 5: Commit.**

### Task 7: Document workspace shell with View / Edit / Export tabs

**Files:**
- Create: `apps/web/src/app/documents/document-workspace.component.{ts,html,scss}`
- Modify: `apps/web/src/app/app.routes.base.ts` (`documents/:documentId` → `DocumentWorkspaceComponent`; `DocumentDetailComponent` route removed, file deleted once its specs' behaviours live in Tasks 5–10)
- Test: `document-workspace.component.spec.ts`

**Interfaces:**
- Consumes: Tasks 5 and 6.
- Produces: `type WorkspaceTab = 'view' | 'edit' | 'export'`, read from and written to `?tab=` (default `view`); `selectedPageId` in `?page=`; header with title, page count, tabs (`role="tablist"`), `Export PDF` shortcut (switches to Export); toolbar slot per tab rendered by Tasks 8–10.

- [ ] **Step 1: Failing specs:** `opens on View when no tab is given`; `tab buttons update ?tab= and keep ?page=`; `View toolbar has Select text, Pan, Continuous, Single page, Two pages, zoom and Search text`; `polls while any page is processing and stops polling on destroy`; `Export PDF switches to the Export tab`.
- [ ] **Step 2–4:** run, implement, run. Move the document-detail behaviours still needed (processing poll, failed-page retry) here.
- [ ] **Step 5: Restart API/Worker, verify `/api/documents` 200, open a document in the browser and check the three tabs. Commit.**

### Task 8: Edit tab tools

**Files:**
- Create: `apps/web/src/app/documents/edit-toolbar.component.{ts,html,scss}`
- Modify: `apps/web/src/app/documents/page-text-editor.component.ts` (accept `?tool=add|signature|mark|ocr` and start that mode on load)
- Test: `edit-toolbar.component.spec.ts`, `page-text-editor.component.spec.ts`

**Interfaces:**
- Consumes: `selectedPageId`, `DocumentDetail` from Task 7; `SCAN_LOOKS` from Task 4.
- Produces: toolbar buttons `Crop & look` → `/pages/{id}/crop`, `Clean page` → `/pages/{id}/clean`, `Edit text` → `/pages/{id}/text`, `Add text` → `/text?tool=add`, `Signature` → `?tool=signature`, `Tick / Cross` → `?tool=mark`, `Recognize text` → `?tool=ocr`, plus `Undo`, `Redo`, `History` reusing the text-revision history calls currently in `document-detail.component.ts` (`switchTextRevision`, `openTextHistory`). Every editor's back link returns to `/documents/:id?tab=edit&page=:pageId`. Right panel shows the selected tool's title, description and three steps as in the artboard.

- [ ] **Step 1: Failing specs:** `each tool navigates to its editor for the selected page`; `tool=signature opens the signature creator on load`; `tool=mark starts mark placement on load`; `undo is disabled when history cannot undo`; `history load failure shows the safe message` (the local `page_repair_operations` table must exist — see Task 1 step 5).
- [ ] **Step 2–4:** run, implement, run. **Step 5: Commit.**

### Task 9: View tab text search

**Files:**
- Create: `apps/web/src/app/documents/document-search.ts`
- Modify: `document-workspace.component.{ts,html}` (search box and results in the View toolbar)
- Test: `document-search.spec.ts`, `document-workspace.component.spec.ts`

**Interfaces:**
- Produces: `searchPages(query: string, ocrByPage: ReadonlyMap<string, PageOcr | null>): { hits: { pageId: string; wordIds: string[] }[]; unrecognizedPageIds: string[] }` — case- and diacritic-insensitive, matches a phrase across consecutive words in OCR reading order, ignores queries shorter than 2 characters. Workspace highlights the current hit through `PageViewer.highlights`, with `Previous`/`Next` and `n of m`.

- [ ] **Step 1: Failing specs:** `finds a phrase spanning two words`; `is case-insensitive`; `reports pages without recognized text`; `returns no hits for a one-character query`; workspace `Next moves to the next hit and selects its page`.
- [ ] **Step 2–4:** run, implement (OCR fetched lazily per page with `getPageOcr` when search opens), run. **Step 5: Commit.**

### Task 10: Export tab with PDF, Print and Original file

**Files:**
- Create: `apps/web/src/app/documents/export-panel.component.{ts,html,scss}`, `apps/web/src/app/documents/print-pdf.ts`
- Modify: `apps/web/src/app/documents/export-status.component.ts` (reused inside the panel)
- Test: `export-panel.component.spec.ts`, `print-pdf.spec.ts`

**Interfaces:**
- Produces: Export toolbar kinds `PDF`, `Print`, `Original file` (Images is omitted until its follow-up plan); `printPdf(blob: Blob, doc: Document = document): Promise<void>` loads the blob in a hidden `<iframe>` and calls `contentWindow.print()`, removing the iframe and revoking the URL afterwards. PDF options are `Original size` / `A4` and `Make text searchable` (existing `createExport(id, pageLayout, includeSearchableText)`); `Original file` downloads `/pages/{id}/original` for the selected page.

- [ ] **Step 1: Failing specs:** `PDF export sends the chosen layout and searchable flag`; `Print builds or reuses a Ready export and calls printPdf with its blob`; `printPdf removes the iframe and revokes the URL after printing`; `Original file downloads the selected page's original`.
- [ ] **Step 2–4:** run, implement, run.
- [ ] **Step 5: Full verification:** Angular suite, Python suite, Domain/Application/API suites; restart API/Worker, verify `/api/documents` 200; walk the whole flow in the browser (new scan with three photos → Import pages → change look for all → rotate one page → Confirm → View/Edit/Export); update `docs/operations/scan-filters.md` with the new flow and the rotation migration. Commit.
