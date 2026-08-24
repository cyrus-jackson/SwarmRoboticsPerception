#!/usr/bin/env python3
"""
Analyse a set of matched-start batches: pick a perception radius and derive the stop threshold.

The design being analysed: several batches share the same starting layouts, one with no random
movement and the others with it. The no-randomness batch is the reference. It disperses to a
plateau under separation alone, and that plateau area A*(R) becomes the stop condition for the
randomised runs, so every clip ends at the same degree of dispersion and randomness changes the
manner of the motion rather than how far the swarm got.

What this reports, in the order it matters:

  1. Layout integrity   Do the batches actually share starting layouts? If not, the comparison is
                        not paired and nothing below can be trusted.
  2. Convergence        Did each batch reach a steady state? A plateau measured on a run that is
                        still expanding is just wherever it happened to be when time ran out.
  3. A*(R)              Reference plateau per perception radius, with its spread across layouts.
  4. Crossing           When the randomised runs reach A*(R), how reliably, and how many agents
                        have touched a wall by then.
  5. Recommendation     The largest radius that fires in every layout inside the wall budget.

A* depends on max speed, so references are matched to the max speed of the batch they explain.

Usage
-----
    python3 matched_start.py ../Assets/SimulationRecordings/MatchedStart_PerceptionRad
    python3 matched_start.py PARENT --budget 0.10 --dwell 0.5 --csv
"""

from __future__ import annotations

import argparse
import csv
import sys
from collections import defaultdict
from pathlib import Path

import numpy as np

from plot_grid import load_runs
import steady_state as ss


def run_condition(run: dict) -> tuple:
    """The condition a single run belongs to, read from its own header."""
    return (run["header"].get("swarmType", "unknown"),
            round(float(run["randomMovement"])),
            round(float(run["maxSpeed"]), 1))


def discover_batches(parent: Path) -> dict:
    """
    Load every batch subfolder, split into homogeneous conditions.

    Conditions are read per run and grouped, never averaged over a folder. Averaging was wrong in a
    way that failed silently: a folder holding random {0, 40, 80} at maxSpeed {1.5, 4} averages to
    "random 40, maxSpeed 2.8", a condition that never ran, and it would then be treated as a
    randomised batch with no reference. Perception radius is deliberately not part of the key — it
    is the sweep axis within a condition, so a folder covering several radii is one batch.

    A folder holding more than one condition is split, and each part is named for what it holds.
    """
    folders = [p for p in sorted(parent.iterdir()) if p.is_dir()]
    if not folders and list(parent.glob("*.json")):
        folders = [parent]

    batches = {}
    for folder in folders:
        runs = load_runs(folder)
        if not runs:
            continue

        by_condition: dict = defaultdict(list)
        for r in runs:
            by_condition[run_condition(r)].append(r)

        split = len(by_condition) > 1
        for (swarm_type, random, maxSpeed), group in sorted(by_condition.items()):
            name = folder.name
            if split:
                name = f"{folder.name} [{swarm_type} r{random:g} ms{maxSpeed:g}]"
            batches[name] = {
                "folder": folder, "runs": group, "swarmType": swarm_type,
                "random": random, "maxSpeed": maxSpeed,
                "radii": sorted({round(r["perceptionRadius"], 2) for r in group}),
            }

        if split:
            print(f"   note: {folder.name} holds {len(by_condition)} conditions, split into "
                  f"{len(by_condition)} batches", file=sys.stderr)

    return batches


def merge_conditions(batches: dict) -> dict:
    """
    Group runs by (randomMovement, maxSpeed), merging folders that are chunks of one sweep.

    A sweep is often recorded in several sittings — one folder covering R 0.15-1.90 and another
    R 2.15-3.90 at the same settings. Those are one condition split across folders, not two
    conditions, and treating them separately loses whichever the other overwrites.
    """
    merged: dict = defaultdict(lambda: {"runs": [], "folders": []})

    for name, b in sorted(batches.items()):
        key = (b["swarmType"], b["random"], b["maxSpeed"])
        merged[key]["runs"].extend(b["runs"])
        merged[key]["folders"].append(name)

    for (swarm_type, random, maxSpeed), c in merged.items():
        c["swarmType"] = swarm_type
        c["random"] = random
        c["maxSpeed"] = maxSpeed
        c["radii"] = sorted({round(r["perceptionRadius"], 2) for r in c["runs"]})

    return dict(merged)


