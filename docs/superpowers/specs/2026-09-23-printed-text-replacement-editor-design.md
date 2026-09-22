# Phase 3D Printed Text Replacement Editor Design

**Date:** 2026-09-23  
**Status:** Approved design, pending implementation plan  
**Architecture:** Server-canonical hybrid

## Purpose

Phase 3D allows a user to replace selected printed English text on a scanned page while closely approximating the source text's visual style. Angular provides a responsive draft preview and manual controls. The server validates and stores confirmed edits, and a background worker produces the canonical derived page used by later previews and exports.

The primary user flow is deliberately short:

1. Select one or more printed OCR words.
2. Enter replacement text.
3. Review the immediate matched-style preview.
4. Adjust the replacement box or style when necessary.
5. Apply the change.
6. Undo or redo saved edits when required.

No draft changes the stored document. The imported source and every previously accepted page revision remain recoverable.

## Scope

Phase 3D includes:

- replacement of one contiguous selection of printed English OCR words;
- a local draft preview before confirmation;
- estimation of font family, font size, weight, text colour, letter spacing, baseline, and alignment;
- ranked licensed-font alternatives and manual style controls;
- safe fitting for longer replacement text;
- movement and resizing of a normalized replacement box;
- server-side background reconstruction restricted to the approved replacement region;
- deterministic canonical rendering into a new derived page revision;
- persistent edit history with apply, cancel, undo, and redo;
- use of the active derived page in the existing PDF export flow;
- authorization, concurrency, idempotency, audit, and safe failure handling.

Phase 3D does not include:

- handwriting imitation or replacement;
- signature, seal, identity-document, cheque, prescription, certificate, financial-instrument, or anti-counterfeit editing;
- OpenAI, GPT Image, or another generative-AI dependency;
- automatic movement of neighbouring OCR words;
- automatic multi-line wrapping;
- word-processor-style reflow of arbitrary raster content;
- searchable PDF text layers;
- background or watermark removal outside the explicit replacement workflow;
- guaranteed recovery of an unavailable proprietary font.

## User Experience

### Entering edit mode

The existing Phase 3C OCR overlay remains the selection surface. When the selected OCR elements are all classified as printed text, the selection summary exposes an **Edit selected text** action. The action is unavailable when the selection contains handwriting, invalid geometry, non-word elements, or words from more than one page.

Opening the editor captures the current page revision identifier and selected OCR element identifiers. The initial replacement value equals the recognized phrase so the user can correct OCR before changing other content.

### Draft preview

The editor displays:

- the original recognized phrase;
- a replacement text field;
- a preview over the page;
- the proposed font and confidence;
- controls for font, size, weight, colour, letter spacing, alignment, box position, and box size;
- overflow, low-confidence, and fallback warnings;
- **Apply change** and **Cancel** actions.

Typing and adjusting controls update browser-memory draft state only. Cancel removes the draft and restores the ordinary OCR selection view. Navigating away with a changed draft uses the application's standard unsaved-change confirmation pattern.

The preview suppresses the selected source glyph area visually and draws the replacement above it. This is an approximation: it must be labelled as a preview until the canonical render succeeds.

### Applying an edit

Pressing **Apply change** sends one command containing the source page revision, selected OCR element identifiers, replacement text, normalized replacement box, and the chosen style. The API returns an edit identifier and queued status. The UI disables duplicate submission, polls the edit status, and keeps the previous active page visible while rendering runs.

When rendering succeeds, the new derived revision becomes active and the page preview refreshes. When rendering fails, the previous revision remains active and the user may retry the same edit idempotently or return to the editor.

### Longer replacement text

The safe-fit algorithm applies these operations in order:

1. Measure the replacement using the chosen bundled font.
2. Preserve the estimated font size and reduce positive letter spacing toward the configured minimum.
3. If necessary, reduce font size toward the configured minimum scale.
4. If the text still does not fit, show overflow and require the user to resize or move the box.

Phase 3D never moves neighbouring text and never wraps automatically. A user can enlarge the box only within page bounds. Collision detection warns when the box intersects recognized content outside the original selection, but does not move or erase that content.

### Undo and redo

