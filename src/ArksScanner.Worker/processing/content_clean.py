"""Optional content focused scan for pages with a complete printed frame."""

import cv2
import numpy as np

from shadow_cleanup import clean_document


def _content_frame(image: np.ndarray):
    height, width = image.shape[:2]
    scale = min(1.0, 1100.0 / max(height, width))
    small = cv2.resize(image, None, fx=scale, fy=scale,
                       interpolation=cv2.INTER_AREA) if scale < 1 else image
    gray = cv2.cvtColor(small, cv2.COLOR_BGR2GRAY)
    h, w = gray.shape
    # Find two long, separated vertical rules first. Unlike a contour, these
    # remain intact when printed text intersects the surrounding frame.
    edges = cv2.Canny(gray, 40, 120)
    column_support = np.count_nonzero(
        cv2.dilate(edges, np.ones((1, 5), np.uint8)), axis=0)
    xs = np.flatnonzero((column_support >= .52 * h) &
                       (np.arange(w) > .045 * w) &
                       (np.arange(w) < .96 * w))
    if len(xs) < 2:
        return None
    x0, x1 = int(xs[0]), int(xs[-1])
    if x1 - x0 < .35 * w:
        return None
    # Search for horizontal rules only between the vertical pair. Inspecting
    # a small y-neighborhood joins both edges of a thin printed rule without
    # treating broad page shadows as ink.
    row_support = np.count_nonzero(
        cv2.dilate(edges[:, x0:x1 + 1], np.ones((5, 1), np.uint8)), axis=1)
    ys = np.flatnonzero((row_support >= .65 * (x1 - x0)) &
                       (np.arange(h) > .035 * h) &
                       (np.arange(h) < .96 * h))
    # Perspective correction can leave a small tilt in the printed frame.
    # Its top rule may cross many rows, so row projection alone sees only
    # interior text. A long Hough segment touching a side supplies that edge.
    lines = cv2.HoughLinesP(edges, 1, np.pi / 360, threshold=35,
                            minLineLength=max(70, round((x1 - x0) * .35)),
                            maxLineGap=35)
    slanted_tops = []
    for line in lines[:, 0] if lines is not None else []:
        ax, ay, bx, by = map(int, line)
        run = abs(bx - ax)
        if (run < .55 * (x1 - x0) or abs(by - ay) > .10 * run or
                min(abs(ax - x0), abs(ax - x1), abs(bx - x0), abs(bx - x1)) > .06 * (x1 - x0)):
            continue
        candidate = min(ay, by)
        if .035 * h < candidate < .65 * h:
            slanted_tops.append(candidate)
    if len(ys) < 1 or (len(ys) < 2 and not slanted_tops):
        return None
    y0 = min(int(ys[0]), min(slanted_tops)) if slanted_tops else int(ys[0])
    y1 = int(ys[-1])
    box_width, box_height = x1 - x0, y1 - y0
    aspect = box_width / max(1, box_height)
    if not (.25 <= box_width * box_height / (w * h) <= .82 and
            .42 <= aspect <= 1.1 and x0 > .045 * w and y0 > .035 * h and
            x1 < .96 * w and y1 < .96 * h):
        return None
    # Require the two side rules to span nearly the entire candidate height.
    # Otherwise a page edge plus interior rules could mimic a frame.
    side_edges = cv2.dilate(edges[y0:y1 + 1], np.ones((1, 7), np.uint8))
    if (np.count_nonzero(side_edges[:, x0]) < .7 * box_height or
            np.count_nonzero(side_edges[:, x1]) < .7 * box_height):
        return None
    connected_sides = cv2.dilate(edges, np.ones((1, 15), np.uint8))
    connected_sides = cv2.morphologyEx(connected_sides, cv2.MORPH_CLOSE,
                                      np.ones((20, 1), np.uint8))
    connected_sides = cv2.morphologyEx(connected_sides, cv2.MORPH_OPEN,
                                      np.ones((max(45, round(h * .06)), 1), np.uint8))
    for x in (x0, x1):
        side_rows = np.flatnonzero(connected_sides[:, x])
        if len(side_rows) == 0 or abs(int(side_rows[0]) - y0) > .045 * h:
            return None
    padding = max(3, round(min(w, h) * .008))
    return (max(0, round((x0 - padding) / scale)),
            max(0, round((y0 - padding) / scale)),
            min(width, round((x1 + padding) / scale)),
            min(height, round((y1 + padding) / scale)))


def _focused_ink(image: np.ndarray) -> np.ndarray:
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    kernel_size = min(61, max(21, round(min(gray.shape) * .06)) | 1)
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (kernel_size, kernel_size))
    background = cv2.morphologyEx(gray, cv2.MORPH_CLOSE, kernel)
    background = cv2.GaussianBlur(background.astype(np.float32), (0, 0),
                                  max(1, kernel_size / 5))
    relative = gray.astype(np.float32) / np.maximum(background, 1)
    ink = np.clip((.82 - relative) / .19, 0, 1)
    return np.rint(255 * (1 - ink)).astype(np.uint8)


def render_content_clean(image: np.ndarray) -> np.ndarray:
    if image.ndim != 3 or image.shape[2] != 3 or image.dtype != np.uint8:
        raise ValueError('Invalid scan image')
    frame = _content_frame(image)
    if frame is None:
        content = clean_document(image, 'CleanDocument')
    else:
        x0, y0, x1, y1 = frame
        content = _focused_ink(image[y0:y1, x0:x1])
    source_height, source_width = image.shape[:2]
    page_width = min(1400, source_width)
    page_height = round(page_width * 297 / 210)
    page = np.full((page_height, page_width), 255, np.uint8)
    fraction = .78 if frame is not None else .94
    scale = min(page_width * fraction / content.shape[1],
                page_height * (.87 if frame is not None else .94) / content.shape[0])
    fitted = cv2.resize(content, None, fx=scale, fy=scale,
                        interpolation=cv2.INTER_AREA if scale < 1 else cv2.INTER_CUBIC)
    x = (page_width - fitted.shape[1]) // 2
    y = (page_height - fitted.shape[0]) // 2
    page[y:y + fitted.shape[0], x:x + fitted.shape[1]] = fitted
    return page
