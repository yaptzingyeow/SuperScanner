import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import cv2
import numpy as np


SCRIPT = Path(__file__).with_name("crop_image.py")


def fixture_image(path: Path) -> None:
    image = np.full((480, 360, 3), 35, dtype=np.uint8)
    points = np.asarray([[42, 35], [327, 47], [344, 448], [20, 455]], dtype=np.int32)
    cv2.fillPoly(image, [points], (238, 238, 238))
    cv2.imwrite(str(path), image)


def run_crop_cli(mode: str, metadata: str | None = None) -> subprocess.CompletedProcess:
    temporary = tempfile.NamedTemporaryFile(suffix=".jpg", delete=False)
    temporary.close()
    path = Path(temporary.name)
    fixture_image(path)
    environment = os.environ.copy()
    environment["SUPERSCANNER_BOUNDARY_MODE"] = mode
    if metadata is not None:
        environment["SUPERSCANNER_BOUNDARY_MODEL_METADATA"] = metadata
    try:
        return subprocess.run(
            [sys.executable, str(SCRIPT), "detect", str(path)],
            env=environment,
            capture_output=True,
            text=True,
            timeout=20,
            check=False)
    finally:
        path.unlink(missing_ok=True)


class CropCliTests(unittest.TestCase):
    def test_detect_cli_emits_exact_provider_neutral_shape(self):
        completed = run_crop_cli("OpenCvOnly")
        self.assertEqual(0, completed.returncode, completed.stderr)
        payload = json.loads(completed.stdout)
        self.assertEqual({
            "points", "confidence", "source", "modelVersion", "diagnosticsCode"
        }, set(payload))
        self.assertNotIn("probabilityMask", payload)

    def test_manual_only_emits_full_image(self):
        completed = run_crop_cli("ManualOnly")
        payload = json.loads(completed.stdout)
        self.assertEqual("FullImage", payload["source"])
        self.assertEqual(0, payload["confidence"])

    def test_ai_preferred_with_absent_model_falls_back_without_crashing(self):
        missing = str(Path(tempfile.gettempdir()) / "private-missing-model.json")
        completed = run_crop_cli("AiPreferred", missing)
        self.assertEqual(0, completed.returncode, completed.stderr)
        payload = json.loads(completed.stdout)
        self.assertIn(payload["source"], {"OpenCvFallback", "FullImage"})
        self.assertIn("ai_model", payload["diagnosticsCode"])
        self.assertNotIn(missing, completed.stderr)


if __name__ == "__main__":
    unittest.main()
