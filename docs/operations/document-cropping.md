# Document cropping

JPEG and PNG uploads now generate an upright source preview, run document-edge detection,
and enter `NeedsCrop`. Open a document and choose **Adjust corners & flatten**. Move the
four handles (or use arrow keys / percentage inputs), then choose **Apply crop**. Auto detect
repeats detection; Reset selects the full image. Applying saves a perspective-corrected
preview and thumbnail without modifying the original upload.

Apply the `PageCrops` EF migration before restarting API and Worker. The manual alternative,
`scripts/database/Apply-PageCrops.sql`, also queues existing accepted JPEG/PNG previews.
Run it as the database owner with the old Worker stopped.

The Worker needs Python with the pinned NumPy, OpenCV, and ONNX Runtime packages in
`processing/requirements.txt`. Locally, `.task-tools/crop-runtime` contains the runtime; the
local Worker launch script sets `Crop__PythonPath` to its Python executable. Publish validates
that every non-secret detector module and its metadata are present. The model binary is
provisioned separately and checksum-verified; it is never committed or baked into the normal
publish output. No paid API is used for detection or perspective correction.

Source and output previews are limited to 2000 pixels on the longest side. This is planar
perspective correction, not curved book-page dewarping. PDF and HEIC crop editing are not
included in this first implementation. Low-confidence detection falls back to full-image
corners for manual adjustment. Original and previous preview objects remain preserved.

OpenCV fallback accepts only quadrilaterals covering at least 35% of the image with at least
55% horizontal and vertical span. The AI path segments the outer paper surface, closes small
fold gaps, rejects disconnected noise and near-full masks, then fits four supporting lines.
Confidence selects AI, OpenCV fallback, or the existing manual editor. Crops shorter than 1400 pixels on their longest side are
upscaled by at most 2x, then receive restrained local-contrast correction and sharpening.
JPEG previews use quality 94; this improves display clarity but cannot recreate detail that
is absent from the uploaded photo.

All crop endpoints require document ownership and existing authentication/App Check controls.
Jobs use crop revisions so stale results cannot replace a newer selection. A subprocess
deadline and size limits bound image processing; failures use the existing job retry system.

See [AI document boundary operations](ai-document-boundary.md) for configuration, model
promotion, Railway monitoring, and rollback.

## Local verification, 2026-09-12

- Angular, API and Worker builds succeeded.
- Database migration applied and services restarted.
- Three existing eligible images completed detection and reached `NeedsCrop`.
- The browser's current guest session showed no documents and could not access the existing
  document URL. End-to-end handle adjustment and Apply remain unverified in the UI.
- Automated tests were not run, as requested. No deployment or GitHub push was performed.
