# Clean Paper, Edge Debris, and A4 Export

Status: Approved; local implementation and verification in progress. Not deployed.

## Intent

Turn document photographs into clean, correctly framed scans without rewriting their contents. Extend the existing OpenCV Clean document work with optional A4 export, conservative punch-hole suggestions, and manual cleanup for ambiguous debris. Preserve handwriting, deletion strokes, printed rules, signatures, marks, and original assets. Use local processing without paid image APIs. Implement natively in the current checkout, preserving unrelated work.

## Existing architecture and scope

DocumentPdfBuilder currently derives page dimensions from image pixels at 96 DPI and places the image across the full page. Signatures, marks, and searchable text independently use full-page dimensions. A4 therefore requires a shared content placement, not merely a MediaBox change.

Clean document is an existing crop filter with three strengths. It is not an arbitrary-region repair workflow. Reuse the proposed background-watermark-repair design for private preview operations, masks, source fencing, immutable revisions, OCR invalidation, and mixed undo/redo; do not implement a second competing repair pipeline. This delivery excludes watermark removal, generated text, and paid providers from that broader proposal.

## A4 export

- Add Original size and A4 to export options. Original size remains the default and retains current behavior (image-derived dimensions, not inferred physical paper size).
- A4 is 210 by 297 mm, portrait for square/portrait content and landscape for landscape content. Use 5 mm minimum white margins, proportional fit and centering; never stretch, crop, or reflow text.
- Persist the layout choice immutably with each export. Old exports and requests without a choice mean Original size. Retry must reuse the original layout snapshot.
- Define one content rectangle in PDF points. Draw the page raster there. Transform signatures and marks from normalized source coordinates into that rectangle. Project OCR words using content dimensions, then translate into page coordinates, accounting for the bottom-origin text baseline convention.
- Keep cleaned raster dimensions unchanged. A4 is an export layout only, so editor coordinates and existing page revisions remain stable.
- Preview the chosen layout and Ready page count before export. Never imply an excluded Processing page will be exported.

## Automatic punch-hole and debris suggestions

- Operate on the current source raster, before separate signature/mark overlays. Detect bounded, isolated dark components in peripheral paper regions using OpenCV shape, size, contrast and surrounding-paper evidence.
- Do not classify all circles as holes or all edge ink as debris. Reject candidates touching likely text/rules, protected OCR regions, overlays, or ambiguous connected components. Missing reliable protection means retain the component, not assume it is blank paper.
- Present editable candidate masks with individual deselection and Before/After preview. Detection is automatic; publishing destructive changes requires Apply.
- Desk/keyboard outside the sheet is handled through existing crop controls. No fixed coordinates from the sample image, whole-border whitening, or inferred content-frame cropping on arbitrary documents.
- General complex clutter and objects overlapping meaningful content are not guaranteed automatic repairs. Leave uncertain regions unchanged with a concise explanation.

## Manual cleanup and lifecycle

- Provide a dedicated large cleanup view with rectangle/brush selection, erase selection, zoom/pan, selection undo, Clear, Preview, Apply and Cancel. Never hide essential actions below the image.
- Preview is server-rendered and private. Keep the selected draft on failure. Apply publishes exactly the reviewed preview, only if its source revision is still current; stale previews require regeneration.
- Use bounded OpenCV inpainting/paper filling within the approved mask. Keep decoded pixels outside the mask identical and preserve image dimensions; store lossless results.
- Protect readable text and existing rules. Decline unrecoverable overlaps rather than invent hidden content. User selection is not permission to reconstruct handwriting or signatures.
- Maintain original assets, mixed text/cleanup undo and redo, retirement of unused previews, ownership/App Check enforcement, and immutable export snapshots as specified in the existing repair design.
- Applying cleanup makes old OCR ineligible for the new image; recognizing the revised page is explicit. Do not silently reuse searchable text over erased content.

## Alternatives

Blind edge whitening is cheap but deletes marginal notes and borders. Generative repair may invent content and incurs service cost. Conservative local suggestions plus manual correction and preview is the chosen approach.

## Delivery order

1. A4 layout contract, snapshot persistence, shared coordinate transform, export UI and regression verification.
2. Shared repair preview/apply foundations needed by hole cleanup, then hole candidate generation and review.
3. Manual debris selection, protected inpainting, mixed-history integration and complete-flow verification.

Do not mark steps 2 or 3 complete on the basis of a synthetic detection demo. The full preview/apply flow must work. No push, production deployment, or live database changes are implied by this design approval.

## Acceptance

- A4 portrait/landscape dimensions and uniform scaling are verified from generated PDFs. Original-size exports remain unchanged.
- Image, signature, mark and OCR text anchors stay aligned on wide/tall pages and with nonzero margins. Visually render PDFs and test search/select positioning.
- Export retries retain layout and asset snapshots; old serialized records remain readable.
- Hole fixtures include true holes, printed circles, edge notes, stamps and connected form rules. Ambiguous cases remain untouched.
- Cleanup preserves dimensions and all decoded pixels outside masks; undo restores the previous revision and exports reference the accepted revision.
- Ownership, stale previews, expiry, resource limits, failure/retry and zoomed pointer mapping receive coverage.
- Use the supplied photo as a private visual fixture, not a hard-coded algorithm or committed document. Report remaining noise and any potentially lost strokes honestly.
