# SuperScanner Google Document AI OCR Design

**Date:** 2026-09-22  
**Status:** Proposed for user review  
**Phase:** 3B — live Google Document AI OCR provider

## 1. Purpose

Phase 3B connects the provider-neutral OCR foundation from Phase 3A to the existing Google Document AI **Document OCR** processor. The first release runs locally for acceptance testing. Railway production enablement remains a separate promotion gate after accuracy, latency, security, and cost are reviewed.

This phase recognizes printed and handwritten English and stores normalized OCR results. It does not add the scanned-page text overlay, text replacement, font reconstruction, reflow, undo/redo, or searchable PDF output.

## 2. Google resource

- Project: `superscanner-dev`
- Location: `asia-southeast1`
- Processor type: Document OCR
- Processor ID: `fc0b14e64c62e7aa`
- Regional endpoint: `asia-southeast1-documentai.googleapis.com`
- Encryption: Google-managed

The processor resource name is assembled server-side from validated configuration. Angular never receives the project identifier, processor identifier, credentials, or provider request details.

## 3. Success criteria

Phase 3B is ready for local acceptance when:

1. `Ocr:Provider=GoogleDocumentAi` selects a live provider without changing the Phase 3A API contract.
2. The worker streams the exact current processed page from private object storage and sends it to the Singapore Document AI endpoint.
3. Printed and handwritten English results are normalized into blocks, lines, words, polygons, confidence, reading order, and classification supported by the Phase 3A model.
4. Text anchors are resolved correctly from the provider's UTF-8 document text, including missing segment starts and multi-segment layouts.
5. Coordinates are normalized to the existing page-relative polygon contract and validated before persistence.
6. Authentication, rate-limit, timeout, unavailable-provider, unsupported-input, and invalid-response failures map to stable safe codes.
7. Retryable failures use the existing bounded queue retry policy; permanent failures do not retry.
8. Recognized text, image bytes, credentials, object keys, and raw provider errors never enter ordinary logs.
9. A deterministic fixture suite passes without network access, and one explicitly enabled live acceptance test succeeds against the configured processor.
10. Disabling OCR leaves scanning, cropping, filters, page management, and image-PDF export operational.

## 4. Scope

### 4.1 Included

- Official Google Cloud Document AI .NET client dependency.
- `GoogleDocumentAiOcrProvider` implementation of the existing `IOcrProvider` interface.
- Provider-specific validated options for project, location, processor ID, endpoint, and request controls.
- Application Default Credentials for local development.
- Google response-to-normalized-result mapping.
- Handwriting classification when Google explicitly marks the recognized token or layout as handwritten.
- Safe provider failure classification and retryability.
- Provider request, page, latency, retry, and failure metrics without content.
- Offline adapter tests using synthetic or sanitized response fixtures.
- One opt-in, billable live acceptance test.
- Local operating instructions and a production-promotion checklist.

### 4.2 Excluded

- Railway credentials or production OCR enablement.
- Service-account key creation or storage.
- Workload Identity Federation provisioning.
- Text overlay, selection, highlighting, replacement, style matching, longer-text reflow, adjustable replacement boxes, undo/redo, and searchable PDF layers.
- Handwriting generation or imitation.
- Automatic language selection beyond English for the product contract.
- Batch/offline Document AI processing.

## 5. Architecture

### 5.1 Provider boundary

The existing `IOcrProvider` remains the only interface consumed by `OcrProcessor`. Three implementations remain available:

- `DisabledOcrProvider` when OCR is off;
- `FakeOcrProvider` for deterministic E2E workflows outside Production; and
- `GoogleDocumentAiOcrProvider` for live recognition.

Provider selection happens during worker startup. Provider-specific Google types do not cross into the application, domain, API, or Angular projects.

### 5.2 Authentication

Local development uses Google Application Default Credentials. The developer signs in with `gcloud auth application-default login`, or uses service-account impersonation through ADC when available. No credential JSON is committed, copied into application configuration, or stored in `.task-tools`.

