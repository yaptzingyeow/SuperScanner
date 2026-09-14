# AI Document Boundary Detection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Detect the physical outer boundary of uploaded documents with a versioned ONNX segmentation model, refine it into four accurate corners, and fall back safely to the existing OpenCV/manual workflow.

**Architecture:** The existing Worker keeps ownership of asynchronous crop jobs. A focused Python boundary package performs preprocessing, ONNX inference, mask-to-geometry refinement, confidence scoring, and OpenCV fallback; the .NET processor validates and persists a provider-neutral result. Model promotion is gated by a protected 60-image benchmark, and the preprocessing/model contract is portable to Phase 2 mobile ONNX Runtime.

**Tech Stack:** .NET 10, EF Core, PostgreSQL, Python 3, OpenCV, NumPy, ONNX Runtime 1.30.0 CPU, Angular, xUnit, Python `unittest`, Railway, Cloudflare R2

**Spec:** `docs/superpowers/specs/2026-09-14-ai-document-boundary-detection-design.md`

## Global Constraints

- Phase 1 inference runs in the existing server Worker; Phase 2 mobile implementation is out of scope.
- At least 90% of a minimum 60-image release set must have all four corners within 2% of manually confirmed coordinates.
- No result with any corner error above 5% may be classified as high confidence in the release set.
- Detection runs on a reduced-resolution image; perspective correction always reads the immutable original-resolution source.
- Google Vision is not used for physical paper-boundary detection.
- Personal images, probability masks, OCR text, API keys, and model tensors must not enter Git or application logs.
- A model cannot be enabled unless its commercial-use license is recorded and its SHA-256 checksum matches configuration.
- Existing optimistic crop revisions and manual overrides remain authoritative.
- `AiPreferred` must not become the production default until the benchmark gate passes.

---

## File Structure

### New Worker processing files

- `src/SuperScanner.Worker/processing/boundary/__init__.py` — public boundary-detection package exports.
- `src/SuperScanner.Worker/processing/boundary/contracts.py` — typed points, detector result, and diagnostics contract.
- `src/SuperScanner.Worker/processing/boundary/preprocess.py` — aspect-preserving letterbox transform and inverse coordinate mapping.
- `src/SuperScanner.Worker/processing/boundary/onnx_segmenter.py` — checksum-verified ONNX Runtime session and probability-mask inference.
- `src/SuperScanner.Worker/processing/boundary/geometry.py` — mask cleanup, contour selection, supporting-line fitting, and ordered intersections.
- `src/SuperScanner.Worker/processing/boundary/confidence.py` — confidence evidence and high/medium/low policy.
- `src/SuperScanner.Worker/processing/boundary/hybrid.py` — orchestration of AI, OpenCV fallback, and full-image/manual outcomes.
- `src/SuperScanner.Worker/processing/requirements.txt` — pinned runtime dependencies.
- `tools/document-boundary/requirements-dev.txt` — pinned ONNX fixture-generation and benchmark dependencies.
- `src/SuperScanner.Worker/processing/models/document-boundary-model.json` — model version, source, license, tensor contract, and checksum metadata; never the model binary.

### New evaluation files

- `tools/document-boundary/benchmark.py` — protected-manifest benchmark runner and JSON summary writer.
- `tools/document-boundary/manifest.example.json` — non-sensitive manifest schema example.
- `tools/document-boundary/fetch_u2netp_model.ps1` — downloads the Apache-2.0 U-2-Net small checkpoint-derived ONNX candidate, calculates SHA-256, and requires explicit metadata promotion.
- `tools/document-boundary/README.md` — dataset preparation, labeling, benchmark, licensing, and promotion procedure.
- `tests/fixtures/document-boundary/` — generated non-sensitive images, masks, and a tiny deterministic ONNX fixture.

### New and modified .NET files

- `src/SuperScanner.Infrastructure/Processing/DocumentBoundaryOptions.cs` — bound and validated detector configuration.
- `src/SuperScanner.Infrastructure/Processing/DocumentBoundaryHealth.cs` — Worker-lifetime AI circuit breaker for non-transient model failures.
- `src/SuperScanner.Infrastructure/Processing/DocumentBoundaryRollout.cs` — stable page-ID rollout selection.
- `src/SuperScanner.Infrastructure/Processing/CropDetectionResult.cs` — provider-neutral subprocess response contract.
- `src/SuperScanner.Infrastructure/Persistence/Migrations/20260914000000_AiDocumentBoundary.cs` — adds model version and safe diagnostics code to pages.
- `src/SuperScanner.Domain/Documents/Page.cs` — adds detection provenance fields.
- `src/SuperScanner.Infrastructure/Persistence/Configurations/PageConfiguration.cs` — bounds provenance column lengths.
- `src/SuperScanner.Infrastructure/Processing/CropProcessor.cs` — passes detector configuration and persists validated results.
- `src/SuperScanner.Worker/Program.cs` — registers and validates boundary options.
- `src/SuperScanner.Worker/appsettings.json` — safe defaults with `OpenCvOnly` production mode.
- `src/SuperScanner.Worker/appsettings.Development.json` — safe development defaults; local environment variables opt into `AiPreferred` after model provisioning.
- `src/SuperScanner.Worker/SuperScanner.Worker.csproj` — publishes Python package and model metadata.
- `src/SuperScanner.Worker/Dockerfile` — installs pinned Python inference dependencies and verifies model provisioning.

### Modified API and web files

- `src/SuperScanner.Api/Endpoints/CropEndpoints.cs` — returns source, confidence, model version, and safe diagnostics code.
- `apps/web/src/app/documents/crop-editor.component.ts` — maps confidence/source to a stable view state.
- `apps/web/src/app/documents/crop-editor.component.html` — displays accurate, verify, or manual guidance.
- `apps/web/src/app/documents/crop-editor.component.scss` — styles non-alarming confidence guidance.

