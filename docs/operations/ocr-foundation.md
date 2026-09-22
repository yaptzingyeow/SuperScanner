# OCR foundation operations

Phase 3A provides provider-neutral, asynchronous English OCR storage and status. It recognizes
printed and handwritten text into normalized Block → Line → Word elements. It does not yet render
a selectable text overlay, edit text, match fonts, or add a searchable PDF layer.

## Configuration

Configure both the API and Worker with the same `Ocr__*` policy values. The API admits requests;
the Worker performs jobs.

| Variable | Safe default | Purpose |
| --- | --- | --- |
| `Ocr__Enabled` | `false` | Enables requests and automatic scheduling |
| `Ocr__Provider` | `Disabled` | `Disabled` normally; `Fake` only for local/E2E verification |
| `Ocr__Language` | `en` | Phase 3A language contract |
| `Ocr__MaxAttempts` | `3` | Maximum leased attempts before terminal failure |
| `Ocr__TimeoutSeconds` | `30` | Per-provider timeout |
| `Ocr__MaxElements` | `10000` | Normalized response element limit |
| `Ocr__MaxRecognizedCharacters` | `1000000` | Text-size safety limit |

Checked-in non-E2E settings stay disabled and credential-free. `Fake` is rejected in Production,
and there is no silent provider fallback. The E2E environment explicitly enables `Fake` in both
composition roots.

Disabling OCR does not change scans, crop/filter processing, page readiness, or image-PDF export.
Existing OCR rows remain stored and readable; no new automatic results are queued.

## Status and failures

The API exposes current-source status only. A crop/filter revision creates a different source
fingerprint, so completion of an older job remains historical and cannot become the page's current
result. Empty recognition is a valid Ready result with zero elements.

Safe failure codes include:

- `ocr_timeout` — provider exceeded the configured deadline; retryable.
- `ocr_invalid_response` — provider output failed normalized validation; not retryable.
- `ocr_unsupported_media` — provider rejected the source media; not retryable.
- `ocr_invalid_job` — queue payload or referenced result was invalid; not retryable.
- `ocr_failed` — unexpected worker failure with provider content removed; retryable until bounded.

Metrics use meter `SuperScanner.Ocr`. They record numeric queue/provider durations, element count,
aggregate confidence, job outcome, retry outcome, safe failure code, and stale-current status.
Allowed tags are `provider`, `outcome`, `failure_code`, and `stale`. Never add OCR text, filenames,
object keys, signed URLs, image bytes, page/result IDs, or credentials as metric tags or log text.

## Local E2E verification

Start disposable PostgreSQL and R2-compatible storage, build the API and Worker, and build Angular
with the `e2e` configuration. Set `E2E_OCR_READY=1`; on Windows also set `E2E_CROP_PYTHON` to the
crop virtual environment's `python.exe`. Then run only `e2e/ocr-foundation.spec.ts`. E2E identity
and Fake OCR settings must never be used in a shared or production environment.

## Phase 3B Google Document AI handoff

No Google SDK, service-account credential, or live adapter is present in Phase 3A. The following
non-secret processor details were provisioned for the next phase:

- Project: `superscanner-dev`
- Processor type: Document OCR
- Processor ID: `fc0b14e64c62e7aa`
- Processor and endpoint region: `asia-southeast1`
- Status: Enabled
- Encryption: Google-managed

Phase 3B should implement `IOcrProvider` with the regional Document AI endpoint, load credentials
from the deployment secret store, preserve the normalized contracts and limits, and add provider
contract tests before enabling it. Never commit a service-account JSON file or access token.