The local principal receives only the permissions required to process documents. The preferred predefined role is `roles/documentai.apiUser`; broader Editor or Administrator roles are not required by the application.

Production authentication is deliberately deferred. Before Railway promotion, prefer Workload Identity Federation with short-lived credentials. A long-lived service-account key requires a separate security decision and rotation procedure.

### 5.3 Client construction

The worker creates the Document AI client with the configured regional endpoint and Application Default Credentials. Startup validation requires:

- OCR enabled;
- provider exactly `GoogleDocumentAi`;
- project exactly matching the configured allowed project;
- location in an allow-list containing `asia-southeast1` for this deployment;
- a syntactically valid processor ID;
- endpoint derived from or consistent with the location; and
- positive timeout and response-limit values.

Misconfiguration fails worker startup rather than silently falling back to another provider.

## 6. Processing flow

1. The existing queue leases a `RecognizePageText` job.
2. `OcrProcessor` loads the OCR result and verifies its source revision.
3. The worker opens the exact private processed object through `IObjectStore`.
4. The provider validates the supported media type and bounded input size.
5. The provider builds an online `ProcessRequest` for the configured processor and sends the image bytes directly to the regional endpoint.
6. The adapter resolves the returned text anchors, hierarchy, confidence, reading order, normalized vertices, and handwriting signals.
7. The existing `OcrResultValidator` validates the complete normalized response before mutation.
8. The repository stores the elements and marks the OCR result Ready in the existing transaction.
9. If the page changed meanwhile, the result remains historical and cannot become the current result.

No signed URL or public object URL is created. The provider receives only the processed page required for recognition.

## 7. Normalization rules

### 7.1 Text and hierarchy

The adapter uses the provider document text plus layout text anchors. It produces the existing hierarchy in deterministic page and reading order:

- block;
- line, derived from paragraph or line-level provider geometry according to the returned schema; and
- word, derived from tokens.

Whitespace and detected-break metadata reconstruct readable full text without altering the provider's recognized characters. Missing optional fields produce conservative defaults rather than fabricated content.

### 7.2 Geometry

Provider polygons are converted to the Phase 3A normalized coordinate range. Absolute vertices use the provider page dimensions; normalized vertices are consumed directly after finite-range validation. Invalid, non-finite, out-of-range, or non-polygon geometry causes `ocr_invalid_response`.

### 7.3 Confidence and handwriting

Confidence values are clamped only for insignificant floating-point drift; materially invalid values reject the response. Aggregate confidence uses the existing validator and persistence rules.

A word is classified as handwritten only when the provider explicitly supplies a handwriting signal. Absence of that signal is stored as unknown or printed according to the existing normalized enum; the adapter does not infer handwriting from confidence, font, or image appearance.

## 8. Failure and retry policy

Provider failures map to the existing safe contract:

- deadline exceeded or local timeout → `ocr_timeout`, retryable;
- resource exhausted or rate limited → `ocr_rate_limited`, retryable;
- transient unavailable/internal transport failure → `ocr_provider_unavailable`, retryable;
- unauthenticated or permission denied → `ocr_auth_failed`, permanent until configuration changes;
- invalid or unsupported input → `ocr_unsupported_media`, permanent for that revision;
- malformed or limit-violating response → `ocr_invalid_response`, permanent;
- missing private source → `ocr_source_missing`, permanent; and
- unclassified provider failure → `ocr_failed`, non-retryable unless explicitly allow-listed.

Raw Google error messages are not persisted or returned to the client. Retry count remains bounded by `Ocr:MaxAttempts` and the existing queue backoff.

## 9. Configuration

Existing options remain authoritative:

- `Ocr:Enabled`
- `Ocr:Provider`
- `Ocr:Language`
- `Ocr:MaxAttempts`
- `Ocr:TimeoutSeconds`
- `Ocr:MaxElements`
- `Ocr:MaxRecognizedCharacters`

