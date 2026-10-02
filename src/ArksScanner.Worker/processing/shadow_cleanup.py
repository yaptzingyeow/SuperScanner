"""Conservative paper illumination correction; no generated or replaced text.

Estimate the lighting field on a reduced image, suppress dark strokes with
closing, then apply one bounded gain to all colour channels. This is intended
for document paper, not restoration of photographs or unreadable black areas.
"""
import cv2
import numpy as np


def clean_document(image: np.ndarray, strength: str = 'CleanDocument') -> np.ndarray:
    """Whiten paper without OCR reconstruction; strong mode can lose faint ink."""
    cutoffs = {'CleanDocumentGentle': .90, 'CleanDocument': .83, 'CleanDocumentStrong': .79}
    if strength not in cutoffs:
        raise ValueError('Unknown cleanup strength')
    if (not isinstance(image, np.ndarray) or image.dtype != np.uint8 or
            image.ndim != 3 or image.shape[2] != 3 or
            min(image.shape[:2]) < 2 or image.shape[0] * image.shape[1] > 4_000_000):
        raise ValueError('Invalid document image')
    gray = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY)
    size = max(3, min(61, round(min(gray.shape) * .043))) | 1
    background = cv2.morphologyEx(gray, cv2.MORPH_CLOSE,
        cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (size, size)))
    background = cv2.GaussianBlur(background.astype(np.float32), (0, 0), max(1, size / 4.5))
    ratio = gray.astype(np.float32) / np.maximum(background, 1)
    ink = np.clip((cutoffs[strength] - ratio) / .18, 0, 1)
    result = np.rint(255 * (1 - ink)).astype(np.uint8)
    # Remove isolated dust only. Small punctuation beside text stays intact.
    binary = np.uint8(result < 220)
    count, labels, stats, _ = cv2.connectedComponentsWithStats(binary, 8)
    pixel_scale = max(1, min(gray.shape) / 900)
    limit = max(2, round(pixel_scale ** 2 * (3 if strength == 'CleanDocumentGentle' else 7)))
    areas = stats[:, cv2.CC_STAT_AREA]
    substantial = areas > limit
    substantial[0] = False
    nearby = np.uint8(substantial[labels])
    reach = max(7, round(pixel_scale * 12)) | 1
    nearby = cv2.dilate(nearby, np.ones((reach, reach), np.uint8))
    isolated = (areas[labels] <= limit) & (labels != 0) & (nearby == 0)
    result[isolated] = 255
    return result


def remove_shadows(image: np.ndarray) -> np.ndarray:
    if (not isinstance(image, np.ndarray) or image.dtype != np.uint8 or
            image.ndim != 3 or image.shape[2] != 3 or
            min(image.shape[:2]) < 2 or image.shape[0] * image.shape[1] > 4_000_000):
        raise ValueError("Invalid document image")
    height, width = image.shape[:2]
    if min(height, width) < 16:
        return image.copy()
    scale = min(1.0, 960.0 / max(height, width))
    small = cv2.resize(image, (max(2, round(width * scale)), max(2, round(height * scale))),
                       interpolation=cv2.INTER_AREA)
    # Max-channel illumination preserves coloured ink better than independent
    # channel normalization, which can neutralize blue/red writing.
    light = small.max(axis=2)
    kernel_size = max(9, round(min(light.shape) * .07)) | 1
    kernel = cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (kernel_size, kernel_size))
    background = cv2.morphologyEx(light, cv2.MORPH_CLOSE, kernel)
    background = cv2.GaussianBlur(background.astype(np.float32), (0, 0),
                                  max(1.0, kernel_size / 6), borderType=cv2.BORDER_REPLICATE)
    target = float(np.percentile(background, 90))
    if target < 30 or float(background.max() - background.min()) < 3:
        return image.copy()
    background = cv2.resize(background, (width, height), interpolation=cv2.INTER_LINEAR)
    gain = np.clip(target / np.maximum(background, 1.0), 1.0, 2.5)
    # Scaling all channels together retains stroke contrast and colour ratios;
    # the cap avoids blowing out noise in severely underexposed areas.
    result = image.astype(np.float32) * gain[:, :, None]
    # Estimate a single mild white balance from bright, low-chroma paper,
    # never per-pixel desaturation (which would erase coloured ink).
    pixels = small.astype(np.float32)
    maximum = pixels.max(axis=2)
    minimum = pixels.min(axis=2)
    paper_mask = ((maximum >= np.percentile(maximum, 75)) & (minimum > 80) &
                  ((maximum - minimum) / np.maximum(maximum, 1) < .22))
    if np.count_nonzero(paper_mask) >= small.shape[0] * small.shape[1] * .10:
        paper_color = np.median(pixels[paper_mask], axis=0)
        balance = np.clip(paper_color.max() / np.maximum(paper_color, 1), 1, 1.22)
        result *= balance[None, None, :]
    result = np.rint(result)
    return np.clip(result, 0, 255).astype(np.uint8)
