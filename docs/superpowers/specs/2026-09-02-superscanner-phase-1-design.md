# SuperScanner Phase 1 Web Application Design

**Date:** 2026-09-02  
**Status:** Approved design, awaiting written-spec review  
**Product:** SuperScanner  
**Phase:** Phase 1 web application

## 1. Summary

SuperScanner is a document-scanning and editing web application. It captures or imports documents, corrects and enhances their appearance, recognizes printed and handwritten English text, permits authorized text replacement while preserving the surrounding visual style, and exports private, auditable PDFs or page images.

Phase 1 is an Angular progressive web application backed by ASP.NET Core 10, EF Core 10, PostgreSQL, private object storage, and asynchronous document-processing workers. The application is structured so its Angular components and backend services can be reused by an Ionic/Capacitor mobile application in Phase 2.

The most technically risky feature is generation of replacement text in an authorized writer's unique handwriting. It is isolated behind a feature flag and cannot launch generally until correctness, style-similarity, authorization, and abuse-prevention requirements pass validation.

## 2. Goals

Phase 1 must:

1. Capture multi-page documents from a browser camera or upload JPEG, PNG, HEIC, and PDF files.
2. Detect page boundaries, offer automatic capture, and allow manual corner correction.
3. Correct perspective, orientation, curvature, shadows, noise, and uneven backgrounds.
4. Provide document-oriented image filters and produce approximately 300 DPI output.
5. Recognize printed and handwritten English text with positions and confidence scores.
6. Replace printed text while approximating its original font, size, weight, colour, spacing, and baseline.
7. Reflow recognized text within a selected editable region and allow manual text-box movement and resizing.
8. Generate replacement handwriting only from an authorized handwriting profile, verify the generated spelling, and preserve edit provenance.
9. Remove ordinary backgrounds and authorized watermarks through a conservative, masked workflow.
10. Export image PDFs, searchable PDFs, JPEG/PNG pages, and ZIP archives.
11. Provide private, revocable, expiring share links and browser-based printing.
12. Preserve immutable originals, reversible edits, document ownership boundaries, and tamper-evident audit history.

## 3. Non-goals

Phase 1 does not include:

- Monetization, tokens, subscriptions, payment processing, advertisements, or weekly usage limits.
- Native mobile packaging; this follows in Phase 2.
- Languages other than English.
- Fax transmission.
- Real-time collaborative editing.
- Signature cloning or generation.
- Editing identity documents, certificates, cheques, prescriptions, financial instruments, official seals, anti-counterfeit features, or other protected/high-risk documents.
- Guaranteed identification of an unavailable proprietary font from raster pixels.
- Word-processor-style reflow of arbitrary graphics or unrecognized content in a flat scanned image.

## 4. Product principles

### 4.1 Preserve the original

Every imported page has an immutable logical original. Enhancements and edits create derived versions. A user can undo changes or restore the source. Deleting a document removes its assets according to the retention policy; "immutable" does not prevent an authorized user deletion.

### 4.2 Constrain edits to explicit regions

Automatic changes operate only within detected pages or user-confirmed masks. Watermark repair composites only the approved region back into the unchanged page. Text reflow affects only recognized text boxes within the selected editable region.

### 4.3 Degrade gracefully

A failed enhancement or AI operation must not prevent basic scanning and PDF export. Low-confidence automation returns control to the user instead of silently applying a questionable result.

### 4.4 Minimize external disclosure

External AI providers receive only the minimum image area and metadata required for an operation. API credentials remain server-side. Document pixels, OCR text, filenames, signed URLs, and handwriting samples are excluded from ordinary application logs.

## 5. Architecture

```text
Angular 22 PWA
|- Camera and file capture
|- OpenCV.js preview and corner adjustment
|- Page and text editor
|- Firebase Authentication and App Check
`- Direct signed upload to private object storage
          |
          v
