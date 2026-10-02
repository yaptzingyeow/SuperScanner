"""Focused boundary regression checks; run with the crop runtime Python."""
import unittest
from unittest.mock import patch
import cv2
import numpy as np
from crop_image import detect, detect_with_opencv, refine_truncated_bottom
from boundary.contracts import BoundaryPoint, DocumentBoundaryResult
from boundary.refine_edges import refine_paper_edges


class PaperDetectionTests(unittest.TestCase):
    def test_refines_bottom_edge_when_shaded_paper_is_darker_than_desk(self):
        image = np.full((600, 450, 3), 225, np.uint8)
        cv2.rectangle(image, (40, 40), (410, 530), (180, 180, 180), -1)
        cv2.line(image, (80, 470), (360, 470), (30, 30, 30), 2)
        corners = np.array([[40/449, 40/599], [410/449, 40/599],
                            [410/449, 555/599], [40/449, 555/599]])
        result = refine_paper_edges(image, corners)
        self.assertLess(abs(result[2][1] * 599 - 530), 6)
        self.assertLess(abs(result[3][1] * 599 - 530), 6)

    def test_grabcut_uses_bounded_preview_and_single_iteration(self):
        image = np.full((2000, 1500, 3), 220, np.uint8)
        image[:, :300] = 40
        observed = {}

        def capture(source, mask, rectangle, background, foreground, iterations, mode):
            observed["shape"] = source.shape[:2]
            observed["iterations"] = iterations
            mask[:] = cv2.GC_PR_FGD

        with patch("crop_image.cv2.grabCut", side_effect=capture):
            detect_with_opencv(image)

        self.assertLessEqual(max(observed["shape"]), 700)
        self.assertEqual(observed["iterations"], 1)

    def test_boundary_result_serializes_provider_neutral_fields(self):
        result = DocumentBoundaryResult(
            points=(BoundaryPoint(.1, .1), BoundaryPoint(.9, .1),
                    BoundaryPoint(.9, .9), BoundaryPoint(.1, .9)),
            confidence=.82,
            source="OpenCvFallback",
            model_version=None,
            diagnostics_code="opencv_candidate")

        payload = result.to_json_dict()

        self.assertEqual(payload["source"], "OpenCvFallback")
        self.assertEqual(payload["confidence"], .82)
        self.assertEqual(payload["modelVersion"], None)
        self.assertEqual(payload["diagnosticsCode"], "opencv_candidate")
        self.assertEqual(payload["points"], [
            {"x": .1, "y": .1}, {"x": .9, "y": .1},
            {"x": .9, "y": .9}, {"x": .1, "y": .9},
        ])

    def test_folded_corner_with_soft_shadow(self):
        image = np.full((900, 700, 3), 45, np.uint8)
        outline = np.array([[160, 110], [570, 100], [610, 810],
                            [95, 800], [100, 185]], np.int32)
        cv2.fillConvexPoly(image, outline, (220, 220, 220))
        for row in range(900):
            image[row] = np.clip(image[row].astype(float) *
                                 (0.65 + 0.35 * row / 900), 0, 255).astype(np.uint8)
        for row in range(260, 700, 35):
            cv2.line(image, (180, row), (500, row), (55, 55, 55), 2)
        result = detect(image)
        self.assertEqual(result['source'], 'Automatic')
        points = np.array([[p['x'] * 699, p['y'] * 899] for p in result['points']])
        # Like dedicated scanner apps, the corner is where the straight top and
        # left paper edges meet, so no paper content is cut; the Magic filter
        # whitens the small desk triangle left by the folded flap.
        self.assertLess(np.linalg.norm(points[0] - [100.6, 111.5]), 20)
        self.assertLess(np.linalg.norm(points[2] - [610, 810]), 20)

    def test_empty_image_needs_manual_selection(self):
        self.assertEqual(detect(np.zeros((600, 400, 3), np.uint8))['source'], 'FullImage')
        self.assertEqual(detect(np.full((600, 400, 3), 255, np.uint8))['source'], 'FullImage')

    def test_jpeg_keyboard_above_paper_does_not_become_top_edge(self):
        image = np.full((1000, 750, 3), (35, 28, 24), np.uint8)
        for column in range(15, 735, 85):
            cv2.rectangle(image, (column, 5), (column + 65, 155), (205, 205, 198), -1)
        # The extra upper-left vertex represents a folded flap. The crop must
        # begin at the inner paper corner, not the flap's outer tip.
        paper = np.array([[112, 92], [660, 88], [749, 948], [65, 975],
                          [90, 160], [45, 110]], np.int32)
        cv2.fillPoly(image, [paper], (218, 218, 210))
        for row in range(210, 790, 32):
            cv2.line(image, (210, row), (570, row), (75, 75, 72), 2)
        encoded = cv2.imencode('.jpg', image, [cv2.IMWRITE_JPEG_QUALITY, 88])[1]
        recompressed = cv2.imdecode(encoded, cv2.IMREAD_COLOR)

        result = detect(recompressed)

        self.assertEqual(result['source'], 'Automatic')
        self.assertGreater(result['points'][0]['y'], .05)
        self.assertGreater(result['points'][1]['y'], .05)
        self.assertGreater(result['points'][0]['x'], .12)

    def test_long_header_rule_is_not_used_as_paper_edge(self):
        image = np.full((1000, 750, 3), 35, np.uint8)
        paper = np.array([[92, 82], [710, 80], [749, 940], [28, 930]], np.int32)
        cv2.fillConvexPoly(image, paper, (225, 225, 225))
        cv2.line(image, (175, 190), (705, 194), (40, 40, 40), 2)
        for row in range(260, 760, 35):
            cv2.line(image, (175, row), (570, row), (80, 80, 80), 2)

        result = detect(image)

        self.assertEqual(result['source'], 'Automatic')
        self.assertLess(result['points'][0]['y'], .12)
        self.assertLess(result['points'][1]['y'], .12)

    def test_top_rule_does_not_move_paper_corner_inward(self):
        image = np.full((1000, 750, 3), 35, np.uint8)
        paper = np.array([[92, 82], [710, 80], [749, 940], [28, 930]], np.int32)
        cv2.fillConvexPoly(image, paper, (225, 225, 225))
        # A printed rule starting well inside the sheet is not its outer edge.
        # Mocking the Hough segment isolates the historical false-positive:
        # its y-position aligns with the paper top, but its x-start does not.
        # The detector downsamples to 525 x 700 before asking Hough for lines.
        internal_rule = np.array([[[133, 57, 490, 57]]], np.int32)
        with patch('crop_image.cv2.HoughLinesP', return_value=internal_rule):
            result = detect(image)

        self.assertEqual(result['source'], 'Automatic')
        self.assertLess(result['points'][0]['x'], .17)

    def test_bottom_sheet_edge_replaces_image_border_when_supported(self):
        image = np.full((700, 525, 3), 125, np.uint8)
        paper = np.array([[65, 62], [492, 62], [524, 640], [18, 634]], np.int32)
        cv2.fillConvexPoly(image, paper, (220, 220, 220))
        corners = np.array([[.125, .09], [.94, .09], [.998, .99], [.10, .998]])
        edge_segments = np.array([
            [[18, 634, 132, 634]], [[135, 635, 469, 641]],
            [[170, 646, 510, 657]],
        ], np.int32)

        with patch('crop_image.cv2.HoughLinesP', return_value=edge_segments):
            result = refine_truncated_bottom(image, corners)

        self.assertLess(result[3, 1], .93)
        self.assertLess(result[2, 1], .94)
        self.assertLess(result[3, 0], .06)

    def test_bottom_printed_rule_without_background_transition_is_ignored(self):
        image = np.full((700, 525, 3), 220, np.uint8)
        cv2.line(image, (18, 634), (469, 641), (30, 30, 30), 2)
        corners = np.array([[.125, .09], [.94, .09], [.998, .99], [.10, .998]])
        edge_segments = np.array([[[18, 634, 132, 634]], [[135, 635, 469, 641]]], np.int32)

        with patch('crop_image.cv2.HoughLinesP', return_value=edge_segments):
            result = refine_truncated_bottom(image, corners)

        np.testing.assert_array_equal(result, corners)


if __name__ == '__main__':
    unittest.main()
