"""Bounded OpenCV crop subprocess. Input is the Worker-generated upright JPEG."""
import os
os.environ["OPENCV_IO_MAX_IMAGE_PIXELS"] = "4000000"
os.environ["OPENBLAS_NUM_THREADS"] = "1"
os.environ["OMP_NUM_THREADS"] = "1"
import json
import sys
import math
if sys.platform != "win32":
    import resource
    resource.setrlimit(resource.RLIMIT_AS, (1536 * 1024**2, 1536 * 1024**2))
    resource.setrlimit(resource.RLIMIT_CPU, (25, 25))
import cv2
import numpy as np
from boundary.contracts import BoundaryPoint, DocumentBoundaryResult
cv2.setNumThreads(1)

FULL = [{"x": 0, "y": 0}, {"x": 1, "y": 0}, {"x": 1, "y": 1}, {"x": 0, "y": 1}]
MIN_DOCUMENT_AREA = 0.35
MIN_DOCUMENT_SPAN = 0.55

def ordered(points):
    center = points.mean(axis=0)
    points = points[np.argsort(np.arctan2(points[:, 1] - center[1], points[:, 0] - center[0]))]
    return np.roll(points, -int(np.argmin(points.sum(axis=1))), axis=0)

def valid(p):
    if p.shape != (4, 2) or not np.isfinite(p).all() or (p < 0).any() or (p > 1).any():
        return False
    twice_area = 0.0
    for i in range(4):
        a, b, c = p[i], p[(i+1) % 4], p[(i+2) % 4]
        ab, bc = b-a, c-b
        if ab[0]*bc[1]-ab[1]*bc[0] <= 0.0001 or float(ab @ ab) < 0.0004:
            return False
        twice_area += a[0]*b[1]-b[0]*a[1]
    return twice_area >= 0.02 and p[0,1]+p[1,1] < p[2,1]+p[3,1] and p[0,0]+p[3,0] < p[1,0]+p[2,0]

def _boundary_result(points, confidence, source, diagnostics_code):
    return DocumentBoundaryResult(
        points=tuple(BoundaryPoint(float(point["x"]), float(point["y"])) for point in points),
        confidence=float(confidence),
        source=source,
        model_version=None,
        diagnostics_code=diagnostics_code)


