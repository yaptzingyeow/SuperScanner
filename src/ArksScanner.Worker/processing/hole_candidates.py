"""Conservative paper-margin punch-hole suggestions; never edits an image."""
import math
import cv2
import numpy as np


def find_hole_candidates(image: np.ndarray, protected_boxes=()):
    """Return normalized (left, top, right, bottom) boxes for likely holes."""
    if (not isinstance(image, np.ndarray) or image.dtype != np.uint8 or
            image.ndim != 3 or image.shape[2] != 3 or
            min(image.shape[:2]) < 100 or image.shape[0] * image.shape[1] > 4_000_000):
        raise ValueError('Invalid document image')
    h, w = image.shape[:2]
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    dark = cv2.threshold(gray, 105, 255, cv2.THRESH_BINARY_INV)[1]
    contours, _ = cv2.findContours(dark, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
    candidates = []
    hollow_rings = []
    for contour in contours:
        area = cv2.contourArea(contour)
        perimeter = cv2.arcLength(contour, True)
        x, y, width, height = cv2.boundingRect(contour)
        if not 60 <= area <= min(w, h) ** 2 * .007 or perimeter <= 0:
            continue
        if not .7 <= width / height <= 1.4 or 4 * math.pi * area / perimeter**2 < .72:
            continue
        if not (x + width < w * .18 or x > w * .82):
            continue
        # A hollow printed circle has a circular contour but little ink inside.
        interior = dark[y:y+height, x:x+width]
        if np.count_nonzero(interior) / (width * height) < .48:
            hollow_rings.append((x, y, x + width, y + height))
            continue
        box = (x / w, y / h, (x + width) / w, (y + height) / h)
        if any(box[0] < right and box[2] > left and box[1] < bottom and box[3] > top
               for left, top, right, bottom in protected_boxes):
            continue
        candidates.append(box)
    # Real punched holes can appear as crescents because the paper curls and
    # the desk shines through. A contour may then be non-circular. Only offer
    # those as suggestions when at least two similar circles align in a margin.
    circles = cv2.HoughCircles(cv2.medianBlur(gray, 5), cv2.HOUGH_GRADIENT,
        dp=1.2, minDist=max(35, int(h * .06)), param1=80, param2=18,
        minRadius=max(5, int(min(w, h) * .008)),
        maxRadius=max(12, int(min(w, h) * .04)))
    if circles is not None:
        aligned = []
        for cx, cy, radius in circles[0]:
            if cx + radius >= w * .16 and cx - radius <= w * .84:
                continue
            if any(abs(cx - ox) <= max(12, min(radius, other) * 1.4) and
                   abs(cy - oy) > max(radius, other) * 3 and
                   .5 <= radius / other <= 2 for ox, oy, other in circles[0]):
                aligned.append((cx, cy, radius))
        for cx, cy, radius in aligned:
            if any(left <= cx <= right and top <= cy <= bottom
                   for left, top, right, bottom in hollow_rings):
                continue
            # A printed circular outline is dark uniformly around its rim.
            # A punched, curled edge has a pronounced light/dark crescent.
            contrast = 0
            for ring in (.7, 1.0, 1.2):
                samples = []
                for quadrant in range(4):
                    angles = np.linspace(quadrant * np.pi / 2,
                                         (quadrant + 1) * np.pi / 2, 20)
                    values = [gray[int(np.clip(cy + ring * radius * np.sin(angle), 0, h-1)),
                                   int(np.clip(cx + ring * radius * np.cos(angle), 0, w-1))]
                              for angle in angles]
                    samples.append(float(np.mean(values)))
                contrast = max(contrast, max(samples) - min(samples))
            if contrast < 35:
                continue
            box = (max(0, (cx-radius*1.2)/w), max(0, (cy-radius*1.2)/h),
                   min(1, (cx+radius*1.2)/w), min(1, (cy+radius*1.2)/h))
            if any(box[0] < right and box[2] > left and box[1] < bottom and box[3] > top
                   for left, top, right, bottom in protected_boxes):
                continue
            if not any(box[0] < prior[2] and box[2] > prior[0] and
                       box[1] < prior[3] and box[3] > prior[1] for prior in candidates):
                candidates.append(box)
    return sorted(candidates, key=lambda box: (box[1], box[0]))
