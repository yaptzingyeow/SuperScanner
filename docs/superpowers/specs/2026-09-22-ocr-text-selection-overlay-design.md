# OCR Text Selection Overlay Design

**Date:** 2026-09-22
**Phase:** 3C
**Status:** Approved design, pending implementation plan

## Purpose

Allow a user to drag across one or more recognized English words on a scanned page, see a temporary editor-only highlight, and inspect the selected phrase before Phase 3D introduces text replacement.

The first release must support multi-word selection immediately. A user must be able to select a phrase such as `Yap Tzing Yeow` in one drag gesture rather than selecting each word separately.

## Scope

Phase 3C includes:

- an OCR word overlay aligned with each Ready page preview;
- pointer dragging across multiple words;
- single-word selection by click or a very small drag;
- reverse and diagonal drag gestures;
- deterministic phrase ordering using OCR hierarchy and reading order;
- keyboard navigation and selection extension;
- a selection summary showing the phrase, word count, average confidence, and printed/handwritten classification;
- safe handling of missing or invalid OCR geometry;
- responsive alignment during page resizing and browser zoom.

Phase 3C does not include:

- changing or replacing recognized text;
- font, colour, size, weight, spacing, or alignment estimation;
- replacement-box resizing or text reflow;
- undo or redo;
- searchable PDF generation;
- storing a user's selection;
- drawing highlights into an image or exported PDF.

## Privacy and Export Boundary

The highlight is a temporary Angular user-interface state. It must never become part of the document content.

- The page image remains unchanged.
- Selection state is held only in browser memory.
- Selection state is not posted to the API or written to PostgreSQL or object storage.
- Selection state is not supplied to image processing or PDF export.
- Selected OCR text, word coordinates, and page content are not written to application logs, analytics, metrics, or error reports.
- Navigating away, changing pages, clicking empty space, or pressing `Escape` clears the selection.
- Export continues to use the clean processed page asset and cannot access the SVG overlay.

## Recommended Architecture

Add a dedicated Angular `OcrTextOverlayComponent` to a Ready page preview. The processed page image remains the base layer. A same-size SVG viewport is positioned above it and uses the OCR result's normalized coordinates.

Only OCR elements whose kind is `Word` become interactive polygons. Block and line elements remain structural information used to determine hierarchy and reading order; they are not rendered as visible boundaries.

The component owns ephemeral selection state and emits a presentation-only selection summary to its parent. It does not depend on an API mutation service, persistence service, export service, or telemetry service.

No backend or database schema change is required. The existing page OCR response already contains text, kind, parent-child hierarchy, confidence, text type, reading order, and four-point normalized polygons.

## Component Responsibilities

### OCR text overlay

The overlay:

- converts each normalized OCR polygon point into SVG viewport coordinates;
- keeps the SVG `viewBox` normalized so browser scaling preserves alignment;
- renders word polygons invisible and pointer-active in the normal state;
- renders selected word polygons with a subtle translucent blue fill and outline;
- calculates intersection between the drag selection region and word polygons;
- sorts selected words into OCR reading order before forming a phrase;
- clears its state when its page identity or OCR result changes;
- provides focusable word targets and keyboard selection behavior.

### Selection summary

The summary displays:

- selected phrase;
- selected word count;
- average confidence as a percentage;
- text classification: `Printed`, `Handwritten`, `Mixed`, or `Unknown`.

The summary must not send its contents to logging, analytics, or the server.

### Page preview integration

The page preview:

- displays the overlay only when OCR is `Ready`;
- passes the existing OCR hierarchy and page identity into the overlay;
- keeps the processed image and overlay in the same positioned container;
- preserves existing crop, filter, page-management, recognition, and export actions;
- displays `No selectable text detected` when OCR is Ready but has no valid word polygons.

## Pointer Interaction

1. Pointer-down inside the overlay begins a selection gesture and captures the pointer.
2. Pointer movement updates a normalized drag region.
3. Every usable word polygon intersecting that region becomes selected.
4. Pointer-up finalizes the selection.
5. A movement below the click threshold selects the word under the pointer instead of producing an empty drag region.
6. A new gesture replaces the previous selection.
7. Clicking empty space clears the selection.

