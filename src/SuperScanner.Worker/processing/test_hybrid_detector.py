import unittest

import numpy as np

from boundary.confidence import ConfidencePolicy
from boundary.contracts import BoundaryPoint, DocumentBoundaryResult
from boundary.geometry import GeometryEstimate, GeometryEvidence
from boundary.hybrid import HybridBoundaryDetector
from boundary.onnx_segmenter import ModelConfigurationError, ModelInferenceError


POINTS = np.asarray([[.1, .1], [.9, .1], [.9, .9], [.1, .9]], dtype=np.float32)


def ai_result(strength: float) -> tuple[GeometryEstimate, str]:
    evidence = GeometryEvidence(
        strength, .64, strength,
        (strength, strength, strength, strength), strength, strength)
    return GeometryEstimate(POINTS, evidence), "test-model-1"


def opencv_result(confidence: float) -> DocumentBoundaryResult:
    source = "OpenCvFallback" if confidence else "FullImage"
    return DocumentBoundaryResult(
        tuple(BoundaryPoint(float(x), float(y)) for x, y in POINTS),
        confidence,
        source,
        None,
        "opencv_candidate" if confidence else "manual_required")


class HybridDetectorTests(unittest.TestCase):
    def detector(self, ai, opencv):
        return HybridBoundaryDetector(
            mode="AiPreferred",
            ai_detector=ai,
            opencv_detector=opencv,
            policy=ConfidencePolicy())

    def test_high_confidence_ai_result_is_selected(self):
        result = self.detector(lambda _: ai_result(.94), lambda _: opencv_result(.5)).detect(
            np.zeros((10, 10, 3), dtype=np.uint8))
        self.assertEqual("Ai", result.source)
        self.assertGreaterEqual(result.confidence, .78)

    def test_low_confidence_ai_runs_opencv_fallback(self):
        result = self.detector(lambda _: ai_result(.2), lambda _: opencv_result(.72)).detect(
            np.zeros((10, 10, 3), dtype=np.uint8))
        self.assertEqual("OpenCvFallback", result.source)

    def test_two_unreliable_detectors_require_manual_selection(self):
        result = self.detector(lambda _: None, lambda _: opencv_result(.2)).detect(
            np.zeros((10, 10, 3), dtype=np.uint8))
        self.assertEqual("FullImage", result.source)
        self.assertEqual(0, result.confidence)

    def test_known_model_failure_falls_back_with_safe_diagnostic(self):
        def failed_ai(_):
            raise ModelConfigurationError("secret model path is unavailable")

        result = self.detector(failed_ai, lambda _: opencv_result(.72)).detect(
            np.zeros((10, 10, 3), dtype=np.uint8))
        self.assertEqual("OpenCvFallback", result.source)
        self.assertEqual("ai_model_missing_opencv_candidate", result.diagnostics_code)
        self.assertNotIn("secret", result.diagnostics_code)

    def test_programming_errors_are_not_swallowed(self):
        def broken_ai(_):
            raise TypeError("programming error")

        with self.assertRaises(TypeError):
            self.detector(broken_ai, lambda _: opencv_result(.72)).detect(
                np.zeros((10, 10, 3), dtype=np.uint8))

    def test_manual_and_opencv_only_modes_do_not_invoke_ai(self):
        def forbidden(_):
            raise AssertionError("AI must not run")

        image = np.zeros((10, 10, 3), dtype=np.uint8)
        manual = HybridBoundaryDetector(
            "ManualOnly", forbidden, lambda _: opencv_result(.8), ConfidencePolicy()).detect(image)
        opencv = HybridBoundaryDetector(
            "OpenCvOnly", forbidden, lambda _: opencv_result(.8), ConfidencePolicy()).detect(image)
        self.assertEqual("FullImage", manual.source)
        self.assertEqual("OpenCvFallback", opencv.source)


if __name__ == "__main__":
    unittest.main()
