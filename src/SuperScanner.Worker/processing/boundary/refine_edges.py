"""Refine coarse paper sides using a sustained dark-to-light paper transition."""
import cv2
import numpy as np


def inset_paper_edges(corners, width, height):
    """Offset each detected side inward, not each corner toward the centre.

    Keep this tiny (0.2% of the short side) to remove edge interpolation
    fringes. It is not a substitute for correct detection. Manual crops do
    not pass through this function.
    """
    scale = np.array([width - 1, height - 1], np.float64)
    points = np.asarray(corners, np.float64) * scale
    distance = min(scale) * .002
    lines = []
    for index in range(4):
        direction = points[(index + 1) % 4] - points[index]
        length = np.linalg.norm(direction)
        if length < 1:
            return corners
        direction /= length
        normal = np.array([-direction[1], direction[0]])
        lines.append((points[index] + normal * distance, direction))
    result = []
    for index in range(4):
        a, u = lines[index - 1]
        b, v = lines[index]
        matrix = np.column_stack((u, -v))
        if abs(np.linalg.det(matrix)) < .1:
            return corners
        result.append((a + np.linalg.solve(matrix, b-a)[0] * u) / scale)
    return np.asarray(result)


def refine_paper_edges(image, corners):
    height, width = image.shape[:2]
    gray = cv2.GaussianBlur(cv2.cvtColor(image, cv2.COLOR_BGR2GRAY), (5, 5), 0).astype(np.float32)
    scale = np.array([width - 1, height - 1], np.float32)
    points = np.asarray(corners, np.float32) * scale
    lines = []
    distance = max(8, round(min(width, height) * .085))
    offsets = np.arange(-distance, distance + 1, dtype=np.float32)
    def sample(locations):
        return cv2.remap(gray, locations[..., 0].astype(np.float32),
                         locations[..., 1].astype(np.float32), cv2.INTER_LINEAR,
                         borderMode=cv2.BORDER_REPLICATE)
    for index in range(4):
        a, b = points[index], points[(index + 1) % 4]
        direction = (b - a) / max(1, np.linalg.norm(b - a))
        inward = np.array([-direction[1], direction[0]])
        anchors = a + np.linspace(.14, .86, 55)[:, None] * (b - a)
        locations = anchors[:, None, :] + offsets[None, :, None] * inward
        # Both samples are away from the edge: a thin printed frame is dark
        # on the line but has light paper on both sides and scores poorly.
        # Paper may be darker than a pale desk when shaded. Require the
        # transition to persist at two distances; a thin printed rule has
        # similar paper on both sides and must not become the outer edge.
        near = sample(locations + inward * 5) - sample(locations - inward * 5)
        far = sample(locations + inward * 9) - sample(locations - inward * 9)
        positive = np.maximum(near, 0)
        negative = np.where((near < 0) & (far < 0), np.minimum(-near, -far), 0)
        # Prefer the usual bright interior when it is consistently supported;
        # reverse polarity is a fallback, not permission to snap to keyboard
        # keys outside an already supported bright paper boundary.
        contrast = positive if np.count_nonzero(positive.max(axis=1) > 22) >= 22 else negative
        choices = np.argmax(contrast, axis=1)
        strengths = contrast[np.arange(len(anchors)), choices]
        supported = strengths > 22
        if np.count_nonzero(supported) < 22:
            lines.append((a, direction))
            continue
        edge = locations[np.arange(len(anchors)), choices][supported]
        vx, vy, x, y = cv2.fitLine(edge.astype(np.float32), cv2.DIST_HUBER, 0, .01, .01).ravel()
        fitted = np.array([vx, vy])
        if abs(float(fitted @ direction)) < .997:
            lines.append((a, direction))
        else:
            lines.append((np.array([x, y]), fitted))
    refined = []
    for index in range(4):
        a, u = lines[index - 1]
        b, v = lines[index]
        matrix = np.column_stack((u, -v))
        if abs(np.linalg.det(matrix)) < .1:
            return corners
        corner = a + np.linalg.solve(matrix, b - a)[0] * u
        if np.linalg.norm(corner - points[index]) > distance * 2:
            return corners
        towards_center = points.mean(axis=0) - points[index]
        towards_center /= max(1, np.linalg.norm(towards_center))
        if float((corner - points[index]) @ towards_center) < -distance * .35:
            corner = points[index]
        refined.append(np.clip(corner / scale, 0, 1))
    return np.asarray(refined)
