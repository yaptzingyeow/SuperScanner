# Document Signature Overlay Design

**Date:** 2026-09-26

**Status:** Proposed for user review

**Architecture:** Server-canonical, editable page overlays

## Purpose

Let a user place their own signature on a scanned document with minimal friction. They can upload a signature image and remove its light background, or draw a signature with a mouse, pen, or finger. Placement remains editable after saving and survives refresh. The page preview and exported PDF must show the same signature in the same position. This is a visual signing tool, not an identity-verified or legally certified e-signature service.

The user has prioritized ease of use. A successful first use should be: open a page, choose **Add signature**, upload or draw, inspect the transparent result, place it, and save. No OCR, font matching, paid background-removal API, or reusable signature library is required.

## Scope and boundaries

- Both upload and draw modes are included in the first release.
- A signature belongs to one document page in the current document. Multiple overlays may exist, but no account-wide signature library is created.
- A saved signature can be selected again, moved, resized, or deleted before or after a page refresh.
- The imported page image remains intact. Signature changes affect overlay records and document revision, not the source scan or OCR result.
- Export includes the signature in the PDF; exported files are immutable snapshots. Editing a signature later requires a new export.
- There is no identity verification, consent workflow, recipient invitation, certificate, legal attestation, or cryptographic document-signing claim.
- There is no paid background-removal or image-generation API. Existing hosting and object-storage usage still incurs normal infrastructure cost.

## User experience

### Entry and creation

Place an obvious **Add signature** action beside the existing page editing actions in the full-page editor. Opening it shows two equal choices: **Upload image** and **Draw signature**. OCR status does not hide or disable this action.

Upload accepts PNG or JPEG. The browser shows the original and a checkerboard-backed transparent preview. A local light-background-to-alpha operation supplies a useful default for signatures photographed or scanned on white paper. A single **Background removal** strength slider updates the preview immediately, with **Keep original** as an escape hatch when automatic removal damages pale ink. The original is not replaced until the user confirms. Already transparent PNG pixels remain transparent. This simple local algorithm is not advertised as capable of removing arbitrary photographic backgrounds.

Draw mode opens a large responsive canvas, supports mouse, pen, and touch, and offers **Undo stroke**, **Clear**, and **Use signature**. A blank drawing cannot be accepted. Both modes produce a transparent preview before placement.

### Placement and editing

Choosing **Use signature** adds a draft overlay near the visible page center. The user drags its body to move it and drags corner handles to resize it while preserving its aspect ratio. The overlay image is transparent; its selection border and handles are editor-only and never exported. Zoom and pan continue to work. Clear **Save** and **Cancel** actions accompany the draft; Cancel discards only the draft.

Saved overlays remain selectable. Selecting one exposes **Move/resize**, **Delete**, and a small preview; Save confirms geometry changes. Deleting asks for confirmation and can be reversed by cancelling before confirmation. Refresh retrieves saved overlays. The interface distinguishes unsaved changes from saved placement and shows an error without removing the previous version if a save fails.

### Accessibility and small screens

The same flow works on desktop and mobile-sized web views. Handles have a touch-friendly target. A selected overlay also exposes numeric position and size controls so keyboard users need not drag. Focus order, labels, visible focus, and status announcements cover create, save, and errors. Page pan and signature dragging must not intercept the action buttons or each other.

## Architecture and data flow

### Client draft processing

An Angular signature creation component owns a browser-memory draft. Upload processing and draw strokes run locally, so adjusting the strength slider sends no API requests and no paid API is called. The component emits a transparent PNG candidate and its dimensions only after confirmation. The page overlay component handles placement, handles, and keyboard controls using normalized page coordinates. Angular keeps unsaved edits separate from the last server state.

### Canonical storage and API

The API authorizes each operation against the document owner and active page. It accepts a bounded signature PNG, validates file signature, decoded dimensions, pixel count, and transparency, strips metadata, and re-encodes it before private object-store write. It never trusts browser-reported media type or geometry. A page-signature record stores its ID, document/page IDs, private asset key, normalized x/y/width/height, image aspect ratio, revision, and timestamps. Mutations require an expected revision to prevent overwriting a concurrent edit and are auditable without logging image content.

Endpoints provide list/create/update/delete for overlays under a document page, plus an authorized image-read endpoint. The create request commits an asset and a record coherently; failures clean up or make orphaned private assets eligible for cleanup. Deleting an overlay removes its active record immediately but retains its asset for any already queued export snapshot until export retention permits cleanup. A signature mutation increments the document revision so an earlier export is marked stale.

### Preview and export

The page API returns active signature metadata with page data. Angular draws images over the page using normalized coordinates. PDF export snapshots each overlay's image key and geometry together with its page entry, then draws the image over the page before completing the PDF. An export created before an edit uses its original snapshot; a new export uses the updated overlay. The same coordinate convention and aspect-ratio rule apply to the web preview and PDF renderer. The searchable text layer remains a separate existing feature; signature overlays must not mutate the OCR source.

## Validation and failure handling

- Reject unsupported files, oversized uploads, extreme dimensions, invalid PNG data, empty transparent images, and geometry outside the page, with actionable messages.
- Reject a stale expected revision and reload the current overlay without discarding the user's draft silently.
- Do not save a partially uploaded asset, a half-applied placement, or a PDF missing a snapshotted signature. Export failure leaves the prior Ready export intact.
- Only the authorized document owner may read, create, edit, or delete its signature assets and overlay records. Assets are private, and API responses never expose raw storage credentials.
- Treat uploads as untrusted images. Decode with resource limits, remove metadata, and avoid rendering active content. No SVG upload in the first release.
- Make clear in UI copy that this is a visual signature, not a certified electronic-signature service.

## Verification

1. Component tests cover upload defaults, strength adjustment, Keep original, drawing, undo/clear, placement, keyboard controls, cancel, saved re-edit, and touch/pointer interactions.
2. API tests cover ownership, invalid files, geometry, optimistic concurrency, idempotent retries, and asset cleanup paths.
3. PDF tests compare snapshotted overlay placement with page geometry, verify transparency and aspect ratio, and confirm that changed overlays require a new export.
4. End-to-end checks cover refresh persistence, mobile-sized layout, preview/export agreement, and failure recovery without loss of the previous saved page.

## Delivery boundaries

Implementation can be staged as: canonical data/API and export snapshot support; upload/draw draft processing; page placement and saved editing; export composition and regression verification. No signature implementation starts until this specification and the subsequent implementation plan are reviewed and approved.