def coverage_report(conditions: dict) -> None:
    """Which perception radii each condition actually covers. Gaps invalidate comparisons."""
    print("\n\n3. COVERAGE\n")
    print(f"   {'motion':14} {'random':>7} {'maxSpeed':>9} {'clips':>6} {'folders':>8}   "
          f"perception radii")

    for (swarm_type, random, maxSpeed), c in sorted(conditions.items()):
        radii = c["radii"]
        span = f"{radii[0]:g}-{radii[-1]:g} ({len(radii)})" if radii else "none"
        print(f"   {swarm_type:14} {random:7g} {maxSpeed:9.1f} {len(c['runs']):6d} "
              f"{len(c['folders']):8d}   {span}")

    for swarm_type, maxSpeed in sorted({(k[0], k[2]) for k in conditions}):
        ref = conditions.get((swarm_type, 0, maxSpeed))
        if ref is None:
            continue
        for (st, random, ms), c in sorted(conditions.items()):
            if st != swarm_type or ms != maxSpeed or random == 0:
                continue
            missing = [R for R in c["radii"] if R not in ref["radii"]]
            unmatched = [R for R in ref["radii"] if R not in c["radii"]]
            if missing:
                print(f"\n   {swarm_type} random {random:g} at maxSpeed {maxSpeed:g} has radii "
                      f"with no reference: {', '.join(f'{R:g}' for R in missing)}")
            if unmatched:
                print(f"\n   {swarm_type} reference at maxSpeed {maxSpeed:g} covers radii with no "
                      f"random {random:g} run: {', '.join(f'{R:g}' for R in unmatched)}")


def layout_report(batches: dict) -> bool:
    """Confirm the batches really do share starting layouts. Returns True when they do."""
    print("1. LAYOUT INTEGRITY\n")

    per_batch = {}
    consistent = True

    for name, b in batches.items():
        fingerprints = defaultdict(set)
        for r in b["runs"]:
            h = r["header"]
            fingerprints[h.get("spawnLayoutId")].add(h.get("spawnLayoutFingerprint"))

        within_ok = all(len(v) == 1 for v in fingerprints.values())
        consistent &= within_ok
        per_batch[name] = {list(v)[0] for v in fingerprints.values() if len(v) == 1}

        print(f"   {name}: {len(b['runs'])} clips, {len(fingerprints)} layouts, "
              f"{'each layout identical across its clips' if within_ok else 'MISMATCH WITHIN A LAYOUT'}")

    if not any(per_batch.values()):
        print("\n   No layout fingerprints found. These recordings predate matched-start capture,")
        print("   so runs cannot be paired and the comparison below is between different spawns.")
        return False

    shared = set.intersection(*per_batch.values()) if len(per_batch) > 1 else set()
    union = set.union(*per_batch.values())

    print(f"\n   {len(union)} distinct layouts across all batches, {len(shared)} shared by every batch")
    if len(shared) == len(union) and len(union) > 0:
        print("   -> starts match across batches, the comparison is paired")
        return consistent

    print("   -> batches do NOT share all starts; differences include spawn variation")
    return False


def convergence_report(batches: dict) -> dict:
    """Did each batch settle? Reports the fraction of runs that reached a plateau."""
    print("\n\n2. CONVERGENCE\n")
    print(f"   {'batch':22} {'random':>7} {'maxSpeed':>9} {'R range':>11} {'settled':>8} "
          f"{'plateau at':>11} {'growth in last 20%':>19}")

    verdicts = {}
    for name, b in sorted(batches.items(), key=lambda kv: (kv[1]["random"], kv[1]["maxSpeed"])):
        settled, when, rise = [], [], []
        for r in b["runs"]:
            s = ss.summarise(r)
            settled.append(s["settled"])
            when.append(s["plateau_time"])

            n = len(r["areas"])
            a = float(np.mean(r["areas"][int(n * 0.8):int(n * 0.9)]))
            c = float(np.mean(r["areas"][int(n * 0.9):]))
            rise.append((c - a) / max(a, 1e-9))

        frac = float(np.mean(settled))
        verdicts[name] = frac
        radii = sorted({round(r["perceptionRadius"], 2) for r in b["runs"]})
        span = f"{radii[0]:g}-{radii[-1]:g}" if radii else "-"
        print(f"   {name:22} {b['random']:7d} {b['maxSpeed']:9.1f} {span:>11} {frac:7.0%} "
              f"{np.mean(when):10.1f}s {np.mean(rise):18.1%}")

    print("\n   A batch well below 100% never reached steady state. Its own plateau is not")
    print("   meaningful, but it can still be compared against a reference that did converge.")
    return verdicts


