# Plan 4: Background and Authorized Watermark Repair

Status: Proposed implementation design for user review.

## Intent and existing decisions

Let users clean their own document photos and authorized watermarks with an understandable selection, preview, and apply workflow. Preserve legible text, page geometry, original files, existing marks, and signatures. The user has requested Plan 4 development, prefers native execution one task at a time, English specifications, and good usability. The existing September 2 specification supplies the scope. Paid external image repair remains optional and requires a separately configured provider.

## Approach

Use the existing .NET worker and its pinned OpenCV dependency for deterministic repairs, then publish an immutable page revision. This reuses private storage, processing jobs, ownership checks, previews, and PDF snapshots.

Alternatives considered: browser-only repair offers immediate feedback but duplicates canonical rendering and makes export consistency harder; external image generation for every edit adds cost and can alter text. Local canonical repair with an optional crop-only provider is the selected approach.

## User workflow

1. Expose a visible **Clean up** action beside Crop & filters and Edit text.
2. Open a large dedicated page workspace with zoom, pan, Before / After, and a sticky toolbar.
3. Offer **Paper background** for shadows and uneven paper colour, and **Remove a selected mark** for localized cleanup. Reuse Crop for desk/background outside the paper; do not run unsolicited edge detection.
4. Select areas with a rectangle or brush. Offer brush size, erase-from-selection, undo stroke, clear selection, and a translucent mask. Pan mode must never paint; masks stay aligned under zoom and touch gestures.
5. Suggestions are optional editable masks, never automatically applied removals. A suggestion means a candidate stain or watermark, not a reliable semantic classification.
6. Show a concise ownership/permission confirmation when removing a watermark. Preserve the existing design restriction on security marks, official seals, signatures, and unauthorized third-party ownership marks.
7. **Preview cleanup** starts a bounded background operation with visible status, cancellation of polling when leaving, and a retry action. Users can continue adjusting the draft after a failed preview.
8. **Apply cleanup** activates the exact previewed image after checking that the source revision is still current. Cancel leaves the page unchanged. Expose Undo / Redo next to Apply.

The selection tint, handles, and comparison controls never appear in exports. Explain a failed operation in terms of the actual issue: page changed, processing unavailable, or insufficient recoverable detail. Do not promise to recover original pixels hidden behind opaque content.

## Rendering and quality

Paper-background mode estimates illumination at a scale larger than text strokes and corrects only the selected paper pixels. Protect foreground ink and lines. Local mark removal uses bounded OpenCV inpainting with masks refined to the selected pixels. It must not erase a whole rectangular bounding box merely because the brush's bounds intersect text.

Process at the canonical source resolution, with resource limits checked before decode and allocation. Initial configurable limits: 25 MB encoded input, 40 megapixels decoded input, 1 MB mask request, 10,000 stroke points, one active repair per page, 60-second renderer timeout. Local mark removal is limited to 20% of page area; paper normalization may cover the whole page. Validate all coordinates and numeric inputs on the server. Derive the raster mask server-side from normalized strokes and the authoritative image dimensions.

Composite results into the original decoded image only inside the approved mask. Persist a lossless PNG so an outside-mask decoded-pixel equality check is meaningful; JPEG recompression would invalidate that guarantee. Existing JPEG revisions remain readable. Page dimensions and orientation must remain identical.

Current-revision OCR protects recognized words. A preview intersecting printed words requires valid source OCR and post-repair verification. For reconstructable printed text, reuse the approved font catalogue and deterministic layout, preserve recognized spelling, and visibly preview the reconstruction. If text cannot be verified, retain it or reject that region with actionable guidance. Do not reconstruct handwriting, signatures, or unknown hidden text. OCR confidence is evidence, not proof that every protected object was detected.

Optional complex-background repair receives only a bounded crop and mask. It uses an application-owned interface, explicit user invocation, configured per-operation and daily limits, and the same final masked composite and validation. Provider/model selection and current pricing must be verified before enabling a live adapter. Plan 4's local delivery must function without OpenAI credentials; a disabled optional adapter is reported as deferred, not completed.

