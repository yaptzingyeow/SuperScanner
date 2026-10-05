import unittest

import cv2
import numpy as np

from masked_repair import repair_masked_image


class MaskedRepairTests(unittest.TestCase):
    def setUp(self):
        self.image = np.full((300, 220, 3), 240, np.uint8)
        cv2.circle(self.image, (15, 100), 8, (25, 25, 25), -1)
        cv2.line(self.image, (60, 150), (190, 150), (10, 10, 10), 2)

    def test_removes_only_confirmed_mask_and_preserves_every_other_pixel(self):
        repaired = repair_masked_image(self.image, [(5, 90, 25, 110)])
        self.assertTrue(np.array_equal(repaired[150], self.image[150]))
        self.assertTrue(np.array_equal(repaired[:, 30:], self.image[:, 30:]))
        self.assertGreater(int(repaired[100, 15, 0]), 180)

    def test_rejects_mask_over_protected_text(self):
        with self.assertRaises(ValueError):
            repair_masked_image(self.image, [(5, 90, 25, 110)], [(10, 95, 20, 105)])

    def test_rejects_oversized_and_out_of_bounds_masks(self):
        for mask in [[(0, 0, 220, 300)], [(-1, 0, 20, 20)], [(5, 90, 5, 110)]]:
            with self.subTest(mask=mask), self.assertRaises(ValueError):
                repair_masked_image(self.image, mask)

    def test_does_not_mutate_source(self):
        before = self.image.copy()
        repair_masked_image(self.image, [(5, 90, 25, 110)])
        self.assertTrue(np.array_equal(self.image, before))

    def test_brush_stroke_clears_only_thin_selected_ink(self):
        repaired = repair_masked_image(self.image, [], strokes=[(9, [(15, 95), (15, 105)])])
        self.assertGreater(int(repaired[100, 15, 0]), 180)
        self.assertTrue(np.array_equal(repaired[:, 40:], self.image[:, 40:]))

    def test_eraser_brush_may_cover_recognised_text_and_changes_only_painted_pixels(self):
        # The Eraser is deliberate: painting over a recognised word erases it.
        repaired = repair_masked_image(self.image, [], [(12, 98, 18, 102)],
                                       strokes=[(9, [(15, 95), (15, 105)])])
        self.assertGreater(int(repaired[100, 15, 0]), 180)
        self.assertTrue(np.array_equal(repaired[:, 40:], self.image[:, 40:]))

    def test_eraser_brush_erases_part_of_a_line_and_keeps_the_rest(self):
        repaired = repair_masked_image(self.image, [], strokes=[(4, [(100, 150), (120, 150)])])
        self.assertGreater(int(repaired[150, 110, 0]), 180)
        self.assertTrue(np.array_equal(repaired[150, 130:], self.image[150, 130:]))
        self.assertTrue(np.array_equal(repaired[150, :90], self.image[150, :90]))

    def test_rectangle_still_cannot_cover_protected_content(self):
        with self.assertRaises(ValueError):
            repair_masked_image(self.image, [(5, 90, 25, 110)], [(10, 95, 20, 105)],
                                strokes=[(9, [(150, 50), (160, 50)])])

    def test_rejects_a_rule_crossing_a_cleanup_area_without_ocr(self):
        with self.assertRaisesRegex(ValueError, 'crosses page content'):
            repair_masked_image(self.image, [(95, 145, 125, 155)])
