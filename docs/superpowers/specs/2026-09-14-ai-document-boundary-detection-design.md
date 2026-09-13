# AI Document Boundary Detection Design

**Date:** 2026-09-14  
**Status:** Proposed for implementation  
**Scope:** Phase 1 web uploads, designed for Phase 2 mobile reuse

## Purpose

SuperScanner must identify the physical outer boundary of a photographed paper document, estimate four perspective-aware corners, and let the user correct those corners before applying a full-resolution perspective transform. The detector must handle folds, shadows, clutter, weak edges, and printed lines that can be mistaken for paper edges.

Phase 1 runs detection in the existing server Worker after upload validation. Phase 2 will reuse the selected ONNX model on the phone for live camera guidance and automatic capture. Google Cloud Vision remains an OCR service after cropping; its Crop Hints output is not used for physical paper-boundary detection.

## Success Criteria

- On an initial evaluation set of at least 60 representative photographs, at least 90% of pages have all four automatically suggested corners within 2% of the image width and height of the manually confirmed corners.
- Printed borders, rules, and text inside a page are not selected as the paper boundary.
- Folded or damaged corners and soft shadows do not cause a confident internal crop.
- A low-confidence result opens the existing manual corner editor without claiming that the result is accurate.
- Detection uses a reduced-resolution copy, while perspective correction reads the original-resolution image.
- Original uploads are immutable.
- The selected model has a license suitable for commercial distribution and a practical path to Android and iOS inference.

The 2% target is an evaluation threshold, not a guarantee for every possible photograph. No result with a corner error above 5% may be classified as high confidence in the release evaluation set. Results outside the threshold must be surfaced through confidence handling and manual adjustment.

## Architecture

The existing upload, validation, storage, job, and crop endpoints remain the system boundary. A new document-boundary detector is introduced behind a stable Worker-side interface.

```text
Validated upload
    -> upright crop-source image
    -> ONNX segmentation detector
    -> document probability mask
    -> OpenCV geometry refinement
    -> four normalized corners + confidence + diagnostics
    -> existing crop state and manual editor
    -> confirmed full-resolution perspective correction
```

The detector has four isolated responsibilities:

1. **Model inference:** Produce a document probability mask from a normalized image tensor.
2. **Mask refinement:** Remove noise, fill small gaps, retain plausible document regions, and preserve outer folds.
3. **Geometry estimation:** Fit four supporting boundary lines and calculate ordered corner intersections.
4. **Decision policy:** Score the result and choose AI, OpenCV fallback, or manual adjustment.

The API and Angular application consume only normalized corners, confidence, source, status, and a safe user-facing message. They do not depend on a specific ML runtime or model architecture.

## Model Selection

No model is promoted directly into production. A small evaluation harness will compare commercially usable, lightweight segmentation candidates exported to ONNX. Candidate selection considers:

- Corner error against manually labeled ground truth
- Failure rate on folds, shadows, clutter, and low contrast
- CPU latency and peak memory in the Railway Worker
- Model size and future mobile latency
- ONNX operator compatibility across server and mobile runtimes
- License and redistribution terms

The first implementation milestone is therefore a model benchmark, not a permanent model commitment. The winning model and its version are recorded in configuration and detection diagnostics. Model binaries are versioned independently from application source when their size makes normal Git storage unsuitable; their checksum is pinned in deployment configuration.

## Input Processing

- Read the Worker-generated upright crop-source image only after upload validation succeeds.
- Honor EXIF orientation before inference.
- Preserve the original aspect ratio and letterbox rather than stretching into the model input.
- Normalize color and pixel values exactly as required by the chosen model.
- Cap inference dimensions and memory use to protect the Worker.
- Retain the scale and padding transform so mask coordinates map precisely to the source image.

## Mask and Geometry Refinement

The model output is a probability mask, not a crop rectangle. Refinement performs the following:

- Apply a configurable probability threshold.
- Close small gaps caused by shadows, folds, holes, or printed content.
- Remove small disconnected regions.
- Score plausible document components using area, centrality, boundary support, and quadrilateral plausibility.
- Extract the outer contour of the best component.
- Fit four robust supporting lines to the contour rather than relying on four exact contour vertices.
- Intersect adjacent lines to obtain top-left, top-right, bottom-right, and bottom-left.
- Refine strong edge locations locally without allowing internal printed lines to replace the outer boundary.
- Map corners back through the letterbox transform and normalize them to the range `[0, 1]`.

Corner ordering and geometry validation continue to use domain-level rules. Invalid, self-intersecting, extremely small, or implausibly skewed quadrilaterals are rejected.

## Confidence and Fallback Policy

Confidence combines model probability and geometric evidence. It includes:

- Mean boundary probability
- Mask area and connectedness
- Support for each fitted side
- Quadrilateral convexity and coverage
- Corner visibility and distance from image limits
- Agreement between AI and classical OpenCV estimates

Thresholds are configuration values, not hard-coded product policy.

- **High confidence:** Return AI corners as the suggested crop.
- **Medium confidence:** Return suggested corners but explicitly ask the user to verify them.
- **Low confidence or inference failure:** Run the existing OpenCV detector.
- **Fallback also unreliable:** Use a conservative full-image suggestion and require manual adjustment.

No fallback may silently overwrite a manually saved crop. Each request remains revision-controlled using the existing crop revision mechanism.

## Interfaces and Persistence

The Worker detector returns a provider-neutral result:

