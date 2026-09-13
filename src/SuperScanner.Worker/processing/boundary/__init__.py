"""Provider-neutral document boundary detection."""

from .confidence import ConfidencePolicy, score_geometry
from .contracts import BoundaryPoint, DocumentBoundaryResult
from .geometry import GeometryEstimate, GeometryEvidence, estimate_boundary
from .hybrid import HybridBoundaryDetector
from .onnx_segmenter import OnnxDocumentSegmenter

__all__ = [
    "BoundaryPoint",
    "ConfidencePolicy",
    "DocumentBoundaryResult",
    "GeometryEstimate",
    "GeometryEvidence",
    "HybridBoundaryDetector",
    "OnnxDocumentSegmenter",
    "estimate_boundary",
    "score_geometry",
]
