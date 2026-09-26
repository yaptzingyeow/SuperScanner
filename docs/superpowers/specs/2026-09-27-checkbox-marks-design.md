# Checkbox marks design

## User outcome

Users can tick forms smoothly without OCR. They choose a check (✓) or cross (×),
click the page to place it, and adjust its color, size, stroke weight and position.
Marks have transparent backgrounds and appear in downloaded PDFs. Automatic
checkbox detection and snapping are a later feature, not part of this delivery.

## Editor experience

- Add a prominent **Tick / Cross** tool beside Add text and Add signature.
- Selecting the tool opens compact controls near the top of the workspace:
  check/cross, black/blue/red/green swatches, custom color, size and stroke sliders.
- Default: black check, medium size and standard stroke.
- Show a clear instruction: "Click a checkbox on the page to place your mark."
  Escape exits placement mode. Placement does not select OCR words or pan the page.
- A placed mark is selected immediately. Drag to move; corner handles resize with
  locked proportions. Color and stroke changes preview immediately.
- Clicking an existing mark in placement mode removes it (toggle behavior).
  In selection mode, clicking selects it for editing; Delete removes it.
- Provide explicit Save/Cancel controls and visible saving/error feedback. Preserve
  the draft on failure. Mark controls and hit targets remain usable when zoomed.
- Undo/redo covers mark placement, style, movement, resizing and deletion in the
  current editor session. It does not imply a shared history for text/signatures.
- Refresh restores saved marks. Existing text and signature tools remain unchanged.

## Canonical data and rendering

Add a separate page-mark model; do not label checkbox marks as signatures or save
them as uploaded image assets. Fields: ID, document/page IDs, client request ID,
kind (check/cross), normalized box, color (#RRGGBB), normalized stroke width,
revision, timestamps and soft deletion. Enforce finite page-bounded coordinates,
allowed kinds, color syntax and stroke limits on the server.

Use fixed normalized path coordinates with rounded stroke caps/joins. Angular SVG
renders the editor preview; the PDF renderer draws the same coordinates as vector
paths. The box has no fill. Scaling changes mark size without raster blur.

Keep each mark inside the page; do not move or erase existing page content. Marks
are annotations, not replacements, and therefore do not alter OCR text geometry.

## Persistence and PDF exports

Use owner-checked page-mark list/create/update/delete endpoints following the
existing signature endpoint conventions. Create is idempotent; update/delete use
expected revisions. Mutation and export snapshot creation share the existing
document lock, increment document revision and invalidate stale exports.

Capture mark kind, geometry, color and stroke in immutable export snapshots.
Queued exports must render their captured version even if the user later edits or
deletes the mark. Render vector marks above the page image, with no opaque
background. Existing signatures and searchable text remain supported. Old export
snapshots without marks remain readable. No external AI API or image upload is
required for marks.

## Delivery sequence

1. Canonical model, migration, ownership/idempotency/revision endpoints.
2. Immutable export snapshots and vector PDF rendering.
3. Editor placement, selection, movement, color/size/stroke controls and save flow.
4. Session undo/redo, integration verification and local testing handoff.

## Acceptance checks

- Add a check or cross without running OCR; no white rectangle covers the form.
- Preview and PDF agree on location, shape, color, relative size and stroke.
- Marks survive refresh; deletion and failed saves behave predictably.
- Zoom/pan, OCR selection, text editing and signatures retain their own controls.
- Unowned resources are hidden, stale revisions conflict and duplicate creates do
  not add duplicate marks.
- Previously queued exports retain their mark snapshot after later mutations.
- Tests cover validation, endpoint ownership, snapshot immutability, SVG/PDF path
  mapping, editor interactions and undo/redo. Verify the actual local flow before
  claiming completion. Preserve unrelated dirty work; do not push or deploy.
