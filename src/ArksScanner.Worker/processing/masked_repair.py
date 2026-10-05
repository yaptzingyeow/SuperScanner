"""Conservative, user-confirmed image repair. The caller must preview the result."""
import cv2
import numpy as np


def repair_masked_image(image: np.ndarray, rectangles, protected_rectangles=(), strokes=()):
    """Inpaint reviewed rectangles and brush strokes; preserve every other pixel."""
    if (not isinstance(image, np.ndarray) or image.dtype != np.uint8 or
            image.ndim != 3 or image.shape[2] != 3 or
            min(image.shape[:2]) < 100 or image.shape[0] * image.shape[1] > 4_000_000):
        raise ValueError('Invalid document image')
    height, width = image.shape[:2]
    if not 1 <= len(rectangles) + len(strokes) <= 50 or len(rectangles) > 20:
        raise ValueError('Choose between one and fifty repair areas (at most twenty boxes)')
    mask = np.zeros((height, width), np.uint8)
    for rectangle in rectangles:
        if len(rectangle) != 4 or any(not isinstance(v, int) for v in rectangle):
            raise ValueError('Invalid repair area')
        left, top, right, bottom = rectangle
        if not (0 <= left < right <= width and 0 <= top < bottom <= height):
            raise ValueError('Repair area is outside the image')
        if (right - left) * (bottom - top) > width * height * .03:
            raise ValueError('One repair area is too large')
        for protected in protected_rectangles:
            pl, pt, pr, pb = protected
            if left < pr and right > pl and top < pb and bottom > pt:
                raise ValueError('Repair area overlaps protected page content')
        mask[top:bottom, left:right] = 255
    # Rectangles (punch holes, debris) keep the strict checks below. Brush strokes are the
    # Eraser: the user paints exactly what should go, so they may cover recognised words or part
    # of a pen line. Only painted pixels ever change (see the copy-through at the end).
    rectangle_mask = mask.copy()
    for radius, points in strokes:
        if (not isinstance(radius, int) or not 1 <= radius <= min(width, height) * .05 or
                not 1 <= len(points) <= 256 or any(len(point) != 2 or
                    not all(isinstance(value, int) for value in point) or
                    not 0 <= point[0] < width or not 0 <= point[1] < height
                    for point in points)):
            raise ValueError('Invalid repair brush stroke')
        path = np.asarray(points, dtype=np.int32).reshape((-1, 1, 2))
        cv2.polylines(mask, [path], False, 255, radius * 2)
        for x, y in (points[0], points[-1]):
            cv2.circle(mask, (x, y), radius, 255, -1)
    for left, top, right, bottom in protected_rectangles:
        if right > left and bottom > top and cv2.countNonZero(rectangle_mask[top:bottom, left:right]):
            raise ValueError('Repair area overlaps protected page content')
    if cv2.countNonZero(mask) > width * height * .05:
        raise ValueError('Combined repair area is too large')
    # A selected fragment of a long rule, handwriting stroke, or printed glyph
    # must not be erased just because OCR did not recognise it. Reject any dark
    # connected component that continues beyond the reviewed mask.
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    paper_level = float(np.median(gray))
    dark = np.uint8(gray < min(170, paper_level - 55))
    count, labels, stats, _ = cv2.connectedComponentsWithStats(dark, 8)
    inside = np.bincount(labels[rectangle_mask != 0], minlength=count)
    for label in np.flatnonzero(inside[1:]) + 1:
        if stats[label, cv2.CC_STAT_AREA] >= 5 and stats[label, cv2.CC_STAT_AREA] - inside[label] >= 5:
            raise ValueError('Repair area crosses page content')
    # TELEA only estimates replacement pixels. Copying through the original
    # outside the selection gives an exact preservation guarantee.
    estimate = cv2.inpaint(image, mask, 3, cv2.INPAINT_TELEA)
    result = image.copy()
    result[mask != 0] = estimate[mask != 0]
    return result
