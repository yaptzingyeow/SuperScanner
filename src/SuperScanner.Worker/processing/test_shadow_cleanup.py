"""Synthetic paper fixtures: no private document content is used."""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

import cv2
import numpy as np

from shadow_cleanup import remove_shadows


def shaded_page():
    page = np.full((600, 480, 3), 235, np.uint8)
    cv2.putText(page, "Sample 12345", (35, 190), cv2.FONT_HERSHEY_SIMPLEX,
                .8, (35, 35, 35), 2, cv2.LINE_AA)
    cv2.line(page, (30, 300), (440, 300), (170, 85, 30), 2)
    cv2.putText(page, "Faint pencil", (35, 410), cv2.FONT_HERSHEY_SIMPLEX,
                .65, (165, 165, 165), 1, cv2.LINE_AA)
    illumination = np.linspace(.48, 1.0, 480, dtype=np.float32)[None, :, None]
    return np.rint(page * illumination).astype(np.uint8), page


class ShadowCleanupTests(unittest.TestCase):
    def test_corrects_mild_blue_cast_without_erasing_pencil_or_blue_ink(self):
        image, reference = shaded_page()
        image = np.rint(image.astype(np.float32) * np.array([1.0, .91, .84])).astype(np.uint8)
        result = remove_shadows(image)
        paper = result[50:100].mean(axis=(0, 1))
        self.assertLess(float(paper.max() - paper.min()), 8)
        faint = np.all(reference == 165, axis=2)
        self.assertLess(float(result[faint].mean()), float(paper.mean()) - 30)
        self.assertGreater(int(result[300, 200, 0]), int(result[300, 200, 2]) + 50)

    def test_reduces_paper_gradient_without_erasing_dark_or_faint_strokes(self):
        image, reference = shaded_page()
        before = image.copy()
        result = remove_shadows(image)
        np.testing.assert_array_equal(image, before)
        self.assertEqual(image.shape, result.shape)
        self.assertEqual(np.uint8, result.dtype)
        self.assertLess(result[50:100, :, 0].std(), image[50:100, :, 0].std() * .35)
        dark = reference[:, :, 0] == 35
        self.assertLess(float(result[dark].mean()), 100)
        faint = np.all(reference == 165, axis=2)
        self.assertLess(float(result[faint].mean()), float(result[50:100].mean()) - 30)

    def test_preserves_coloured_rule_and_neutral_paper(self):
        image, _ = shaded_page()
        result = remove_shadows(image)
        self.assertGreater(int(result[300, 200, 0]), int(result[300, 200, 1]) + 30)
        self.assertGreater(int(result[300, 200, 1]), int(result[300, 200, 2]) + 15)
        self.assertEqual(int(result[50, 50, 0]), int(result[50, 50, 2]))

    def test_uniform_paper_and_black_image_remain_unchanged(self):
        for color in [(220, 220, 220), (195, 218, 233), (0, 0, 0)]:
            image = np.full((100, 160, 3), color, np.uint8)
            np.testing.assert_array_equal(image, remove_shadows(image))

    def test_tiny_images_are_stable(self):
        for height, width in [(2, 2), (3, 20), (20, 3)]:
            image = np.full((height, width, 3), 120, np.uint8)
            np.testing.assert_array_equal(image, remove_shadows(image))

    def test_invalid_arrays_are_rejected(self):
        for image in [None, np.zeros((0, 5, 3), np.uint8), np.zeros((5, 5), np.uint8),
                      np.zeros((5, 5, 3), np.float32)]:
            with self.assertRaises(ValueError):
                remove_shadows(image)

    def test_real_crop_cli_accepts_filter_and_writes_enhanced_thumbnail(self):
        with tempfile.TemporaryDirectory() as directory:
            source, output, thumb = [Path(directory) / name for name in
                                     ("source.png", "result.jpg", "thumb.jpg")]
            image, _ = shaded_page()
            cv2.imwrite(str(source), image)
            corners = [{"x": 0, "y": 0}, {"x": 1, "y": 0},
                       {"x": 1, "y": 1}, {"x": 0, "y": 1}]
            completed = subprocess.run([sys.executable, str(Path(__file__).with_name("crop_image.py")),
                "apply", str(source), json.dumps(corners), str(output), str(thumb), "RemoveShadows"],
                capture_output=True, text=True, timeout=25)
            self.assertEqual(0, completed.returncode, completed.stderr)
            rendered = cv2.imread(str(output))
            self.assertIsNotNone(rendered)
            self.assertLess(rendered[20:60, :, 0].std(), image[20:60, :, 0].std() * .4)
            self.assertLessEqual(max(cv2.imread(str(thumb)).shape[:2]), 320)


if __name__ == "__main__":
    unittest.main()
