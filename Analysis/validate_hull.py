#!/usr/bin/env python3
"""
Independently check the hull areas Unity recorded, using scipy.spatial.ConvexHull.


The trim step is reproduced here because it is not part of the hull algorithm: drop the fraction
of agents furthest from the centroid, then hull whatever is left. Everything after that is scipy's.


Usage
-----
    python3 validate_hull.py RECORDING.json
    python3 validate_hull.py FOLDER/ --tolerance 1e-3
"""

from __future__ import annotations

import argparse
import itertools
import json
import math
import sys
from pathlib import Path

import numpy as np
from scipy.spatial import ConvexHull, QhullError

STRIDE = 5


def drop_count(n: int, trim_fraction: float) -> int:
    drop = int(math.floor(n * max(0.0, min(0.5, trim_fraction))))
    return min(drop, max(0, n - 3))


def trimmed_points(points: np.ndarray, trim_fraction: float) -> np.ndarray:
    """The trim step from SwarmDensityMetrics.TrimmedHull, reproduced so scipy can hull the rest."""
    drop = drop_count(len(points), trim_fraction)
    if drop == 0:
        return points

    centre = points.mean(axis=0)
    order = np.argsort(((points - centre) ** 2).sum(axis=1))
    return points[order][: len(points) - drop]


def candidate_areas(points: np.ndarray, trim_fraction: float, decimals: int, max_candidates: int = 64):
    """
    This tries to take different floating point rounding into account when the trimed.
    """
    n = len(points)
    drop = drop_count(n, trim_fraction)
    if drop == 0:
        return [scipy_area(points)], 1

    centre = points.mean(axis=0)
    d2 = ((points - centre) ** 2).sum(axis=1)
    order = np.argsort(d2)

    cut = d2[order[n - drop - 1]]

    # Worst-case shift in d^2 from rounding both coordinates by half a place.
    quantum = 0.5 * 10.0 ** (-decimals)
    uncertainty = 4.0 * math.sqrt(max(cut, 0.0)) * quantum + 1e-9

    definite = [int(i) for i in order[n - drop:] if d2[i] - cut > uncertainty]
    ambiguous = [int(i) for i in order if abs(d2[i] - cut) <= uncertainty]

    still_to_drop = drop - len(definite)
    if still_to_drop < 0 or not ambiguous:
        return [scipy_area(points[order][: n - drop])], 1

    combos = list(itertools.combinations(ambiguous, still_to_drop))
    if len(combos) > max_candidates:
        return [scipy_area(points[order][: n - drop])], 1

    areas = []
    for combo in combos:
        keep = np.delete(points, definite + list(combo), axis=0)
        areas.append(scipy_area(keep))

    return areas, len(combos)


def scipy_area(points: np.ndarray) -> float:
    if len(points) < 3:
        return 0.0
    try:
        # In 2D, .volume is the enclosed area.
        return float(ConvexHull(points).volume)
    except QhullError:
        # Degenerate input, e.g. every agent collinear or coincident.
        return 0.0


def validate(path: Path, tolerance: float, decimals: int) -> bool:
    data = json.loads(path.read_text())
    header = data.get("header", {})
    frames = data.get("frames", [])

    if not header.get("hullAreaRecorded"):
        print(f"{path.name}: no recorded hull area to check")
        return True

    trim = float(header.get("hullTrimFraction", 0.1))

    recorded = np.empty(len(frames))
    reference = np.empty(len(frames))
    relative = np.empty(len(frames))
    tie_resolved = 0

    for i, frame in enumerate(frames):
        pts = np.asarray(frame["v"], dtype=float).reshape(-1, STRIDE)[:, :2]
        area = frame.get("a", 0.0)
        recorded[i] = area

        nominal = scipy_area(trimmed_points(pts, trim))
        nominal_rel = abs(area - nominal) / max(abs(nominal), 1e-6)

        if nominal_rel <= tolerance:
            reference[i] = nominal
            relative[i] = nominal_rel
            continue

        # The straightforward ordering disagrees. Before calling it an error, check whether the
        # file's precision even determines which agents the trim drops.
        options, count = candidate_areas(pts, trim, decimals)
        best = min(options, key=lambda a: abs(area - a))
        best_rel = abs(area - best) / max(abs(best), 1e-6)

        if count > 1 and best_rel <= tolerance:
            tie_resolved += 1
            reference[i] = best
            relative[i] = best_rel
        else:
            reference[i] = nominal
            relative[i] = nominal_rel

    absolute = np.abs(recorded - reference)
    worst = int(np.argmax(relative))
    failures = int((relative > tolerance).sum())
    passed = failures == 0

    print(f"{path.name}")
    print(f"   {len(frames)} frames, trim {trim:.2f}, {header.get('agentCount')} agents")
    print(f"   max abs error  {absolute.max():.6f} u²")
    print(f"   max rel error  {relative.max():.2e}   at frame {worst} "
          f"(unity {recorded[worst]:.4f} vs scipy {reference[worst]:.4f})")
    print(f"   mean rel error {relative.mean():.2e}")
    if tie_resolved:
        print(f"   {tie_resolved} frame(s) had a trim tie the file's precision cannot settle, "
              f"matched against the alternative drop set")
    print(f"   {'PASS' if passed else f'FAIL on {failures} frame(s)'} "
          f"against tolerance {tolerance:.0e}")
    return passed


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("input", type=Path, help="a trajectory .json, or a folder of them")
    parser.add_argument("--decimals", type=int, default=3,
                        help="decimal places the recording was written at, used to size the "
                             "trim tie band (default 3, matching SwarmTrajectory.Save)")
    parser.add_argument("--tolerance", type=float, default=2e-3,
                        help="maximum acceptable relative error (default 2e-3, allowing for the "
                             "3 decimal places the recording is written at)")
    args = parser.parse_args()

    if args.input.is_dir():
        files = sorted(p for p in args.input.glob("*.json")
                       if p.stem != "batch_config" and not p.stem.endswith("_config"))
    else:
        files = [args.input]

    if not files:
        print(f"No trajectory files found in {args.input}", file=sys.stderr)
        return 1

    ok = True
    for path in files:
        try:
            ok &= validate(path, args.tolerance, args.decimals)
        except (ValueError, json.JSONDecodeError) as exc:
            print(f"  skipped {path.name}: {exc}")

    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
