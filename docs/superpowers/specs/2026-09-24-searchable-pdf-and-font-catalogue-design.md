# Searchable PDF and Expanded Font Catalogue Design

**Date:** 2026-09-24  
**Status:** Approved conversational design; written specification pending user review  
**Scope:** Phase 3 searchable PDF export and expansion of the printed-text font catalogue

## Purpose

ArksScanner will export PDFs that retain the exact visual appearance of the processed page image while allowing recognized text to be searched, selected, and copied. The export reuses stored OCR results and never invokes Google Document AI during PDF generation.

The printed-text editor will also expand from two bundled font families to twenty licensed families. The expanded catalogue improves automatic matching and gives users practical manual alternatives without making PDF export depend on the selected visible-editing font.

## Goals

- Add a standards-compatible invisible word-level text layer to eligible exported pages.
- Bind every text layer to the exact page source captured by the immutable export snapshot.
- Preserve image-only export for pages without matching Ready OCR.
- Keep PDF generation deterministic, asynchronous, private, and resilient.
- Keep editor selections, SVG highlights, handles, and other UI state out of exported files.
- Expand the visible printed-text editor to twenty open, redistributable font families.
- Load browser fonts on demand instead of downloading the full catalogue at startup.
- Preserve existing export jobs and snapshots through a backwards-compatible rollout.

## Non-Goals

- Running or waiting for OCR during PDF export.
- Making every page searchable when matching OCR is unavailable.
- Reconstructing the visual page from PDF text.
- Exposing a font selector for the invisible text layer.
- Preserving source-PDF vector content.
- Adding PDF annotations, visible highlights, or editor controls.
- Adding arbitrary user-uploaded fonts.
- Guaranteeing an exact match for proprietary fonts such as Arial, Calibri, Cambria, or Times New Roman.
- Adding new OCR languages in this phase. Stored English OCR remains the supported input.

## User Experience

### Export status

Before export, the UI reports the number of included Ready pages and how many currently have matching Ready OCR, for example `4 pages · 3 searchable`. A page without eligible OCR is explicitly described as image-only but does not block export.

A completed export is labelled:

- **Searchable PDF** when every included page has a text layer;
- **Partially searchable** when at least one, but not every, page has a text layer; or
- **Image PDF** when no page has a text layer.

Export does not wait for queued or processing OCR. If OCR later becomes Ready, the user can generate an updated PDF.

Opening the PDF looks the same as the existing image-based export. No highlight is visible by default. A PDF reader may show its own temporary selection colour only when the user searches for or selects text.

### Font selection

The visible printed-text editor groups fonts into Sans Serif, Serif, Monospace, and Handwriting categories. It provides search, recently used fonts, and a preview rendered in the font itself. The three highest-ranked automatic matches appear first, with the full catalogue available on demand.

Only supported weights and styles are selectable. The browser loads the active font and a small candidate set lazily. It does not download the entire catalogue during application startup.

The invisible searchable-PDF layer uses one server-controlled Unicode font. Users do not choose this font because it has no visual effect; the scanned page image remains the visible content.

## Architecture

The feature extends the existing `DocumentExport`, immutable export snapshot, `BuildDocumentPdf` job, and `DocumentPdfBuilder`. It does not introduce a second OCR pipeline or a synchronous export path.

### Export snapshot

For each Ready page, export creation captures:

- page identifier and position;
- processed object key;
- crop and filter revision information already required by export;
- an immutable page-source identity, using the active source object key and fingerprint or equivalent revision identity;
- the identifier of the matching Ready OCR result, when one exists; and
- the searchable eligibility recorded at snapshot time.

The OCR result identifier is nullable. Existing snapshots that do not contain the new fields remain valid and produce image-only PDFs. Snapshot deserialization must therefore remain backwards compatible.

The snapshot is the sole authority for PDF construction. OCR that completes after snapshot creation is not added to that export. Page edits, crop changes, filters, reordering, and later OCR results cannot mutate a queued or completed export.

### Eligibility

An OCR result is eligible only when all of these conditions hold at export creation and construction:

- it belongs to the snapshotted page;
- its state is Ready;
- its source object key and source fingerprint match the snapshotted active source exactly;
- its language is supported;
- its elements pass existing stored-data invariants; and
- the result is the exact identifier recorded in the snapshot.