ASP.NET Core 10 API on Railway
|- Firebase ID-token and App Check verification
|- Authorization and document management
|- Signed upload/download URL issuance
|- Processing-job orchestration
|- Provider adapters
|- Export orchestration
`- Audit-event signing
          |
          |- PostgreSQL: metadata, jobs, OCR layout, edits, audit events
          |- Cloudflare R2: originals, derived pages, previews, exports
          |- Railway worker: CPU image processing and PDF generation
          |- Google Document AI: printed/handwritten OCR
          |- GPT Image 2: optional masked complex-region repair
          `- Cloud Run GPU: authorized handwriting generation
```

### 5.1 Frontend

Angular 22 provides the PWA shell, capture flow, page organizer, image adjustment UI, OCR overlay, text editor, removal-mask editor, job progress, export flow, and share management.

The application is organized as an Angular workspace with reusable domain services and UI libraries. It avoids browser-only assumptions in shared code so Ionic and Capacitor can reuse the authentication, editor, state, API client, and workflow components in Phase 2. Native camera and filesystem integrations are added only in the mobile shell.

OpenCV.js produces responsive capture guidance and preview transformations. Browser output is not the canonical export; full-resolution server processing ensures consistent results across devices.

### 5.2 Public API

ASP.NET Core 10 exposes versioned HTTP APIs, validates Firebase ID tokens and Firebase App Check tokens, derives the current user from verified claims, and applies ownership checks to every document-scoped operation.

The API creates signed object-storage requests, records metadata, queues background work, returns job progress, manages edits, and creates exports. It does not hold complete uploads in API memory.

### 5.3 Processing worker

A separate Railway service claims PostgreSQL-backed jobs and performs deterministic CPU work: image decoding, validation, orientation, perspective correction, filters, masks, compositing, PDF rasterization, PDF generation, hashes, thumbnails, and OCR-result normalization.

PostgreSQL is the initial job queue. Claims use transactional locking, leases, heartbeats, and idempotency keys. Redis is deferred until measured throughput or queue contention requires it.

### 5.4 Provider adapters

OCR, complex-region repair, object storage, handwriting inference, and authentication verification sit behind interfaces owned by the application. Provider request and response formats do not leak into editor domain models. A provider can be replaced without changing the document editor.

### 5.5 Storage

PostgreSQL stores structured metadata. R2 stores binary assets. Original images, derived pages, thumbnails, masks, calibration images, and exported files never reside in PostgreSQL columns.

## 6. Core data model

### 6.1 Principal records

- **UserAccount:** Firebase UID, application state, policy acknowledgements, timestamps.
- **Document:** owner, title, status, page count, current version, retention policy, hashes.
- **Page:** document, order, dimensions, orientation, original asset, current derived asset.
- **Asset:** private object key, media type, byte size, dimensions, hash, lifecycle state.
- **ProcessingJob:** operation, state, attempts, lease, progress, provider, idempotency key, error code.
- **OcrBlock:** page, hierarchy, normalized polygon, recognized text, confidence, reading order, text type.
- **TextStyleEstimate:** font candidate, size, weight, colour, spacing, baseline, confidence.
- **EditOperation:** page, operation type, selected region, before/after values, sequence, actor.
- **HandwritingProfile:** owner, encrypted style artifact, authorization evidence, state, creation/deletion timestamps.
- **Export:** document version, format, options, asset, source hash, output hash, state.
- **ShareGrant:** document/export, permission, secret hash, expiry, revocation and access state.
- **AuditEvent:** actor, action, target, timestamp, previous hash, event hash, signature metadata.
- **ApplicationConfiguration:** document limits, thresholds, provider feature flags, retention defaults.

All image coordinates are normalized to page width and height. Rendering resolves them against the current resolution, preventing mobile previews and server exports from drifting.

### 6.2 Document states

```text
Draft -> Uploading -> Processing -> Ready -> Editing -> Exporting -> Completed
                         |                       |
                         `---- Failed/Retry ----'
```