def detect_with_opencv(image):
    height, width = image.shape[:2]
    scale = min(1.0, 1000.0 / max(width, height))
    small = cv2.resize(image, (max(2, round(width*scale)), max(2, round(height*scale))))
    h, w = small.shape[:2]
    gray = cv2.GaussianBlur(cv2.cvtColor(small, cv2.COLOR_BGR2GRAY), (5, 5), 0)
    if float(gray.std()) < 2:
        return _boundary_result(FULL, 0.0, "FullImage", "manual_required")
    best, best_score = None, 0.0
    masks = []
    for low, high in [(40, 120), (75, 200), (15, 60)]:
        edges = cv2.Canny(gray, low, high)
        edges = cv2.morphologyEx(edges, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8))
        masks.append(edges)
    # Region masks connect paper across soft shadows and printed text, where
    # Canny alone often leaves an open outline. Multiple cutoffs retain shaded
    # paper without assuming a particular desk color.
    _, paper = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY | cv2.THRESH_OTSU)
    masks.append(cv2.morphologyEx(paper, cv2.MORPH_CLOSE, np.ones((11, 11), np.uint8)))
    # Color segmentation supplements edges when a pale background object joins
    # the sheet. The center is only probable foreground, never a forced paper
    # label; border pixels are probable background to allow clipped sheets.
    segmentation = np.full((h, w), cv2.GC_PR_BGD, np.uint8)
    segmentation[int(h*.08):int(h*.98), int(w*.08):int(w*.98)] = cv2.GC_PR_FGD
    segmentation[:max(1, int(h*.03))] = cv2.GC_BGD
    cv2.setRNGSeed(2)
    cv2.grabCut(small, segmentation, None, np.zeros((1, 65), np.float64),
                np.zeros((1, 65), np.float64), 3, cv2.GC_INIT_WITH_MASK)
    region = np.where((segmentation == cv2.GC_FGD) | (segmentation == cv2.GC_PR_FGD), 255, 0).astype(np.uint8)
    region = cv2.morphologyEx(region, cv2.MORPH_OPEN, np.ones((21, 21), np.uint8))
    masks.append(cv2.morphologyEx(region, cv2.MORPH_CLOSE, np.ones((5, 5), np.uint8)))
    for mask in masks:
        contours, _ = cv2.findContours(mask, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_SIMPLE)
        for contour in sorted(contours, key=cv2.contourArea, reverse=True)[:30]:
            # A folded corner or small tear adds vertices to an otherwise
            # rectangular sheet. The convex hull removes inward notches; fit
            # four supporting sides rather than requiring four exact vertices.
            hull = cv2.convexHull(contour)
            perimeter = cv2.arcLength(hull, True)
            candidates = [(cv2.approxPolyDP(hull, tolerance * perimeter, True), 0.0)
                          for tolerance in [0.015, 0.025, 0.04]]
            raw_polygon = cv2.approxPolyDP(contour, 0.012 * cv2.arcLength(contour, True), True)
            if 5 <= len(raw_polygon) <= 10:
                raw = raw_polygon.reshape(-1, 2).astype(np.float32)
                top_limit = raw[:, 1].min() + .22 * np.ptp(raw[:, 1])
                top = raw[raw[:, 1] <= top_limit]
                midpoint = (raw[:, 0].min() + raw[:, 0].max()) / 2
                left_top, right_top = top[top[:, 0] < midpoint], top[top[:, 0] >= midpoint]
                if len(left_top) >= 2 and len(right_top):
                    # A fold contributes an outer tip and an inner corner near
                    # the top edge. Cropping from the inner (rightmost) point
                    # excludes the flap instead of treating it as paper area.
                    top_left = left_top[np.argmax(left_top[:, 0])]
                    fold_width = top_left[0] - left_top[:, 0].min()
                    if fold_width > .03 * w:
                        top_right = right_top[np.argmax(right_top[:, 0])]
                        bottom_right = raw[np.argmax(raw[:, 0] + raw[:, 1])]
                        bottom_left = raw[np.argmax(raw[:, 1] - raw[:, 0])]
                        folded = ordered(np.array([top_left, top_right, bottom_right, bottom_left],
                                                  dtype=np.float32)).reshape(4, 1, 2)
                        candidates.append((folded, .05))
            polygon = cv2.approxPolyDP(hull, 0.012 * perimeter, True)
            if 5 <= len(polygon) <= 8:
                p = polygon.reshape(-1, 2).astype(np.float32)
                # Discard short fold edges, intersect the four dominant sides.
                lengths = np.linalg.norm(np.roll(p, -1, axis=0) - p, axis=1)
                indices = sorted(np.argsort(lengths)[-4:])
                corners = []
                for index, previous in zip(indices, indices[-1:] + indices[:-1]):
                    a, b = p[previous], p[(previous + 1) % len(p)]
                    c, d = p[index], p[(index + 1) % len(p)]
                    matrix = np.column_stack((b-a, -(d-c)))
                    if abs(float(np.linalg.det(matrix))) < 1:
                        break
                    corners.append(a + np.linalg.solve(matrix, c-a)[0] * (b-a))
                if len(corners) == 4:
                    fitted = ordered(np.array(corners, dtype=np.float32)).reshape(4, 1, 2)
                    fitted_area = abs(cv2.contourArea(fitted))
                    if 0.82 <= cv2.contourArea(hull) / max(1, fitted_area) <= 1.02:
                        candidates.append((fitted, 0.0))
            for quad, fold_bonus in candidates:
                if len(quad) != 4 or not cv2.isContourConvex(quad):
                    continue
                area = cv2.contourArea(quad) / (w*h)
                if area < MIN_DOCUMENT_AREA or area > 0.98:
                    continue
                points = ordered(quad.reshape(4, 2).astype(np.float32)) / np.array([w-1, h-1])
                if not valid(points):
                    continue
                # A background region touching both sides of the photo is not
                # evidence of a paper boundary, even when its area scores well.
                if (points[:, 0].min() < 0.005 and points[:, 0].max() > 0.995):
                    continue
                # Two corners exactly on the image's top border usually mean
                # pale background objects were joined to the sheet. Prefer a
                # lower-scoring paper outline or manual selection over a
                # confidently wrong full-height crop.
                if sum(point[1] < 0.02 for point in points) >= 2:
                    continue
                x_span = float(points[:, 0].max() - points[:, 0].min())
                y_span = float(points[:, 1].max() - points[:, 1].min())
                if x_span < MIN_DOCUMENT_SPAN or y_span < MIN_DOCUMENT_SPAN:
                    continue
                rect = cv2.minAreaRect(quad)
                rectangularity = cv2.contourArea(quad) / max(1, rect[1][0] * rect[1][1])
                coverage = min(x_span, y_span)
                score = 0.60*area + 0.25*rectangularity + 0.15*coverage + fold_bonus
                if score > best_score:
                    best, best_score = points, score
    if best is not None:
        current_top_y = (best[0, 1] + best[1, 1]) * h / 2
        lines = cv2.HoughLinesP(cv2.Canny(gray, 15, 60), 1, np.pi / 180, 25,
                                minLineLength=int(.18*w), maxLineGap=int(.10*w))
        segments = []
        for line in lines if lines is not None else []:
            x1, y1, x2, y2 = line[0]
            if x1 > x2:
                x1, y1, x2, y2 = x2, y2, x1, y1
            middle_y = (y1+y2)/2
            if (abs(middle_y-current_top_y) < .04*h
                    and abs(y2-y1) < .08*max(1, x2-x1)):
                segments.append((x1, y1, x2, y2))
        refinements = []
        for anchor in segments:
            middle_y = (anchor[1]+anchor[3])/2
            aligned = [line for line in segments
                       if abs((line[1]+line[3])/2-middle_y) < .025*h]
            x1, x2 = min(line[0] for line in aligned), max(line[2] for line in aligned)
            if (x2-x1 > .45*w and x1 < .35*w and x1/w > best[0, 0] + .03
                    and abs(x2/w-best[1, 0]) < .12):
                y1 = int(np.median([line[1] for line in aligned]))
                y2 = int(np.median([line[3] for line in aligned]))
                refinements.append((x2-x1, x1, y1, x2, y2))
        if refinements:
            _, x1, y1, x2, y2 = max(refinements)
            refined = best.copy()
            refined[0], refined[1] = [x1/(w-1), y1/(h-1)], [x2/(w-1), y2/(h-1)]
            if valid(refined):
                best = refined
    if best is None or best_score < 0.45:
        return _boundary_result(FULL, 0.0, "FullImage", "manual_required")
    return _boundary_result(
        [{"x": float(x), "y": float(y)} for x, y in best],
        round(min(best_score, 1.0), 3),
        "OpenCvFallback",
        "opencv_candidate")


