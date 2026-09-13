import json
import unittest

import numpy as np

from benchmark import page_passes, summarize


def corners(left=.1, top=.1, right=.9, bottom=.9):
    return np.asarray([
        [left, top], [right, top], [right, bottom], [left, bottom]
    ], dtype=np.float32)


def case(max_error=0, confidence=.9, source="Ai", valid=True, tags=None):
    return {
        "id": "case-001",
        "tags": tags or ["clean"],
        "maxError": max_error,
        "confidence": confidence,
        "source": source,
        "valid": valid,
        "latencyMs": 12.5,
        "modelVersion": "candidate-1" if source == "Ai" else None,
    }


class BenchmarkMetricTests(unittest.TestCase):
    def test_page_pass_requires_every_corner_within_two_percent(self):
        expected = corners()
        actual = expected.copy()
        actual[2, 0] += .021
        self.assertFalse(page_passes(expected, actual, tolerance=.02))

    def test_high_confidence_five_percent_miss_blocks_promotion(self):
        results = [case() for _ in range(59)] + [case(max_error=.051, confidence=.9)]
        summary = summarize(results)
        self.assertEqual(1, summary["confidenceViolations"])
        self.assertFalse(summary["promotionPassed"])

    def test_report_contains_no_image_paths_or_document_text(self):
        report = json.dumps(summarize([case()]))
        self.assertNotIn("C:\\", report)
        self.assertNotIn("recognizedText", report)
        self.assertNotIn("image", report.lower())

    def test_minimum_sixty_cases_is_required(self):
        summary = summarize([case() for _ in range(59)])
        self.assertFalse(summary["promotionPassed"])
        self.assertIn("minimum_cases_not_met", summary["failureReasons"])


if __name__ == "__main__":
    unittest.main()
