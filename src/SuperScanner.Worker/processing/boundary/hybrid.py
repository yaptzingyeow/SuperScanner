"""Safe decision policy for AI, classical, and manual page boundaries."""

from collections.abc import Callable

import numpy as np

from .confidence import ConfidencePolicy, score_geometry
from .contracts import BoundaryPoint, DocumentBoundaryResult
from .geometry import GeometryEstimate
from .onnx_segmenter import ModelConfigurationError, ModelInferenceError


AiDetector = Callable[[np.ndarray], tuple[GeometryEstimate, str] | None]
OpenCvDetector = Callable[[np.ndarray], DocumentBoundaryResult]


def _full_image(diagnostics_code: str = "manual_required") -> DocumentBoundaryResult:
    return DocumentBoundaryResult(
        points=(
            BoundaryPoint(0.0, 0.0),
            BoundaryPoint(1.0, 0.0),
            BoundaryPoint(1.0, 1.0),
            BoundaryPoint(0.0, 1.0)),
        confidence=0.0,
        source="FullImage",
        model_version=None,
        diagnostics_code=diagnostics_code)


def _configuration_diagnostic(error: ModelConfigurationError) -> str:
    message = str(error).lower()
    if "checksum" in message:
        return "ai_checksum_invalid"
    if "unavailable" in message or "missing" in message:
        return "ai_model_missing"
    if "unsupported" in message or "incompatible" in message:
        return "ai_model_unsupported"
    return "ai_model_invalid"


class HybridBoundaryDetector:
    def __init__(
            self,
            mode: str,
            ai_detector: AiDetector | None,
            opencv_detector: OpenCvDetector,
            policy: ConfidencePolicy):
        if mode not in {"AiPreferred", "OpenCvOnly", "ManualOnly"}:
            raise ModelConfigurationError("boundary mode is invalid")
        self._mode = mode
        self._ai_detector = ai_detector
        self._opencv_detector = opencv_detector
        self._policy = policy

    def detect(self, image: np.ndarray) -> DocumentBoundaryResult:
        if self._mode == "ManualOnly":
            return _full_image()
        if self._mode == "OpenCvOnly":
            return self._accepted_fallback(self._opencv_detector(image), None)

        ai_diagnostic = None
        try:
            candidate = self._ai_detector(image) if self._ai_detector is not None else None
            if candidate is not None:
                geometry, model_version = candidate
                confidence = score_geometry(geometry.evidence)
                if confidence >= self._policy.medium:
                    points = tuple(
                        BoundaryPoint(float(x), float(y)) for x, y in geometry.points)
                    return DocumentBoundaryResult(
                        points=points,
                        confidence=confidence,
                        source="Ai",
                        model_version=model_version,
                        diagnostics_code=(
                            "ai_high_confidence"
                            if confidence >= self._policy.high
                            else "ai_review_recommended"))
            ai_diagnostic = "ai_geometry_invalid"
        except ModelConfigurationError as error:
            ai_diagnostic = _configuration_diagnostic(error)
        except ModelInferenceError:
            ai_diagnostic = "ai_inference_failed"

        return self._accepted_fallback(self._opencv_detector(image), ai_diagnostic)

    def _accepted_fallback(
            self,
            result: DocumentBoundaryResult,
            ai_diagnostic: str | None) -> DocumentBoundaryResult:
        if (result.source == "OpenCvFallback"
                and result.confidence >= self._policy.fallback_minimum):
            if ai_diagnostic is None:
                return result
            return DocumentBoundaryResult(
                points=result.points,
                confidence=result.confidence,
                source=result.source,
                model_version=None,
                diagnostics_code=f"{ai_diagnostic}_opencv_candidate")
        return _full_image(
            f"{ai_diagnostic}_manual_required" if ai_diagnostic else "manual_required")