def detect(image):
    """Compatibility wrapper retained until the hybrid detector owns the CLI."""
    payload = detect_with_opencv(image).to_json_dict()
    if payload["source"] == "OpenCvFallback":
        payload["source"] = "Automatic"
    return payload

def apply_filter(image, name):
    if name == "Original":
        return image
    if name == "Bright":
        table = np.array([round(255 * ((i / 255.0) ** 0.75)) for i in range(256)], dtype=np.uint8)
        return cv2.LUT(image, table)
    if name == "Grayscale":
        return cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    if name == "BlackAndWhite":
        gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
        # Adaptive thresholding preserves text under uneven lighting.
        block = min(51, min(gray.shape))
        if block % 2 == 0:
            block -= 1
        if block < 3:
            return cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY | cv2.THRESH_OTSU)[1]
        return cv2.adaptiveThreshold(gray, 255, cv2.ADAPTIVE_THRESH_GAUSSIAN_C,
                                     cv2.THRESH_BINARY, block, 12)
    if name != "Document":
        raise ValueError("Unknown scan filter")
    lab = cv2.cvtColor(image, cv2.COLOR_BGR2LAB)
    luminance, channel_a, channel_b = cv2.split(lab)
    improved = cv2.createCLAHE(clipLimit=1.6, tileGridSize=(8, 8)).apply(luminance)
    luminance = cv2.addWeighted(luminance, 0.55, improved, 0.45, 0)
    result = cv2.cvtColor(cv2.merge((luminance, channel_a, channel_b)), cv2.COLOR_LAB2BGR)
    softened = cv2.GaussianBlur(result, (0, 0), 1.0)
    return cv2.addWeighted(result, 1.28, softened, -0.28, 0)

