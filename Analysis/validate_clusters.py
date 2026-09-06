#!/usr/bin/env python3
"""
Independent check of the cluster sizes Unity recorded.

SwarmClusterMetrics builds the perception graph with union-find and stores the component sizes in
each frame. This rebuilds the same graph from the recorded agent positions using scipy's connected
components on a sparse adjacency matrix — a different algorithm and a different implementation —
and compares the answers frame by frame.

The point is the same as validate_hull.py: the production path has one implementation, and this is
a deliberate second one used only for verification.

The edge rule must match SwarmAgent exactly. An agent perceives another when the distance from its
centre to the *edge* of the other's collider is under the perception radius:

    centre_distance - agent_radius < perceptionRadius

so the effective centre-to-centre cutoff is perceptionRadius + agentRadius. The radius comes from
the recording header, where the recorder stores it for the wall contact test.

Usage
-----
    python3 validate_clusters.py RECORDING.json
    python3 validate_clusters.py FOLDER/ --frames 400
"""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

import numpy as np
from scipy.sparse import csr_matrix
from scipy.sparse.csgraph import connected_components
from scipy.spatial import cKDTree

STRIDE = 5  # x, y, rotation, vx, vy


def sizes_from_positions(points: np.ndarray, cutoff: float) -> list[int]:
    """Connected component sizes, largest first, via a KD-tree and scipy's graph routine."""
    n = len(points)
    if n == 0:
        return []

    tree = cKDTree(points)
    pairs = tree.query_pairs(cutoff, output_type="ndarray")

    if len(pairs) == 0:
        return [1] * n

    data = np.ones(len(pairs), dtype=np.int8)
    graph = csr_matrix((data, (pairs[:, 0], pairs[:, 1])), shape=(n, n))

    _, labels = connected_components(graph, directed=False)
    return sorted(np.bincount(labels).tolist(), reverse=True)


def check(path: Path, max_frames: int | None = None, verbose: bool = False) -> dict:
    data = json.loads(path.read_text())
    header = data.get("header", {})
    frames = data.get("frames", [])

    if not header.get("clustersRecorded"):
        return {"file": path.name, "skipped": "no cluster data"}

    radius = float(header.get("perceptionRadius", 0.0))
    agent_radius = float(header.get("agentRadius", 0.0))
    cutoff = radius + agent_radius

    step = 1
    if max_frames and len(frames) > max_frames:
        step = len(frames) // max_frames

    checked = mismatched = 0
    worst = None

    for index in range(0, len(frames), step):
        frame = frames[index]
        recorded = sorted(frame.get("k") or [], reverse=True)
        if not recorded:
            continue

        flat = np.asarray(frame["v"], dtype=float).reshape(-1, STRIDE)
        expected = sizes_from_positions(flat[:, :2], cutoff)

        checked += 1
        if recorded != expected:
            mismatched += 1
            if worst is None:
                worst = {"frame": index, "t": frame.get("t"),
                         "unity": recorded, "scipy": expected}

    result = {"file": path.name, "checked": checked, "mismatched": mismatched,
              "cutoff": cutoff, "worst": worst}

    if verbose and worst:
        print(f"  first mismatch at frame {worst['frame']} (t={worst['t']}):")
        print(f"     unity {worst['unity']}")
        print(f"     scipy {worst['scipy']}")

    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("target", type=Path, help="a recording, or a folder of them")
    parser.add_argument("--frames", type=int, default=300,
                        help="sample at most this many frames per file (default 300)")
    args = parser.parse_args()

    if args.target.is_dir():
        files = [p for p in sorted(args.target.rglob("*.json"))
                 if p.stem != "batch_config" and not p.stem.endswith("_config")]
    else:
        files = [args.target]

    if not files:
        print(f"no recordings under {args.target}", file=sys.stderr)
        return 1

    total_checked = total_bad = skipped = 0

    for path in files:
        result = check(path, args.frames, verbose=True)

        if "skipped" in result:
            skipped += 1
            continue

        total_checked += result["checked"]
        total_bad += result["mismatched"]

        status = "ok" if result["mismatched"] == 0 else f"{result['mismatched']} MISMATCHED"
        print(f"  {path.name[:66]:68} {result['checked']:5d} frames  {status}")

    print(f"\n{len(files) - skipped} files, {total_checked} frames compared, "
          f"{total_bad} mismatched" + (f", {skipped} skipped (no cluster data)" if skipped else ""))

    if total_checked == 0:
        print("\nNothing to verify. Re-record with captureClusters enabled.")
        return 1

    print("\nPASS" if total_bad == 0 else "\nFAIL")
    return 0 if total_bad == 0 else 1


if __name__ == "__main__":
    raise SystemExit(main())
