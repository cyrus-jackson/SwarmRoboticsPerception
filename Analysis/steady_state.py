#!/usr/bin/env python3
"""
Choose a perception radius, and turn the result into a termination threshold.

The problem this solves: for Dispersion you want a perception radius where the swarm settles into a
steady spread on its own, rather than one where it simply expands until the walls stop it. Then you
want the hull area it settles at, so that runs with added randomness can be terminated at the same
degree of dispersion. Holding the endpoint constant means randomness changes the manner of the
motion rather than how far the swarm got.

Three things are measured per run, all from data already in the recordings:

  plateau        the area the hull settles at, and when it got there. Detected by a rolling slope
                 falling below a threshold and staying there, not by simply taking the last frame,
                 which would be fooled by a run still slowly expanding at the timeout.

  wall budget    how much of the swarm has touched a wall by the time it plateaus. This is what
                 separates a genuine steady state from one the arena imposed.

  arena fraction the plateau area against the usable arena. A plateau at most of the arena is
                 saturation wearing a steady state's clothes.

Usage
-----
    python3 steady_state.py FOLDER/                     # table for choosing a radius
    python3 steady_state.py FOLDER/ --budget 0.10
    python3 steady_state.py FOLDER/ --target 390        # when would that threshold fire?
"""

from __future__ import annotations

import argparse
import sys
from pathlib import Path

import numpy as np

from plot_grid import load_runs

# Scene6_Expand: x from -20.20 to 22.23, y from -12.60 to 11.18.
DEFAULT_ARENA_AREA = (22.23 + 20.20) * (11.18 + 12.60)

DEFAULT_WALL_BUDGET = 0.20


# --------------------------------------------------------------------------- plateau


def find_plateau(times: np.ndarray, areas: np.ndarray,
                 slope_tolerance: float = 0.02,
                 hold_seconds: float = 3.0,
                 window_seconds: float = 1.0):
    """
    First moment the hull stops changing meaningfully and stays that way.

    The slope is measured over a rolling window and compared against a tolerance expressed as a
    fraction of the run's own area scale per second, so the same setting works whether a swarm
    settles at 25 u² or 900 u². A run that never settles returns reached=False, and the caller
    should treat its final area as a lower bound rather than a plateau.
    """
    if len(times) < 3:
        return {"reached": False, "area": float(areas[-1]) if len(areas) else 0.0,
                "time": float(times[-1]) if len(times) else 0.0, "index": len(times) - 1}

    dt = float(np.median(np.diff(times))) or 1e-3
    window = max(2, int(round(window_seconds / dt)))
    hold_frames = max(1, int(round(hold_seconds / dt)))

    # Rolling slope in area units per second.
    slope = np.full(len(areas), np.nan)
    slope[window:] = (areas[window:] - areas[:-window]) / (times[window:] - times[:-window])

    scale = max(float(np.nanmax(areas)), 1e-6)
    settled = np.abs(slope) <= slope_tolerance * scale

    # Earliest index from which `settled` stays true for hold_frames.
    run_length = 0
    start = None
    for i, ok in enumerate(settled):
        if ok:
            run_length += 1
            if run_length == 1:
                start = i
            if run_length >= hold_frames:
                return {"reached": True,
                        "index": int(start),
                        "time": float(times[start]),
                        "area": float(np.median(areas[start:]))}
        else:
            run_length = 0
            start = None

    return {"reached": False, "index": len(areas) - 1,
            "time": float(times[-1]), "area": float(areas[-1])}


def wall_budget(run: dict, upto_index: int) -> dict:
    """How much of the swarm had touched a wall by a given frame."""
    n_agents = max(int(run["header"].get("agentCount", 0)), 1)
    upto = max(0, min(upto_index, len(run["contacts"]) - 1))

    return {
        "unique": int(run["unique"][upto]),
        "unique_fraction": float(run["unique"][upto]) / n_agents,
        "peak": int(run["contacts"][: upto + 1].max()) if upto >= 0 else 0,
        "mean": float(run["contacts"][: upto + 1].mean()) if upto >= 0 else 0.0,
    }