State transitions are validated server-side. Jobs are idempotent, so retries cannot duplicate pages, accepted edits, exports, provider requests, or future billing events.

## 7. Capture and enhancement

### 7.1 Inputs

Phase 1 accepts browser-camera capture and JPEG, PNG, HEIC, and PDF uploads. A document supports up to 50 pages by default. Limits are database-configurable.

### 7.2 Camera behavior

The capture screen:

1. Displays a live boundary polygon.
2. Measures stability, focus, glare, and page completeness.
3. Automatically captures when thresholds pass.
4. Allows manual capture at any time.
5. Supports repeated capture into a multi-page document.
6. Allows reorder, rotate, delete, and recapture.
7. Opens manual corner handles when edge confidence is low.

### 7.3 Enhancement pipeline

Each page runs through:

1. Safe decode and orientation detection.
2. Page-boundary detection.
3. Perspective correction.
4. Curved-page dewarping when detected and confidence is sufficient.
5. Shadow and uneven-illumination correction.
6. Noise reduction and sharpening.
7. Interior and exterior background cleanup.
8. User-selected filter.
9. Full-resolution derived-asset generation.

Initial filters are Auto Enhance, Original Colour, Enhanced Colour, Grayscale, Black and White, Form/Document Cleanup, and Whiteboard Cleanup.

If automatic boundaries or dewarping fail confidence thresholds, the page remains usable and opens manual adjustment. Processing targets approximately 300 DPI for exported pages.

## 8. OCR and printed-text editing

### 8.1 OCR model

Google Document AI Enterprise OCR returns page text and geometry. The adapter normalizes provider results into blocks, lines, words, reading order, confidence, and printed/handwritten classification.

### 8.2 Editor behavior

The editor displays a scanned-page background with selectable OCR regions. A user highlights text, enters a replacement, previews it, and can undo or redo the operation.

For printed text, the processor:

1. Refines the glyph mask.
2. Reconstructs background pixels behind the original word.
3. Matches the text to a licensed font catalogue.
4. Estimates size, weight, colour, spacing, and baseline.
5. Renders exact replacement characters.
6. Blends the replacement with the scan's blur, noise, and compression characteristics.

The font catalogue starts with appropriately licensed open fonts and any fonts the product is licensed to redistribute or render. Raster input cannot prove the original font family. If no catalogue match is sufficiently confident, the editor presents ranked approximations and manual controls.

### 8.3 Reflow

Within an explicitly selected editable region, replacement text can:

- Expand into available space.
- Shift neighbouring recognized text boxes.
- Wrap when the region permits it.
- Warn on collisions.
- Be moved, resized, rotated, and manually styled.

The system does not shift arbitrary graphics or unrecognized raster content. The server performs canonical final rendering; the Angular canvas supplies a close preview using the same normalized layout and font assets.

## 9. Authorized handwriting replacement

Recognition and generation remain separate. OCR recognizes the source text. A dedicated GPU model generates the authorized writer's replacement text.

### 9.1 Profile enrollment

Before creating a profile, the user must:

1. Confirm ownership of the handwriting or permission from its writer.
2. Accept the AI-edit provenance requirements.
3. Complete a calibration sheet containing uppercase and lowercase English letters, numbers, punctuation, and natural sentences.

Calibration assets and derived style representations are encrypted and private to the profile owner. A user can revoke and delete a profile. Profile creation, use, revocation, and deletion create audit events.

### 9.2 Generation

The service:

1. Loads the encrypted style representation in the isolated GPU service.
2. Generates the requested English text.
3. Runs independent OCR over the generated output.
4. Rejects or regenerates output that does not exactly match the requested characters after normalization.
5. Matches ink colour, stroke thickness, scale, angle, spacing, and baseline.
6. Composites the result only into the selected region.
7. Returns a manually adjustable text box.

DiffusionPen is the initial research candidate, not a guaranteed production dependency. A feasibility benchmark chooses the final model based on correctness, similarity, latency, licensing, and operational safety.