---

### Task 1: Establish a Safe Classical Detector Seam

**Files:**
- Modify: `src/SuperScanner.Worker/processing/crop_image.py`
- Create: `src/SuperScanner.Worker/processing/boundary/__init__.py`
- Create: `src/SuperScanner.Worker/processing/boundary/contracts.py`
- Test: `src/SuperScanner.Worker/processing/test_crop_detection.py`

**Interfaces:**
- Consumes: existing `detect(image: numpy.ndarray) -> dict` behavior.
- Produces: `detect_with_opencv(image: numpy.ndarray) -> DocumentBoundaryResult`; `DocumentBoundaryResult.to_json_dict() -> dict`.

- [x] **Step 1: Add a failing contract test**

```python
from boundary.contracts import BoundaryPoint, DocumentBoundaryResult

def test_boundary_result_serializes_provider_neutral_fields(self):
    result = DocumentBoundaryResult(
        points=(BoundaryPoint(.1, .1), BoundaryPoint(.9, .1),
                BoundaryPoint(.9, .9), BoundaryPoint(.1, .9)),
        confidence=.82, source="OpenCvFallback",
        model_version=None, diagnostics_code="opencv_candidate")
    self.assertEqual(result.to_json_dict()["source"], "OpenCvFallback")
    self.assertEqual(len(result.to_json_dict()["points"]), 4)
```

- [x] **Step 2: Run the focused test and verify RED**

Run: `C:\yeow\SuperScanner\.task-tools\crop-runtime\Scripts\python.exe -m unittest discover -s src/SuperScanner.Worker/processing -p "test_crop_detection.py" -v`

Expected: FAIL because `boundary.contracts` does not exist.

- [x] **Step 3: Implement the immutable result contract**

```python
@dataclass(frozen=True)
class BoundaryPoint:
    x: float
    y: float

@dataclass(frozen=True)
class DocumentBoundaryResult:
    points: tuple[BoundaryPoint, BoundaryPoint, BoundaryPoint, BoundaryPoint]
    confidence: float
    source: Literal["Ai", "OpenCvFallback", "FullImage"]
    model_version: str | None
    diagnostics_code: str

    def to_json_dict(self) -> dict:
        return {
            "points": [asdict(point) for point in self.points],
            "confidence": self.confidence,
            "source": self.source,
            "modelVersion": self.model_version,
            "diagnosticsCode": self.diagnostics_code,
        }
```

Move the current OpenCV body to `detect_with_opencv`. Delete the development-only `shutil.copyfile` capture and its unused import. Keep a compatibility `detect` wrapper until Task 6 replaces it.

- [x] **Step 4: Run existing crop regressions and verify GREEN**

Run: `C:\yeow\SuperScanner\.task-tools\crop-runtime\Scripts\python.exe -m unittest discover -s src/SuperScanner.Worker/processing -p "test_*.py" -v`

Expected: all current paper-boundary regression tests PASS with unchanged coordinates/tolerances.

- [x] **Step 5: Commit the seam**

```powershell
git add src/SuperScanner.Worker/processing/crop_image.py src/SuperScanner.Worker/processing/boundary src/SuperScanner.Worker/processing/test_crop_detection.py
git commit -m "refactor: isolate classical document detector"
```

### Task 2: Add Deterministic Letterbox Preprocessing

**Files:**
- Create: `src/SuperScanner.Worker/processing/boundary/preprocess.py`
- Create: `src/SuperScanner.Worker/processing/test_boundary_preprocess.py`

**Interfaces:**
- Consumes: BGR `numpy.ndarray`, configured square input size.
- Produces: `letterbox(image, input_size) -> LetterboxedImage`; `LetterboxedImage.to_source_points(points) -> numpy.ndarray`.

- [x] **Step 1: Write failing aspect-ratio and inverse-mapping tests**

```python
def test_letterbox_preserves_aspect_ratio_and_inverts_points(self):
    image = np.zeros((2000, 1500, 3), np.uint8)
    prepared = letterbox(image, 320)
    self.assertEqual(prepared.tensor_bgr.shape, (320, 320, 3))
    source = prepared.to_source_points(np.array([[40, 0], [280, 320]], np.float32))
    np.testing.assert_allclose(source, [[0, 0], [1500, 2000]], atol=7)
```

- [x] **Step 2: Run the test and verify RED**

Run: `C:\yeow\SuperScanner\.task-tools\crop-runtime\Scripts\python.exe -m unittest discover -s src/SuperScanner.Worker/processing -p "test_boundary_preprocess.py" -v`

Expected: FAIL because `letterbox` is undefined.

- [x] **Step 3: Implement letterboxing and inverse mapping**

```python
@dataclass(frozen=True)
class LetterboxedImage:
    tensor_bgr: np.ndarray
    scale: float
    pad_x: int
    pad_y: int
    source_width: int
    source_height: int

    def to_source_points(self, points: np.ndarray) -> np.ndarray:
        result = points.astype(np.float32).copy()
        result[:, 0] = np.clip((result[:, 0] - self.pad_x) / self.scale, 0, self.source_width - 1)
        result[:, 1] = np.clip((result[:, 1] - self.pad_y) / self.scale, 0, self.source_height - 1)
        return result
```

Use `cv2.INTER_AREA` when shrinking, `cv2.INTER_LINEAR` when enlarging, symmetric padding, and a zero-filled canvas.

- [x] **Step 4: Run preprocessing tests and the full Python crop suite**

Run: `C:\yeow\SuperScanner\.task-tools\crop-runtime\Scripts\python.exe -m unittest discover -s src/SuperScanner.Worker/processing -p "test_*.py" -v`

Expected: all tests PASS.

- [x] **Step 5: Commit preprocessing**

