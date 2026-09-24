# Searchable PDF export operations

Searchable PDF export adds an invisible, per-word text layer over the existing scanned-page images. It uses only the exact Ready OCR result captured in the immutable export snapshot. Missing, queued, failed, stale, or malformed OCR falls back to an image-only page; export never waits for OCR and never sends content to OpenAI.

## Railway configuration

Configure both API and Worker from the same release. Railway environment variables use double underscores:

- `PdfExport__SearchableTextEnabled=true`
- `PdfExport__MaximumWordsPerPage=10000`
- `PdfExport__MaximumCharactersPerPage=100000`
- `PdfExport__MaximumCharactersPerWord=4096`
- `PdfExport__MinimumDimensionPoints=0.1`
- `PdfExport__MinimumHorizontalScalePercent=50`
- `PdfExport__MaximumHorizontalScalePercent=200`
- `PdfExport__MaximumAbsoluteAngleDegrees=20`

Invalid or unbounded values fail startup. The Worker must publish `assets/fonts/NotoSans-Regular.ttf`; the font is embedded in each searchable PDF. Apply the `SearchableDocumentExports` migration before enabling the Worker. Existing rows default to `SearchablePageCount=0` and remain `ImageOnly`.

## Verification and privacy

Run the opt-in Playwright suite with `E2E_SEARCHABLE_PDF_READY=1` and `E2E_PROCESSED_IMAGE_PATH` pointing to the sanitized processed-page fixture. The host needs `pdftotext`, `pdftoppm`, and ImageMagick `compare`. Verify text order and bounding boxes, zero pixel difference, a fully searchable export, and a mixed `PartiallySearchable` export.

Monitor counts and safe failure codes only. Do not log OCR text, PDF text operands, source/object paths, signed URLs, image bytes, Firebase tokens, or storage credentials. UI selection highlights are browser-only and are never serialized into the PDF.

## Disable and rollback

Set `PdfExport__SearchableTextEnabled=false` on the Worker and restart it. New exports remain fully functional but image-only; existing searchable PDFs remain downloadable and unchanged. Keep the migration and count column in place. Do not delete exports, OCR results, font assets, or R2 objects during rollback. Re-enable after validating a sanitized export and Worker health.
