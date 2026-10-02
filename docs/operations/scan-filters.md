# Scan filters

Open a JPEG/PNG document, choose **Crop & filters**, select a filter, then **Save scan**.
The corner-selection image stays unfiltered; the saved preview and thumbnail show the result.
Changing only the filter retains the current corners. Original keeps the perspective crop
and original colors without the Document enhancement. It does not restore uncropped geometry.

Available filters:
- Original: no color or contrast enhancement.
- Document (default): the existing mild local contrast and sharpening.
- Bright: gamma correction that lifts darker tones while preserving color.
- Remove shadows: local OpenCV illumination normalization; reduces uneven paper lighting without an external AI API. Save scan to see the actual result, then toggle Original photo / Processed scan. This does not reconstruct missing text, remove all folds, or remove watermarks.
- Clean document: grayscale paper-background normalization with Gentle, Balanced (default), and Strong cleanup. Save to see actual output and compare with the original. Strong can remove faint handwriting; no character regeneration is performed. The original crop source is retained. Existing Auto detect and manual corner controls still determine the crop; no sample-specific coordinates are embedded. Hole removal is a separate, explicitly reviewed **Clean page** workflow; A4 is a separate PDF export layout.
- Clean content: optional content-focused A4-shaped preview. A complete printed rectangular frame is located from its four long rules; only then is the framed area cleaned and fitted onto a white A4 canvas. If a reliable frame is missing, the full cropped page is cleaned and fitted instead, so content is not silently discarded. Faint ink may fade. Compare with the preserved original before export, and choose A4 at export for the exact physical paper size. No OCR text is rewritten, and there are no sample-specific crop coordinates.

2026-09-30 local acceptance: the preserved `original.jpg` sample passed the actual crop CLI's detect → apply(ContentClean) path. The rendered preview retains the full agreement through its last address line and excludes the keyboard and punch holes. A slightly slanted printed frame and a missing top rule have regression tests. Python processing: 64/64 tests; Angular: 196/196; A4/PDF builder tests: 45/45. Browser end-to-end selection/export remains unverified because the isolated headless browser could not reach Firebase (`auth/network-request-failed`); API and Worker health checks returned 200. Do not treat the CLI preview alone as live browser acceptance.
- Grayscale: luminance conversion without thresholding.
- Black & White: adaptive thresholding for uneven lighting; faint handwriting may disappear.

Every render starts from the preserved upright crop source. Filters do not stack.
Filter is the requested setting; AppliedFilter identifies the last published preview.
The existing crop revision and guarded publication keep older jobs from overwriting newer
filter choices. Unknown filter names are rejected by API, Domain, and Worker.

## Clean page and A4 export (local implementation, not deployed)

After a page is Ready, open **Clean page** from its document card. **Find punch holes** offers conservative candidates; remove any incorrect suggestion. Use Rectangle or Brush for a small manual area, or Erase selection to remove a chosen mask. Zoom and Pan help place the mask. Undo selection and Clear all affect only the draft. Preview cleanup creates a private, lossless PNG; compare it with the original and apply only if handwriting, rules and nearby marks are intact. Cancel returns to the document without applying. The source image and old revision remain available for page undo/redo. Old OCR is not reused on a repaired image; run text recognition again when needed.

PDF export offers **Original size** (the existing default) and **A4**. A4 preserves the image aspect ratio, centers the content, adds at least 5 mm white margins, and snapshots the layout per export. It does not automatically recreate a clean document or remove folds, shadows, show-through, notes, or all edge debris.

Before running this local code, apply EF migration `20260928095755_PageRepairOperations` to the intended database, then restart the API and Worker and rebuild/serve Angular. Do not apply the migration to Railway or production as part of local testing. Repair previews are private and unused previews are retired after 24 hours by the Worker. The latest implementation is still awaiting a full browser-to-worker acceptance pass.

## Installation / handoff

### OpenCV shadow trial (2026-09-28)

No additional migration or paid API is required for RemoveShadows. Restart the updated
API and Worker and serve the updated Angular app. Use this filter before text editing;
the existing protection against cropping a text-edited page remains in place.
Original/result comparison uses the uncropped source and cropped output, so geometry
can differ. The saved output is used by the normal PDF export flow.