The gesture works in any direction. The implementation must not assume the drag starts at the phrase's top-left corner.

Polygon intersection, rather than axis-aligned word boxes alone, is the source of truth. A coarse bounding-box pre-check may be used as an optimization, but it must not change the selected result.

## Reading Order

Selection order is not derived from pointer direction or screen position. It uses the OCR hierarchy returned by the API:

1. parent block reading order;
2. parent line reading order;
3. word reading order;
4. stable element ID as a final deterministic tie-breaker.

The selected phrase joins words with a single display space. This phrase is a selection preview only and does not replace the OCR provider's full text.

## Keyboard and Accessibility

- Each usable word is keyboard-focusable within one overlay interaction region.
- Arrow keys move focus through OCR reading order.
- `Shift` plus an arrow key extends or contracts the active selection through reading order.
- `Enter` selects the focused word.
- `Escape` clears the selection.
- Selected polygons expose an accessible selected state.
- Accessible labels may identify the word locally in the DOM, but must not be copied to telemetry or error reporting.
- The selection summary uses an appropriate live region so its change is announced without moving focus.

## Visual Behaviour

- Normal state: polygons are visually invisible.
- Active selection: selected polygons use a translucent blue fill and a clear blue outline.
- The style remains legible over light and dark scans without hiding the underlying text.
- Low-confidence words do not receive a different overlay colour. Confidence appears in the summary to avoid visual noise.
- No always-visible debug boundary mode is included in Phase 3C.

## Geometry and Responsive Alignment

OCR points are normalized to the range `0..1`. The SVG uses a normalized coordinate system, preserving alignment independently of rendered pixel size.

A usable word polygon must contain exactly four finite points within `0..1`. Invalid polygons are skipped rather than repaired in the browser. Skipping one invalid word must not prevent other valid words from being selected.

The image and SVG share the same containing box and aspect ratio. Alignment must remain correct when:

- the responsive layout changes width;
- the browser zoom changes;
- device pixel ratio differs;
- the page is displayed on desktop or mobile-sized viewports.

## Error Handling

- OCR not Ready: do not render the interactive overlay.
- Ready with zero words: show `No selectable text detected`.
- Ready with only invalid word polygons: show the same safe empty state.
- One invalid polygon among valid words: skip only that word.
- Pointer cancellation or lost capture: finish safely without retaining a half-active gesture.
- Component destruction or page change: clear all ephemeral selection state.

The underlying scan, page actions, and PDF export remain usable for every overlay error.

## Testing Strategy

Component and geometry tests must cover:

- dragging across multiple words selects the complete phrase;
- selected words are returned in OCR reading order;
- reverse, vertical, and diagonal dragging;
- rotated and perspective-shaped polygons;
- click-threshold single-word selection;
- responsive coordinate scaling;
- clearing by empty click, `Escape`, page change, and component destruction;
- keyboard focus and `Shift` selection extension;
- printed, handwritten, mixed, and unknown summary classifications;
- confidence averaging;
- invalid polygon isolation and safe empty state;
- no API mutation during selection;
- no selection or overlay state passed to export;
- no logging or analytics calls containing OCR text or coordinates;
- existing recognition, page management, and export regression tests.

Manual acceptance uses a user-approved or synthetic scanned page. The reviewer verifies that a multi-word phrase can be selected, highlights remain aligned while resizing, and the exported PDF contains no highlight.

## Success Criteria

Phase 3C is complete when:

1. A user can drag once across multiple recognized words and see the correct phrase highlighted.
2. The phrase is ordered by OCR reading order regardless of drag direction.
3. The highlight remains aligned across supported viewport sizes and zoom levels.
4. Keyboard users can navigate, select, extend, and clear word selections.
5. Invalid OCR geometry cannot break the page preview or export.
6. Selection data remains browser-memory-only and is absent from storage, logs, analytics, processing, and PDF output.
7. Existing scan, OCR, page management, and PDF export behavior continues to work.
