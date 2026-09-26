# Checkbox marks delivery (2026-09-27)

The page editor now has a **Tick / Cross** tool. Click the page to place a transparent vector mark without OCR, then drag or resize it, choose a color, adjust size and stroke thickness, and save. Saved marks reload with the page. Undo/Redo applies only to mark changes in the current editor session. Automatic checkbox detection and snapping remain future work.

The API stores marks in `page_marks` with document-owner checks, idempotent creation, revision checks and audit events. Each document mutation advances the document revision. Export creation captures active marks in an immutable page snapshot; the worker draws check/cross paths above the scanned image as colored PDF vector strokes. Existing snapshots with no marks remain readable.

Migration: `20260926181624_PageMarks` runs with the API's existing EF migration startup. Do not deploy the UI ahead of the API and migration. No paid service or API key is needed for marks.

Verification: Angular unit suite, API integration suite, application suite, focused domain mark/export tests, production/e2e builds and the mocked browser flow for zoomed placement, style, save, reload, delete all pass. The full Domain suite still has two pre-existing empty-replacement failures from unrelated text-edit work. The browser flow uses mocked API responses; it does not replace a live end-to-end export test against local R2/PostgreSQL.