### 9.3 Restrictions

The system does not create or replace signatures. It blocks supported high-risk document categories and protected regions. Handwriting generation is feature-flagged, rate-limited, unavailable through a public third-party API, and disabled until acceptance thresholds pass.

## 10. Background and watermark removal

### 10.1 Background cleanup

Exterior cleanup removes desk surfaces, fingers, and surroundings outside the confirmed page. Interior cleanup corrects shadows, stains, and uneven paper colour. Users can refine automatic masks with brush and erase tools.

### 10.2 Watermark workflow

Removal applies only to content the user owns or is authorized to modify.

1. The system suggests possible watermark regions conservatively.
2. The user confirms and edits the mask.
3. Deterministic OpenCV repair handles plain paper, repeated lines, and simple backgrounds.
4. When printed text is affected, OCR and font reconstruction rebuild recognized text.
5. GPT Image 2 may repair a complex photographic or textured crop.
6. The system compares unmasked pixels and OCR before and after.
7. A result is rejected if unrelated text or pixels changed.
8. Only the repaired masked region is composited back into the untouched page.

GPT Image masks are guidance rather than pixel-exact constraints, so GPT Image output is never accepted as a whole replacement page. The operation cannot recover the true hidden pixels; it reconstructs plausible content. For critical or unverifiable content, the product requests an original source or declines the operation.

Security marks, official seals, signatures, ownership marks on third-party content, and anti-counterfeit patterns cannot be removed.

## 11. Export, sharing, and printing

### 11.1 Export formats

- Image-based PDF.
- Searchable PDF with an invisible OCR text layer.
- Individual JPEG or PNG pages.
- ZIP archive of page images.

Users choose page size, orientation, quality, and compression. Output is a visually faithful rendered document, not a reconstructed Word-processing file.

### 11.2 Provenance

Every substantively edited export contains an embedded AI-edited metadata marker, original hash, export hash, edit timestamp, editing account reference, and audit-chain reference. Ordinary capture, crop, orientation, and visual filters do not cause a visible marker.

A page containing AI-generated handwriting also receives a small visible "AI-edited" footer. Removing a visible watermark does not remove the application's embedded provenance.

### 11.3 Sharing

Share grants are private, revocable, scoped to one document or export, and expire. The stored token is hashed. A recipient receives the minimum permission required. Named-user sharing can require Firebase authentication; link sharing uses a high-entropy secret and can be revoked immediately.

### 11.4 Printing

The application creates a print-optimized PDF and invokes the browser's native print workflow. Fax integration is deferred.

## 12. Security and privacy

### 12.1 Authentication and client attestation

The Angular application obtains Firebase Authentication and Firebase App Check tokens. The API validates signature, issuer, audience, expiry, and revocation-relevant claims. Tokens are sent in headers, never URLs. Replay-sensitive endpoints use limited-use App Check tokens when supported.

### 12.2 Authorization

The API derives the user ID only from verified claims. Every document query and mutation enforces ownership or an explicit active share grant. Object keys supplied by clients are treated as untrusted identifiers and resolved through owned database records.

Roles follow least privilege:

- Users access their own documents and granted shares.
- Support staff see operational metadata but no document contents by default.
- Configuration administrators cannot browse documents.
- Break-glass content access requires a reason, elevated approval, expiry, and an audit event.

### 12.3 Upload quarantine

Uploads enter a quarantine prefix and are unavailable for normal viewing until validation completes. Validation checks magic bytes, media type, byte size, page count, decoded-pixel count, malformed structures, archive/decompression bombs, and malware. PDFs are stripped of active content and rasterized in a restricted worker. Safe internal filenames replace user-provided filenames.

Signed URLs are short-lived and scoped to one object, operation, size, and media type where supported. A server-side upload intent permits one accepted completion and prevents a replay from creating additional document state.

### 12.4 Network isolation

