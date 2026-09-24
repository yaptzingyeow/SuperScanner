# SuperScanner

Private document scanning and printed-text editing with Angular, ASP.NET Core, PostgreSQL, Firebase Auth/App Check, and private object storage.

For local services, Railway deployment, migration, and incident controls, see the [foundation runbook](docs/operations/foundation-runbook.md). The [printed-text editing runbook](docs/operations/printed-text-editing.md) lists its feature flag, font licences, migration order, recovery, and production enablement gate. The [searchable PDF runbook](docs/operations/pdf-export.md) covers text-layer limits, verification, privacy, and rollback. Printed-text editing stays disabled in checked-in production defaults; do not enable it without the documented benchmark and review.

The approved Phase 3D [design](docs/superpowers/specs/2026-09-23-printed-text-replacement-editor-design.md) and [implementation plan](docs/superpowers/plans/2026-09-23-printed-text-replacement-editor.md) describe the image-based editor. Handwriting generation and OpenAI image repair remain outside this phase; searchable PDF text layers are implemented separately for export.