Verified: 6 shadow-renderer tests, 9 existing crop Python tests, 1 domain acceptance
test, and 8 Angular crop tests passed. API/Worker builds and Angular development build
passed. A local comparison using the supplied original.jpg showed reduced lower-page
shading with visible text and rules retained; folds and show-through remain. This is
not a full browser-to-worker end-to-end verification. Local services were not restarted.
Masked repair and watermark cleanup are not part of this first delivery.

Apply EF migration `20260913000000_PageFilters` before restarting the updated API and Worker.
Alternatively, run `scripts/database/Apply-PageFilters.sql` as the database owner.
Existing rows default to Document to match the previous enhancement behavior. No images are
automatically reprocessed by the migration. Save scan generates the selected filter.

## Verification performed (2026-09-13)

- Applied migration `20260913000000_PageFilters` to the local PostgreSQL database.
- Restarted the local API and Worker after applying the migration.
- Angular development build passed.
- API and Worker .NET builds passed with zero warnings and zero errors.
- Python compilation passed.
- A focused Python smoke test passed for Original, Document, Bright, Grayscale, and
  BlackAndWhite.
- Browser end-to-end check passed: Grayscale was selected and saved, the page returned to
  Ready, and the persisted row reported Filter/AppliedFilter `Grayscale` with matching crop
  revision 6.
- Domain tests passed: 5/5.

The complete automated suite is not green yet:

- Application tests: 33 passed and 1 failed. The failing upload-validation runner test
  expects a page identifier, but the current result contains null.
- Angular tests: 27 passed and 8 failed. These tests still assert the previous protected,
  login-first navigation and older authentication method names; the product now uses a
  public scanner-first layout with optional sign-in.
- API and Infrastructure integration tests could not complete because Testcontainers could
  not connect to `npipe://./pipe/docker_engine`. No Docker executable or running Docker engine
  was found on this machine during this verification.
# Faithful photo scanning default (2026-09-29)

2026-09-30 OpenCV follow-up: a printed/internal horizontal line was overriding an
already-correct contour top edge, cutting away the upper-left corner of the
appointment photo. That override was removed. When a contour instead runs to
the image's bottom border, a lower-edge correction now requires both a long
outer-edge line and a sustained paper/background brightness transition; a
printed rule alone is rejected. Private previews of `original.jpg` and
`IMG_9685.jpeg` were visually checked. The appointment photo's paper bottom
now excludes the desk, while the original keyboard photo still has a small
keyboard fringe along the irregular folded top edge. Automatic detection is
therefore improved, but not yet claimed to match a dedicated mobile scanner
on all photos. The original and manual corner adjustment remain available.

Actual-photo follow-up: automatic OpenCV boundaries receive a 0.2%-short-side parallel inward offset (manual corners remain untouched). Reverse-polarity refinement is used only without adequate positive-polarity support. Python 56/56 and Worker build pass. Local CLI output from the user's original (1).jpg was visually inspected: perspective correction executes, but a narrow keyboard strip remains at the top and folds remain visible. Therefore real-photo acceptance is FAILED, not complete. The private output is .task-tools/faithful-scan-check.jpg; no private fixture was added to source control. Further paper-boundary localization work is required; larger blind insets are not a valid fix.

Boundary follow-up: edge refinement now supports both bright-paper/dark-desk and shaded-paper/bright-desk transitions, requiring agreement across two sampling distances to reject thin printed rules. A synthetic bottom-edge regression failed by 25 pixels before the fix and passes afterward. Python processing suite: 54/54. This does not establish accuracy on the appointment-photo sample currently in the browser; retrieving that original through browser media download timed out. Curved-page/fold restoration and end-to-end visual PDF acceptance are still incomplete.

Follow-up: mild color-cast compensation now estimates one bounded white balance from bright low-chroma paper after illumination correction. It does not desaturate individual pixels or erase marks. Uniform images retain the existing no-op behavior; strongly colored backgrounds do not qualify as paper samples. Synthetic blue-cast, faint-pencil and colored-rule coverage passes; Python suite 53/53. This heuristic cannot reliably distinguish tinted paper from lighting and does not address the observed bottom-edge crop error. Real-photo acceptance remains pending.

New photo imports use automatic boundary detection and perspective correction followed by the existing `RemoveShadows` renderer, displayed as **Faithful scan**. The automatic detection completion also selects this filter, so a queued photo cannot silently revert to black-and-white cleanup. Existing edited pages and PDF imports are not bulk reprocessed.

