# Checkbox Marks Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users place, style, move and remove transparent check/cross marks without OCR and export them as vectors.

**Architecture:** Store page marks separately from uploaded signatures. Use normalized path geometry shared conceptually by SVG preview and PDF drawing; immutable export snapshots preserve the queued result. Reuse document locking, owner checks and revision conventions.

**Tech Stack:** Angular, TypeScript/SVG, .NET 10, EF Core/PostgreSQL, PDFsharp.

**Spec:** `docs/superpowers/specs/2026-09-27-checkbox-marks-design.md`

## Global Constraints

- Automatic checkbox detection and snapping are a later feature, not part of this delivery.
- Marks have transparent backgrounds and appear in downloaded PDFs.
- Refresh restores saved marks. Existing text and signature tools remain unchanged.
- Undo/redo covers marks in the current editor session, not shared text/signature history.
- No external AI API or image upload is required for marks.
- Preserve unrelated dirty work; do not push or deploy.
- Execution: native inline, one task at a time; retain the five-hour 20% reserve.

## Review Focus

1. Zoomed placement must use image bounds, not viewport scroll offsets (Task 3).
2. Tiny marks near page edges must fit completely; reject NaN/out-of-page geometry (Tasks 1, 3).
3. Failed/repeated saves must not lose drafts or create duplicate marks (Tasks 1, 3).
4. Deleted/edited marks must not change an already queued export (Task 2).
5. Failed undo/redo and stale revisions must preserve history and show actionable feedback (Task 4).

## Task 1: Canonical marks and owner-checked persistence

**Files:** Create `src/ArksScanner.Domain/Documents/PageMark.cs`,
`src/ArksScanner.Infrastructure/Persistence/Configurations/PageMarkConfiguration.cs`,
`src/ArksScanner.Api/Endpoints/PageMarkEndpoints.cs`,
`tests/ArksScanner.Domain.Tests/Documents/PageMarkTests.cs`,
`tests/ArksScanner.Api.IntegrationTests/Documents/PageMarkEndpointsTests.cs`.
Modify `src/ArksScanner.Infrastructure/Persistence/AppDbContext.cs`,
`src/ArksScanner.Api/Program.cs`; generate an EF migration under the existing migrations directory.

**Interfaces:** `PageMarkKind { Check, Cross }`; immutable validated
`PageMarkStyle(string color, double strokeWidth)`; `PageMark` stores a `SignatureBox`
as the existing normalized rectangle value object, not a signature asset.
`Create(...)`, `Update(kind, box, style, expectedRevision, now)`, `Delete(expectedRevision, now)`.
DTO `{ id, pageId, kind, box, color, strokeWidth, revision }`.
GET/POST `/api/documents/{documentId}/pages/{pageId}/marks`;
PUT/DELETE `.../marks/{markId}`. POST uses a UUID Idempotency-Key;
PUT/DELETE use expectedRevision. Store mark creation request IDs uniquely per page.
Defaults: Check, #000000, stroke .08 of the shortest mark dimension; accepted stroke .02–.20.
Use the same document FOR UPDATE lock, audit and stale-export conventions as signatures.

- [ ] Write tests: invalid kind/color/NaN/stroke/bounds rejected; stale revisions rejected;
  unrelated owner and removed page return 404; repeated POST returns the same ID;
  successful update/delete increments document revision and marks exports stale.
- [ ] Run Domain/API test filters `FullyQualifiedName~PageMark` and observe RED.
- [ ] Implement the domain model, EF mapping/migration and endpoints; register the routes.
  Canonicalize color to #RRGGBB; reject, rather than clamp, invalid API values.
- [ ] Run full Domain and API suites; report existing failures separately.
- [ ] Review the task diff and commit only task-owned files, never broad-stage dirty work.

## Task 2: Immutable snapshots and vector PDF rendering

**Files:** Modify `src/ArksScanner.Domain/Documents/DocumentExport.cs`,
`src/ArksScanner.Application/Documents/CreateDocumentExport.cs`, and
`src/ArksScanner.Infrastructure/Processing/DocumentPdfBuilder.cs`.
Create `src/ArksScanner.Infrastructure/Processing/PdfMarkRenderer.cs`.
Extend existing Domain/export/API/Application PDF tests in their current files.

