"""Contract tests for checksum-verified ONNX segmentation."""

import hashlib
import json
from pathlib import Path
import tempfile
import unittest

import numpy as np

from boundary.onnx_segmenter import ModelConfigurationError, OnnxDocumentSegmenter


FIXTURE_MODEL = (Path(__file__).parents[3] / "tests" / "fixtures" /
                 "document-boundary" / "tiny-segmenter.onnx")


class OnnxDocumentSegmenterTests(unittest.TestCase):
    def setUp(self):
        self.temporary_directory = tempfile.TemporaryDirectory()
        self.directory = Path(self.temporary_directory.name)
        self.model = self.directory / "tiny-segmenter.onnx"
        self.model.write_bytes(FIXTURE_MODEL.read_bytes())

    def tearDown(self):
        self.temporary_directory.cleanup()

    def write_metadata(self, sha256: str) -> Path:
        metadata = {
            "enabled": True,
            "modelVersion": "tiny-fixture-1",
            "fileName": self.model.name,
            "sha256": sha256,
            "license": "Apache-2.0",
            "source": "tests/fixtures/document-boundary/create_fixture_model.py",
            "inputSize": 320,
            "inputLayout": "NCHW",
            "colorOrder": "RGB",
            "normalization": "imagenet",
        }
        path = self.directory / "model.json"
        path.write_text(json.dumps(metadata), encoding="utf-8")
        return path

    def test_rejects_model_when_sha256_does_not_match(self):
        metadata_path = self.write_metadata("0" * 64)

        with self.assertRaisesRegex(ModelConfigurationError, "checksum"):
            OnnxDocumentSegmenter(metadata_path)

    def test_returns_finite_probability_mask_in_letterbox_space(self):
        digest = hashlib.sha256(self.model.read_bytes()).hexdigest()
        segmenter = OnnxDocumentSegmenter(self.write_metadata(digest))
        image = np.zeros((2000, 1500, 3), np.uint8)

        prediction = segmenter.predict(image)

        self.assertEqual(prediction.probability_mask.shape, (320, 320))
        self.assertTrue(np.isfinite(prediction.probability_mask).all())
        self.assertGreaterEqual(float(prediction.probability_mask.min()), 0)
        self.assertLessEqual(float(prediction.probability_mask.max()), 1)
        self.assertEqual(prediction.model_version, "tiny-fixture-1")
        self.assertEqual(prediction.mapping.source_width, 1500)
        self.assertEqual(prediction.mapping.source_height, 2000)


if __name__ == "__main__":
    unittest.main()