```powershell
git add src/SuperScanner.Worker/processing/boundary/preprocess.py src/SuperScanner.Worker/processing/test_boundary_preprocess.py
git commit -m "feat: add portable boundary preprocessing"
```

### Task 3: Add Checksum-Verified ONNX Segmentation

**Files:**
- Create: `src/SuperScanner.Worker/processing/boundary/onnx_segmenter.py`
- Create: `src/SuperScanner.Worker/processing/test_onnx_segmenter.py`
- Create: `src/SuperScanner.Worker/processing/requirements.txt`
- Create: `tools/document-boundary/requirements-dev.txt`
- Create: `src/SuperScanner.Worker/processing/models/document-boundary-model.json`
- Create: `tests/fixtures/document-boundary/create_fixture_model.py`
- Create: `tests/fixtures/document-boundary/tiny-segmenter.onnx`

**Interfaces:**
- Consumes: model metadata path and BGR image.
- Produces: `OnnxDocumentSegmenter(metadata_path).predict(image) -> SegmentationPrediction`; throws `ModelConfigurationError` for absent/checksum-invalid/incompatible models.

- [x] **Step 1: Generate and commit a tiny deterministic ONNX test model**

`create_fixture_model.py` must create an ONNX graph that maps normalized NCHW RGB input to a one-channel sigmoid-like center mask without external data. Run:

```powershell
python tests/fixtures/document-boundary/create_fixture_model.py
Get-FileHash tests/fixtures/document-boundary/tiny-segmenter.onnx -Algorithm SHA256
```

Record the printed digest in the test metadata fixture. The fixture is test-only and must stay below 100 KB.

- [x] **Step 2: Write failing checksum and inference tests**

```python
def test_rejects_model_when_sha256_does_not_match(self):
    with self.assertRaises(ModelConfigurationError):
        OnnxDocumentSegmenter(self.metadata_with_sha("0" * 64))

def test_returns_probability_mask_in_source_letterbox_space(self):
    prediction = OnnxDocumentSegmenter(self.valid_metadata).predict(self.image)
    self.assertEqual(prediction.probability_mask.shape, (320, 320))
    self.assertTrue(np.isfinite(prediction.probability_mask).all())
    self.assertGreaterEqual(prediction.probability_mask.min(), 0)
    self.assertLessEqual(prediction.probability_mask.max(), 1)
```

- [x] **Step 3: Run tests and verify RED**

Run: `python -m unittest discover -s src/SuperScanner.Worker/processing -p "test_onnx_segmenter.py" -v`

Expected: FAIL because `OnnxDocumentSegmenter` does not exist.

- [x] **Step 4: Implement the session and pin dependencies**

`requirements.txt`:

```text
numpy==2.3.3
onnxruntime==1.30.0
opencv-python-headless==4.14.0.94
```

`tools/document-boundary/requirements-dev.txt`:

```text
onnx==1.22.0
```

The metadata schema is:

```json
{
  "enabled": false,
  "modelVersion": "u2netp-candidate-1",
  "fileName": "document-boundary.onnx",
  "sha256": "disabled-until-benchmark-promotion",
  "license": "Apache-2.0",
  "source": "https://github.com/xuebinqin/U-2-Net",
  "inputSize": 320,
  "inputLayout": "NCHW",
  "colorOrder": "RGB",
  "normalization": "imagenet"
}
```

The constructor must reject `enabled: true` unless `sha256` is exactly 64 lowercase hexadecimal characters and matches the binary. Configure ONNX Runtime with one intra-op and one inter-op thread and CPU execution only.

- [x] **Step 5: Run inference, crop regression, and dependency import checks**

Run:

```powershell
python -m pip install -r src/SuperScanner.Worker/processing/requirements.txt
python -m pip install -r tools/document-boundary/requirements-dev.txt
python -c "import cv2,numpy,onnxruntime; print(cv2.__version__, numpy.__version__, onnxruntime.__version__)"
python -m unittest discover -s src/SuperScanner.Worker/processing -p "test_*.py" -v
```

Expected: versions `4.14.0`, `2.3.3`, and `1.30.0` are reported and all tests PASS.

- [x] **Step 6: Commit runtime support**

```powershell
git add src/SuperScanner.Worker/processing/boundary/onnx_segmenter.py src/SuperScanner.Worker/processing/test_onnx_segmenter.py src/SuperScanner.Worker/processing/requirements.txt src/SuperScanner.Worker/processing/models/document-boundary-model.json tools/document-boundary/requirements-dev.txt tests/fixtures/document-boundary
git commit -m "feat: add verified ONNX boundary runtime"
```

### Task 4: Convert Segmentation Masks into Four Corners

**Files:**
- Create: `src/SuperScanner.Worker/processing/boundary/geometry.py`
- Create: `src/SuperScanner.Worker/processing/test_boundary_geometry.py`

**Interfaces:**
- Consumes: probability mask, threshold, and `LetterboxedImage` mapping.
- Produces: `estimate_boundary(mask, mapping, threshold) -> GeometryEstimate | None`; `GeometryEstimate` contains ordered normalized points plus numeric evidence.

- [x] **Step 1: Write failing clean, folded, shadowed, and internal-rule tests**

```python
def test_fits_supporting_lines_around_folded_outer_mask(self):
    mask = folded_document_probability_mask()
    result = estimate_boundary(mask, identity_mapping(320), .52)
    self.assertIsNotNone(result)
    np.testing.assert_allclose(result.points[0], [.14, .09], atol=.02)

def test_rejects_internal_rectangle_when_outer_mask_exists(self):
    mask = outer_page_with_internal_rule_mask()
    result = estimate_boundary(mask, identity_mapping(320), .52)
    self.assertLess(result.points[0][1], .12)
    self.assertLess(result.points[1][1], .12)
```

