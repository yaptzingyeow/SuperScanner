# SuperScanner OCR Foundation Design

**Date:** 2026-09-22
**Status:** Proposed for user review
**Phase:** 3A — provider-independent OCR foundation

## 1. Purpose

Phase 3A adds the asynchronous processing, normalized data model, persistence, API, and status UI required for OCR. It does not connect to Google Document AI yet. A deterministic fake provider makes the complete workflow implementable and verifiable before external credentials and billing are configured.

This foundation supports printed and handwritten English recognition data. Text replacement in this release remains limited to printed text; imitation of a writer's unique handwriting is a separate, gated project.

## 2. Success criteria

Phase 3A is complete when:

1. An owner can request OCR for any active page whose processed image is Ready.
2. OCR work runs asynchronously and is idempotent for the exact processed page revision.
3. Results store blocks, lines, words, polygons, confidence, reading order, and printed/handwritten classification in a provider-neutral format.
4. A crop or filter change creates a distinct OCR source revision, and a late result for an older revision never becomes the current result.
5. Retryable failures use bounded retries; permanent failures surface a safe error state.
6. OCR failure never demotes the page from Ready and never blocks ordinary image-PDF export.
7. Only the document owner can request or retrieve OCR data.
8. The Angular page UI shows Not requested, Queued, Processing, Ready, or Failed without introducing the text editor yet.
9. Production remains functional without Google credentials because the OCR feature is disabled until Phase 3B configuration is complete.

## 3. Scope

### 3.1 Included

- OCR domain state and normalized result models.
- PostgreSQL persistence and EF Core migration.
- An `IOcrProvider` application boundary.
- A deterministic fake provider for automated and local acceptance workflows.
- Background-job scheduling, execution, retry, and stale-result protection.
- Owner-authorized OCR request and result endpoints.
- OCR status and retry controls in the existing page UI.
- Structured operational metrics that exclude document content.

### 3.2 Excluded

- Google Document AI credentials, client libraries, or live calls.
- Selectable text overlays and highlighting on the scanned image.
- Manual OCR correction.
- Font, colour, size, weight, spacing, or alignment estimation.
- Text replacement, reflow, adjustable replacement boxes, undo, or redo.
- Searchable PDF text layers.
- Unique-handwriting imitation or generation.

## 4. Architectural decisions

### 4.1 Provider-independent boundary

The application layer defines `IOcrProvider`. Infrastructure supplies its implementation. The provider receives an image stream, media type, language, and cancellation token, and returns a normalized document result. Provider-specific response types never cross the infrastructure boundary.

The worker, rather than the provider, owns object-storage access. It opens the exact private processed image referenced by the queued OCR revision and passes the stream to the provider. This prevents storage credentials and signed URLs from leaking into provider adapters.

Phase 3A registers a deterministic fake provider only in explicit development or test configuration. Production configuration must not silently fall back to the fake provider.

### 4.2 Independent OCR lifecycle

OCR state belongs to an OCR result, not to `Page.State` or `Document.Status`. Absence of a result represents **Not requested**. A stored result moves through:

```text
Queued -> Processing -> Ready
                     `-> Failed