def summarise(run: dict, arena_area: float = DEFAULT_ARENA_AREA,
              budget: float = DEFAULT_WALL_BUDGET, **plateau_kwargs) -> dict:
    """Everything needed to judge one run as a candidate reference."""
    p = find_plateau(run["times"], run["areas"], **plateau_kwargs)
    w = wall_budget(run, p["index"])

    return {
        "file": run["path"].stem,
        "perception": run["perceptionRadius"],
        "random": run["randomMovement"],
        "maxSpeed": run["maxSpeed"],
        "plateau_area": round(p["area"], 1),
        "plateau_time": round(p["time"], 2),
        "settled": p["reached"],
        "start_area": round(float(run["areas"][0]), 1),
        "growth": round(p["area"] / float(run["areas"][0]), 2) if run["areas"][0] > 0 else np.nan,
        "arena_fraction": round(p["area"] / arena_area, 3),
        "wall_unique": w["unique"],
        "wall_fraction": round(w["unique_fraction"], 3),
        "wall_peak": w["peak"],
        "within_budget": w["unique_fraction"] <= budget,
        "duration": round(float(run["times"][-1]), 2),
        "ended": run["header"].get("endReason"),
    }


def reference_runs(runs: list[dict], random_value: float = 0.0) -> list[dict]:
    """The zero randomness runs, which define the reference dispersion."""
    return [r for r in runs if abs(r["randomMovement"] - random_value) < 1e-9]


def choose_perception(runs: list[dict], arena_area: float = DEFAULT_ARENA_AREA,
                      budget: float = DEFAULT_WALL_BUDGET, **plateau_kwargs) -> list[dict]:
    """
    Summarise the zero randomness runs, which are the candidates for the reference.

    Sorted by perception radius. A usable candidate has settled=True, within_budget=True, and an
    arena_fraction well short of 1.
    """
    rows = [summarise(r, arena_area, budget, **plateau_kwargs) for r in reference_runs(runs)]
    return sorted(rows, key=lambda row: (row["perception"], row["maxSpeed"]))


def recommend(rows: list[dict]) -> dict | None:
    """The largest perception radius that settles and stays inside the wall budget."""
    usable = [r for r in rows if r["settled"] and r["within_budget"]]
    return max(usable, key=lambda r: r["perception"]) if usable else None


# --------------------------------------------------------------------------- threshold


def crossing_time(run: dict, target_area: float, dwell: float = 0.5,
                  growing: bool | None = None) -> dict:
    """
    When a target hull area rule would have ended this run.

    Mirrors what SimRecorder does: the condition must hold continuously for `dwell` seconds before
    the clip stops, so an oscillating hull cannot end a recording on a single spike.

    `growing` says whether the target is above the start, i.e. whether this is an expansion test.
    Left as None it is inferred per run from that run's own frame-0 area, matching
    SwarmDensityMonitor.IsHullAreaReached. That inference is only safe when the target is clear of
    the spread of starting areas: a target sitting inside that spread flips direction from run to
    run, so an expanding swarm gets scored against a contraction test and never fires. Callers that
    know the direction — because the target came from a reference batch — should pass it
    explicitly rather than let each run guess.
    """
    times, areas = run["times"], run["areas"]
    if len(times) == 0 or target_area <= 0:
        return {"fires": False}

    if growing is None:
        growing = target_area >= areas[0]
    met = areas >= target_area if growing else areas <= target_area

    held_from = None
    for i, ok in enumerate(met):
        if not ok:
            held_from = None
            continue
        if held_from is None:
            held_from = i
        if times[i] - times[held_from] >= dwell:
            return {"fires": True,
                    "time": float(times[i]),
                    "area": float(areas[i]),
                    "frame": int(i),
                    "fraction_of_clip": float(times[i] / times[-1]) if times[-1] > 0 else 0.0}

    return {"fires": False, "closest": float(areas.max() if growing else areas.min())}