Also assert ordered clockwise points, finite coordinates in `[0,1]`, rejection of disconnected noise, and `None` for empty/near-full masks.

- [x] **Step 2: Run tests and verify RED**

Run: `python -m unittest discover -s src/SuperScanner.Worker/processing -p "test_boundary_geometry.py" -v`

Expected: FAIL because `estimate_boundary` does not exist.

- [x] **Step 3: Implement focused mask cleanup and supporting-line geometry**

```python
@dataclass(frozen=True)
class GeometryEvidence:
    mean_boundary_probability: float
    mask_area_ratio: float
    connectedness: float
    side_support: tuple[float, float, float, float]
    convexity: float
    coverage: float

@dataclass(frozen=True)
class GeometryEstimate:
    points: np.ndarray
    evidence: GeometryEvidence
```

Threshold the mask, close gaps with a kernel capped at 2% of the short side, remove components below 1% area, and reject candidate areas outside 20–98%. Fit each side with `cv2.fitLine` over contour bands selected relative to the oriented minimum-area rectangle. Intersect adjacent infinite lines, validate convexity, and map points through `LetterboxedImage`.

- [x] **Step 4: Run geometry and full Python suites**

Run: `python -m unittest discover -s src/SuperScanner.Worker/processing -p "test_*.py" -v`

Expected: all tests PASS.

- [x] **Step 5: Commit geometry extraction**

```powershell
git add src/SuperScanner.Worker/processing/boundary/geometry.py src/SuperScanner.Worker/processing/test_boundary_geometry.py
git commit -m "feat: refine document masks into corners"
```

### Task 5: Implement Confidence and Hybrid Fallback Policy

**Files:**
- Create: `src/SuperScanner.Worker/processing/boundary/confidence.py`
- Create: `src/SuperScanner.Worker/processing/boundary/hybrid.py`
- Create: `src/SuperScanner.Worker/processing/test_boundary_confidence.py`
- Create: `src/SuperScanner.Worker/processing/test_hybrid_detector.py`

**Interfaces:**
- Consumes: `GeometryEstimate`, optional OpenCV result, configured thresholds.
- Produces: `score_geometry(evidence) -> float`; `HybridBoundaryDetector.detect(image) -> DocumentBoundaryResult`.

- [x] **Step 1: Write failing decision-policy tests**

```python
def test_high_confidence_ai_result_is_selected(self):
    result = detector(ai=good_ai(), opencv=poor_opencv()).detect(image())
    self.assertEqual(result.source, "Ai")
    self.assertGreaterEqual(result.confidence, .78)

def test_low_confidence_ai_runs_opencv_fallback(self):
    result = detector(ai=weak_ai(), opencv=good_opencv()).detect(image())
    self.assertEqual(result.source, "OpenCvFallback")

def test_two_unreliable_detectors_require_manual_selection(self):
    result = detector(ai=None, opencv=weak_opencv()).detect(image())
    self.assertEqual(result.source, "FullImage")
    self.assertEqual(result.confidence, 0)
```

- [x] **Step 2: Run policy tests and verify RED**

Run: `python -m unittest discover -s src/SuperScanner.Worker/processing -p "test_boundary_*.py" -v`

Expected: FAIL because the policy modules do not exist.

- [x] **Step 3: Implement explicit policy configuration**

```python
@dataclass(frozen=True)
class ConfidencePolicy:
    high: float = .78
    medium: float = .58
    fallback_minimum: float = .45

def score_geometry(e: GeometryEvidence) -> float:
    side = min(e.side_support)
    return float(np.clip(.30 * e.mean_boundary_probability
                         + .25 * side + .15 * e.connectedness
                         + .15 * e.convexity + .15 * e.coverage, 0, 1))
```

`HybridBoundaryDetector` catches only declared model/configuration/inference exceptions, records a safe diagnostics code, and invokes the injected OpenCV function. It must not swallow programming errors such as `TypeError` or `AssertionError`.

- [x] **Step 4: Run policy and regression suites**

Run: `python -m unittest discover -s src/SuperScanner.Worker/processing -p "test_*.py" -v`

Expected: all tests PASS.

- [x] **Step 5: Commit policy**

```powershell
git add src/SuperScanner.Worker/processing/boundary/confidence.py src/SuperScanner.Worker/processing/boundary/hybrid.py src/SuperScanner.Worker/processing/test_boundary_confidence.py src/SuperScanner.Worker/processing/test_hybrid_detector.py
git commit -m "feat: add safe hybrid boundary policy"
```

### Task 6: Connect Hybrid Detection to the Crop Subprocess

**Files:**
- Modify: `src/SuperScanner.Worker/processing/crop_image.py`
- Modify: `src/SuperScanner.Worker/processing/boundary/__init__.py`
- Create: `src/SuperScanner.Worker/processing/test_crop_cli.py`

**Interfaces:**
- Consumes: CLI environment values `SUPERSCANNER_BOUNDARY_MODE`, `SUPERSCANNER_BOUNDARY_MODEL_METADATA`, `SUPERSCANNER_BOUNDARY_MASK_THRESHOLD`, `SUPERSCANNER_BOUNDARY_HIGH_CONFIDENCE`, and `SUPERSCANNER_BOUNDARY_MEDIUM_CONFIDENCE`.
- Produces: one JSON `DocumentBoundaryResult` on stdout and safe diagnostics on stderr; existing `apply` CLI remains unchanged.

- [x] **Step 1: Write failing subprocess contract tests**

```python
def test_detect_cli_emits_exact_provider_neutral_shape(self):
    completed = run_crop_cli("detect", fixture_image(), mode="OpenCvOnly")
    payload = json.loads(completed.stdout)
    self.assertEqual(set(payload), {
        "points", "confidence", "source", "modelVersion", "diagnosticsCode"
    })
    self.assertNotIn("probabilityMask", payload)
```

