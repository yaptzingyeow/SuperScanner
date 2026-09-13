"""Privacy-preserving release gate for document boundary candidates."""

import argparse
from collections import Counter
import json
import os
from pathlib import Path
import sys
import time

import numpy as np


PASS_TOLERANCE = .02
SEVERE_MISS = .05
HIGH_CONFIDENCE = .78


def page_passes(expected, actual, tolerance=PASS_TOLERANCE):
    expected_points = np.asarray(expected, dtype=np.float32)
    actual_points = np.asarray(actual, dtype=np.float32)
    return bool(
        expected_points.shape == (4, 2)
        and actual_points.shape == (4, 2)
        and np.isfinite(expected_points).all()
        and np.isfinite(actual_points).all()
        and np.max(np.abs(expected_points - actual_points)) <= tolerance)


def _percentile(values, percentile):
    return float(np.percentile(values, percentile)) if values else 0.0


def summarize(results):
    safe_results = list(results)
    total = len(safe_results)
    passes = sum(
        bool(result.get("valid")) and float(result.get("maxError", 1)) <= PASS_TOLERANCE
        for result in safe_results)
    violations = sum(
        float(result.get("confidence", 0)) >= HIGH_CONFIDENCE
        and float(result.get("maxError", 1)) > SEVERE_MISS
        for result in safe_results)
    invalid = sum(not bool(result.get("valid")) for result in safe_results)
    pass_rate = passes / total if total else 0.0
    sources = Counter(str(result.get("source", "Invalid")) for result in safe_results)
    latencies = [float(result.get("latencyMs", 0)) for result in safe_results]

    tags = {}
    for tag in sorted({tag for result in safe_results for tag in result.get("tags", [])}):
        tagged = [result for result in safe_results if tag in result.get("tags", [])]
        tagged_passes = sum(
            bool(result.get("valid")) and float(result.get("maxError", 1)) <= PASS_TOLERANCE
            for result in tagged)
        tags[tag] = {
            "cases": len(tagged),
            "completePagePassRate": tagged_passes / len(tagged),
            "maximumCornerError": max(float(result.get("maxError", 1)) for result in tagged),
        }

    reasons = []
    if total < 60:
        reasons.append("minimum_cases_not_met")
    if pass_rate < .90:
        reasons.append("complete_page_pass_rate_below_target")
    if violations:
        reasons.append("high_confidence_severe_miss")
    if invalid:
        reasons.append("invalid_results_present")

    cases = [{
        "id": str(result.get("id", "unknown")),
        "tags": sorted(str(tag) for tag in result.get("tags", [])),
        "valid": bool(result.get("valid")),
        "source": str(result.get("source", "Invalid")),
        "confidence": float(result.get("confidence", 0)),
        "maxError": float(result.get("maxError", 1)),
        "latencyMs": float(result.get("latencyMs", 0)),
    } for result in safe_results]

    return {
        "totalCases": total,
        "completePagePassRate": pass_rate,
        "confidenceViolations": violations,
        "invalidResults": invalid,
        "medianLatencyMs": float(np.median(latencies)) if latencies else 0.0,
        "p95LatencyMs": _percentile(latencies, 95),
        "sourceCounts": dict(sorted(sources.items())),
        "fallbackCount": total - sources.get("Ai", 0),
        "modelVersions": sorted({
            str(result["modelVersion"])
            for result in safe_results if result.get("modelVersion")}),
        "scenarios": tags,
        "cases": cases,
        "failureReasons": reasons,
        "promotionPassed": not reasons,
    }


def _load_detector(mode):
    processing = Path(__file__).resolve().parents[2] / "src" / "SuperScanner.Worker" / "processing"
    sys.path.insert(0, str(processing))
    from crop_image import create_boundary_detector

    environment = os.environ.copy()
    environment["SUPERSCANNER_BOUNDARY_MODE"] = mode
    return create_boundary_detector(environment)


def _resolve_case_path(manifest_path, value):
    candidate = Path(value)
    if candidate.is_absolute():
        return candidate
    repository_candidate = Path.cwd() / candidate
    return repository_candidate if repository_candidate.exists() else manifest_path.parent / candidate


def run_manifest(manifest_path, mode):
    import cv2

    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("version") != 1 or not isinstance(manifest.get("cases"), list):
        raise ValueError("manifest contract is invalid")
    detector = _load_detector(mode)
    results = []
    for case in manifest["cases"]:
        case_id = str(case.get("id", "unknown"))
        tags = [str(tag) for tag in case.get("tags", [])]
        expected = np.asarray(case.get("expected"), dtype=np.float32)
        source_path = _resolve_case_path(manifest_path, case.get("image", ""))
        started = time.perf_counter()
        source = cv2.imread(str(source_path), cv2.IMREAD_COLOR)
        if source is None or expected.shape != (4, 2):
            results.append({
                "id": case_id, "tags": tags, "valid": False, "source": "Invalid",
                "confidence": 0, "maxError": 1,
                "latencyMs": (time.perf_counter() - started) * 1000,
                "modelVersion": None})
            continue
        detection = detector.detect(source)
        actual = np.asarray([[point.x, point.y] for point in detection.points])
        valid = actual.shape == (4, 2) and np.isfinite(actual).all()
        maximum_error = float(np.max(np.abs(expected - actual))) if valid else 1.0
        results.append({
            "id": case_id,
            "tags": tags,
            "valid": valid,
            "source": detection.source,
            "confidence": detection.confidence,
            "maxError": maximum_error,
            "latencyMs": (time.perf_counter() - started) * 1000,
            "modelVersion": detection.model_version,
        })
    return summarize(results)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--mode", choices=("AiPreferred", "OpenCvOnly", "ManualOnly"),
                        default="OpenCvOnly")
    parser.add_argument("--output", type=Path, required=True)
    arguments = parser.parse_args()
    report = run_manifest(arguments.manifest.resolve(), arguments.mode)
    arguments.output.parent.mkdir(parents=True, exist_ok=True)
    arguments.output.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
    print(json.dumps({
        "promotionPassed": report["promotionPassed"],
        "failureReasons": report["failureReasons"],
        "totalCases": report["totalCases"],
    }, sort_keys=True))
    return 0 if report["promotionPassed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
