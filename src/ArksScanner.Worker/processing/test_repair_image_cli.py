import json
import os
import subprocess
import sys
import tempfile
import unittest

import cv2
import numpy as np


SCRIPT = os.path.join(os.path.dirname(__file__), 'repair_image.py')


class RepairImageCliTests(unittest.TestCase):
    def test_large_protected_list_can_be_read_from_request_file(self):
        with tempfile.TemporaryDirectory() as directory:
            source = os.path.join(directory, 'source.png')
            request_path = os.path.join(directory, 'request.json')
            cv2.imwrite(source, np.full((300, 220, 3), 245, np.uint8))
            protected = [[.5, .5, .51, .51] for _ in range(1000)]
            with open(request_path, 'w', encoding='utf-8') as file:
                json.dump(protected, file)
            result = subprocess.run([sys.executable, SCRIPT, 'detect', source,
                '@' + request_path], capture_output=True, text=True, timeout=15)
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertIn('candidates', json.loads(result.stdout))

    def test_normalized_brush_preview_preserves_unselected_pixels(self):
        with tempfile.TemporaryDirectory() as directory:
            source = os.path.join(directory, 'source.png')
            output = os.path.join(directory, 'preview.png')
            image = np.full((300, 220, 3), 245, np.uint8)
            cv2.circle(image, (15, 100), 5, (20, 20, 20), -1)
            cv2.imwrite(source, image)
            request = {'rectangles': [], 'protected': [], 'normalized': True,
                       'strokes': [{'radius': .04, 'points': [[15/220, 95/300], [15/220, 105/300]]}]}
            result = subprocess.run([sys.executable, SCRIPT, 'preview', source, output,
                json.dumps(request)], capture_output=True, text=True, timeout=15)
            self.assertEqual(result.returncode, 0, result.stderr)
            repaired = cv2.imread(output)
            self.assertGreater(int(repaired[100, 15, 0]), 180)
            self.assertTrue(np.array_equal(repaired[:, 40:], image[:, 40:]))

    def test_detect_and_preview_without_changing_source(self):
        with tempfile.TemporaryDirectory() as directory:
            source = os.path.join(directory, 'source.png')
            output = os.path.join(directory, 'preview.png')
            image = np.full((300, 220, 3), 245, np.uint8)
            cv2.circle(image, (15, 100), 8, (20, 20, 20), -1)
            cv2.imwrite(source, image)
            detected = subprocess.run([sys.executable, SCRIPT, 'detect', source],
                                      capture_output=True, text=True, timeout=15)
            self.assertEqual(detected.returncode, 0, detected.stderr)
            self.assertTrue(json.loads(detected.stdout)['candidates'])
            repaired = subprocess.run([sys.executable, SCRIPT, 'preview', source, output,
                json.dumps({'rectangles': [[5, 90, 25, 110]], 'protected': []})],
                capture_output=True, text=True, timeout=15)
            self.assertEqual(repaired.returncode, 0, repaired.stderr)
            self.assertTrue(np.array_equal(cv2.imread(source), image))
            preview = cv2.imread(output)
            self.assertGreater(int(preview[100, 15, 0]), 180)
            self.assertTrue(np.array_equal(preview[:, 30:], image[:, 30:]))