```

The public API maps absence to Not requested so the UI always receives one stable status shape. An empty but valid OCR response completes as Ready with zero elements; it is not a failure.

### 4.3 Immutable source revisions

Each OCR result is tied to the exact processed page artifact through:

- `PageId`;
- the processed image object key recorded when work is queued; and
- a SHA-256 source fingerprint used for uniqueness and job idempotency.

The unique constraint is `(PageId, SourceFingerprint)`. Requesting OCR repeatedly for the same source returns the existing result and does not duplicate provider work.

When crop or filter processing produces a new processed image, its different source fingerprint creates a new OCR result. A worker may finish an older revision, but the API and UI expose it as current only when its source still matches the page's current processed image. A stale completion cannot overwrite or replace the current revision.

### 4.4 Normalized hierarchy

`PageOcrResult` stores result-level metadata:

- identifier and page identifier;
- source object key and source fingerprint;
- state and attempt count;
- provider name and provider model/version label;
- language (`en` in Phase 3);
- complete recognized text;
- queued, started, and completed timestamps;
- safe failure code and retryability; and
- aggregate confidence and element count.

`OcrElement` stores a flexible hierarchy in one table:

- identifier and OCR result identifier;
- nullable parent element identifier;
- kind: Block, Line, or Word;
- text;
- confidence in the inclusive range 0–1;
- text type: Printed, Handwritten, or Unknown;
- zero-based reading order;
- a JSONB polygon containing normalized image coordinates; and
- optional provider-neutral language metadata.

Normalized coordinates use the inclusive range 0–1 with origin at the processed image's top-left. The application validates finite values, polygon shape, hierarchy ownership, reading-order uniqueness within a parent, maximum text length, and maximum element count before persistence.

## 5. Persistence

The migration adds:

### `page_ocr_results`

- Primary key on `Id`.
- Foreign key to `Pages`.
- Unique index on `(PageId, SourceFingerprint)`.
- Index on `(PageId, State, QueuedAt)` for current-status and worker queries.
- Bounded lengths for provider labels, source keys, failure codes, and language.

### `ocr_elements`

- Primary key on `Id`.
- Foreign key to `PageOcrResults` with cascade deletion.
- Self-referencing parent foreign key restricted to the same result by application validation.
- Index on `(PageOcrResultId, ParentElementId, ReadingOrder)`.
- JSONB polygon column.

Document ownership remains authoritative through the existing Page-to-Document relationship. Owner identity is not duplicated onto OCR rows.

## 6. Processing workflow

1. The page-processing workflow finishes perspective correction or filtering and marks the page Ready.
2. If OCR is enabled, an OCR scheduler ensures a result exists for the current processed image and enqueues one `RecognizePageText` job.
3. An owner may also call the OCR request endpoint. This is the manual path while automatic OCR is disabled.
4. The worker acquires the existing processing-job lease and atomically moves the result from Queued to Processing.
5. It rechecks that the page and processed object exist, opens the exact private object, and calls `IOcrProvider`.
6. The worker validates and normalizes the returned hierarchy.
7. In one database transaction, it inserts the elements and marks the result Ready.
8. If the source is no longer current, the completed historical result remains stored but is not presented as the page's current OCR result.

The idempotency key format is logically `page:{pageId}:ocr:{sourceFingerprint}`. The existing processing-job dispatcher gains the `RecognizePageText` job type rather than introducing a second queue system.

## 7. Retry and failure behaviour

OCR uses a configurable retry limit with a default of three provider attempts. Timeouts, rate limits, and transient provider unavailability are retryable with the queue's bounded backoff. Invalid provider output, unsupported media, missing source data, and validation-limit violations are permanent for that revision.

The UI and API expose stable safe codes such as:

- `ocr_timeout`;
- `ocr_provider_unavailable`;
- `ocr_unsupported_media`;
- `ocr_invalid_response`;
- `ocr_source_missing`; and
- `ocr_failed`.

Raw provider messages, document text, filenames, image bytes, credentials, and signed URLs are never persisted as failure details or written to logs. OCR failures do not alter page readiness, document readiness, or image-PDF eligibility.

## 8. API contract

### Request or retry OCR

`POST /api/documents/{documentId}/pages/{pageId}/ocr`

- Requires Firebase authentication and App Check under the existing API policy.
- Returns `202 Accepted` with the current OCR status.
- Creates work only for an active, owned, Ready page with a current processed image.
- Is idempotent for an existing Queued, Processing, or Ready result of the same source.
- For a Failed result, requeues only when the failure is retryable or the caller explicitly requests a retry permitted by policy.

### Read OCR status and result

`GET /api/documents/{documentId}/pages/{pageId}/ocr`

- Requires the same owner authorization.
- Returns `200 OK` with Not requested when no current result exists.
- Returns status and timestamps while work is pending.
- Returns full text and the nested ordered hierarchy only when Ready.
- Returns only the safe failure code when Failed.

As with existing document endpoints, a document or page that is missing, inactive, or owned by another user returns the same not-found response to avoid resource enumeration.

## 9. Angular experience

The existing page detail/editor surface gains a compact **Text recognition** card:

- **Not requested:** “Recognize text” action.
- **Queued:** pending indicator.
- **Processing:** progress indicator without a fabricated percentage.
- **Ready:** recognized word count, confidence summary, and completion time.
- **Failed:** safe explanation and Retry action when permitted.

The UI polls only while Queued or Processing, stops on a terminal state, and cancels polling when the component is destroyed. Phase 3A does not render text boxes on the image and does not expose editing controls.

## 10. Configuration

OCR configuration is database-independent and supplied through server options:

- `Ocr:Enabled` — false by default outside explicit OCR environments;
- `Ocr:Provider` — `Fake` for automated/local acceptance or `GoogleDocumentAi` after Phase 3B;
- `Ocr:Language` — `en` in Phase 3;
- `Ocr:MaxAttempts` — default 3;
- `Ocr:TimeoutSeconds`;
- `Ocr:MaxElements`; and
- `Ocr:MaxRecognizedCharacters`.

Startup validation rejects `Fake` in Production and rejects an enabled but unknown provider. With OCR disabled, scanning, cropping, filtering, page management, and PDF export continue normally.

## 11. Security and privacy

- All OCR endpoints use existing Firebase identity, App Check, and document-owner authorization.
- Provider credentials remain server-side and are never returned to Angular.
- The provider receives only the processed page required for the requested operation.
- Private source objects are streamed server-side; no public object URL is created.
- Logs contain identifiers, state transitions, latency, attempt count, element count, and aggregate confidence only.
- API response and provider-result limits protect memory, database size, and client rendering.
- Existing deletion and retention workflows must include OCR result rows through relational cleanup.

## 12. Observability

Operational metrics may record:

- jobs queued, started, retried, completed, failed, or stale;
- provider latency;
- total element count;
- aggregate confidence;
- safe failure code; and
- queue-to-completion duration.

Metrics and traces must exclude recognized text and document pixels.

## 13. Verification strategy

Implementation verification covers:

1. Domain transitions and invalid-transition rejection.
2. Normalized hierarchy and polygon validation.
3. PostgreSQL migration, constraints, and indexes.
4. Deterministic fake-provider output.
5. Duplicate requests producing one result and one active job.
6. Retryable versus permanent failure handling.
7. A stale worker completion not becoming current after crop/filter change.
8. OCR failure leaving page and document readiness unchanged.
9. Owner isolation and App Check enforcement on both endpoints.
10. Angular polling teardown and each visible status.
11. End-to-end recognition of a Ready page through the fake provider.

## 14. Delivery boundary

Phase 3A delivers a complete provider-neutral OCR pipeline using the fake provider. Phase 3B will add the live Google Document AI adapter, cloud authentication, cost controls, and provider-specific acceptance tests without changing the normalized API consumed by later editor phases.