Undo activates the previous accepted page revision. Redo reactivates the next revision on the current edit branch. Applying a new edit after undo starts a new branch and makes the obsolete redo branch unavailable through the ordinary UI; its immutable audit records and assets remain subject to retention rules.

Undo and redo are server operations, survive browser refreshes, and are authorized against document ownership. Controls are disabled while an edit render or revision switch is in progress.

## Architecture

### Browser responsibilities

Angular owns transient interaction state:

- selected OCR element identifiers;
- draft replacement text and style overrides;
- safe-fit calculation and overflow indication;
- normalized box manipulation;
- approximate replacement preview;
- polling and presentation of server edit status.

The browser never directly mutates a stored page image. It loads the exact bundled web-font assets represented by stable font catalogue identifiers. Draft replacement text is excluded from analytics and ordinary client logs.

### API and application responsibilities

The ASP.NET Core API exposes owner-scoped commands and queries for:

- obtaining an initial style proposal for a printed OCR selection;
- applying a confirmed replacement edit;
- retrieving edit status;
- listing page edit history;
- undoing and redoing the active page revision.

Application services validate ownership, App Check according to the existing application policy, page and OCR revision consistency, selected element membership, printed classification, normalized geometry, replacement length, style bounds, font catalogue membership, and idempotency.

No endpoint accepts an arbitrary object-storage key or client-provided source image. Server code resolves assets from owned page revisions.

### Worker responsibilities

The worker performs canonical rendering:

1. Acquire the immutable source revision and validated edit operation.
2. Recalculate layout with the server font metrics.
3. Refine a glyph mask within the approved region.
4. Reconstruct the exposed local background.
5. Render the exact replacement characters.
6. Blend rendering to approximate the scan's local blur, noise, and compression.
7. Verify that pixels outside the permitted mask are unchanged.
8. Store the rendered asset and create a derived page revision.
9. Atomically activate the revision when the edit is still current.

The output is deterministic for the same source hash, edit payload, renderer version, and font asset version. Provider calls are not part of this workflow.

### Export responsibilities

The existing export builder resolves each page's active revision at export creation time. An export created after an edit completes includes the canonical derived page. Exports created before completion retain their existing version semantics.

OCR selection polygons, draft previews, handles, warnings, and other editor UI never become page pixels or PDF annotations.

## Domain Model

### FontCatalogueEntry

A versioned, server-owned entry identifies an approved font asset:

- stable catalogue identifier;
- display and family names;
- asset version and hash;
- supported weights and styles;
- redistribution licence metadata;
- web-preview asset reference;
- server-rendering asset reference;
- enabled state.

Only enabled catalogue entries may be chosen for a new edit. Existing revisions remain reproducible with the exact recorded asset version.

### TextStyleEstimate

The proposed or user-confirmed style contains:

- font catalogue identifier and asset version;
- font-match confidence;
- normalized or page-relative font size;
- weight;
- RGB or RGBA text colour;
- letter spacing;
- baseline position and angle;
- horizontal alignment;
- per-property provenance indicating estimated, fallback, or user-overridden.

Style values use bounded numeric ranges configured on the server. The API never accepts an arbitrary font filename, CSS expression, or unvalidated colour string.

### TextEditOperation

An immutable confirmed operation records:

- edit identifier, document identifier, and page identifier;
- actor and audit correlation identifier;
- source page revision and source asset hash;
- source OCR result identifier and selected OCR element identifiers;
- original recognized text and replacement text in protected persistence;
- normalized approved region and replacement box;
- confirmed style and renderer/font versions;
- sequence and branch parent;
- queued, processing, succeeded, or failed status;
- safe failure code, timestamps, and resulting page revision identifier;
- idempotency key and canonical request hash.

Recognized and replacement text must not appear in ordinary logs, metrics labels, exception messages, or audit summaries. Audit data identifies the operation and actor without duplicating sensitive text.

### PageRevision

A page revision identifies its immutable asset, parent revision, producing operation, dimensions, media type, hash, and creation time. The Page aggregate identifies one active revision. Original, filtered, cropped, and edited revisions share this versioning boundary rather than overwriting assets.

## Style Estimation and Font Matching

Phase 3D uses deterministic local estimation rather than an external AI service.

