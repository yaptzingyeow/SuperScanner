"""Aspect-preserving model preprocessing and coordinate restoration."""

from dataclasses import dataclass

import cv2
import numpy as np


@dataclass(frozen=True)
class LetterboxedImage:
    tensor_bgr: np.ndarray
    scale: float
    pad_x: int
    pad_y: int
    source_width: int
    source_height: int

    def to_source_points(self, points: np.ndarray) -> np.ndarray:
        result = np.asarray(points, dtype=np.float32).copy()
        if result.ndim != 2 or result.shape[1] != 2:
            raise ValueError("points must have shape (n, 2)")
        result[:, 0] = np.clip(
            (result[:, 0] - self.pad_x) / self.scale,
            0,
            self.source_width - 1)
        result[:, 1] = np.clip(
            (result[:, 1] - self.pad_y) / self.scale,
            0,
            self.source_height - 1)
        return result


def letterbox(image: np.ndarray, input_size: int) -> LetterboxedImage:
    if input_size < 2:
        raise ValueError("input_size must be at least 2")
    if image.ndim != 3 or image.shape[2] != 3:
        raise ValueError("image must be a three-channel BGR array")

    source_height, source_width = image.shape[:2]
    if source_height < 1 or source_width < 1:
        raise ValueError("BGR image dimensions must be positive")

    scale = input_size / max(source_width, source_height)
    resized_width = max(1, min(input_size, round(source_width * scale)))
    resized_height = max(1, min(input_size, round(source_height * scale)))
    interpolation = cv2.INTER_AREA if scale < 1 else cv2.INTER_LINEAR
    resized = cv2.resize(image, (resized_width, resized_height), interpolation=interpolation)

    pad_x = (input_size - resized_width) // 2
    pad_y = (input_size - resized_height) // 2
    canvas = np.zeros((input_size, input_size, 3), dtype=image.dtype)
    canvas[pad_y:pad_y + resized_height, pad_x:pad_x + resized_width] = resized

    return LetterboxedImage(
        tensor_bgr=canvas,
        scale=scale,
        pad_x=pad_x,
        pad_y=pad_y,
        source_width=source_width,
        source_height=source_height)