Google-specific worker options add:

- `Ocr:Google:ProjectId`
- `Ocr:Google:Location`
- `Ocr:Google:ProcessorId`
- `Ocr:Google:Endpoint` as an optional validated override
- `Ocr:Google:MaxInputBytes`

Local values identify resources but contain no credentials. `GOOGLE_APPLICATION_CREDENTIALS` may point to a valid ADC configuration, but the recommended developer flow uses the well-known ADC file created by `gcloud`.

The API needs `Ocr:Enabled=true` to admit OCR requests. The worker additionally needs `Ocr:Provider=GoogleDocumentAi` and valid Google resource configuration to execute them.

## 10. Cost and abuse controls

- Existing idempotency permits at most one OCR result/job per page source fingerprint.
- Only Ready, owned, active pages with a current processed image are eligible.
- Input bytes, recognized characters, and normalized element counts are bounded.
- Provider calls have a strict timeout and bounded retry count.
- OCR remains independently disableable in API and worker configuration.
- Metrics count provider requests, processed pages, completion latency, retries, and safe failures.
- Live acceptance tests require an explicit environment flag and never run in ordinary CI.

Phase 3B does not add user billing or quotas. Those controls remain part of the later monetization and operational-hardening work.

## 11. Security and privacy

- Firebase identity, App Check, and owner authorization remain enforced by existing API endpoints.
- Google credentials are available only to the worker process.
- The API and browser never receive Google access tokens or credential material.
- Only the required processed page is sent to Google Document AI.
- Logs and traces exclude document text, pixels, filenames, object keys, signed URLs, credential paths, tokens, and raw provider payloads.
- Test fixtures must be synthetic or sanitized and contain no real customer documents or personal data.
- Production enablement requires a documented Google data-handling review and Railway credential threat assessment.

## 12. Verification strategy

### 12.1 Offline automated tests

- provider selection and startup validation;
- regional endpoint and processor resource construction;
- text-anchor extraction, including missing starts and multiple segments;
- block, line, and word hierarchy mapping;
- normalized and absolute polygon conversion;
- deterministic reading order;
- confidence and explicit handwriting mapping;
- empty valid document handling;
- malformed response and configured-limit rejection;
- Google status-to-safe-error and retryability mapping;
- cancellation and timeout propagation;
- no content or credentials in structured log templates; and
- regression coverage for Fake and Disabled providers.

### 12.2 Opt-in live acceptance

One test runs only when an explicit live-test flag is set and ADC is available. It sends a synthetic English page containing printed and handwritten samples to the configured processor, verifies a non-empty normalized result with valid geometry, and reports only counts, latency, and aggregate confidence. It does not print recognized text.

### 12.3 Local user acceptance

1. Enable Google OCR in the local API and worker.
2. Upload and process representative English scans.
3. Request text recognition from a Ready page.
4. Confirm Queued → Processing → Ready behavior.
5. Review recognition accuracy, handwriting classification, latency, and provider usage in the Google console.
6. Confirm disabling OCR returns the safe unavailable message while scan and PDF flows still work.

## 13. Production promotion gate

Railway enablement occurs only after local acceptance. The promotion review must decide and document:

1. Workload Identity Federation configuration and trust restrictions;
2. least-privilege IAM binding;
3. Railway secret/configuration delivery without repository credentials;
4. production request and cost limits;
5. provider privacy and regional-processing commitments;
6. alerting and emergency OCR disablement;
7. deployment and rollback steps; and
8. one controlled production smoke test.

Until that gate is approved, production keeps `Ocr:Enabled=false` and scanning/export remain available.

## 14. Delivery boundary

Phase 3B ends when live Google Document AI recognition passes local acceptance through the existing Phase 3A workflow and the production-promotion checklist is ready. The next independent phase adds the visual selectable-text overlay and manual OCR correction before any text replacement or searchable-PDF work.
