import unittest
import cv2
import numpy as np
from crop_image import apply_filter


class CleanDocumentTests(unittest.TestCase):
    def test_removes_isolated_dust_but_preserves_punctuation_next_to_text(self):
        image = np.full((240, 320, 3), 230, np.uint8)
        image[25:27, 25:27] = 0
        cv2.rectangle(image, (100, 100), (106, 116), (0, 0, 0), -1)
        image[114:116, 109:111] = 0
        result = apply_filter(image, 'CleanDocument')
        self.assertTrue(np.all(result[25:27, 25:27] == 255))
        self.assertLess(int(result[114, 109]), 40)

    def test_clean_modes_whiten_paper_and_preserve_dark_rules(self):
        gray = np.full((240,320),220,np.uint8)
        cv2.line(gray,(30,90),(290,90),30,2)
        gray=(gray*np.linspace(.55,1,320)[None,:]).astype(np.uint8)
        image=cv2.cvtColor(gray,cv2.COLOR_GRAY2BGR)
        original=image.copy()
        for mode in ['CleanDocumentGentle','CleanDocument','CleanDocumentStrong']:
            result=apply_filter(image,mode)
            self.assertEqual(result.shape,gray.shape)
            self.assertGreater(np.mean(result[20:50,30:290]),250)
            self.assertLess(np.mean(result[90,40:280]),30)
        np.testing.assert_array_equal(image,original)

    def test_stronger_cleanup_removes_more_faint_marks(self):
        image=np.full((240,320,3),220,np.uint8)
        cv2.line(image,(30,90),(290,90),(180,180,180),2)
        gentle=apply_filter(image,'CleanDocumentGentle')
        strong=apply_filter(image,'CleanDocumentStrong')
        self.assertGreater(float(strong[90,100]),float(gentle[90,100]))

    def test_tiny_image_is_supported(self):
        self.assertEqual(apply_filter(np.full((2,2,3),220,np.uint8),'CleanDocument').shape,(2,2))