The estimator uses OCR geometry and a bounded crop around the selected printed words to derive text height, baseline angle, foreground colour, stroke-weight category, letter spacing, and alignment. Font matching compares extracted glyph features and measured word proportions against rendered candidates from the licensed catalogue.

The result contains ranked candidates and confidence. High-confidence results preselect the top candidate. Low-confidence results preselect a neutral fallback, display alternatives, and make the uncertainty visible. The product describes all matches as approximations; it does not claim to identify an unavailable original font.

The initial catalogue contains a deliberately small set of open, redistributable fonts covering common serif, sans-serif, monospaced, and form-printing styles. Font assets are checked into or fetched through the controlled build process with their licence notices and hashes.

## Background Reconstruction and Pixel Containment

The approved repair mask is derived from the selected OCR word polygons, locally refined around foreground glyph pixels, and clipped to the approved region. Conservative dilation may cover antialiasing fringes but cannot cross the region boundary.

Background reconstruction uses deterministic local methods suitable for paper and simple form backgrounds. A protection mask prevents recognized content outside the selection from being used as writable output. If the region contains complex artwork, a border, a stamp, or overlapping unselected text that cannot be reconstructed safely, the operation fails without activating a new revision.

After rendering, the worker compares source and output outside the approved mask. Any detected change outside tolerance rejects the result. The original and active prior revision remain intact.

## API Behaviour

Routes follow the existing document and page ownership hierarchy. Concrete names are finalized in the implementation plan, but the contract provides these operations:

- propose style for a page OCR selection;
- create a replacement edit with an idempotency key;
- get one edit and its status;
- list revision-aware edit history for a page;
- undo the active revision;
- redo the active revision.

Commands return safe problem details with stable error codes. Expected conflicts include stale page revision, stale OCR result, unsupported handwritten selection, noncontiguous selection, invalid box, unavailable font version, unsafe reconstruction, rendering failure, and concurrent revision change.

The API limits replacement length, selection word count, box area, request size, queued edits per page, and retries. These limits are configuration-backed and validated at startup.

## Concurrency and Idempotency

An edit is bound to an exact source page revision and OCR result. If either is no longer current when the command is accepted, the API returns a conflict and asks the user to reopen the editor. A worker activates its result only if the expected source revision is still active.

The same owner, page, and idempotency key with the same canonical payload returns the existing edit. Reusing the key with a different payload returns a conflict. Worker retries reuse the original operation and never create duplicate page revisions.

Only one state-changing edit or revision-switch operation may be active for a page at a time. Work on different pages remains independent.

## Security and Privacy

- Every read and mutation enforces the existing document-ownership boundary.
- App Check enforcement follows the existing protected mutation policy.
- Object storage remains private; only server-resolved owned keys are processed.
- Replacement and recognized text are treated as sensitive document content.
- Logs, metrics, traces, correlation data, and audit summaries exclude document text and image bytes.
- Temporary render files use process-scoped locations and are deleted after completion or failure.
- Font inputs are restricted to the server catalogue; users cannot upload executable or arbitrary font files in Phase 3D.
- The editor refuses handwritten selections and protected/high-risk document categories when detected or declared under the application's restricted-content policy.
- The feature does not generate or modify signatures, official seals, or anti-counterfeit features.

## Accessibility and Responsive Behaviour

All editing actions are usable with keyboard and assistive technology. The replacement field has a persistent label. Style controls expose their value and units. Move and resize operations have keyboard alternatives with announced normalized or page-relative changes. Warnings are conveyed by text and programmatic status, not colour alone.

Touch targets meet the application's mobile-ready sizing rules. The normalized overlay remains aligned during responsive resizing, browser zoom, and supported device orientation changes. Focus returns to a predictable control after apply, cancel, failure, undo, or redo.

## Failure Handling

- **Handwriting selected:** Do not open the printed editor; explain that handwriting replacement belongs to a later phase.
- **Low font confidence:** Use a labelled fallback and present ranked manual alternatives.
- **Overflow:** Preserve the draft, show the collision/overflow state, and require box adjustment before apply.
- **Stale revision or OCR:** Reject the command and reload current page/OCR state without losing the stored document.
- **Unsafe background:** Fail the edit without activating any output.
- **Renderer or storage failure:** Mark the operation failed with a safe code; retain the previous active revision and support idempotent retry.
- **Concurrent edit:** Accept only the operation that owns the expected active revision; return a conflict for the other.
- **Browser closure:** Continue confirmed background work; restore status and the resulting active revision when the user returns.
- **Font later disabled:** Existing edits remain renderable from their pinned asset version, while new edits cannot select the disabled entry.

