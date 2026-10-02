"""Convert a document probability mask into stable outer-page corners."""

from dataclasses import dataclass

import cv2
import numpy as np

from boundary.preprocess import LetterboxedImage


@dataclass(frozen=True)
class GeometryEvidence:
    mean_boundary_probability: float
    mask_area_ratio: float
    connectedness: float
    side_support: tuple[float, float, float, float]
    convexity: float
    coverage: float


@dataclass(frozen=True)
class GeometryEstimate:
    points: np.ndarray
    evidence: GeometryEvidence


def _ordered_corners(points: np.ndarray) -> np.ndarray:
    points = np.asarray(points, dtype=np.float32)
    sums = points.sum(axis=1)
    differences = np.diff(points, axis=1).reshape(-1)
    return np.asarray([
        points[np.argmin(sums)],
        points[np.argmin(differences)],
        points[np.argmax(sums)],
        points[np.argmax(differences)],
    ], dtype=np.float32)


def _fit_supporting_line(points: np.ndarray) -> tuple[np.ndarray, np.ndarray] | None:
    if len(points) < 8:
        return None
    vx, vy, x, y = cv2.fitLine(
        points.astype(np.float32), cv2.DIST_HUBER, 0, .01, .01).reshape(-1)
    direction = np.asarray([vx, vy], dtype=np.float64)
    length = np.linalg.norm(direction)
    if not np.isfinite(direction).all() or length < 1e-8:
        return None
    return np.asarray([x, y], dtype=np.float64), direction / length


def _cross_2d(first: np.ndarray, second: np.ndarray) -> np.ndarray:
    return first[..., 0] * second[..., 1] - first[..., 1] * second[..., 0]


def _intersection(
        first: tuple[np.ndarray, np.ndarray],
        second: tuple[np.ndarray, np.ndarray]) -> np.ndarray | None:
    p, r = first
    q, s = second
    denominator = _cross_2d(r, s)
    if abs(denominator) < 1e-5:
        return None
    distance = _cross_2d(q - p, s) / denominator
    result = p + distance * r
    return result if np.isfinite(result).all() else None


def _clean_mask(probability_mask: np.ndarray, threshold: float) -> tuple[np.ndarray, float] | None:
    binary = (probability_mask >= threshold).astype(np.uint8)
    height, width = binary.shape
    short_side = min(height, width)
    kernel_size = max(3, int(round(short_side * .02)))
    if kernel_size % 2 == 0:
        kernel_size -= 1
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (kernel_size, kernel_size))
    binary = cv2.morphologyEx(binary, cv2.MORPH_CLOSE, kernel)

    component_count, labels, stats, _ = cv2.connectedComponentsWithStats(binary, 8)
    minimum_area = height * width * .01
    component_areas = stats[1:, cv2.CC_STAT_AREA]
    retained_labels = np.flatnonzero(component_areas >= minimum_area) + 1
    if len(retained_labels) == 0:
        return None

    retained = np.isin(labels, retained_labels).astype(np.uint8)
    retained_area = int(np.count_nonzero(retained))
    largest_area = int(component_areas[retained_labels - 1].max())
    connectedness = largest_area / retained_area if retained_area else 0.0

    # A page is one connected surface. Keeping only the largest component also
    # prevents bright keyboard keys or desk highlights becoming page corners.
    largest_label = int(retained_labels[np.argmax(component_areas[retained_labels - 1])])
    return (labels == largest_label).astype(np.uint8), connectedness


def estimate_boundary(
        mask: np.ndarray,
        mapping: LetterboxedImage,
        threshold: float) -> GeometryEstimate | None:
    probability_mask = np.asarray(mask, dtype=np.float32)
    if probability_mask.ndim != 2 or probability_mask.size == 0:
        return None
    if not np.isfinite(probability_mask).all() or not 0 < threshold < 1:
        return None

    cleaned_result = _clean_mask(probability_mask, threshold)
    if cleaned_result is None:
        return None
    cleaned, connectedness = cleaned_result
    image_area = cleaned.shape[0] * cleaned.shape[1]
    mask_area = int(np.count_nonzero(cleaned))
    mask_area_ratio = mask_area / image_area
    if mask_area_ratio < .20 or mask_area_ratio > .98:
        return None

    contours, _ = cv2.findContours(cleaned, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
    if not contours:
        return None
    contour = max(contours, key=cv2.contourArea).reshape(-1, 2).astype(np.float32)
    contour_area = float(cv2.contourArea(contour))
    if contour_area <= 0:
        return None

    rectangle = cv2.minAreaRect(contour)
    box = _ordered_corners(cv2.boxPoints(rectangle))
    short_side = max(1.0, min(rectangle[1]))
    band_width = max(3.0, short_side * .07)

    fitted_lines: list[tuple[np.ndarray, np.ndarray]] = []
    support: list[float] = []
    for side_index in range(4):
        start = box[side_index].astype(np.float64)
        end = box[(side_index + 1) % 4].astype(np.float64)
        side = end - start
        side_length = np.linalg.norm(side)
        if side_length < 1:
            return None
        unit = side / side_length
        relative = contour.astype(np.float64) - start
        projection = relative @ unit
        perpendicular = np.abs(_cross_2d(unit, relative))
        selected = contour[
            (projection >= -band_width) &
            (projection <= side_length + band_width) &
            (perpendicular <= band_width)]
        fitted = _fit_supporting_line(selected)
        if fitted is None:
            return None
        fitted_lines.append(fitted)
        support.append(float(min(1.0, len(selected) / max(side_length, 1.0))))

    corners = []
    for corner_index in range(4):
        corner = _intersection(fitted_lines[(corner_index - 1) % 4], fitted_lines[corner_index])
        if corner is None:
            return None
        corners.append(corner)
    model_points = _ordered_corners(np.asarray(corners, dtype=np.float32))

    height, width = cleaned.shape
    margin = max(height, width) * .08
    if ((model_points[:, 0] < -margin).any() or
            (model_points[:, 0] > width - 1 + margin).any() or
            (model_points[:, 1] < -margin).any() or
            (model_points[:, 1] > height - 1 + margin).any()):
        return None
    if not cv2.isContourConvex(np.rint(model_points).astype(np.int32)):
        return None
    quad_area = abs(float(cv2.contourArea(model_points)))
    if quad_area < image_area * .20:
        return None

    source_points = mapping.to_source_points(model_points)
    denominators = np.asarray([
        max(mapping.source_width - 1, 1),
        max(mapping.source_height - 1, 1),
    ], dtype=np.float32)
    normalized = np.clip(source_points / denominators, 0, 1).astype(np.float32)

    hull_area = float(cv2.contourArea(cv2.convexHull(contour)))
    convexity = min(1.0, contour_area / hull_area) if hull_area > 0 else 0.0
    contour_indices = np.rint(contour).astype(np.int32)
    contour_indices[:, 0] = np.clip(contour_indices[:, 0], 0, width - 1)
    contour_indices[:, 1] = np.clip(contour_indices[:, 1], 0, height - 1)
    mean_probability = float(np.mean(
        probability_mask[contour_indices[:, 1], contour_indices[:, 0]]))
    coverage = min(1.0, contour_area / quad_area) if quad_area > 0 else 0.0

    return GeometryEstimate(
        points=normalized,
        evidence=GeometryEvidence(
            mean_boundary_probability=mean_probability,
            mask_area_ratio=mask_area_ratio,
            connectedness=connectedness,
            side_support=tuple(support),
            convexity=convexity,
            coverage=coverage))
