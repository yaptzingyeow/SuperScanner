# Printed text editing: rollout and recovery

Printed-text replacement is a server-canonical, image-based workflow. It is **disabled by default** in both API and worker checked-in settings. It does not imitate handwriting, create a searchable PDF text layer, or call OpenAI. Do not enable it on Railway until the manual quality and security gate below passes. Do not place source OCR text, replacement text, image bytes, signed URLs, Firebase tokens, or R2 credentials in logs or tickets.

## Configuration

Set the same `TextEditing__*` limits in API and worker where applicable. The API gates proposal/apply/history; the worker uses rendering and retry limits. Configuration is validated at startup. Defaults come from `TextEditingOptions` unless explicitly listed below.

| Environment variable | Default | Purpose |
| --- | --- | --- |
| `TextEditing__Enabled` | `false` | Master API gate; leave off in production until approved. |
| `TextEditing__MaxSelectionWords` | `50` | Maximum contiguous OCR words in a selection. |
| `TextEditing__MaxReplacementCharacters` | `4000` | Maximum replacement length. |
| `TextEditing__MaxReplacementBoxArea` | `0.5` | Maximum normalized replacement area. |
| `TextEditing__MaxQueuedEditsPerPage` | `2` | Per-page pending edit limit. |
| `TextEditing__MinimumLetterSpacing` | `-0.02` | Lower bound for fit compression. |
| `TextEditing__MinimumFontScale` | `0.7` | Lower bound for font-size reduction. |
| `TextEditing__MaskDilationPixels` | `2` | Local glyph-mask expansion. |
| `TextEditing__ContainmentTolerance` | `0.01` | Outside-mask pixel-change tolerance. |
| `TextEditing__MaxAttempts` | `3` | Worker retry ceiling. |
| `TextEditing__FontManifestPath` | `assets/fonts/manifest.json` | Relative pinned-font manifest; absolute and parent-traversal paths are rejected. |

Never copy local E2E overrides into Railway. Check both production services for absence of a `TextEditing__Enabled=true` override before deployment.

## Migration and assets

Back up PostgreSQL and confirm the target project, database, and R2 bucket. Run the one-off SDK migration job described in [foundation-runbook.md](foundation-runbook.md), in migration order: `PageRevisions` then `PrintedTextEditing`. Do not run multiple migration jobs concurrently. Existing pages retain their legacy preview key when `ActiveRevisionId` is null. Confirm an old document still opens and exports before enabling edits.

After migration, rerun `scripts/database/Grant-ApplicationPermissions.ps1` as the database administrator, then `scripts/database/Test-DatabaseSetup.ps1`. Both scripts include the new `page_revisions` and `text_edit_operations` tables; without these grants the least-privilege application role cannot apply edits or load history. Verify against the exact deployment database, not a local development copy.

The bundled Noto Sans and Noto Serif regular/bold faces are SIL OFL 1.1. The checked-in [manifest](../../assets/fonts/manifest.json) pins exact SHA-256 hashes and versions; [licence](../../assets/fonts/OFL.txt) travels with the assets. API and worker must use the same manifest and binaries. Do not substitute a system font or accept client-supplied font paths.

Canonical outputs are private immutable R2 objects under the `page-revisions/` namespace; existing preview and export objects remain immutable. A `RenderTextEdit` job payload is only an edit GUID. Successful activation swaps the page's active revision in PostgreSQL after the output object is verified. PDF export snapshots the active object key at export creation; an earlier export stays on its previous snapshot.

## Monitor and recover

Before enabling, add or verify counts and latency by safe edit state and failure code, job attempt count/age, font-catalogue load failures, containment rejection count, and output-object conflicts. Do not assume these alerts already exist. Never label metrics with OCR/replacement content, object keys, document titles, or owner IDs. Alert on rising `text_edit_containment_failed`, `text_edit_object_conflict`, `text_edit_source_mismatch`, or sustained queue age. Worker failures include `text_edit_stale_revision`, `text_edit_stale_ocr`, `text_edit_source_missing`, `text_edit_source_invalid`, `text_edit_render_invalid`, `text_edit_overflow`, and `text_edit_failed`; use the safe code and opaque edit ID for triage.

On an incident, set `TextEditing__Enabled=false` on API and stop or scale down workers before changing renderer binaries. Existing active revisions remain readable. Do not manually overwrite an R2 revision key. Inspect the edit state, queue lease, current `ActiveRevisionId`, source/result revision IDs, and object HEAD without downloading document content to staff devices. A failed edit leaves the prior revision active; a retry must use the documented idempotent command, not a hand-written database update. To undo a completed edit, use the owner-scoped undo endpoint/UI; verify the preview and a new export. Do not reverse schema during a rapid application rollback. Restore an earlier application image against the additive schema, then perform a separately reviewed database rollback only from a verified backup.

Unreachable deterministic outputs can arise when object PUT succeeds but activation is rejected. Retain them while jobs can retry and while audit/retention policy requires them. Inventory references from `page_revisions` and `text_edit_operations`, review the candidate list, and use a separate approved cleanup job after the retention window; never delete by broad `page-revisions/` prefix.

## Production enablement gate

1. Verify locked restore/build, unit and PostgreSQL integration suites, fresh-database migration, and the opt-in E2E flow in a non-production environment.
2. Review a licensed-font benchmark on representative English printed forms: exact OCR spelling, font/style approximation, no protected-content edit, overflow and collision handling, and outside-mask pixel containment.
3. Verify keyboard/screen-reader controls, edit/undo/redo after reload, export snapshot stability, owner/App Check isolation, and no text in telemetry.
4. Confirm queue/cost limits, alerts, backup restore, feature-off rollback, and incident owner. Record approval before setting `TextEditing__Enabled=true` on API; coordinate compatible worker deployment.

For the opt-in browser scenario, start the local dependencies from the foundation runbook and set `E2E_TEXT_EDIT_READY=1` before `npm --prefix apps/web run e2e -- --grep "printed text replacement"`. This flag is only for the disposable E2E process and selects the deterministic printed-word OCR fixture. The fake OCR scenario is not a quality benchmark. Production remains off until all gates are recorded, even when automated tests pass.

On Windows, also set `E2E_CROP_PYTHON` to the absolute path of the project's crop-runtime `python.exe` before running the browser scenario (for example, resolve `.task-tools/crop-runtime/Scripts/python.exe`). The default `python3` executable name is for Unix-like hosts and is not available on a standard Windows setup; the selected interpreter must have the pinned crop requirements installed. Build the E2E Angular configuration before invoking Playwright directly, or use the `npm run e2e` script, which builds it first.