## Testing Strategy

### Unit tests

- printed-selection eligibility and contiguity;
- style bounds, catalogue validation, and confidence/fallback decisions;
- safe-fit ordering, limits, overflow, and collision warnings;
- normalized move/resize constraints;
- edit state transitions, branch history, undo, redo, and redo invalidation;
- idempotency hashes and stale-revision checks;
- pixel-containment comparison and unsafe-background rejection.

### Integration tests

- owner-scoped style, create, status, history, undo, and redo endpoints;
- PostgreSQL persistence and concurrency;
- job leasing, retry, and exactly-once revision creation;
- private object-store reads/writes without accepting client keys;
- export selection of the active derived revision;
- log capture proving document text is absent.

### Angular tests

- selection-to-editor transition and handwriting rejection;
- draft-only typing, preview updates, cancel, and explicit apply;
- font alternatives, manual styling, warnings, and fallback copy;
- pointer, touch, and keyboard box adjustment;
- polling cancellation, safe errors, refresh restoration, undo, and redo;
- no selection or editor UI in exports.

### Render verification

A sanitized benchmark set uses known licensed fonts, common forms, plain and lightly textured paper, rotated baselines, dark and light text, compression noise, and longer replacement strings. Golden-image checks verify exact spelling, acceptable placement, stable server output, and no pixel changes outside the permitted mask.

Sensitive production documents and user replacement text are never committed as fixtures.

## Operational Configuration and Observability

Configuration controls feature enablement, catalogue version, numeric style bounds, replacement-length and word-count limits, safe-fit thresholds, permitted mask dilation, containment tolerance, retry policy, and maximum queued edits per page. Production rejects invalid or incomplete settings during startup.

Metrics record operation counts, status, duration, retry count, renderer version, fallback category, and safe error code. They never contain document identifiers at unsafe cardinality, recognized text, replacement text, filenames, object keys, or image content.

The operational runbook describes font licensing, catalogue upgrades, deterministic renderer upgrades, job recovery, failed-edit diagnosis, containment alerts, rollback, and restoration of a prior page revision.

## Delivery Boundaries

Phase 3D is delivered incrementally behind a feature flag. Persistence and APIs may ship disabled before the editor is exposed. Production enablement requires the deterministic renderer, containment checks, authorization tests, benchmark evidence, and rollback procedure to be complete.

No OpenAI account, image-generation model, or additional external recognition service is required for Phase 3D. Google Document AI remains the source of OCR words and geometry from Phase 3B.

## Success Criteria

Phase 3D is complete when:

1. A user can select contiguous printed English words, enter replacement text, and see an immediate approximate preview.
2. Nothing is persisted until the user presses **Apply change**.
3. A confirmed edit creates one canonical derived page revision without overwriting the original.
4. The renderer reproduces the exact replacement string with an estimated or manually selected licensed style.
5. Longer text follows the defined spacing-then-size fitting policy and requires manual box adjustment when it still overflows.
6. The replacement box can be moved and resized within page bounds with pointer, touch, and keyboard input.
7. Pixel-containment verification prevents activation when output changes outside the approved mask.
8. Undo and redo survive browser refreshes, and a new edit after undo invalidates the ordinary redo branch.
9. Existing PDF export uses the active canonical revision and contains no OCR highlight or editor controls.
10. Handwriting and restricted-content selections cannot enter the printed replacement workflow.
11. Ownership, App Check, concurrency, idempotency, privacy, and safe failure tests pass.
12. Scan, OCR, page management, and unedited PDF export continue to work when Phase 3D is disabled or an edit fails.

## Deferred Work

- Phase 3E: optional language or document assistance using a separately approved OpenAI integration.
- Advanced text reflow: automatic wrapping and movement of neighbouring recognized text.
- Searchable PDF text-layer generation.
- Dedicated authorized-handwriting profile and generation phase.
- Background and authorized-watermark repair outside printed replacement masks.