Failure of any eligibility check degrades that page to image-only. It must never substitute another, older, or newer OCR result.

### PDF construction

For every snapshot entry, the worker:

1. Loads and validates the exact processed image.
2. Adds a PDF page with the existing dimensions and draws the image unchanged.
3. Resolves the snapshotted OCR result, if present.
4. Filters to non-empty `Word` elements with valid four-point normalized polygons.
5. Orders words by the stored OCR reading order and hierarchy.
6. Maps normalized image coordinates to PDF points, accounting for the PDF coordinate origin.
7. Estimates font size from polygon height, derives baseline angle from the polygon, and applies bounded horizontal scaling so selectable text covers the recognized word width.
8. Writes each word using PDF invisible-text rendering mode.
9. Skips only invalid individual words while retaining the page and all valid words.

The text layer uses an embedded, pinned Unicode font with deterministic metrics. The initial implementation uses Noto Sans for searchable English text. Its purpose is character coverage and stable extraction, not visual font matching.

The builder retains existing limits for pages, pixels, source bytes, output bytes, cancellation, immutable object creation, and output validation. New limits bound OCR words per page, characters per word, total text-layer characters, coordinate operations, and embedded-font output growth.

## Text Geometry and Reading Order

Coordinates remain normalized from `0` to `1`. The renderer converts each polygon against the actual image dimensions used to build the page, then maps it to the PDF coordinate system.

The first release supports ordinary horizontal English text and bounded baseline rotation. It preserves the OCR provider's word hierarchy and reading order rather than deriving order from screen position. Text width is adjusted within safe limits to improve selection alignment without changing the underlying image.

Words are omitted when text is empty, coordinates are non-finite, polygons are malformed or outside allowed bounds, dimensions collapse below minimum size, or encoded glyphs cannot be represented safely. Omission of one word never fails the page. Structural corruption, unsafe resource growth, or inability to produce a valid PDF fails the entire export before publication.

## Expanded Font Catalogue

The editor catalogue will contain exactly twenty families in the first expanded release:

### Sans Serif

1. Noto Sans
2. Liberation Sans
3. Carlito
4. Roboto
5. Open Sans
6. Lato
7. Montserrat
8. Source Sans 3
9. Poppins
10. Oswald

### Serif

11. Noto Serif
12. Liberation Serif
13. Caladea
14. Source Serif 4
15. Merriweather
16. Libre Baskerville

### Monospace

17. Noto Sans Mono
18. Liberation Mono

### Handwriting-style display fonts

19. Caveat
20. Dancing Script

Handwriting-style fonts are ordinary licensed typefaces for user-authorized visible text edits. Their presence does not enable imitation of a real person's unique handwriting and does not change the existing restrictions around signatures or protected documents.

Every enabled face requires:

- a stable catalogue ID and version;
- display and family names;
- supported weight and style metadata;
- pinned web and renderer assets;
- a verified SHA-256 hash;
- redistributable licence metadata and bundled notice; and
- successful server-startup validation.

Regular and bold faces are included when the upstream family provides appropriate licensed assets. The exact face count may therefore exceed forty while the family count remains twenty. Existing Noto entries retain their stable identifiers so stored edits remain reproducible.

The catalogue may grow later without a database redesign. Disabled font versions remain available for reproducing prior edits but cannot be selected for new edits.

## API and UI Contracts

The export API response adds counts or status sufficient for the UI to distinguish searchable, partially searchable, and image-only exports. It does not return OCR text as part of export status.

The font catalogue is exposed through a read-only, versioned, cacheable contract containing safe presentation metadata and web asset references. It never exposes server filesystem paths. The editor continues to submit stable catalogue and version identifiers, not CSS font expressions or filenames.

Recently used fonts are a local user convenience and contain only catalogue identifiers. They are not document content and do not affect server-side style validation.

## Failure Handling

- Missing, queued, processing, failed, stale, or mismatched OCR produces an image-only page.
- Invalid individual words are skipped and counted safely.
- A missing snapshotted OCR row fails closed to image-only rather than selecting a replacement result.
- A corrupt image, invalid PDF structure, exceeded resource limit, failed immutable write, or invalid embedded font fails the export with an existing safe failure boundary.
- A missing or disabled editor font prevents a new edit from using that version; it does not break unrelated scanning or export.
- Browser font-loading failure falls back to a neutral preview and reports that the selected preview is unavailable. The server remains authoritative on apply.
- Partial or corrupt PDF objects are never exposed as Ready.

