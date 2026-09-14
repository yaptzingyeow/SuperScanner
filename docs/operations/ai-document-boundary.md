# AI document boundary operations

## Safe default

The repository default is `DocumentBoundary__Mode=OpenCvOnly` with rollout `0`. Google Cloud Vision remains a later, independent OCR stage; it is not used for page-boundary detection, perspective correction, or model hosting.

The Worker accepts these Railway variables:

| Variable | Safe default | Meaning |
| --- | --- | --- |
| `DocumentBoundary__Mode` | `OpenCvOnly` | `AiPreferred`, `OpenCvOnly`, or `ManualOnly` |
| `DocumentBoundary__ModelMetadataPath` | `processing/models/document-boundary-model.json` | Trusted metadata location inside the Worker |
| `DocumentBoundary__MaskThreshold` | `0.52` | Segmentation probability cutoff |
| `DocumentBoundary__HighConfidence` | `0.78` | Boundary may be described as accurate |
| `DocumentBoundary__MediumConfidence` | `0.58` | Boundary is suggested with verification guidance |
| `DocumentBoundary__InferenceTimeoutSeconds` | `20` | Detection subprocess deadline, maximum 25 seconds |
| `DocumentBoundary__RolloutPercentage` | `0` | Stable page-ID allocation from 0 to 100 |

Options are validated when the Worker starts. Invalid modes, thresholds, timeouts, or rollout values prevent startup. Model selection is passed to the subprocess only from trusted Worker configuration, never from request data.

## Candidate provisioning and promotion

1. Fetch a candidate with `tools/document-boundary/fetch_u2netp_model.ps1` into `.task-tools/document-boundary/models`.
2. Review the upstream artifact and dataset licenses.
3. Label and independently review at least 60 non-sensitive evaluation photos covering all required scenarios.
4. Run both AI and OpenCV benchmark modes. The report must contain no paths, text, or image data.
5. Promote only when at least 90% of complete pages keep every coordinate within 2%, no high-confidence result misses by more than 5%, and there are zero invalid results.
6. Manually copy the exact SHA-256, model version, license, source, and input contract to the metadata JSON in a reviewed pull request. Provision that exact model beside the metadata file.
7. Keep the repository mode at `OpenCvOnly`; begin deployment with a 5% Railway rollout.

The Worker verifies the model checksum before creating the ONNX Runtime session. A checksum, unsupported contract, or invalid model failure trips a Worker-lifetime circuit breaker, so later jobs use OpenCV until restart.

## Diagnostics and UI behavior

- `ai_high_confidence`: AI boundary accepted; UI says the paper was detected.
- `ai_review_recommended`: medium-confidence AI boundary; UI asks the user to verify corners.
- `ai_geometry_invalid` or `ai_inference_failed`: image-specific AI result rejected; OpenCV or manual adjustment follows.
- `ai_model_missing`, `ai_checksum_invalid`, `ai_model_unsupported`, `ai_model_invalid`: safe configuration failures; paths and exception text are not returned.
- `opencv_candidate`: classical boundary suggestion; user verification is required.
- `manual_required`: the full photo is selected and every corner must be adjusted.

The structured Worker event begins with `Document boundary completed for page` and includes only page ID, crop revision, source, confidence, model version, diagnostics code, and elapsed milliseconds. In Railway Worker logs, search for that event text. Monitor AI allocation, confidence distribution, fallback/manual rate, timeouts, p95 latency, and any difference between automatic and final manual corners. Never log OCR text, local/object paths, image bytes, API keys, tensors, or subprocess environment values.

## Rollback and recovery

Set `DocumentBoundary__Mode=OpenCvOnly` in Railway and restart only the Worker. This is the immediate rollback for accuracy, latency, or model-provisioning problems. Existing manual crop revisions remain authoritative; a stale detector job cannot overwrite a newer manual selection.

After a corrected candidate passes the protected benchmark, provision its exact checksum-matching file and restart the Worker to clear the circuit breaker. Do not work around checksum failures or relax thresholds directly in production.

## Verification record — 2026-09-14 (Asia/Singapore)

| Check | Result |
| --- | --- |
| Worker Python suite | 27 passed |
| Boundary benchmark unit suite | 4 passed |
| .NET boundary policy/result suite | 14 passed |
| API guidance suite | 4 passed |
| Angular crop-guidance suite | 3 passed |
| Solution build | Passed, 0 warnings and 0 errors |
| Full Angular suite | 30 passed, 8 pre-existing auth/navigation expectation failures; production build succeeds |
| Full .NET suite | Domain passed 5 and Application passed 34; container suites unavailable without Docker |
| Release Worker publish | Passed; required assets present, tests/models excluded |
| Example benchmark | Correctly blocked promotion: 1 case, minimum-case and invalid-result gates failed |
| PostgreSQL container checks | Pending; Docker Desktop engine unavailable |
| Docker Worker image/runtime check | Pending; Docker CLI unavailable on this machine |
| Protected 60-photo AI benchmark | Pending; no private release manifest has been assembled |
| AI rollout | Not enabled; model metadata remains disabled and rollout remains 0 |

The failed/incomplete candidate gate is a safe outcome. Production remains on OpenCV plus manual corner adjustment until a reviewed dataset and model pass every gate.
