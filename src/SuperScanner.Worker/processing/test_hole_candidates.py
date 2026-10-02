import unittest
import cv2
import numpy as np
from hole_candidates import find_hole_candidates


class HoleCandidateTests(unittest.TestCase):
    def test_filled_margin_hole_is_suggested_but_printed_ring_and_rule_are_not(self):
        image = np.full((700, 500, 3), 245, np.uint8)
        cv2.circle(image, (35, 220), 12, (20, 20, 20), -1)
        cv2.circle(image, (35, 320), 12, (20, 20, 20), 2)
        cv2.line(image, (35, 420), (180, 420), (20, 20, 20), 2)
        boxes = find_hole_candidates(image)
        self.assertEqual(len(boxes), 1)
        self.assertLess(boxes[0][0], .1)
        self.assertTrue(.28 < boxes[0][1] < .34)

    def test_protected_region_blocks_candidate(self):
        image = np.full((700, 500, 3), 245, np.uint8)
        cv2.circle(image, (35, 220), 12, (20, 20, 20), -1)
        self.assertEqual(find_hole_candidates(image, [(0, .25, .15, .4)]), [])

    def test_nonmargin_circle_is_not_suggested(self):
        image = np.full((700, 500, 3), 245, np.uint8)
        cv2.circle(image, (250, 220), 12, (20, 20, 20), -1)
        self.assertEqual(find_hole_candidates(image), [])

    def test_realistic_crescent_holes_aligned_in_margin_are_suggested(self):
        image = np.full((900,600,3),205,np.uint8)
        for y in (250,650):
            cv2.circle(image,(45,y),14,(55,55,55),-1)
            cv2.circle(image,(49,y-3),11,(205,205,205),-1)
        boxes=find_hole_candidates(image)
        self.assertGreaterEqual(len(boxes),2)
        self.assertTrue(any(.26 < box[1] < .3 for box in boxes))
        self.assertTrue(any(.69 < box[1] < .75 for box in boxes))