def verify_threshold(runs: list[dict], target_area: float, dwell: float = 0.5) -> list[dict]:
    """Apply a candidate threshold to every run, to see what the batch would look like."""
    rows = []
    for run in sorted(runs, key=lambda r: (r["perceptionRadius"], r["randomMovement"], r["maxSpeed"])):
        c = crossing_time(run, target_area, dwell)
        rows.append({
            "perception": run["perceptionRadius"],
            "random": run["randomMovement"],
            "maxSpeed": run["maxSpeed"],
            "fires": c["fires"],
            "at_time": round(c["time"], 2) if c["fires"] else None,
            "clip_fraction": round(c["fraction_of_clip"], 2) if c["fires"] else None,
            "closest_area": None if c["fires"] else round(c.get("closest", 0.0), 1),
            "recorded_duration": round(float(run["times"][-1]), 2),
        })
    return rows


# --------------------------------------------------------------------------- cli


def _print_table(rows: list[dict], columns: list[str]) -> None:
    widths = {c: max(len(c), *(len(str(r.get(c, ""))) for r in rows)) for c in columns}
    print("   " + "  ".join(c.rjust(widths[c]) for c in columns))
    for r in rows:
        print("   " + "  ".join(str(r.get(c, "")).rjust(widths[c]) for c in columns))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("folder", type=Path, help="folder of trajectory recordings")
    parser.add_argument("--budget", type=float, default=DEFAULT_WALL_BUDGET,
                        help=f"fraction of agents allowed to touch a wall by the plateau "
                             f"(default {DEFAULT_WALL_BUDGET})")
    parser.add_argument("--arena", type=float, default=DEFAULT_ARENA_AREA,
                        help=f"usable arena area in u² (default {DEFAULT_ARENA_AREA:.0f})")
    parser.add_argument("--slope", type=float, default=0.02,
                        help="plateau slope tolerance, as a fraction of the run's area scale per second")
    parser.add_argument("--hold", type=float, default=3.0,
                        help="seconds the slope must stay under tolerance to count as settled")
    parser.add_argument("--target", type=float, default=None,
                        help="test this hull area as a termination threshold across every run")
    parser.add_argument("--dwell", type=float, default=0.5,
                        help="dwell time used when testing a target (default 0.5s)")
    args = parser.parse_args()

    runs = load_runs(args.folder)
    if not runs:
        print(f"No trajectory recordings in {args.folder}", file=sys.stderr)
        return 1

    print(f"{len(runs)} runs, arena {args.arena:.0f} u², wall budget {args.budget:.0%}\n")

    rows = choose_perception(runs, args.arena, args.budget,
                             slope_tolerance=args.slope, hold_seconds=args.hold)
    if not rows:
        print("No zero-randomness runs found; those define the reference dispersion.", file=sys.stderr)
        return 1

    print("Reference runs (randomMovement = 0)")
    _print_table(rows, ["perception", "maxSpeed", "plateau_area", "plateau_time", "settled",
                        "arena_fraction", "wall_unique", "wall_fraction", "within_budget"])

    pick = recommend(rows)
    print()
    if pick:
        print(f"Largest radius that settles and stays within budget: perception {pick['perception']:g}")
        print(f"   plateau {pick['plateau_area']} u² at {pick['plateau_time']}s, "
              f"{pick['arena_fraction']:.0%} of the arena, "
              f"{pick['wall_unique']} agents touched ({pick['wall_fraction']:.0%})")
        print(f"   use {pick['plateau_area']} u² as the AbsoluteHullAreaReached target")
    else:
        print("No radius both settles and stays within the wall budget.")
        print("   Either widen --budget, lengthen the clips so plateaus are reached, or sweep smaller radii.")

    if args.target:
        print(f"\nTesting target {args.target:g} u² with {args.dwell:g}s dwell across every run")
        _print_table(verify_threshold(runs, args.target, args.dwell),
                     ["perception", "random", "maxSpeed", "fires", "at_time",
                      "clip_fraction", "closest_area", "recorded_duration"])

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