Add tests that `ManualOnly` emits `FullImage`, an absent model in `AiPreferred` emits fallback rather than crashing, and stderr never contains the input path or image bytes.

- [x] **Step 2: Run CLI tests and verify RED**

Run: `python -m unittest discover -s src/SuperScanner.Worker/processing -p "test_crop_cli.py" -v`

Expected: FAIL because the CLI still emits the legacy three-field object.

- [x] **Step 3: Build the detector from validated environment configuration**

```python
def create_boundary_detector(env: Mapping[str, str]) -> HybridBoundaryDetector:
    mode = env.get("SUPERSCANNER_BOUNDARY_MODE", "OpenCvOnly")
    if mode not in {"AiPreferred", "OpenCvOnly", "ManualOnly"}:
        raise ModelConfigurationError("boundary_mode_invalid")
    return HybridBoundaryDetector.from_configuration(mode=mode, env=env,
                                                      opencv_detector=detect_with_opencv)
```

Keep stdout exclusively machine-readable JSON. Map known failures to diagnostics codes such as `ai_model_missing`, `ai_checksum_invalid`, `ai_inference_timeout`, `ai_geometry_invalid`, `opencv_candidate`, and `manual_required`.

- [x] **Step 4: Run CLI and all Python tests**

Run: `python -m unittest discover -s src/SuperScanner.Worker/processing -p "test_*.py" -v`

Expected: all tests PASS.

- [x] **Step 5: Commit CLI integration**

```powershell
git add src/SuperScanner.Worker/processing/crop_image.py src/SuperScanner.Worker/processing/boundary/__init__.py src/SuperScanner.Worker/processing/test_crop_cli.py
git commit -m "feat: connect hybrid detector to crop jobs"
```

### Task 7: Validate Configuration and Persist Detection Provenance

**Files:**
- Create: `src/SuperScanner.Infrastructure/Processing/DocumentBoundaryOptions.cs`
- Create: `src/SuperScanner.Infrastructure/Processing/DocumentBoundaryHealth.cs`
- Create: `src/SuperScanner.Infrastructure/Processing/DocumentBoundaryRollout.cs`
- Create: `src/SuperScanner.Infrastructure/Processing/CropDetectionResult.cs`
- Modify: `src/SuperScanner.Infrastructure/Processing/CropProcessor.cs`
- Modify: `src/SuperScanner.Worker/Program.cs`
- Modify: `src/SuperScanner.Worker/appsettings.json`
- Modify: `src/SuperScanner.Worker/appsettings.Development.json`
- Modify: `src/SuperScanner.Domain/Documents/Page.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/Configurations/PageConfiguration.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Migrations/20260914000000_AiDocumentBoundary.cs`
- Create: `src/SuperScanner.Infrastructure/Persistence/Migrations/20260914000000_AiDocumentBoundary.Designer.cs`
- Modify: `src/SuperScanner.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs`
- Create: `tests/SuperScanner.Infrastructure.IntegrationTests/Processing/CropDetectionResultTests.cs`
- Create: `tests/SuperScanner.Infrastructure.IntegrationTests/Processing/DocumentBoundaryPolicyTests.cs`
- Modify: `tests/SuperScanner.Infrastructure.IntegrationTests/Persistence/DocumentPersistenceTests.cs`

**Interfaces:**
- Consumes: Python JSON from Task 6.
- Produces: `CropDetectionResult(CropPoint[] Points, double Confidence, string Source, string? ModelVersion, string DiagnosticsCode)` and persisted `Page.CropModelVersion`, `Page.CropDiagnosticsCode`.
- Produces: `DocumentBoundaryHealth.CanAttemptAi`, `DocumentBoundaryHealth.MarkUnhealthy(code)`, and `DocumentBoundaryRollout.ShouldUseAi(pageId, percentage)`.

- [x] **Step 1: Write failing JSON validation tests**

```csharp
[Fact]
public void AiResultRequiresModelVersion()
{
    var result = new CropDetectionResult(ValidPoints, .8, "Ai", null, "ai_candidate");
    Assert.False(result.IsValid());
}

[Theory]
[InlineData("Ai")]
[InlineData("OpenCvFallback")]
[InlineData("FullImage")]
public void AcceptsKnownSources(string source)
{
    var version = source == "Ai" ? "u2netp-candidate-1" : null;
    Assert.True(new CropDetectionResult(ValidPoints, .8, source, version, "ok").IsValid());
}
```

- [x] **Step 2: Run focused .NET tests and verify RED**

Run: `dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter FullyQualifiedName~CropDetectionResultTests`

Expected: FAIL because `CropDetectionResult` does not exist.

- [x] **Step 3: Implement and bind validated options**

```csharp
public sealed class DocumentBoundaryOptions
{
    public const string SectionName = "DocumentBoundary";
    public string Mode { get; init; } = "OpenCvOnly";
    public string ModelMetadataPath { get; init; } = "processing/models/document-boundary-model.json";
    public double MaskThreshold { get; init; } = .52;
    public double HighConfidence { get; init; } = .78;
    public double MediumConfidence { get; init; } = .58;
    public int InferenceTimeoutSeconds { get; init; } = 20;
    public int RolloutPercentage { get; init; } = 0;
}
```

Use `AddOptions<DocumentBoundaryOptions>().BindConfiguration(...).Validate(...)` to enforce known modes, ordered thresholds in `[0,1]`, timeout `1..25`, and rollout `0..100`, followed by `ValidateOnStart()`.

- [x] **Step 4: Pass configuration through `ProcessStartInfo.Environment`**

Set the five `SUPERSCANNER_BOUNDARY_*` variables explicitly in `CropProcessor`; never inherit model choice from arbitrary request data. Deserialize `CropDetectionResult`, call `IsValid`, and persist its exact source, model version, confidence, and diagnostics code. `DocumentBoundaryRollout.ShouldUseAi` must use the first unsigned 32 bits of SHA-256 over RFC-4122 page-ID bytes, modulo 100, so the same page always receives the same rollout decision.