def main():
    mode, source = sys.argv[1:3]
    if os.path.getsize(source) > 25*1024**2:
        raise ValueError("Input too large")
    image = cv2.imread(source, cv2.IMREAD_COLOR)
    if image is None or max(image.shape[:2]) > 2000 or min(image.shape[:2]) < 2:
        raise ValueError("Invalid source image")
    if mode == "detect":
        print(json.dumps(detect(image), allow_nan=False))
        return
    if mode != "apply":
        raise ValueError("Unknown operation")
    points = json.loads(sys.argv[3])
    normalized = np.array([[p["x"], p["y"]] for p in points], dtype=np.float32)
    if not valid(normalized):
        raise ValueError("Invalid quadrilateral")
    h, w = image.shape[:2]
    p = normalized * np.array([w-1, h-1], dtype=np.float32)
    out_w = max(2, round(max(np.linalg.norm(p[1]-p[0]), np.linalg.norm(p[2]-p[3]))))
    out_h = max(2, round(max(np.linalg.norm(p[3]-p[0]), np.linalg.norm(p[2]-p[1]))))
    # Small crops otherwise become small JPEGs that the viewer must enlarge. A
    # bounded bicubic upscale smooths the display; it cannot restore missing detail.
    longest = max(out_w, out_h)
    scale = min(2000.0/longest, max(1.0, min(2.0, 1400.0/longest)))
    out_w, out_h = max(2, round(out_w*scale)), max(2, round(out_h*scale))
    target = np.array([[0,0], [out_w-1,0], [out_w-1,out_h-1], [0,out_h-1]], dtype=np.float32)
    matrix = cv2.getPerspectiveTransform(p, target)
    if not np.isfinite(matrix).all():
        raise ValueError("Invalid transformation")
    flat = cv2.warpPerspective(image, matrix, (out_w, out_h), flags=cv2.INTER_CUBIC, borderMode=cv2.BORDER_REPLICATE)
    flat = apply_filter(flat, sys.argv[6] if len(sys.argv) > 6 else "Document")
    preview_path, thumb_path = sys.argv[4:6]
    if not cv2.imwrite(preview_path, flat, [cv2.IMWRITE_JPEG_QUALITY, 94]):
        raise ValueError("Could not save output")
    scale = min(1.0, 320/max(out_w, out_h))
    thumb = cv2.resize(flat, (max(1, round(out_w*scale)), max(1, round(out_h*scale))), interpolation=cv2.INTER_AREA)
    if not cv2.imwrite(thumb_path, thumb, [cv2.IMWRITE_JPEG_QUALITY, 85]):
        raise ValueError("Could not save thumbnail")
    if os.path.getsize(preview_path) > 12*1024**2:
        raise ValueError("Output too large")
    print(json.dumps({"width": out_w, "height": out_h}))

if __name__ == "__main__":
    try:
        main()
    except Exception:
        print("Crop processing failed", file=sys.stderr)
        sys.exit(1)
