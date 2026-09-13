"""Checksum-verified CPU inference for document segmentation models."""

from dataclasses import dataclass
import hashlib
import json
from pathlib import Path
import re

import numpy as np
import onnxruntime as ort

from .preprocess import LetterboxedImage, letterbox


class ModelConfigurationError(RuntimeError):
    """Raised when a model cannot be trusted or its contract is unsupported."""


class ModelInferenceError(RuntimeError):
    """Raised when an otherwise valid model returns an unusable result."""


@dataclass(frozen=True)
class SegmentationPrediction:
    probability_mask: np.ndarray
    mapping: LetterboxedImage
    model_version: str


class OnnxDocumentSegmenter:
    def __init__(self, metadata_path: str | Path):
        self._metadata_path = Path(metadata_path)
        try:
            metadata = json.loads(self._metadata_path.read_text(encoding="utf-8"))
        except (OSError, UnicodeError, json.JSONDecodeError) as error:
            raise ModelConfigurationError("model metadata is invalid") from error

        if metadata.get("enabled") is not True:
            raise ModelConfigurationError("model is disabled")
        self.model_version = self._required_text(metadata, "modelVersion")
        self.input_size = metadata.get("inputSize")
        if not isinstance(self.input_size, int) or not 32 <= self.input_size <= 2048:
            raise ModelConfigurationError("model inputSize is invalid")
        if metadata.get("inputLayout") != "NCHW":
            raise ModelConfigurationError("model inputLayout is unsupported")
        if metadata.get("colorOrder") != "RGB":
            raise ModelConfigurationError("model colorOrder is unsupported")
        if metadata.get("normalization") != "imagenet":
            raise ModelConfigurationError("model normalization is unsupported")

        digest = metadata.get("sha256")
        if not isinstance(digest, str) or re.fullmatch(r"[0-9a-f]{64}", digest) is None:
            raise ModelConfigurationError("model checksum is invalid")
        model_name = self._required_text(metadata, "fileName")
        model_path = self._metadata_path.parent / model_name
        try:
            actual_digest = hashlib.sha256(model_path.read_bytes()).hexdigest()
        except OSError as error:
            raise ModelConfigurationError("model file is unavailable") from error
        if actual_digest != digest:
            raise ModelConfigurationError("model checksum does not match")

        options = ort.SessionOptions()
        options.intra_op_num_threads = 1
        options.inter_op_num_threads = 1
        options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
        try:
            self._session = ort.InferenceSession(
                str(model_path), sess_options=options, providers=["CPUExecutionProvider"])
        except Exception as error:
            raise ModelConfigurationError("model runtime is incompatible") from error
        self._input_name = self._session.get_inputs()[0].name
        self._output_name = self._session.get_outputs()[0].name

    @staticmethod
    def _required_text(metadata: dict, name: str) -> str:
        value = metadata.get(name)
        if not isinstance(value, str) or not value.strip():
            raise ModelConfigurationError(f"model {name} is invalid")
        return value

    def predict(self, image: np.ndarray) -> SegmentationPrediction:
        mapping = letterbox(image, self.input_size)
        rgb = mapping.tensor_bgr[:, :, ::-1].astype(np.float32) / 255.0
        rgb = (rgb - np.array([.485, .456, .406], dtype=np.float32)) / np.array(
            [.229, .224, .225], dtype=np.float32)
        tensor = np.transpose(rgb, (2, 0, 1))[None, ...]
        try:
            raw = self._session.run([self._output_name], {self._input_name: tensor})[0]
        except Exception as error:
            raise ModelInferenceError("model inference failed") from error

        mask = np.asarray(raw, dtype=np.float32).squeeze()
        if mask.shape != (self.input_size, self.input_size) or not np.isfinite(mask).all():
            raise ModelInferenceError("model probability mask is invalid")
        if float(mask.min()) < 0 or float(mask.max()) > 1:
            raise ModelInferenceError("model probability mask is outside range")
        return SegmentationPrediction(mask, mapping, self.model_version)
