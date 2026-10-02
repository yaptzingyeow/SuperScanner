# Clean Paper and A4 Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax.

**Goal:** Add A4 PDF layout, conservative hole cleanup suggestions, and reviewed manual debris removal.

**Architecture:** Snapshot export layout per immutable export and apply one image placement transform to raster, marks, signatures, and OCR. Reuse the existing proposed page-repair revision pipeline for local OpenCV suggestions and manual masks.

**Tech Stack:** .NET 10, PDFsharp, Angular, PostgreSQL, Python OpenCV.

**Spec:** `docs/superpowers/specs/2026-09-28-clean-paper-a4-design.md`

## Global Constraints

- Original size is the default. A4 is 210 × 297 mm, orientation follows image aspect, 5 mm minimum margins and uniform fit.
- Never use fixed corners from the sample image; preserve originals and existing overlays.
- Hole and debris removal require user reviewed preview and fenced apply.
- Keep unrelated dirty files unchanged; no push or deployment.

## Review Focus

- Wide and tall images keep correct A4 orientation and no clipping.
- Searchable words and marks align with the raster after margins are added.
- Old export snapshots and retries keep their originally chosen layout.
- Printed circles, margin notes and ruling are not erased as holes.
- Stale repair previews fail without changing the current page.

### Task 1: A4 export

**Files:** `src/ArksScanner.Domain/Documents/DocumentExport.cs`, `src/ArksScanner.Application/Documents/CreateDocumentExport.cs`, `src/ArksScanner.Api/Endpoints/DocumentExportEndpoints.cs`, `src/ArksScanner.Infrastructure/Processing/DocumentPdfBuilder.cs`, `apps/web/src/app/documents/{documents-api.service,export-status.component}*`; tests beside existing export tests.

- [x] Add failing tests for layout selection, snapshot default, A4 geometry, signature/mark/OCR alignment, and retry.
- [x] Run focused tests and confirm feature failures.
- [x] Implement validated layout option and immutable snapshot; route UI choice through API.
- [x] Implement shared placement transform for all page content.
- [x] Run focused tests, builds, and visually render a synthetic A4 PDF.

### Task 2: Hole suggestions and reviewed removal

**Files:** `src/ArksScanner.Worker/processing/` new focused detector and repair modules; domain, application, API, persistence repair operation/revision; Angular cleanup view; tests at each boundary.

- [x] Add synthetic fixtures for holes, a printed circle, frame line, protected regions, and untouched pixels; inspect the supplied photo privately.
- [x] Implement conservative edge candidates and editable masks.
- [x] Add private bounded preview and source revision fenced apply, using the planned repair lifecycle.
- [x] Verify result preview, ownership, stale rejection, and revision history with focused tests.

### Task 3: Manual debris cleanup

**Files:** Angular cleanup controls; same repair API and worker modules as Task 2.

- [x] Add tests for selection, outside-mask preservation, and stale operation.
- [x] Implement rectangle/brush/erase, zoom/pan, selection undo/clear, preview/apply, and constrained local inpainting.
- [x] Verify OCR invalidation, immutable export, asset retirement, API-to-OpenCV-to-PDF flow and docs. Browser manual acceptance is still pending.

## Progress

2026-09-28 local progress: A4 snapshot, shared raster/overlay/OCR placement, and export UI are implemented. A synthetic landscape A4 PDF was rendered and inspected; margins, aspect ratio, and mark placement looked correct. Portrait/landscape geometry and immutable retries have automated coverage. No production export or browser end-to-end check has been done.

Hole suggestions now detect the two punch holes in the supplied private photo without committing it. A private masked PNG preview, explicit revision-fenced Apply, mixed undo/redo, ownership/App Check, and 24-hour retirement of unused previews are implemented. OCR words and active signatures/marks are protected. Manual rectangle, brush, erase-selection, zoom/pan, undo-selection, clear-all and Cancel are available. Deterministic invalid repair jobs now fail promptly rather than retrying for a long time. The local private preview still shows folds, shadows, show-through and a narrow strip of keyboard above the page; automatic complex clutter removal is not claimed.

Final automated verification: Python processing 49/49, Domain 110/110, Application 431/431 plus one pre-existing external OCR skip, Angular 194/194, API integration 113/113, Infrastructure integration 103/103, and Angular build passed with bundle-size warnings. A cross-boundary integration test exercised the API, isolated PostgreSQL, local OpenCV, private preview, Apply, OCR invalidation, A4 export snapshot and final PDF build. Testcontainers applied the new EF migration successfully on isolated PostgreSQL. The standalone `dotnet ef migrations script` command could not run because the local `dotnet-ef` tool is absent. Manual browser acceptance remains pending. No local services restarted, live database migrated, push, or deployment performed.

2026-09-28 follow-up review: added a crop-revision fence so stale manual selections fail before preview creation; OCR/protected-area payloads now travel through a bounded private request file instead of a Windows command argument; the repair runtime rejects dark ink connected beyond the selected mask (including a rule without OCR); the brush overlay uses the source image aspect ratio; and a Ready export exposes its saved page layout so the UI offers a new PDF after a page-size change. The repair integration fixture now selects the actual left punch hole instead of keyboard background. Focused Docker-backed worker and API-to-A4-export tests passed after this correction. Full manual browser acceptance is still pending.

Final regression after review fixes: Python 51/51; Angular 196/196; .NET Domain 110/110, Application 432 passed with one pre-existing external OCR skip, Infrastructure integration 103/103, API integration 114/114. No production database or cloud services were changed.

2026-09-29 scan experience follow-up: replaced the crop form with a large preview, page rail, filter tiles, zoom and original comparison. Strength changes are debounced and automatically submitted; controls remain usable during processing, with the latest choice queued. Photos now queue automatic boundary detection and Clean Document processing; uncertain detections retain the full image and automatic failures restore the original. PDF imports remain unchanged. Added side-edge refinement and isolated speck removal with nearby-ink protection. This is raster cleanup and A4 placement, not OCR-based document reconstruction or proprietary Magic Pro equivalence.

Verification for this follow-up: .NET solution build and Angular development build passed; Python 52/52; focused crop/import .NET tests 58/58; new filter autosave tests 2/2. Local API and Worker restarted; frontend, API and Worker health endpoints returned 200. Manual visual/browser acceptance and comprehensive new automatic-enhancement failure-path coverage remain pending. No push or deployment performed.
