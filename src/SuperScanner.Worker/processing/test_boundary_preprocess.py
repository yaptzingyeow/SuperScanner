"""Regression checks for model input geometry."""

import unittest

import numpy as np

from boundary.preprocess import letterbox


class BoundaryPreprocessTests(unittest.TestCase):
    def test_letterbox_preserves_aspect_ratio_and_inverts_points(self):
        image = np.zeros((2000, 1500, 3), np.uint8)

        prepared = letterbox(image, 320)
        source = prepared.to_source_points(
            np.array([[40, 0], [280, 320]], np.float32))

        self.assertEqual(prepared.tensor_bgr.shape, (320, 320, 3))
        self.assertEqual(prepared.scale, .16)
        self.assertEqual((prepared.pad_x, prepared.pad_y), (40, 0))
        np.testing.assert_allclose(source, [[0, 0], [1499, 1999]], atol=1)

    def test_letterbox_rejects_invalid_input_size(self):
        with self.assertRaisesRegex(ValueError, "input_size"):
            letterbox(np.zeros((20, 20, 3), np.uint8), 1)

    def test_letterbox_rejects_non_color_image(self):
        with self.assertRaisesRegex(ValueError, "BGR"):
            letterbox(np.zeros((20, 20), np.uint8), 320)


if __name__ == "__main__":
    unittest.main()