## Security and Privacy

- Existing document ownership and private object-storage boundaries remain in force.
- The worker resolves OCR only through server-owned snapshot identifiers; clients cannot supply object keys or arbitrary OCR IDs for export.
- OCR text, recognized names, word coordinates, copied search queries, replacement text, and document content are excluded from logs, metrics, traces, analytics, and safe error messages.
- Metrics may include page count, searchable page count, skipped-word count, duration, output size, and bounded safe error codes.
- The text layer contains recognized document text by design and is therefore protected by the same download authorization and retention policy as the visible PDF.
- SVG selection overlays, highlight state, replacement boxes, handles, and draft UI state never enter the export snapshot or PDF builder.
- Font files are repository-controlled or fetched only through a controlled build process, hash-pinned, licence-validated, and never supplied by end users.

## Accessibility

Searchable text improves assistive-technology access, but this phase does not claim full tagged-PDF or PDF/UA conformance. Reading order follows stored OCR hierarchy. A future tagged-PDF phase may add semantic structure separately.

The font picker supports keyboard navigation, labelled groups, text search, visible focus, and screen-reader announcements. Font previews never replace the readable family name.

## Testing Strategy

### Domain and application tests

- Snapshot captures only matching Ready OCR identifiers.
- Snapshot remains immutable after OCR, page, and document changes.
- Legacy snapshots deserialize and export image-only.
- Missing or stale OCR cannot be substituted.
- Searchable-page counts and export classifications are correct.

### Infrastructure tests

- Normalized word polygons map to expected PDF coordinates.
- Word ordering follows stored hierarchy and reading order.
- Rotated and bounded-width words remain extractable.
- Invalid words are skipped without losing valid words or the page.
- The generated PDF contains the original page image plus invisible text.
- PDF text extraction returns expected phrases in order.
- No visible text layer changes rendered-page pixels beyond the established visual tolerance.
- Resource limits, cancellation, retry, immutable storage, and output validation remain effective.
- OCR text is absent from captured logs and error output.

### Font tests

- The manifest contains exactly twenty unique enabled families.
- Every face has an allowed licence, notice, stable version, and matching hash.
- Catalogue IDs remain unique and existing Noto IDs remain stable.
- Supported weights map to real files; invalid weight/file combinations are rejected.
- Browser assets are requested lazily and server renderer assets produce deterministic metrics.

### Angular and end-to-end tests

- Export UI reports page and searchable-page counts correctly.
- Completed exports show searchable, partially searchable, or image-only status.
- A multi-page document exports successfully with mixed OCR availability.
- Searching and copying a known phrase works in the downloaded PDF.
- The rendered PDF remains visually equal to the clean processed page and contains no selection overlay.
- Font search, category grouping, candidate ranking, recent fonts, lazy loading, preview, weight selection, and fallback states work with keyboard and pointer input.

## Rollout and Operations

Searchable export and the expanded catalogue are independently feature-configurable. Production can retain image-only export if the searchable-layer flag is disabled. A disabled searchable layer must not change the existing PDF path except for backwards-compatible snapshot parsing.

Rollout proceeds through local fixtures, Docker integration, sanitized end-to-end verification, and a production promotion gate. Operations documentation covers feature disablement, font-asset validation failures, export diagnosis, safe metrics, and rollback.

No new external service account is required. Searchable export reuses stored Google Document AI results, and font matching uses local licensed assets.

## Success Criteria

The feature is complete when:

1. Eligible OCR words are searchable, selectable, and copyable at their corresponding visual locations.
2. The PDF's normal appearance remains equivalent to the prior image-based export.
3. Export never waits for or invokes OCR.
4. Pages without matching Ready OCR still export as images.
5. Old or mismatched OCR is never attached to a newer page revision.
6. UI overlay state never appears in the PDF.
7. Existing export snapshots remain supported.
8. Export status accurately reports full, partial, or absent searchability without exposing OCR text.
9. The editor exposes twenty licensed font families and loads previews on demand.
10. All font assets are versioned, hash-pinned, licence-validated, and reproducible.
11. Searchable-layer failures do not compromise private storage, output atomicity, logging privacy, or ordinary image export.
12. Automated extraction, visual-equivalence, security, catalogue, and mixed-page tests pass before production enablement.