**Interfaces:** `MarkOverlaySnapshot(Guid MarkId, PageMarkKind Kind, SignatureBox Box,
string Color, double StrokeWidth)`; append optional `MarkOverlays` to snapshot entries
with an empty fallback for old JSON. Append optional `marksByPage` to export creation.
`PdfMarkRenderer.Draw(XGraphics graphics, MarkOverlaySnapshot mark, double pageWidth,
double pageHeight)`.
Paths in a 100×100 viewBox: check (18,52)→(42,76)→(82,24);
cross (24,24)→(76,76) and (76,24)→(24,76).
Round caps/joins, no fill; stroke width is normalized to the shorter box dimension.

- [ ] Write RED tests: old snapshots remain readable; queued mark stays unchanged after
  edit/delete; new exports include latest marks; PDF contains colored vector strokes
  at the hand-derived page position, with no opaque mark rectangle.
- [ ] Run affected export/PDF test filters and observe failures.
- [ ] Capture active marks within the export document transaction; render validated
  snapshots above the image using the declared paths. Do not query live marks in Worker.
- [ ] Run full Application/API suites; visually inspect a synthetic PDF with ✓ and ×
  on ruled boxes to verify position, stroke and transparency.
- [ ] Review and commit only task-owned changes; isolate any files already dirty at start.

## Task 3: Placement and styling editor

**Files:** Create `apps/web/src/app/documents/page-mark.models.ts`, `page-mark.service.ts`,
`page-mark-overlay.component.ts/.html/.scss`, `page-mark-tools.component.ts/.html/.scss`,
and corresponding `.spec.ts` files. Modify `page-text-editor.component.ts/.html/.scss/.spec.ts`.

**Interfaces:** `PageMarkDto` matches Task 1; service `list/create/update/delete`
returns canonical DTOs and supplies idempotency/expected revisions.
Overlay inputs `marks`, `draft`, `selectedId`, `placementMode`, `panMode`, `disabled`;
outputs `place({x,y})`, `select(id)`, `toggleDelete(id)`, `boxChange({id,box})`.
Tools emit kind/color/stroke/size changes plus Save/Cancel/Delete.
Mark default width: .025 of page width; derive normalized height from actual image
aspect ratio to make its physical box square. Size slider 50%–300% of default;
clamp placement at edges to keep the full box on-page.

- [ ] Write RED tests: tool works with OCR NotRequested; page click places a square
  at correct zoomed coordinates; SVG paths/styles reflect controls; corner resizing
  preserves proportions; placement click toggles an existing mark but selection
  mode only selects; Escape exits; pan and OCR interaction remain separate.
- [ ] Run the frontend test command with affected specs and observe RED.
- [ ] Implement SVG overlay/tools and typed service. Add **Tick / Cross** beside
  Add text/signature; show "Click a checkbox on the page to place your mark."
  Keep visible Save/Cancel and errors near the top. Preserve failed drafts and their
  idempotency key. Guard navigation with unsaved mark changes.
- [ ] Run the full frontend suite and production build; verify desktop and narrow
  viewports, zoomed drag, color/size/stroke, save and reload against the local API.
- [ ] Review and commit only task changes.

## Task 4: Session undo/redo and whole-flow verification

**Files:** Create `apps/web/src/app/documents/page-mark-history.ts/.spec.ts`;
extend mark editor/service tests and the existing browser-test harness with
`apps/web/e2e/page-marks.spec.ts`.
Update a focused delivery note under `docs/superpowers/`.

**Interfaces:** History stores before/after canonical mark states and exposes
`canUndo/canRedo`, `undo/redo` using awaited create/update/delete service operations.
Undoing deletion re-creates the equivalent mark with a fresh request ID and remaps
the returned server ID. Store latest revisions after each successful operation;
never move the history cursor on failure. New edits clear the redo branch.

- [ ] Write RED tests: place→undo→redo; style/move/resize→undo;
  delete→undo restores equivalent mark; failed compensation retains history;
  stale revision conflict reloads canonical state and clears incompatible history.
- [ ] Run history/editor tests and observe RED.
- [ ] Implement mark-scoped history and disabled-busy Undo/Redo controls; do not
  interfere with text input or existing text/signature history shortcuts.
- [ ] Run full affected suites, browser flow and PDF visual check. Include check/cross,
  custom color, smallest/largest sizes, edge placement, refresh, deletion, queued
  export followed by editing, and a denied ownership request. Record known unrelated
  failing tests rather than describing the whole solution as green.
- [ ] Complete one final independent review, address actionable findings, record
  exact test results and migration/local startup instructions, then commit task-only files.

## Execution readiness

Spec coverage and interface names self-reviewed. All five Review Focus items have
owning tests. Before execution, record dirty files and avoid committing pre-existing
changes without approval. Keep API/Worker network permissions intact when starting
local services so Firebase verification and OCR do not regress.