Only the frontend and public API have public endpoints. PostgreSQL and the processing worker use Railway private networking. Production and non-production environments use separate projects, credentials, buckets, databases, Firebase configurations, and provider accounts.

### 12.5 Encryption and secrets

- Public traffic requires modern HTTPS.
- R2 provider encryption protects objects at rest; application-level envelope encryption additionally protects handwriting calibration assets and style profiles.
- Per-profile data keys are wrapped by a managed key service.
- Database connections require encryption.
- Provider keys, signing keys, and storage credentials remain server-side in protected secret variables.
- Keys and credentials support rotation without rewriting application code.

### 12.6 Browser isolation

The editor uses a strict Content Security Policy and does not load third-party advertising or analytics scripts within the document-processing context. Future advertising must run before the editor or in an isolated frame without access to document pixels, OCR text, filenames, authentication tokens, or signed URLs.

### 12.7 Provider privacy

OCR receives the processed page only when required. GPT Image receives only the masked crop with necessary surrounding context. Handwriting generation receives requested text and the encrypted style representation, not the whole document. Provider choices and regional configuration must satisfy the product's published privacy commitments before production use.

### 12.8 Audit integrity

Audit events are insert-only for the application role and form a hash chain. The service signs periodic root hashes with a key held outside PostgreSQL and stores the signed roots separately. This makes deletion or rewriting detectable. Audit payloads identify operations and regions without recording document text or pixels.

### 12.9 Abuse prevention

The service applies per-user and per-IP upload, OCR, export, share, watermark, and handwriting limits. It detects unusual bulk processing, repeated blocked-document attempts, share-link enumeration, and provider-cost spikes. Operators can revoke sessions, disable providers, revoke share grants, quarantine accounts, and rotate keys.

### 12.10 Retention and deletion

Users can delete documents and handwriting profiles. Deletion covers originals, derived files, thumbnails, calibration assets, exports, and provider references after the configured recovery window. Minimal security events may remain without document content. Backups are encrypted, access-controlled, restoration-tested, and expire under a documented retention schedule.

## 13. Failure handling

- **Boundary detection failure:** Open manual corner adjustment.
- **Dewarping uncertainty:** Keep the perspective-corrected version and request confirmation.
- **OCR failure:** Preserve scanning, enhancement, and PDF export; disable text editing for the affected page.
- **Low OCR confidence:** Require user correction or confirmation.
- **Font-match failure:** Present ranked alternatives and manual styling.
- **Handwriting spelling mismatch:** Reject and regenerate; never composite unverified output.
- **Style-similarity failure:** Keep the source unchanged and report that the profile needs better samples.
- **Masked-repair spill:** Reject any output that changes unapproved pixels or unrelated OCR.
- **Provider outage:** Retry transient errors, apply circuit breaking, and retain the job for later retry.
- **Export failure:** Preserve all accepted edits and allow an idempotent retry.
- **Browser closure:** Continue background work and restore status when the user returns.
- **Permanent job failure:** Store a safe error code and correlation ID without sensitive payload data.

## 14. Testing strategy

### 14.1 Automated tests

- Unit tests for layout, normalized coordinates, text reflow, edit history, job transitions, authorization, hashes, and audit signatures.
- ASP.NET Core and PostgreSQL integration tests.
- Provider-contract tests using recorded, sanitized fixtures.
- Golden-image tests for boundaries, perspective, filters, masks, and compositing.
- PDF render verification and searchable-text extraction tests.
- Security tests for IDOR, expired/revoked grants, Firebase claims, App Check, upload confusion, decompression bombs, and signed-URL scope.
- AI regression tests verifying spelling, mask containment, OCR preservation, and feature-flag enforcement.

### 14.2 Device and browser tests

Camera and editor flows are tested on supported Android Chrome, iPhone Safari, desktop Chrome, Edge, Firefox, and Safari. Tests cover orientation changes, camera denial, low memory, interrupted uploads, offline recovery, keyboard-only editing, screen readers, and touch target accessibility.

### 14.3 Test data