# A* must differ from the spawn area by at least this factor, in whichever direction the motion
# type moves. Dispersion grows past 1.2x; Densification shrinks past 1/1.2. A target between the
# two is inside the run-to-run spread of starting areas and is not a threshold at all.
MIN_SEPARATION = 1.2


def reference_areas(runs: list[dict], budget: float = 0.20) -> dict:
    """
    A*(R) from a reference batch: mean, spread, and two validity checks.

    `usable` is False when the target is not a meaningful stop condition:

      * degenerate - A* barely exceeds the frame-0 area, so the swarm never dispersed and the
        threshold is satisfied by jitter around the starting hull rather than by any motion.
      * arena-limited - the reference itself had more than `budget` of its agents on a wall by the
        plateau, so A* measures where the box stopped the swarm, not where separation balanced.
    """
    by_radius = defaultdict(list)
    for r in runs:
        s = ss.summarise(r, budget=budget)
        by_radius[round(r["perceptionRadius"], 2)].append(
            (s["plateau_area"], float(r["areas"][0]), s["wall_fraction"]))

    out = {}
    for R, v in sorted(by_radius.items()):
        areas = np.array([x[0] for x in v])
        growth = float(np.mean(areas) / max(np.mean([x[1] for x in v]), 1e-9))
        walls = float(np.mean([x[2] for x in v]))

        reasons = []
        if 1.0 / MIN_SEPARATION < growth < MIN_SEPARATION:
            reasons.append(f"reference barely moved (A* is {growth:.2f}x the spawn area, inside the "
                           f"spread of starting areas, so the target has no direction)")
        if walls > budget:
            reasons.append(f"reference arena-limited ({walls:.0%} of agents on a wall at plateau)")

        out[R] = {"mean": float(np.mean(areas)), "std": float(np.std(areas)),
                  "cv": float(np.std(areas) / max(np.mean(areas), 1e-9)), "n": len(v),
                  "growth": growth, "ref_walls": walls, "growing": growth >= 1.0,
                  "usable": not reasons, "why_not": "; ".join(reasons)}

    return out


def crossing_report(conditions: dict, budget: float, dwell: float) -> list[dict]:
    """For each randomised condition, when it reaches the reference dispersion."""
    references = {(st, ms): c for (st, random, ms), c in conditions.items() if random == 0}
    if not references:
        print("\n   No zero-randomness batch found; there is no reference to compare against.",
              file=sys.stderr)
        return []

    rows = []
    for (swarm_type, maxSpeed), ref in sorted(references.items()):
        astar = reference_areas(ref["runs"], budget)
        others = [c for (st, random, ms), c in conditions.items()
                  if random > 0 and ms == maxSpeed and st == swarm_type]

        print(f"\n\n4. REFERENCE A*(R) for {swarm_type} at maxSpeed {maxSpeed:g}\n")
        print(f"   {'R':>6} {'A* (u2)':>9} {'sd':>7} {'cv':>7} {'growth':>7} {'ref walls':>10}  verdict")
        for R, s in astar.items():
            print(f"   {R:6.2f} {s['mean']:9.1f} {s['std']:7.1f} {s['cv']:6.1%} "
                  f"{s['growth']:6.1f}x {s['ref_walls']:9.0%}  "
                  f"{'ok' if s['usable'] else 'UNUSABLE - ' + s['why_not']}")

        if not others:
            print(f"\n   (no randomised batch at maxSpeed {maxSpeed:g} to compare)")
            continue

        covered = {round(r["perceptionRadius"], 2) for c in others for r in c["runs"]}
        astar = {R: s for R, s in astar.items() if R in covered}
        if not astar:
            print(f"\n   (reference and randomised runs at maxSpeed {maxSpeed:g} share no radii)")
            continue

        print(f"\n\n5. CROSSING A*(R), dwell {dwell:g}s, wall budget {budget:.0%}\n")
        header = f"   {'R':>6} {'A* (u2)':>9}"
        for b in sorted(others, key=lambda x: x["random"]):
            header += f" | random {b['random']:<3d} {'fires':>6} {'at':>7} {'walls':>6}"
        print(header)

        for R, s in astar.items():
            line = f"   {R:6.2f} {s['mean']:9.1f}"
            for b in sorted(others, key=lambda x: x["random"]):
                at_r = [x for x in b["runs"] if abs(x["perceptionRadius"] - R) < 1e-6]
                times, walls = [], []
                for run in at_r:
                    # Direction comes from the reference that defined the target, not from each
                    # run's own frame 0. At small R the target sits inside the spread of starting
                    # areas, so per-run inference flips to a contraction test on some layouts and
                    # scores a swarm that expanded tenfold as "never reached".
                    c = ss.crossing_time(run, s["mean"], dwell, growing=s["growing"])
                    if c["fires"]:
                        times.append(c["time"])
                        walls.append(run["unique"][c["frame"]] /
                                     max(int(run["header"].get("agentCount", 1)), 1))

                fires = f"{len(times)}/{len(at_r)}"
                if times:
                    line += f" | {'':11}{fires:>6} {np.mean(times):6.1f}s {np.mean(walls):5.0%}"
                else:
                    line += f" | {'':11}{fires:>6} {'never':>7} {'-':>6}"

                rows.append({
                    "swarmType": swarm_type,
                    "maxSpeed": maxSpeed, "perception": R, "random": b["random"],
                    "target_area": round(s["mean"], 1), "fired": len(times), "of": len(at_r),
                    "mean_cross_s": round(float(np.mean(times)), 2) if times else None,
                    "min_cross_s": round(float(np.min(times)), 2) if times else None,
                    "max_cross_s": round(float(np.max(times)), 2) if times else None,
                    "wall_fraction": round(float(np.mean(walls)), 3) if walls else None,
                })
            print(line)

        recommend(rows, astar, swarm_type, maxSpeed, budget)

    return rows


