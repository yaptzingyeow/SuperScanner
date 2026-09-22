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
| `Ocr__Provider` | `Disabled` | `Disabled` normally; `Fake` for local/E2E or `GoogleDocumentAi` for approved live OCR |
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

## Phase 3B Google Document AI local operation

The Worker contains a live Google Document AI adapter. Checked-in settings remain disabled and
contain only these non-secret resource identifiers:

- Project: `superscanner-dev`
- Processor type: Document OCR
- Processor ID: `fc0b14e64c62e7aa`
- Processor and endpoint region: `asia-southeast1`
- Status: Enabled
- Encryption: Google-managed

### Local authentication

Install the Google Cloud CLI and authenticate through Application Default Credentials (ADC):

```powershell
gcloud auth application-default login
gcloud config set project superscanner-dev
```

Grant the signed-in development principal only `roles/documentai.apiUser` on
`superscanner-dev`. Do not use Editor or Administrator for application execution. Never copy an
ADC file, service-account JSON file, refresh token, or access token into this repository,
`.task-tools`, an appsettings file, or a shell script. The recommended ADC login stores credentials
in the operating system's Google configuration directory outside the repository.

### Local configuration

The API admits OCR requests and needs only OCR admission enabled:

```powershell
$env:Ocr__Enabled = 'true'
```

The Worker performs recognition and needs the live provider plus resource configuration:

```powershell
$env:Ocr__Enabled = 'true'
$env:Ocr__Provider = 'GoogleDocumentAi'
$env:Ocr__Google__ProjectId = 'superscanner-dev'
$env:Ocr__Google__Location = 'asia-southeast1'
$env:Ocr__Google__ProcessorId = 'fc0b14e64c62e7aa'
$env:Ocr__Google__Endpoint = 'asia-southeast1-documentai.googleapis.com'
$env:Ocr__Google__MaxInputBytes = '26214400'
$env:Ocr__Google__EnableStyleInfo = 'false'
```

Start the API and Worker in separate terminals after setting the variables in their respective
terminal. The browser never receives Google credentials or resource identifiers.

### Opt-in billable acceptance test

Ordinary tests skip the live test and make no Google request. Run it explicitly only after ADC and
IAM are configured:

```powershell
$env:GOOGLE_DOCUMENT_AI_LIVE_TEST = '1'
dotnet test tests/SuperScanner.Application.Tests/SuperScanner.Application.Tests.csproj --filter GoogleDocumentAiLiveTests
Remove-Item Env:GOOGLE_DOCUMENT_AI_LIVE_TEST
```

The test sends one synthetic English PNG to the Singapore endpoint. Output is limited to element
count, word count, latency, and aggregate confidence; it never prints recognized text. An
`ocr_auth_failed` result means ADC or IAM must be corrected. Do not create a service-account key as
a workaround.

`Ocr__Google__EnableStyleInfo=true` requests token style metadata for handwriting classification.
The currently deployed processor rejects both the legacy and newer premium style-info request
forms, so keep this flag `false` until a compatible OCR processor version is deployed and the live
test passes with `GOOGLE_DOCUMENT_AI_ENABLE_STYLE_INFO=1`. Review Google's style-information pricing
before activation. SDK-level retries are disabled; the bounded `Ocr__MaxAttempts` queue policy is
the only retry owner, keeping request counts and cost metrics predictable.

### Safe disablement

To stop new OCR admission in an API terminal:

```powershell
$env:Ocr__Enabled = 'false'
```

To disable the Worker provider:

```powershell
$env:Ocr__Enabled = 'false'
$env:Ocr__Provider = 'Disabled'
```

Restart the affected process after changing environment variables. Scanning, crop/filter, page
management, and image-PDF export continue while OCR is disabled.

### Railway production promotion gate

Production remains disabled until all of these are reviewed and approved:

1. Configure Workload Identity Federation with narrowly restricted trust; do not upload a
   long-lived service-account key by default.
2. Bind only `roles/documentai.apiUser` to the production workload identity.
3. Deliver non-secret resource settings and identity configuration through Railway without adding
   credentials to Git.
4. Set production request, byte, retry, timeout, and cost limits.
5. Confirm Google regional processing and data-handling requirements.
6. Add alerts and a documented emergency `Ocr__Enabled=false` control.
7. Document deployment and rollback commands.
8. Run one controlled production smoke test, then review usage and logs for content leakage.
