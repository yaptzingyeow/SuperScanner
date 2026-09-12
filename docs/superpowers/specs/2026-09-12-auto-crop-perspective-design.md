# Auto-crop and Perspective Correction Design

## Goal

Add reliable document-edge detection, perspective flattening, and manual four-corner adjustment to the existing upload-to-preview workflow. The immutable uploaded original remains unchanged. This first delivery supports JPEG and PNG photographs; PDF and HEIC manual corner editing follow later.

## User flow

After upload validation, the Worker renders the source image and attempts to detect the largest document-shaped quadrilateral. When confidence is insufficient, the full image bounds become the initial corners. The document view displays the source image with four draggable corner handles and actions for **Auto detect**, **Reset**, and **Apply crop**.

Applying a crop sends normalized corner coordinates to the authenticated API. The API validates ownership, coordinate bounds, convex ordering, and minimum crop area, saves a new crop revision, marks the document `Processing`, and enqueues an idempotent perspective-correction job. The UI polls until the page becomes `Ready` and then loads the new preview.

## Data model

Each page stores the current crop revision and four normalized points in top-left, top-right, bottom-right, bottom-left order. It also stores detection confidence and whether the corners came from automatic detection or manual adjustment. Normalized coordinates range from 0 to 1 so they remain stable across preview sizes.

Generated preview and thumbnail object keys include the crop revision. Previous generated assets are retained initially so a processing failure never removes the last usable preview. Cleanup is deferred to a later retention task.

## Processing

The Worker uses a server-side OpenCV-compatible implementation for contour detection and homography. Detection converts the image to grayscale, reduces noise, finds edges, searches external contours, approximates polygons, and scores convex four-corner candidates by area and rectangularity. The highest valid candidate above the confidence threshold is selected; otherwise the full image bounds are returned.

Perspective correction orders and validates the four points, calculates output dimensions from opposing edges, applies a homography, and writes a flattened JPEG preview and thumbnail to private R2. Processing enforces the existing 25 MB input limit plus pixel-dimension, memory, execution-time, and output-size limits.

## Components

- Domain: crop geometry, revision, source, and page lifecycle methods.
- Persistence: PostgreSQL columns and EF migration for page crop metadata.
- Application/API: owner-authorized crop retrieval, auto-detection result, and manual crop submission.
- Worker: `DetectDocumentEdges` and `ApplyPerspectiveCrop` job handlers with existing bounded retry behavior.
- Angular: corner editor overlay with pointer dragging, keyboard-accessible coordinate controls, reset/apply actions, progress, and safe failure messages.

## Status and errors

Pages needing confirmation report `NeedsCrop`. Applying corners moves the document to `Processing`; successful perspective output moves it to `Ready`. Detection failure is not fatal because full-image corners remain editable. Invalid manual geometry returns a validation error without queuing work. Processing failure preserves the previous preview and exposes retry.

## Security

All crop endpoints require Firebase authentication and verify document ownership. The API accepts only four bounded numeric points and rejects non-finite values, self-intersecting shapes, incorrect ordering, and crops below the minimum area. Image decoders receive validated JPEG or PNG originals only. Generated objects remain private and are streamed through owner-authorized endpoints.

## Verification handoff

Automated tests are deferred to the user's GPT-5.6 Sol Light task. This implementation session will use backend and frontend compilation plus a manual JPEG workflow: upload, automatic corners, drag a corner, apply, perspective output, preview refresh, and final `Ready` status.

