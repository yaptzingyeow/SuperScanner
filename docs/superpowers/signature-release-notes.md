# Document signatures: implementation and verification

## Delivered behavior

- Upload a PNG/JPEG signature, preview local light-background removal, adjust its strength, or keep the original.
- Draw with mouse, pen, or touch; undo a stroke and clear the drawing.
- Place, resize, save, reopen, move, and delete signatures independently of OCR.
- Export immutable signature asset and geometry snapshots with transparent PNG ink at normalized page coordinates.
- Retain retired assets while queued/processing or unexpired exports reference them.
- Clean retired assets in bounded rotating hourly batches, retaining idempotency tombstones.
- Retire signatures on removed pages during maintenance.
- Journal object writes before uploading, so failed creates remain discoverable after storage/DB recovery failures. Orphan intents become eligible after 24 hours.

This is a visual signature feature, not identity verification or certified electronic signing. No paid image API is involved.

## Database changes

Apply the existing PageSignatures migration and the new `SignatureAssetPurged` and `SignatureAssetWriteJournal` migrations before running the updated API/worker. No migration is applied to local user databases or production as part of this implementation run. The hourly maintenance service runs in the worker.

## Decisions and tradeoffs

- Preserve the existing feature checkout and unrelated dirty changes; integration may require later separation.
- Use isolated builds and disposable PostgreSQL containers to avoid interrupting running services; this consumes additional local disk space.
- Maintain the Windows progress ledger directly rather than requiring Bash tooling; bookkeeping remains manual.
- An identical create retry returns current saved geometry, not the original create geometry, preventing accidental overwrites.
- Reuse `Document.MarkContentChanged` for export staleness rather than adding another revision mechanism.
- Use an isolated stubbed-API Chromium test for real Canvas pixel behavior; it adds test tooling but leaves normal E2E defaults unchanged.
- Sort signature repository results after materialization for SQLite compatibility; document-scoped results are held in memory.
- Cleanup preserves tombstones and uses `AssetPurgedAt`, requiring a schema migration but preventing repeated deletion calls.
- Rotate bounded cleanup batches hourly to prevent retained assets starving later work; deletion can wait several intervals for large queues.
- Retire removed-page signatures during maintenance rather than expanding the page-removal application contract; private assets can remain until the next maintenance pass and export retention expires.
- Durable upload intents have no cascading document FK so cleanup survives document removal; orphan cleanup has a 24-hour grace period and needs a second schema migration.

## Verification caveats

Final verification: API 104 passed; infrastructure 100 passed; application 411 passed and 1 opt-in live OCR skipped; domain 87 passed and the 2 pre-existing cases below failed. Angular 166 passed; isolated Chromium signature flow 1 passed. Actual PDF image resources contain an alpha soft mask and the expected normalized transform. Three Important final-review findings were fixed and covered by regressions; no deferred Minor findings.

Whole-solution verification includes unrelated pre-existing text-edit changes. Two existing `TextEditOperationTests.Queue_rejects_empty_replacement` cases (empty string and whitespace) expect rejection, while the current selected-text deletion implementation accepts empty replacement. They were already failing before signature implementation; this feature does not alter those tests or text-edit behavior.

The opt-in `GoogleDocumentAiLiveTests.SyntheticEnglishPage_ProducesNormalizedRecognition` requires external live-test configuration and is intentionally skipped in ordinary runs. Signature verification uses no paid OCR calls.