- [x] **Step 5: Add the Worker-lifetime circuit breaker and structured telemetry**

```csharp
public sealed class DocumentBoundaryHealth
{
    private string? _unhealthyCode;
    public bool CanAttemptAi => Volatile.Read(ref _unhealthyCode) is null;
    public void MarkUnhealthy(string code) => Interlocked.CompareExchange(ref _unhealthyCode, code, null);
}
```

Register it as a singleton. When the subprocess reports `ai_checksum_invalid`, `ai_model_unsupported`, or `ai_model_invalid`, mark AI unhealthy and force subsequent jobs to `OpenCvOnly` until Worker restart. Do not trip the circuit for timeouts or image-specific invalid geometry. Inject `ILogger<CropProcessor>` and log one structured completion event containing page ID, revision, source, confidence, model version, diagnostics code, and elapsed milliseconds; never log paths, image data, OCR text, model tensors, or subprocess environment values.

- [x] **Step 6: Add provenance columns and migration**

Add nullable `CropModelVersion` and `CropDiagnosticsCode` properties with maximum lengths 100 and 64. Generate the migration with:

```powershell
dotnet ef migrations add AiDocumentBoundary --project src/SuperScanner.Infrastructure --startup-project src/SuperScanner.Api --output-dir Persistence/Migrations
```

Rename the generated timestamp to the repository convention only if EF generated a different timestamp; keep migration ID, designer attribute, and snapshot consistent.

- [ ] **Step 7: Run persistence and policy tests**

Run:

```powershell
dotnet test tests/SuperScanner.Infrastructure.IntegrationTests/SuperScanner.Infrastructure.IntegrationTests.csproj --filter "FullyQualifiedName~CropDetectionResultTests|FullyQualifiedName~DocumentBoundaryPolicyTests|FullyQualifiedName~DocumentPersistenceTests"
dotnet build SuperScanner.slnx --no-restore
```

Expected: focused tests PASS and solution build succeeds.

- [x] **Step 8: Commit configuration and persistence**

```powershell
git add src/SuperScanner.Infrastructure/Processing src/SuperScanner.Worker/Program.cs src/SuperScanner.Worker/appsettings*.json src/SuperScanner.Domain/Documents/Page.cs src/SuperScanner.Infrastructure/Persistence tests/SuperScanner.Infrastructure.IntegrationTests
git commit -m "feat: persist boundary detector provenance"
```

### Task 8: Expose Confidence Guidance in the Existing Crop Editor

**Files:**
- Modify: `src/SuperScanner.Api/Endpoints/CropEndpoints.cs`
- Modify: `apps/web/src/app/documents/crop-editor.component.ts`
- Modify: `apps/web/src/app/documents/crop-editor.component.html`
- Modify: `apps/web/src/app/documents/crop-editor.component.scss`
- Create: `apps/web/src/app/documents/crop-editor.component.spec.ts`
- Modify: `tests/SuperScanner.Api.IntegrationTests/Documents/DocumentsEndpointsTests.cs`

**Interfaces:**
- Consumes: `source`, `confidence`, `modelVersion`, and `diagnosticsCode` from the crop status endpoint.
- Produces: `guidance: 'accurate' | 'verify' | 'manual'` and accessible UI copy; diagnostics code and model version are not displayed as raw error text.

- [x] **Step 1: Write failing component guidance tests**

```typescript
it('asks for verification when AI confidence is medium', () => {
  component.applyCropState({ source: 'Ai', confidence: 0.66, diagnosticsCode: 'ai_candidate' });
  expect(component.guidance).toBe('verify');
});

it('requires manual adjustment for a full-image fallback', () => {
  component.applyCropState({ source: 'FullImage', confidence: 0, diagnosticsCode: 'manual_required' });
  expect(component.guidance).toBe('manual');
});
```

- [x] **Step 2: Run Angular test and verify RED**

Run: `npm --prefix apps/web test -- --watch=false --include src/app/documents/crop-editor.component.spec.ts`

Expected: FAIL because `applyCropState` and `guidance` do not exist.

- [x] **Step 3: Implement stable guidance mapping**

```typescript
type CropGuidance = 'accurate' | 'verify' | 'manual';

function cropGuidance(source: string, confidence: number): CropGuidance {
  if (source === 'Ai' && confidence >= 0.78) return 'accurate';
  if (source !== 'FullImage' && confidence >= 0.58) return 'verify';
  return 'manual';
}
```

Use the copy “Paper detected. Check the suggested corners.”, “We found a possible boundary. Please verify every corner.”, and “We could not confidently detect the paper. Adjust the corners manually.” Preserve existing dragging, precise coordinate entry, revision conflicts, and apply behavior.

- [x] **Step 4: Extend API response coverage**

Assert the crop status JSON includes nullable `modelVersion` and a safe `diagnosticsCode`, while ownership and authorization behavior remain unchanged.

- [ ] **Step 5: Run web and API focused tests**

Run:

```powershell
npm --prefix apps/web test -- --watch=false --include src/app/documents/crop-editor.component.spec.ts
dotnet test tests/SuperScanner.Api.IntegrationTests/SuperScanner.Api.IntegrationTests.csproj --filter FullyQualifiedName~DocumentsEndpointsTests
```

Expected: all focused tests PASS.

- [x] **Step 6: Commit UI guidance**

```powershell
git add src/SuperScanner.Api/Endpoints/CropEndpoints.cs apps/web/src/app/documents/crop-editor.component.* tests/SuperScanner.Api.IntegrationTests/Documents/DocumentsEndpointsTests.cs
git commit -m "feat: explain boundary confidence in crop editor"
```