## Persistence and concurrency

Introduce PageRepairOperation with owner/page/source-revision references, canonical request hash, idempotency key, private mask reference, mode, renderer version, result key/hash, status, safe failure code, and timestamps. Jobs use the existing lease/retry queue. A repeated request with the same key and hash returns the same operation; a different hash conflicts.

Extend PageRevision with an optional repair-operation reference and explicit media type. Keep existing producing-text-edit references intact. Enforce one producing operation per derived revision and unique repair output linkage. Do not manufacture a text edit to represent cleanup.

Unify revision traversal sufficiently to undo and redo mixed text/cleanup history. Redo follows the current accepted branch; a new accepted operation after undo invalidates the old redo path. This needs explicit branch metadata or equivalent persisted ordering shared by both operation types. Existing SwitchPageRevision currently derives redo from text-edit records only and must be changed and regression-tested.

Preview generation stores an owner-private candidate for 24 hours. Apply runs in a transaction, checks ownership and expected active revision, and activates the candidate without rerendering. An expired or stale candidate requires a new preview. Failed writes and expired unused previews are cleaned up with the existing object-write/retirement patterns; referenced revision assets must never be purged by preview cleanup.

Marks and signatures remain separate overlays and are included by the existing export snapshot mechanism. Never flatten them into cleanup inputs. Applying cleanup invalidates old OCR eligibility by source revision, and exports must not reuse stale searchable text. Users can recognize the new revision explicitly; no automatic paid OCR for unrelated page opens.

## API and UI boundaries

Use owner-scoped endpoints under /api/documents/{documentId}/pages/{pageId}/repairs for creating previews, reading operation status, reading private result images, and applying candidates. Require existing Firebase authentication/App Check policy. Return 404 for unowned resources, 409 for stale revisions or idempotency conflicts, 422 for invalid masks or unsupported repairs, and 503 when disabled. Do not accept arbitrary storage keys or remote image URLs from clients.

Keep cleanup tools, mask geometry, API service, and renderer separate from the already large text editor component. Route back to the same document after completion. Private images and status use no-store responses; logs contain IDs and bounded error codes, never document content or signed URLs.

## Delivery sequence

1. Repair operation, revision/media-type extension, migration, and mixed-history contract.
2. Deterministic mask rasterization, paper cleanup, inpainting, and pixel-containment verification.
3. Worker jobs, private preview lifecycle, idempotency, and transactional apply API.
4. Full-page cleanup workspace with rectangle/brush/erase, zoom/pan, progress, comparison, and retry.
5. Printed-text protection/reconstruction and optional editable region suggestions.
6. Mixed edit undo/redo, searchable-PDF invalidation, immutable export snapshots, and asset cleanup.
7. Whole-flow verification, operating instructions, and local user acceptance.

Optional live image-provider work follows a provider/cost decision; it must be listed separately from completed local tasks.

## Acceptance evidence

- Synthetic fixtures include clean paper, shadows, stains, ruled forms, diagonal translucent marks, nearby printed words, and complex regions that must be declined.
- Assert identical decoded pixels outside masks and unchanged image dimensions.
- Verify zoomed pointer mapping, erased mask areas, keyboard controls, touch pan, and draft retention after failures.
- Verify ownership isolation, App Check, bounds, idempotency, stale apply, retry recovery, and orphan cleanup.
- Verify text edit -> cleanup -> undo -> redo -> export and the reverse order, including retained signatures and ticks.
- Verify the applied image matches the preview and old OCR never leaks into a new searchable export.
- Run migrations/integration tests with the test database, not user document data. Report unavailable Docker access explicitly rather than calling those checks passed.
- Production remains a separate deployment action. User-facing completion must distinguish local implementation, successful checks, optional-provider deferrals, and production status.