def recommend(rows: list[dict], astar: dict, swarm_type: str, maxSpeed: float,
              budget: float) -> None:
    """Largest radius that fires in every layout of every condition, inside the wall budget."""
    by_radius = defaultdict(list)
    for r in rows:
        if r["maxSpeed"] == maxSpeed and r["swarmType"] == swarm_type:
            by_radius[r["perception"]].append(r)

    usable = []
    for R, entries in by_radius.items():
        if not entries or not astar.get(R, {}).get("usable", False):
            continue
        all_fire = all(e["fired"] == e["of"] and e["of"] > 0 for e in entries)
        within = all((e["wall_fraction"] or 0) <= budget for e in entries)
        if all_fire and within:
            usable.append(R)

    print()
    if usable:
        R = max(usable)
        tested = sorted(by_radius)
        if R == max(tested):
            print(f"   NOTE: {R:g} is the largest radius with randomised runs, so this is the edge of")
            print(f"   the sweep rather than a found limit. Record above {R:g} to see where it binds.\n")
        entries = by_radius[R]
        span = [e for e in entries if e["mean_cross_s"] is not None]
        print(f"   Largest radius firing in every run within budget: perception {R:g}")
        print(f"      target A* = {astar[R]['mean']:.1f} u2   (spread across layouts {astar[R]['cv']:.1%})")
        for e in sorted(entries, key=lambda x: x["random"]):
            if e["mean_cross_s"] is not None:
                print(f"      random {e['random']:<3d} clips {e['min_cross_s']:.1f}-{e['max_cross_s']:.1f}s, "
                      f"{e['wall_fraction']:.0%} of agents on a wall")
        print(f"\n      Unity: AbsoluteHullAreaReached, target {astar[R]['mean']:.0f}, "
              f"dwell 0.5, max time override above {max(e['max_cross_s'] for e in span):.0f}s")
    else:
        print("   No radius fires in every run inside the budget.")
        print("   Widen --budget, or sweep radii where A* sits further above the spawn area.")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("parent", type=Path,
                        help="folder containing the batch subfolders, e.g. MatchedStart_PerceptionRad")
    parser.add_argument("--budget", type=float, default=0.20,
                        help="fraction of agents allowed to have touched a wall by the crossing")
    parser.add_argument("--dwell", type=float, default=0.5,
                        help="seconds the threshold must hold, matching Unity (default 0.5)")
    parser.add_argument("--csv", type=Path, default=None, help="write the crossing table to this path")
    args = parser.parse_args()

    if not args.parent.is_dir():
        print(f"{args.parent} is not a folder", file=sys.stderr)
        return 1

    batches = discover_batches(args.parent)
    if not batches:
        print(f"No batches with recordings under {args.parent}", file=sys.stderr)
        return 1

    print(f"{len(batches)} batches under {args.parent.name}\n")

    conditions = merge_conditions(batches)

    paired = layout_report(batches)
    convergence_report(batches)
    coverage_report(conditions)
    rows = crossing_report(conditions, args.budget, args.dwell)

    if not paired:
        print("\n\nNOTE: the batches are not fully paired, so differences between conditions")
        print("include spawn variation as well as the parameter.")

    if args.csv and rows:
        with args.csv.open("w", newline="") as fh:
            w = csv.DictWriter(fh, fieldnames=list(rows[0]))
            w.writeheader()
            w.writerows(rows)
        print(f"\nwrote {args.csv}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