### Task 9: Package the Runtime and Model Safely

**Files:**
- Modify: `src/SuperScanner.Worker/SuperScanner.Worker.csproj`
- Modify: `src/SuperScanner.Worker/Dockerfile`
- Create: `tools/document-boundary/fetch_u2netp_model.ps1`
- Create: `tools/document-boundary/README.md`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: pinned requirements and promoted model metadata.
- Produces: Worker image with Python modules and ONNX Runtime; production startup either verifies a provisioned model or safely stays in `OpenCvOnly`.

- [x] **Step 1: Add a failing publish-layout assertion**

Add an MSBuild target named `VerifyBoundaryProcessingAssets` that runs after `Publish` and errors when `processing/boundary/hybrid.py`, `processing/requirements.txt`, or `processing/models/document-boundary-model.json` is absent. Run publish before adding content items and verify it fails.

Run: `dotnet publish src/SuperScanner.Worker/SuperScanner.Worker.csproj -c Release -o .task-tools/boundary-publish`

Expected: FAIL at `VerifyBoundaryProcessingAssets`.

- [x] **Step 2: Publish all non-secret processing assets**

```xml
<Content Include="processing\**\*.py;processing\requirements.txt;processing\models\*.json"
         CopyToOutputDirectory="PreserveNewest"
         CopyToPublishDirectory="PreserveNewest" />
```

Explicitly exclude `*.onnx`, evaluation manifests, images, and probability masks from Git and ordinary publish output.

- [x] **Step 3: Install pinned runtime dependencies in Docker**

Replace the distro `python3-opencv` dependency with Python, pip, and the pinned requirements:

```dockerfile
RUN apt-get update && apt-get install -y --no-install-recommends python3 python3-pip \
    && python3 -m pip install --no-cache-dir --break-system-packages -r /tmp/requirements.txt \
    && rm -rf /var/lib/apt/lists/* /root/.cache/pip
```

Copy `requirements.txt` before the install layer so Docker caching remains effective. The model is mounted or downloaded by deployment provisioning and verified by the application; it is not baked into source control.

- [x] **Step 4: Create the controlled candidate download script**

The script downloads the U-2-Net small candidate from `https://github.com/danielgatis/rembg/releases/download/v0.0.0/u2netp.onnx` to a caller-specified directory, prints its SHA-256 and size, and never edits production metadata automatically. `README.md` must require license review, benchmark completion, manual checksum copy, and pull-request review before enabling the model.

- [ ] **Step 5: Verify publish and container build**

Run:

```powershell
dotnet publish src/SuperScanner.Worker/SuperScanner.Worker.csproj -c Release -o .task-tools/boundary-publish
docker build -f src/SuperScanner.Worker/Dockerfile -t superscanner-worker:boundary .
docker run --rm --entrypoint python3 superscanner-worker:boundary -c "import cv2,numpy,onnxruntime; print('boundary runtime ready')"
```

Expected: publish succeeds, Docker build succeeds, and the container prints `boundary runtime ready`.

- [x] **Step 6: Commit packaging**

```powershell
git add src/SuperScanner.Worker/SuperScanner.Worker.csproj src/SuperScanner.Worker/Dockerfile tools/document-boundary .gitignore
git commit -m "build: package document boundary runtime"
```

### Task 10: Build the Protected Benchmark and Promotion Gate

**Files:**
- Create: `tools/document-boundary/benchmark.py`
- Create: `tools/document-boundary/manifest.example.json`
- Create: `tools/document-boundary/test_benchmark.py`
- Modify: `tools/document-boundary/README.md`

**Interfaces:**
- Consumes: a local manifest with image path, expected ordered normalized corners, and scenario tags.
- Produces: geometry-only JSON containing aggregate accuracy, severe misses, confidence violations, latency, and fallback counts; exit `0` only when all promotion gates pass.

- [x] **Step 1: Write failing metric and privacy tests**

```python
def test_page_pass_requires_every_corner_within_two_percent(self):
    expected = corners(.1, .1, .9, .9)
    actual = expected.copy(); actual[2, 0] += .021
    self.assertFalse(page_passes(expected, actual, tolerance=.02))

def test_high_confidence_five_percent_miss_blocks_promotion(self):
    summary = summarize([case(max_error=.051, confidence=.9, source="Ai")])
    self.assertEqual(summary["confidenceViolations"], 1)
    self.assertFalse(summary["promotionPassed"])

def test_report_contains_no_image_paths_or_document_text(self):
    report = json.dumps(summarize([case()]))
    self.assertNotIn("C:\\\\", report)
    self.assertNotIn("recognizedText", report)
```

- [x] **Step 2: Run benchmark tests and verify RED**

Run: `python -m unittest discover -s tools/document-boundary -p "test_benchmark.py" -v`

Expected: FAIL because benchmark functions do not exist.

- [x] **Step 3: Implement exact metrics and exit gates**

```python
promotion_passed = (
    total_cases >= 60
    and complete_page_pass_rate >= .90
    and confidence_violations == 0
    and invalid_results == 0
)
```

Compute per-corner absolute normalized x/y errors, complete-page pass rate, median and p95 latency, source/fallback counts, and results grouped by scenario tag. Emit only case IDs, tags, numeric metrics, model version, and aggregate counts. Runtime source/confidence/fallback/latency distributions come from the structured completion event added in Task 7; the operational guide must include a Railway log query for that event name.

- [x] **Step 4: Add a non-sensitive manifest example**

```json
{
  "version": 1,
  "cases": [
    {
      "id": "synthetic-clean-a4-001",
      "image": "tests/fixtures/document-boundary/clean-a4.jpg",
      "tags": ["a4", "clean", "contrasting-background"],
      "expected": [[0.12, 0.08], [0.94, 0.08], [0.98, 0.94], [0.04, 0.93]]
    }
  ]
}
```

