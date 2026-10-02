import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest

import cv2
import numpy as np

from magic_scan import detect_document, render_magic, true_aspect, warp

HERE = Path(__file__).resolve().parent


def striped_desk(h=1500, w=1125, seed=4):
    rng = np.random.default_rng(seed)
    desk = np.zeros((h, w, 3), np.uint8)
    y = 0
    while y < h:
        band = int(rng.integers(12, 40))
        tone = int(rng.integers(0, 60))
        desk[y:y + band] = (90 + tone // 2, 150 + tone, 205 + tone // 3)
        y += band
    noise = rng.normal(0, 4, desk.shape)
    return np.clip(desk + noise, 0, 255).astype(np.uint8)


def paste_quad(canvas, content, quad):
    h, w = content.shape[:2]
    src = np.float32([[0, 0], [w - 1, 0], [w - 1, h - 1], [0, h - 1]])
    M = cv2.getPerspectiveTransform(src, np.float32(quad))
    warped = cv2.warpPerspective(content, M, canvas.shape[1::-1])
    mask = cv2.warpPerspective(np.full((h, w), 255, np.uint8), M, canvas.shape[1::-1])
    out = canvas.copy()
    out[mask > 128] = warped[mask > 128]
    return out


def camera_quad(width, height, tilt_degrees, offset, shape, distance=520.0, yaw_degrees=0.0):
    """Image corners of a flat width x height rectangle seen by a phone-like pinhole camera."""
    h, w = shape
    f = .6 * np.hypot(w, h)
    corners = np.array([[-width / 2, -height / 2, 0], [width / 2, -height / 2, 0],
                        [width / 2, height / 2, 0], [-width / 2, height / 2, 0]], float)
    a = np.radians(tilt_degrees)
    rot = np.array([[1, 0, 0], [0, np.cos(a), -np.sin(a)], [0, np.sin(a), np.cos(a)]])
    b = np.radians(yaw_degrees)
    rot = np.array([[np.cos(b), 0, np.sin(b)], [0, 1, 0], [-np.sin(b), 0, np.cos(b)]]) @ rot
    cam = corners @ rot.T + [offset[0], offset[1], distance]
    return np.stack([f * cam[:, 0] / cam[:, 2] + w / 2, f * cam[:, 1] / cam[:, 2] + h / 2], 1).tolist()


def corner_error(found, expected, shape):
    return float(np.max(np.abs(np.float32(found) - np.float32(expected)) / np.float32([shape[1], shape[0]])))


class MagicDetectionTests(unittest.TestCase):
    def test_small_colour_card_on_wood_grain_is_detected(self):
        card = np.full((300, 700, 3), 255, np.uint8)
        card[:, :350] = (70, 30, 140)  # crimson left half
        for row in range(40, 260, 30):
            cv2.rectangle(card, (380, row), (660, row + 10), (40, 40, 40), -1)
        quad = [[300, 800], [860, 815], [880, 1060], [285, 1045]]  # ~13% of the photo
        photo = paste_quad(striped_desk(), card, quad)
        found, score = detect_document(photo)
        self.assertIsNotNone(found)
        self.assertLess(corner_error(found, quad, photo.shape), .01)
        self.assertGreaterEqual(score, .72)

    def test_outer_sheet_wins_over_printed_frame(self):
        photo = np.full((1400, 1050, 3), (70, 70, 70), np.uint8)
        sheet = np.full((1100, 780, 3), 238, np.uint8)
        cv2.rectangle(sheet, (120, 150), (660, 900), (20, 20, 20), 3)
        quad = [[140, 150], [910, 165], [925, 1255], [125, 1240]]
        photo = paste_quad(photo, sheet, quad)
        found, _ = detect_document(photo)
        self.assertLess(corner_error(found, quad, photo.shape), .01)

    def test_blank_image_has_no_document(self):
        found, score = detect_document(np.full((800, 600, 3), 128, np.uint8))
        self.assertIsNone(found)
        self.assertEqual(score, 0.0)

    def test_corners_are_inside_the_image(self):
        photo = np.full((1200, 900, 3), (60, 60, 60), np.uint8)
        sheet = np.full((1000, 700, 3), 240, np.uint8)
        photo = paste_quad(photo, sheet, [[80, 60], [850, 70], [880, 1199], [60, 1199]])
        found, _ = detect_document(photo)
        self.assertIsNotNone(found)
        self.assertTrue(((found >= 0) & (found <= [899, 1199])).all())

    def test_twice_folded_receipt_is_one_sheet(self):
        # Three flat panels whose sides bend slightly at each crease, with the
        # middle panel brighter, like a receipt that was folded in three.
        photo = np.full((1600, 1200, 3), (60, 60, 60), np.uint8)
        panels = [([300, 200], [800, 230], [790, 560], [310, 550], 175),
                  ([310, 550], [790, 560], [830, 1000], [280, 1010], 225),
                  ([280, 1010], [830, 1000], [860, 1420], [250, 1450], 205)]
        for *corners, tone in panels:
            cv2.fillPoly(photo, [np.int32(corners)], (tone, tone, tone))
        for row in range(620, 950, 40):
            cv2.line(photo, (380, row), (720, row), (40, 40, 40), 3)
        found, _ = detect_document(photo)
        self.assertLess(corner_error(found, [[300, 200], [800, 230], [860, 1420], [250, 1450]], photo.shape), .02)

    def test_strong_line_above_dark_card_does_not_stretch_the_crop(self):
        photo = striped_desk(1500, 1125)
        photo[:260] = (35, 35, 35)                      # keyboard body
        photo[255:262, :] = (220, 220, 220)             # bright keyboard edge
        card = np.full((640, 500, 3), 30, np.uint8)
        cv2.rectangle(card, (150, 120), (350, 520), (215, 215, 215), -1)
        quad = [[300, 420], [820, 410], [840, 1080], [280, 1065]]
        photo = paste_quad(photo, card, quad)
        found, _ = detect_document(photo)
        self.assertLess(corner_error(found, quad, photo.shape), .015)

    def test_narrow_strip_against_photo_border_is_not_a_document(self):
        # Keyboard keys along the left border form a strong-edged strip that
        # borrows the photo border as its fourth side; it must not be scored
        # as a document that leaves the frame.
        from magic_scan import Evidence, WORK, evaluate, order
        photo = np.full((2000, 1500, 3), (40, 40, 40), np.uint8)
        corners = np.float32([[0, 0], [207, 32], [166, 1328], [0, 1356]])  # from a real failure
        cv2.fillPoly(photo, [np.int32(corners)], (215, 215, 210))
        cv2.polylines(photo, [np.int32(corners)], True, (20, 20, 20), 6)
        scale = WORK / 2000
        small = cv2.resize(photo, (round(1500 * scale), WORK), interpolation=cv2.INTER_AREA)
        strip = order(corners * scale)
        self.assertIsNone(evaluate(strip, Evidence(small), small.shape[:2]))

    def test_detection_of_full_size_photo_is_fast(self):
        photo = paste_quad(striped_desk(2000, 1500), np.full((900, 640, 3), 245, np.uint8),
                           [[300, 400], [1200, 420], [1230, 1700], [280, 1680]])
        start = time.perf_counter()
        detect_document(photo)
        self.assertLess(time.perf_counter() - start, 8)


class MagicGeometryTests(unittest.TestCase):
    def test_true_aspect_recovers_a4_under_camera_perspective(self):
        # Project a 210 x 297 mm sheet tilted 30 degrees towards a pinhole camera.
        w, h, f = 1500, 2000, 1600.0
        corners = np.array([[-105, -148.5, 0], [105, -148.5, 0], [105, 148.5, 0], [-105, 148.5, 0]], float)
        angle = np.radians(30)
        rot = np.array([[1, 0, 0], [0, np.cos(angle), -np.sin(angle)], [0, np.sin(angle), np.cos(angle)]])
        cam = corners @ rot.T + [10, 5, 520]
        image_points = np.stack([f * cam[:, 0] / cam[:, 2] + w / 2, f * cam[:, 1] / cam[:, 2] + h / 2], 1)
        # Tilt about one axis leaves the focal length unobservable, so the
        # typical phone value is assumed; the paper-size snap absorbs the rest.
        self.assertAlmostEqual(true_aspect(np.float32(image_points), (h, w)), 210 / 297, delta=.02)
        flat = warp(np.zeros((h, w, 3), np.uint8), np.float32(image_points))
        self.assertAlmostEqual(flat.shape[1] / flat.shape[0], 1 / 1.4142, delta=.002)
        self.assertEqual(max(flat.shape[:2]), 2400)


class MagicEnhancementTests(unittest.TestCase):
    def page(self):
        h, w = 1400, 1000
        light = np.linspace(150, 235, h)[:, None] * np.ones((1, w))
        page = np.dstack([light * .97, light * .95, light]).astype(np.uint8)
        for row in range(200, 1100, 60):
            cv2.putText(page, 'Printed line of text', (160, row), cv2.FONT_HERSHEY_SIMPLEX, 1.1, (25, 25, 25), 3)
        cv2.putText(page, 'show through', (200, 1230), cv2.FONT_HERSHEY_SIMPLEX, 1.4,
                    tuple(int(v) for v in page[1230, 200] * .9), 3)
        cv2.putText(page, '1/2', (930, 1330), cv2.FONT_HERSHEY_SIMPLEX, .8, (20, 20, 20), 2)
        cv2.rectangle(page, (0, 0), (180, 18), (30, 30, 30), -1)  # desk sliver on the top border
        return page

    def test_text_page_becomes_white_with_dark_ink_and_margin_content_kept(self):
        page = self.page()
        out = render_magic(page)
        gray = cv2.cvtColor(out, cv2.COLOR_BGR2GRAY)
        self.assertEqual(out.shape, page.shape)
        self.assertGreater(np.median(gray[1120:1180, 100:900]), 250)   # blank paper
        self.assertLess(np.percentile(gray[170:210, 160:600], 5), 60)   # printed ink
        self.assertGreater(np.percentile(gray[1195:1245, 200:500], 1), 200)  # show-through removed
        self.assertLess(gray[0:18, 0:180].min(), 256)
        self.assertGreater(gray[0:18, 0:180].mean(), 250)                # border sliver removed
        self.assertLess(gray[1305:1335, 925:990].min(), 80)              # page number kept

    def test_isolated_margin_character_is_kept_but_punch_hole_removed(self):
        from magic_scan import remove_margin_holes
        page = np.full((1400, 1000, 3), 255, np.uint8)
        cv2.putText(page, '0', (40, 140), cv2.FONT_HERSHEY_SIMPLEX, 1.4, (0, 0, 0), 3)  # printed digit
        cv2.circle(page, (50, 700), 16, (0, 0, 0), -1)                                  # punch hole
        out = cv2.cvtColor(remove_margin_holes(page), cv2.COLOR_BGR2GRAY)
        self.assertLess(out[95:145, 35:80].min(), 50)
        self.assertGreater(out[680:720, 30:70].min(), 250)

    def test_colour_card_keeps_colour(self):
        card = np.full((500, 1000, 3), 240, np.uint8)
        card[:, :500] = (60, 30, 130)
        out = render_magic(card)
        b, g, r = out[250, 250].astype(int)
        self.assertGreater(r - g, 40)


class MagicCliTests(unittest.TestCase):
    def run_crop(self, *args):
        return subprocess.run([sys.executable, str(HERE / 'crop_image.py'), *args],
                              capture_output=True, text=True, timeout=60,
                              env={**os.environ, 'ARKSSCANNER_BOUNDARY_MODE': 'OpenCvOnly'})

    def test_detect_then_apply_magic_filter(self):
        card = np.full((300, 700, 3), 255, np.uint8)
        card[:, :350] = (70, 30, 140)
        photo = paste_quad(striped_desk(), card, camera_quad(210, 90, 25, (0, 60), (1500, 1125), yaw_degrees=15))
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / 'source.jpg'
            cv2.imwrite(str(source), photo)
            detected = self.run_crop('detect', str(source))
            self.assertEqual(detected.returncode, 0, detected.stderr)
            payload = json.loads(detected.stdout)
            self.assertEqual(payload['source'], 'OpenCvFallback')
            self.assertGreaterEqual(payload['confidence'], .72)
            preview, thumb = Path(folder) / 'p.jpg', Path(folder) / 't.jpg'
            applied = self.run_crop('apply', str(source), json.dumps(payload['points']),
                                    str(preview), str(thumb), 'Magic')
            self.assertEqual(applied.returncode, 0, applied.stderr)
            size = json.loads(applied.stdout)
            self.assertEqual(max(size['width'], size['height']), 2400)
            self.assertAlmostEqual(size['width'] / size['height'], 700 / 300, delta=.08)
            self.assertTrue(thumb.exists())


if __name__ == '__main__':
    unittest.main()
