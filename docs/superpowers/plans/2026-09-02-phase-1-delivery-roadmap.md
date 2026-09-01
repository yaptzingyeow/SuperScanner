# SuperScanner Phase 1 Delivery Roadmap

**Spec:** `docs/superpowers/specs/2026-09-02-superscanner-phase-1-design.md`

The approved design contains independent subsystems with different risk profiles. Each plan below must end in deployable, testable software and receive its own review before the next plan begins.

## Delivery order

### Plan 1: Secure application foundation

**Plan:** `docs/superpowers/plans/2026-09-02-secure-application-foundation.md`

Deliver an Angular and ASP.NET Core baseline in which a Firebase-authenticated user can create a private document, upload one supported file directly to R2 quarantine, have a background worker validate it, and see the resulting status. Include PostgreSQL persistence, App Check, ownership enforcement, upload quarantine, malware scanning, audit hashing, Docker development services, CI, and Railway-ready containers.

**Spec coverage:** Sections 5, 6, 12, 13, 14.1, 16, and the foundational portions of Sections 7 and 17.

### Plan 2: Camera capture, enhancement, and basic PDF

Deliver browser-camera capture, stability guidance, page-edge detection, manual corner adjustment, multi-page organization, safe PDF/image import, server-side perspective correction, filters, immutable derived versions, and image-based PDF export.

**Working release:** A useful private scanner without OCR editing.

**Spec coverage:** Sections 7, 11.1, 13 boundary/dewarp handling, 14.2, and acceptance criteria 1, 6, 7, and 8.

### Plan 3: OCR, printed-text editing, and searchable PDF

Integrate Google Document AI through a provider adapter, normalize OCR geometry, build the selectable text overlay, estimate printed styles, replace and reflow text inside explicit regions, preserve undo/redo history, and export searchable PDFs.

**Working release:** A scanner that can edit recognized printed English text.

**Spec coverage:** Sections 8, 11.1 searchable output, relevant Section 13 failures, and acceptance criteria 2 and 7.

### Plan 4: Background and authorized watermark repair

Add exterior/interior cleanup, brush masks, deterministic OpenCV repair, OCR reconstruction over affected printed text, optional GPT Image 2 repair for complex crops, unmasked-pixel verification, provider cost limits, and restricted-content enforcement.

**Working release:** Mask-contained authorized cleanup without handwriting generation.

**Spec coverage:** Section 10, provider-minimization requirements in Section 12.7, and acceptance criterion 5.

### Plan 5: Authorized handwriting feasibility and gated release

Run a feasibility benchmark before production integration. If gates pass, add enrollment and authorization, encrypted profiles, isolated GPU inference, exact OCR spelling verification, style-similarity evaluation, prohibited-region detection, visible provenance, rate limits, and immediate revocation.

**Stop condition:** If the benchmark cannot meet correctness, similarity, latency, licensing, and safety gates, ship Phase 1 with handwriting recognition but without handwriting generation.

**Spec coverage:** Section 9, visible provenance in Section 11.2, sensitive-profile security in Section 12, and acceptance criteria 3, 4, and 10.

### Plan 6: Sharing, printing, operational hardening, and mobile readiness

Add revocable expiring share grants, named-user sharing, browser printing, deletion/retention workflows, signed audit roots, production KMS integration, cost and abuse telemetry, incident controls, backup/restore tests, full cross-browser accessibility testing, and Ionic/Capacitor boundary verification.

**Working release:** Production-ready Phase 1 and a verified base for Phase 2 mobile packaging.

**Spec coverage:** Remaining Sections 4, 11, 12, 14, 15, 16, and 17.

## Cross-plan gates

1. No later plan may weaken owner isolation, immutable-original behavior, audit capture, or upload quarantine established in Plan 1.
2. Each external provider remains behind an application-owned interface with a fake used in deterministic tests.
3. Every AI operation is feature-flagged, cost-limited, auditable, and independently disableable.
4. Real documents, handwriting samples, OCR text, signed URLs, and secrets never enter source control or ordinary logs.
5. A plan is complete only when its unit, integration, security, and user-flow verification commands pass from a clean checkout.
6. Deployment pricing and provider data-handling terms are revalidated immediately before the plan that introduces that provider.