- [x] **Step 5: Run unit tests and a deliberately incomplete benchmark**

Run:

```powershell
python -m unittest discover -s tools/document-boundary -p "test_benchmark.py" -v
python tools/document-boundary/benchmark.py --manifest tools/document-boundary/manifest.example.json --output .task-tools/boundary-benchmark.json
```

Expected: unit tests PASS; the one-case benchmark exits non-zero with `promotionPassed: false` and reason `minimum_cases_not_met`.

- [ ] **Step 6: Assemble the protected 60-image release manifest**

Store it at `.task-tools/document-boundary/release-manifest.json`. Include at least five images for each required scenario: contrasting surface, pale background, dark/patterned background, folds/damage, shadows, internal rules, clipped pages, strong perspective, and non-A4 documents; images may carry multiple tags. Label corners through the existing precise-position editor and have a second review pass confirm every label.

- [ ] **Step 7: Benchmark candidate models**

Run:

```powershell
python tools/document-boundary/benchmark.py --manifest .task-tools/document-boundary/release-manifest.json --mode AiPreferred --output .task-tools/document-boundary/ai-summary.json
python tools/document-boundary/benchmark.py --manifest .task-tools/document-boundary/release-manifest.json --mode OpenCvOnly --output .task-tools/document-boundary/opencv-summary.json
python -m onnxruntime.tools.check_onnx_model_mobile_usability .task-tools/document-boundary/models/u2netp.onnx
```

Expected: summaries contain no paths/text; model usability output is archived locally; no production configuration changes occur.

- [ ] **Step 8: Promote only a passing model**

If and only if `promotionPassed` is true, copy the evaluated model version, exact SHA-256, input contract, source URL, and Apache-2.0 attribution into `document-boundary-model.json`, set deployment `DocumentBoundary__RolloutPercentage=5`, and keep repository `Mode=OpenCvOnly`. If the gate fails, leave the model disabled and use the grouped metrics to decide whether to fine-tune a document-specific model in a separate design.

- [x] **Step 9: Commit the benchmark harness, not private data or results**

```powershell
git add tools/document-boundary/benchmark.py tools/document-boundary/manifest.example.json tools/document-boundary/test_benchmark.py tools/document-boundary/README.md src/SuperScanner.Worker/processing/models/document-boundary-model.json
git commit -m "test: gate boundary model promotion on accuracy"
```

### Task 11: Verify the Complete Phase 1 Boundary Flow

**Files:**
- Modify: `docs/operations/document-cropping.md`
- Create: `docs/operations/ai-document-boundary.md`
- Modify: `docs/superpowers/plans/2026-09-14-ai-document-boundary-detection.md` (check completed steps only)

**Interfaces:**
- Consumes: completed Tasks 1–10 and, for AI rollout, a passing protected benchmark.
- Produces: operational runbook and evidence that OpenCV-only and AI-preferred modes both fail safely.

- [x] **Step 1: Document configuration and recovery**

Document exact Railway variables, model checksum provisioning, startup validation, rollout rollback (`DocumentBoundary__Mode=OpenCvOnly`), expected diagnostics codes, metric meanings, and confirmation that Google OCR is a later independent stage.

- [ ] **Step 2: Run all automated verification**

Run:

```powershell
python -m unittest discover -s src/SuperScanner.Worker/processing -p "test_*.py" -v
python -m unittest discover -s tools/document-boundary -p "test_benchmark.py" -v
dotnet test SuperScanner.slnx --no-restore
npm --prefix apps/web test -- --watch=false
npm --prefix apps/web run build
dotnet publish src/SuperScanner.Worker/SuperScanner.Worker.csproj -c Release -o .task-tools/boundary-publish
```

Expected: every command exits `0`.

- [ ] **Step 3: Verify local OpenCV fallback end to end**

Start API, Worker, and Angular using the existing secure local scripts. Set `DocumentBoundary__Mode=OpenCvOnly`, upload a non-sensitive folded-paper fixture, run auto-detect, adjust a corner, apply the crop, and verify the saved manual revision remains unchanged by a stale detector response.

- [ ] **Step 4: Verify local AI mode when the promotion gate passed**

Set `DocumentBoundary__Mode=AiPreferred`, provision the checksum-matching model, restart only the Worker, upload the same fixture, and verify the UI reports AI/verify guidance consistent with confidence. Change one byte in a disposable model copy and verify the Worker falls back with `ai_checksum_invalid` without logging the model path or image content.

- [x] **Step 5: Record verification evidence**

Add command names, timestamps, exit codes, benchmark aggregate metrics, tested detector modes, and rollback result to `docs/operations/ai-document-boundary.md`. Do not include secrets, local paths, document text, image thumbnails, or private manifest contents.

- [x] **Step 6: Commit operations documentation**

```powershell
git add docs/operations/document-cropping.md docs/operations/ai-document-boundary.md docs/superpowers/plans/2026-09-14-ai-document-boundary-detection.md
git commit -m "docs: operate AI document boundary detection"
```

---

## Execution Checkpoints

- **Checkpoint A — Tasks 1–3:** Classical seam, preprocessing, and verified ONNX inference are independently reviewable.
- **Checkpoint B — Tasks 4–6:** Masks become validated corners and the subprocess uses safe fallback.
- **Checkpoint C — Tasks 7–9:** .NET persistence, UI guidance, and deployment packaging are complete.
- **Checkpoint D — Task 10:** A model is promoted only if the protected benchmark passes; failure keeps production on OpenCV.
- **Checkpoint E — Task 11:** Full verification and rollback evidence are recorded.

Do not skip Checkpoint D by enabling an unbenchmarked artifact. A failed candidate benchmark is a valid outcome and triggers a separate fine-tuning design rather than ad hoc threshold changes in production.
