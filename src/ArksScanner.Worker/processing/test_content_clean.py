import unittest

import cv2
import numpy as np

from crop_image import apply_filter


class ContentCleanTests(unittest.TestCase):
    def test_boxed_page_rehomes_frame_and_ink_without_dirty_outer_margin(self):
        image = np.full((900, 700, 3), 220, np.uint8)
        cv2.rectangle(image, (120, 120), (580, 780), (20, 20, 20), 3)
        cv2.putText(image, 'Signed', (240, 440), cv2.FONT_HERSHEY_SIMPLEX,
                    1.7, (15, 15, 15), 3)
        cv2.circle(image, (45, 450), 20, (0, 0, 0), -1)

        result = apply_filter(image, 'ContentClean')

        self.assertEqual(result.ndim, 2)
        self.assertAlmostEqual(result.shape[1] / result.shape[0], 210 / 297, delta=.002)
        self.assertGreater(np.mean(result[:, :75]), 253)
        self.assertGreater(np.count_nonzero(result[350:650, 200:550] < 100), 200)

    def test_without_enclosing_frame_preserves_marginal_content(self):
        image = np.full((900, 700, 3), 220, np.uint8)
        cv2.putText(image, 'NOTE', (35, 100), cv2.FONT_HERSHEY_SIMPLEX,
                    1.4, (0, 0, 0), 3)
        cv2.line(image, (90, 450), (600, 450), (0, 0, 0), 3)

        result = apply_filter(image, 'ContentClean')

        self.assertGreater(np.count_nonzero(result[:180, :300] < 120), 50)
        self.assertGreater(np.count_nonzero(result[400:600] < 120), 100)

    def test_text_touching_frame_does_not_cut_off_lower_content(self):
        image = np.full((900, 700, 3), 220, np.uint8)
        cv2.rectangle(image, (120, 100), (580, 800), (20, 20, 20), 2)
        cv2.line(image, (120, 480), (500, 480), (20, 20, 20), 2)
        cv2.putText(image, 'BOTTOM', (230, 710), cv2.FONT_HERSHEY_SIMPLEX,
                    1.2, (15, 15, 15), 3)

        from content_clean import _content_frame
        frame = _content_frame(image)

        self.assertIsNotNone(frame)
        self.assertGreater(frame[3], 790)

    def test_slightly_slanted_printed_frame_is_detected_after_perspective_crop(self):
        image = np.full((900, 700, 3), 220, np.uint8)
        corners = np.array([[120, 100], [580, 120], [580, 800], [120, 800]], np.int32)
        cv2.polylines(image, [corners], True, (20, 20, 20), 2)
        cv2.putText(image, 'BOTTOM', (230, 710), cv2.FONT_HERSHEY_SIMPLEX,
                    1.2, (15, 15, 15), 3)

        from content_clean import _content_frame
        frame = _content_frame(image)

        self.assertIsNotNone(frame)
        self.assertLess(frame[1], 110)
        self.assertGreater(frame[3], 790)

    def test_missing_top_border_does_not_crop_content_above_interior_rule(self):
        image = np.full((900, 700, 3), 220, np.uint8)
        cv2.line(image, (120, 100), (120, 800), (20, 20, 20), 2)
        cv2.line(image, (580, 100), (580, 800), (20, 20, 20), 2)
        cv2.line(image, (120, 300), (580, 300), (20, 20, 20), 2)
        cv2.line(image, (120, 800), (580, 800), (20, 20, 20), 2)
        cv2.putText(image, 'ABOVE', (230, 200), cv2.FONT_HERSHEY_SIMPLEX,
                    1, (0, 0, 0), 2)

        from content_clean import _content_frame

        self.assertIsNone(_content_frame(image))


if __name__ == '__main__':
    unittest.main()