A sanitized benchmark set includes forms, receipts, slides, whiteboards, curved pages, shadows, glare, known printed fonts, authorized handwriting samples, and authorized synthetic watermarks. Sensitive production documents are never committed to the repository.

## 15. Acceptance criteria

Phase 1 targets:

1. At least 90% of ordinary document photos crop acceptably without manual corner adjustment on the benchmark set.
2. At least 98% character accuracy for clear printed English on the benchmark set.
3. At least 90% character accuracy for clear English handwriting on the benchmark set.
4. Generated handwriting must pass exact normalized OCR spelling verification before insertion.
5. Final masked repair changes no pixels outside the approved compositing mask.
6. A failed job never loses the immutable original or previously accepted edits.
7. Typical pages become editable within 15 seconds, excluding handwriting generation and unusually large uploads.
8. A 50-page document exports without exhausting API memory.
9. Ownership and share-grant security tests demonstrate no cross-user document access.
10. High-risk document and signature restrictions cannot be bypassed through ordinary API calls.

Targets are release gates measured against versioned fixtures. They are not claims that all real-world documents will achieve the same accuracy.

## 16. Deployment and operations

Railway hosts the Angular application, ASP.NET Core API, CPU worker, and PostgreSQL. The worker and database remain private. R2 stores document assets. Google Document AI supplies initial OCR. Google Cloud Run supplies scale-to-zero GPU inference for approved handwriting requests. GPT Image 2 is an optional repair provider, never the only watermark-removal method.

Operational telemetry includes job latency, failure categories, queue depth, provider latency, cost counters, rejection rates, OCR confidence distributions, storage growth, suspicious-access events, and share-link abuse. Metrics and traces use internal IDs and exclude document contents.

Alerts cover authentication anomalies, elevated 4xx/5xx rates, provider-cost spikes, queue stalls, failed audit signatures, storage failures, malware detections, and repeated high-risk operation attempts.

## 17. Initial cost posture

The design minimizes mandatory paid APIs:

- Browser scanning, OpenCV processing, filters, deterministic compositing, and PDF creation use open-source libraries.
- Google Document AI is the initial paid OCR shortcut; Enterprise OCR pricing is page-based and includes a small free allowance under current published pricing.
- Railway begins with usage-based Hobby or Pro resources; actual cost depends on API, worker, and PostgreSQL memory and CPU.
- R2 begins within its published free storage and operation allowances for a small prototype.
- Firebase email/password and common federated authentication can begin within its no-cost allowances; phone SMS is not required.
- Cloud Run GPU and GPT Image 2 are invoked only for eligible operations and are protected by limits, feature flags, and cost telemetry.

Pricing is revalidated immediately before implementation and production launch because provider rates and terms can change.

## 18. External references

- Angular releases: https://angular.dev/reference/releases
- .NET 10: https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/overview
- EF Core 10: https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew
- Google Document AI pricing: https://cloud.google.com/products/document-ai/pricing
- Railway pricing: https://docs.railway.com/pricing/plans
- Railway private networking: https://docs.railway.com/networking/private-networking
- Cloudflare R2 pricing: https://developers.cloudflare.com/r2/pricing/
- Cloudflare R2 data security: https://developers.cloudflare.com/r2/reference/data-security/
- Firebase pricing: https://firebase.google.com/pricing
- Firebase App Check for custom backends: https://firebase.google.com/docs/app-check/web/custom-resource
- OpenAI image editing: https://developers.openai.com/api/docs/guides/image-generation
- OpenAI pricing: https://developers.openai.com/api/docs/pricing
- DiffusionPen research implementation: https://github.com/koninik/DiffusionPen

## 19. Implementation sequencing constraint

Implementation planning begins only after the user approves this written specification. The plan must validate the riskiest assumptions early: camera boundary quality, printed-font replacement, mask-contained repair, and authorized handwriting feasibility. General handwriting release remains blocked until its dedicated acceptance gates pass.
