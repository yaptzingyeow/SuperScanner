"""Confidence scoring and thresholds for provider-neutral page geometry."""

from dataclasses import dataclass

import numpy as np

from .geometry import GeometryEvidence


@dataclass(frozen=True)
class ConfidencePolicy:
    high: float = .78
    medium: float = .58
    fallback_minimum: float = .45

    def __post_init__(self):
        values = (self.high, self.medium, self.fallback_minimum)
        if any(not 0 <= value <= 1 for value in values):
            raise ValueError("confidence thresholds must be between zero and one")
        if not self.high >= self.medium >= self.fallback_minimum:
            raise ValueError("confidence thresholds must be descending")


def score_geometry(evidence: GeometryEvidence) -> float:
    weakest_side = min(evidence.side_support)
    score = (
        .30 * evidence.mean_boundary_probability
        + .25 * weakest_side
        + .15 * evidence.connectedness
        + .15 * evidence.convexity
        + .15 * evidence.coverage)
    return float(np.clip(score, 0, 1))