```text
DocumentBoundaryResult
  Points: four normalized ordered points
  Confidence: 0..1
  Source: Ai | OpenCvFallback | FullImage
  ModelVersion: optional version identifier
  DiagnosticsCode: safe machine-readable outcome
```

The existing crop endpoint remains asynchronous. The page moves through `Detecting` and then either `Ready` or the existing failure state. Persistence records the result source and model version so accuracy regressions can be traced without storing raw model tensors.

Raw probability masks are disabled by default in production. They may be retained temporarily in a protected development environment for evaluation, with explicit expiration and no public URL.

## Web User Experience

After a user uploads a photo:

1. Existing validation completes.
2. Boundary detection begins automatically.
3. The crop editor displays the image and suggested quadrilateral.
4. The message distinguishes high-confidence automatic detection from a result that needs review.
5. The user may drag any corner or enter precise values.
6. Applying the crop creates a derived preview from the original-resolution source.

The UI must not describe an uploaded laptop photo as a live camera capture. It should provide the same automatic detection behavior while using upload-appropriate wording.

## Phase 2 Mobile Reuse

The mobile app uses the same model family and preprocessing contract, but does not call the Railway Worker for every camera frame. It performs on-device inference on sampled preview frames and adds:

- Temporal smoothing to prevent corner jitter
- Stability tracking across consecutive frames
- Blur, glare, coverage, and perspective guidance
- Automatic capture only after a stable high-confidence interval
- Full-resolution confirmation and manual corner adjustment after capture

Server and mobile implementations share golden input images, expected masks or corners, normalization rules, model checksum, and acceptance thresholds where hardware differences allow.

## Security and Privacy

- Detection runs in SuperScanner infrastructure for Phase 1 and does not send the image to Google.
- Google OCR is invoked only by the separate OCR stage when enabled.
- Model files are verified by checksum before loading.
- Input dimensions, decode time, inference time, and memory are bounded.
- Worker subprocess isolation and job retry limits remain in force.
- Logs contain document and job identifiers, timing, detector source, confidence, and safe error codes; they do not contain OCR text, image bytes, API keys, or model tensors.
- Development artifacts containing customer images or masks are excluded from Git and removed according to the development retention policy.

## Configuration

Configuration includes:

- Detector mode: `AiPreferred`, `OpenCvOnly`, or `ManualOnly`
- Model path, version, and checksum
- Model input dimensions and normalization profile
- Mask threshold and morphology limits
- High- and medium-confidence thresholds
- Inference timeout and memory limit
- Controlled rollout percentage
- Development-only diagnostic retention

Production fails safely to OpenCV or manual adjustment when the model is unavailable or its checksum is invalid.

## Evaluation and Testing

Create a versioned evaluation manifest containing at least 60 representative images and manually confirmed normalized corners. Images with personal data remain outside Git; the manifest refers to protected local or test storage identifiers. Synthetic non-sensitive fixtures may be committed.

The evaluation set must cover:

- Clean A4 paper on a contrasting surface
- White or pale backgrounds
- Dark desks and patterned backgrounds
- Folded, curled, torn, or hole-punched paper
- Soft and hard shadows crossing an edge
- Internal rectangular borders and long printed rules
- Partially clipped pages
- Rotation and strong perspective
- Receipts, forms, slides, and whiteboards

Metrics include per-corner normalized error, complete-page pass rate, false-confidence rate, median and p95 latency, peak memory, and fallback rate. A model cannot ship merely because its average error is low; confident severe misses are release blockers.

Unit tests cover preprocessing transforms, mask cleanup, corner ordering, line intersections, confidence policy, configuration validation, and fallback behavior. Worker integration tests use deterministic model outputs. End-to-end tests confirm asynchronous status changes and manual override preservation.

## Rollout and Observability

1. Run candidate models offline against the evaluation set.
2. Select and pin the winning model.
3. Deploy with `OpenCvOnly` as the default and AI enabled in development.
4. Compare AI suggestions with manual corrections and existing OpenCV results.
5. Enable `AiPreferred` for a small controlled percentage.
6. Expand only when accuracy, latency, memory, and failure-rate thresholds remain acceptable.

Operational metrics record detector source, confidence distribution, latency, timeout rate, fallback rate, and normalized distance between automatic and final manual corners. Metrics must not expose document content.

## Cost

Boundary detection introduces no per-scan third-party API charge. It adds CPU and memory consumption to the existing Railway Worker. The benchmark will measure actual resource use before choosing model input size or concurrency. Google Vision charges remain attributable to the later OCR stage, not boundary detection.

## Error Handling

- Missing or invalid model: log a safe configuration error and use OpenCV fallback.
- Unsupported ONNX operation: mark the model unhealthy and stop repeated inference attempts until the Worker is restarted or configuration changes.
- Timeout or memory guard: terminate inference, release temporary files, and use fallback.
- Invalid mask or geometry: reject the AI result and use fallback.
- Concurrent crop change: preserve the existing optimistic revision conflict behavior.
- Storage failure: retain the last valid crop state and expose a retryable processing error.

## Non-Goals

- OCR, searchable PDF generation, or text editing
- Handwriting imitation or font reconstruction
- Watermark removal
- Live mobile camera capture in Phase 1
- Replacing the manual corner editor
- Sending every camera frame or upload to a paid vision API

## Delivery Boundary

This design is complete when Phase 1 can evaluate, select, configure, and run a versioned ONNX document-segmentation model in the Worker; refine its mask into four corners; fall back safely; expose the result through the existing crop workflow; and measure accuracy against the agreed dataset. Phase 2 camera capture is a separate implementation plan that consumes the shared model contract.
