import unittest

import cv2
import numpy as np

from boundary.geometry import estimate_boundary
from boundary.preprocess import LetterboxedImage


SIZE = 320


def identity_mapping(size: int = SIZE) -> LetterboxedImage:
    return LetterboxedImage(
        tensor_bgr=np.zeros((size, size, 3), dtype=np.uint8),
        scale=1.0,
        pad_x=0,
        pad_y=0,
        source_width=size,
        source_height=size)


def polygon_mask(points, value: float = .92) -> np.ndarray:
    mask = np.full((SIZE, SIZE), .04, dtype=np.float32)
    cv2.fillPoly(mask, [np.asarray(points, dtype=np.int32)], value)
    return mask


class BoundaryGeometryTests(unittest.TestCase):
    def test_fits_supporting_lines_around_folded_outer_mask(self):
        expected = np.asarray([[.14, .09], [.88, .08], [.96, .92], [.06, .93]])
        points = np.rint(expected * (SIZE - 1)).astype(np.int32)
        mask = polygon_mask(points)

        # Simulate a folded/dark top-left corner by cutting a triangular notch
        # from the segmentation while leaving both adjoining sides supported.
        cv2.fillPoly(mask, [np.asarray([[45, 29], [78, 29], [45, 65]])], .08)
        mask[:, :120] *= np.linspace(.72, 1.0, 120, dtype=np.float32)[None, :]

        result = estimate_boundary(mask, identity_mapping(), .52)

        self.assertIsNotNone(result)
        np.testing.assert_allclose(result.points[0], [.14, .09], atol=.02)
        np.testing.assert_allclose(result.points, expected, atol=.025)

    def test_rejects_internal_rectangle_when_outer_mask_exists(self):
        outer = np.asarray([[40, 27], [286, 25], [307, 298], [18, 300]])
        mask = polygon_mask(outer, .76)
        cv2.rectangle(mask, (92, 73), (244, 262), .99, 5)

        result = estimate_boundary(mask, identity_mapping(), .52)

        self.assertIsNotNone(result)
        self.assertLess(result.points[0][1], .12)
        self.assertLess(result.points[1][1], .12)

    def test_returns_clockwise_finite_normalized_points(self):
        mask = polygon_mask([[54, 35], [282, 44], [299, 286], [31, 299]])
        result = estimate_boundary(mask, identity_mapping(), .52)

        self.assertIsNotNone(result)
        self.assertEqual((4, 2), result.points.shape)
        self.assertTrue(np.isfinite(result.points).all())
        self.assertTrue(((result.points >= 0) & (result.points <= 1)).all())
        cross_products = []
        for index in range(4):
            a = result.points[index]
            b = result.points[(index + 1) % 4]
            c = result.points[(index + 2) % 4]
            first = b - a
            second = c - b
            cross_products.append(first[0] * second[1] - first[1] * second[0])
        self.assertTrue(all(value > 0 for value in cross_products))

    def test_ignores_disconnected_noise(self):
        mask = polygon_mask([[48, 31], [281, 35], [302, 294], [25, 298]])
        rng = np.random.default_rng(17)
        for x, y in rng.integers(0, SIZE, size=(150, 2)):
            cv2.circle(mask, (int(x), int(y)), 1, .98, -1)

        result = estimate_boundary(mask, identity_mapping(), .52)

        self.assertIsNotNone(result)
        np.testing.assert_allclose(result.points[0], [48 / 319, 31 / 319], atol=.025)

    def test_rejects_empty_and_near_full_masks(self):
        empty = np.zeros((SIZE, SIZE), dtype=np.float32)
        near_full = np.ones((SIZE, SIZE), dtype=np.float32)

        self.assertIsNone(estimate_boundary(empty, identity_mapping(), .52))
        self.assertIsNone(estimate_boundary(near_full, identity_mapping(), .52))


if __name__ == "__main__":
    unittest.main()
