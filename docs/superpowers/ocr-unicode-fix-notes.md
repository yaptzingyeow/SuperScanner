# OCR Unicode anchor repair — 2026-09-27

## Root cause

Page 2 returned a valid Google Document AI result, but `DocumentAiTextAnchorReader`
interpreted Unicode character indexes as UTF-8 byte offsets. A segment ending
inside a multibyte character raised `ocr_invalid_response`; other segments could
silently return shifted text. The old synthetic Unicode test encoded the same
incorrect byte-offset assumption.

## Repair

- Translate Unicode scalar indexes to .NET UTF-16 boundaries before extracting
  text; preserve supplementary characters and continue rejecting invalid ranges.
- Allow explicit retries of existing `ocr_invalid_response` records. Keep the
  automatic retry flag unchanged to avoid repeated billable parser failures.
- Use the same domain retry policy in the request handler and response DTO.
- Add Unicode extraction, mapper, and legacy manual-retry regressions.

Reference: https://docs.cloud.google.com/document-ai/docs/enterprise-document-ocr
(the Python text-anchor sample slices characters, not encoded bytes).

## Verification

- Regression RED: 8 failures reproduced the indexing/retry defects.
- Application suite: 418 passed, 1 opt-in billable live test skipped.
- API integration suite: 104 passed with Docker access. A prior sandbox run had
  22 Docker-pipe access failures; the permitted rerun passed.
- Domain suite: 87 passed, 2 existing failures in
  `TextEditOperationTests.Queue_rejects_empty_replacement` (empty and whitespace).
  These conflict with the existing delete-text behavior and were not changed.
- API build: zero warnings/errors; `git diff --check` passed.
- User-approved actual page 2: 747 normalized OCR elements, database result Ready,
  processing job Completed, no failure code. No document text or keys logged.

## Local runtime

API and Worker were restarted with isolated artifact builds. The worker needs
access to the existing Google application-default credentials; sandbox execution
could not complete the provider call. Windows Event Log output is disabled in
the local worker helper to prevent logging permissions from stopping jobs.
Frontend remains running at http://127.0.0.1:4201/.

No production deployment, push, schema migration, or unrelated edit cleanup.
