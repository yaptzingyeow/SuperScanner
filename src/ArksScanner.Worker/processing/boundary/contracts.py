"""Provider-neutral document boundary contracts."""

from dataclasses import asdict, dataclass
from typing import Literal


@dataclass(frozen=True)
class BoundaryPoint:
    x: float
    y: float


@dataclass(frozen=True)
class DocumentBoundaryResult:
    points: tuple[BoundaryPoint, BoundaryPoint, BoundaryPoint, BoundaryPoint]
    confidence: float
    source: Literal["Ai", "OpenCvFallback", "FullImage"]
    model_version: str | None
    diagnostics_code: str

    def to_json_dict(self) -> dict:
        return {
            "points": [asdict(point) for point in self.points],
            "confidence": self.confidence,
            "source": self.source,
            "modelVersion": self.model_version,
            "diagnosticsCode": self.diagnostics_code,
        }
