# OpenCV Shadow Filter Implementation Plan

**Goal:** Deliver a usable local OpenCV shadow-removal trial through Crop & filters before expanding Plan 4 to masked repair.

**Architecture:** A standalone Python illumination-normalization module is called by the existing bounded crop worker. The existing filter contract carries RemoveShadows through API, domain, worker, thumbnail, and export. No external AI service is called.

**Spec:** ../specs/2026-09-28-background-watermark-repair-design.md

**Execution:** Native in the existing checkout, as requested. User selected OpenCV on 2026-09-28. This is the first vertical delivery of Plan 4, not completion of its seven tasks. Localized masks, repair revisions, mixed undo/redo, and printed-text reconstruction remain outstanding.

## Constraints and decisions

- Use the existing crop source and geometry; no automatic edge detection, new database fields, or external providers.
- Honor existing crop limits (2000 px maximum dimension, 4 MP decoded, 25 MB encoded); the future dedicated repair workspace has separate larger limits.
- Normalize estimated illumination with bounded gain. Preserve colour ratios and foreground strokes; do not synthesize missing text.
- A CSS brightness filter is not a shadow-removal preview. Label the source as unprocessed and show the real saved result after worker completion.
- Existing edited pages remain protected by the crop endpoint's conflict check. Users must try this filter before text editing until the dedicated revision workflow is implemented.
- Keep unrelated .gitignore and .angular changes untouched.

## Review focus

- Faint strokes on shaded paper remain distinguishable (renderer fixture).
- Thin coloured lines and uniform paper are not erased or over-whitened (renderer fixture).
- Tiny images and malformed array inputs fail safely or remain stable (renderer tests).
- Rapid filter switching and stale asynchronous image responses cannot show the wrong result (Angular test).
- A save failure retains the user's selection; a successful save displays actual output (Angular test).

## Task 1: Illumination engine

- [ ] Add processing/test_shadow_cleanup.py with synthetic gradient-paper, dark text, coloured rule, flat-paper, and invalid-input tests. Run unittest and observe missing-module failure.
- [ ] Add processing/shadow_cleanup.py exposing remove_shadows(image: np.ndarray) -> np.ndarray. Estimate a reduced-resolution luminance background using morphological closing and Gaussian smoothing, calculate capped gain, and blend corrections away from dark foreground.
- [ ] Call it for RemoveShadows in crop_image.apply_filter. Add crop subprocess coverage that writes a real JPEG and thumbnail and verifies measurable shadow reduction.
- [ ] Run Python cleanup tests and existing crop tests; inspect an image comparison saved outside source control.

## Task 2: API/domain and usable filter UI

- [ ] Add a domain acceptance test and Angular filter/save-result tests; run and observe failures.
- [ ] Extend ScanFilter and CropEndpoints validation copy with RemoveShadows.
- [ ] Add the filter, accurate explanatory copy, actual-result display, and an original/result toggle in CropEditorComponent. Preserve existing behaviour for other filters.
- [ ] Run domain and Angular tests and API/worker/web builds.

## Task 3: Delivery record

- [ ] Update scan-filters operations documentation and record test evidence and remaining Plan 4 work.
- [ ] Verify changed-file scope and whitespace; report how to try the feature and whether running services were refreshed.

## Progress

Implementation and focused verification completed. No production deployment or external API charges are part of this trial.

Evidence: 6 shadow Python tests, 9 existing crop Python tests, 1 domain test, and 8 Angular crop tests passed. API and Worker builds passed with zero warnings/errors; Angular development build passed. Local original.jpg comparison inspected: lower-page shadow reduced, text and rules visible, folds/show-through remain. Git whitespace check passed. Services were not restarted; no live end-to-end claim is made. The dedicated masked repair workflow remains future work.