This mode retains raster content and color rather than reconstructing text with OCR. Smart clean remains an explicit stronger option that may suppress faint ink. The current illumination correction does not guarantee removal of folds or complex shadows, and does not implement learned segmentation, curved-page dewarping, or full color-cast correction. A4 export retains its existing aspect-preserving placement.

Verification: .NET solution build passed; Application tests 432 passed, one existing external test skipped; Angular 194 passed; Python processing 52 passed. Real-photo visual acceptance remains pending.

# Magic scan default (2026-10-01)

New photo imports now use **Magic scan** (`Magic`), replacing Faithful scan as the automatic
default. Detection (`processing/magic_scan.py`) builds candidate quadrilaterals from line
segments and contours and scores each side by edge evidence along it plus a colour step across
it, not by area: small cards (tested down to ~13% of the photo) are found, and a printed frame
inside a sheet loses to the sheet's outer edge. A side may run along the photo border when the
paper leaves the frame. If no candidate qualifies, the previous region-based detector is the
fallback. The corner of a folded flap is now the intersection of the straight paper edges, as in
dedicated scanner apps; the Magic filter whitens the small desk triangle this leaves.

Applying Magic flattens with the estimated physical aspect ratio (perspective geometry, snapping
to A4/Letter within 2.5%) at 2400 px on the long side, then: text pages get illumination
normalisation, white paper, dark anti-aliased ink with coloured ink kept, and removal of desk
slivers on the border, faint grey margin debris and round punch holes; colourful pages get white
balance, shadow lift, contrast and mild saturation. Dark margin content such as page numbers is
kept. No text is generated and no external API is used. Other filters keep their previous warp
and sizing. No migration is needed; existing pages are not reprocessed.

Verification: Python processing 73/73 (9 new Magic tests), Angular 197/197, Domain 112/112,
Application 431 passed with 1 opt-in skip and 2 Poppler tests unable to find `pdfinfo` on this
shell's PATH. Three user photos (voucher on wood, tenancy agreement on keyboard, appointment
letter on marble) were checked visually; detection takes about 1-2 s per photo. Local services
must be restarted with the secure launch scripts. Not deployed.

Follow-up (2026-10-01, later): detection was hardened against five user photos (voucher on
wood grain, tenancy sheet on a keyboard, appointment letter on marble, a twice-folded thermal
receipt, a dark card in a plastic sleeve). Changes are general rules, each with a synthetic
regression test: a quad borrowing a photo border must cover at least 15% of the photo and not
be a thin strip; a larger enclosing quad replaces the best candidate only when every side has
at least 90% edge support; adjacent plain-paper candidates that share a crease are merged into
one folded sheet (its corners are kept as-is because the sides bend at folds); near-duplicate
line quads are removed before the 150-candidate budget is applied; a round margin blob is
treated as a punch hole only when it is solid (hollow rings such as a printed 0 are kept).
Colour-mode lighting is estimated at 1/8 resolution (card enhancement 7.4 s to 0.6 s).

Stability check: each photo resized to 960/1200/1500 px and re-encoded at JPEG quality
95/85/75 (45 runs); all corners agree within 2% except one receipt run (2.1%) and one card run
(3.0%). Python processing 77/77. Known gaps: aspect estimation is sensitive to 1 px corner
changes on mildly tilted cards (voucher 2.03 vs 2.10); a sleeve/desk sliver can remain at a
card edge; a faint staple mark remains on the tenancy sheet.

## Import and workspace redesign (2026-10-01)

Flow: New scan -> Import pages (review each upload, choose a look per page or "Apply this look to
all pages", rotate left/right, Adjust corners, then Confirm or "Upload originals without changes")
-> document workspace with View, Edit and Export tabs. Export offers PDF (Original size or A4,
optional searchable text), Print (builds or reuses a Ready PDF and opens the browser print dialog
from a hidden iframe) and Original file (downloads the selected page's uploaded original). The
Images export kind is not included yet.

Database: the import review stores a per-page rotation (0, 90, 180 or 270 degrees clockwise) in the
new `PageRotation` migration (`20261001040649_PageRotation`). It must be applied together with the
still-pending `PageRepairOperations` migration using `scripts/database/Apply-DatabaseMigrations.ps1`
before the API and Worker are restarted.
