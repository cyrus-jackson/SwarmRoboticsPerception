#!/usr/bin/env python3
"""
Plot wall contacts and convex hull area over time from a swarm trajectory recording.

Recordings are written by SwarmTrajectoryRecorder as JSON. Frames hold a flat float array of five
values per agent (x, y, rotation, vx, vy) plus `c`, the indices of the agents touching a wall on
that frame.

Two series are produced against time:

  * Hull area     - read straight from the recording. Unity computes the trimmed convex hull with
                    SwarmDensityMetrics, the same code the density end condition uses, so the plot
                    shows exactly the quantity the recorder was deciding on. Use validate_hull.py
                    for an independent scipy check.

  * Wall contacts - also read straight from the recording, tested against the walls that were
                    active for that run.

Usage
-----
    python3 plot_trajectory.py RECORDING.json
    python3 plot_trajectory.py FOLDER/ --out figures --csv
"""

from __future__ import annotations

import argparse
import csv
import json
import math
import sys
from pathlib import Path

import matplotlib
import matplotlib.pyplot as plt
import numpy as np

STRIDE = 5  # x, y, rotation, vx, vy


# --------------------------------------------------------------------------- analysis


def load_trajectory(path: Path) -> dict:
    with path.open() as fh:
        data = json.load(fh)
    if "frames" not in data or "header" not in data:
        raise ValueError(f"{path.name} is not a trajectory file")
    return data


def analyse(data: dict):
    """
    Reads the series straight out of the recording. Hull area and wall contacts are both computed
    in Unity at capture time, by the same code the density end condition uses, so nothing is
    recomputed here. See validate_hull.py for an independent scipy check of those areas.
    """
    frames = data["frames"]

    times = np.empty(len(frames))
    hull_areas = np.empty(len(frames))
    contacts = np.zeros(len(frames), dtype=int)
    cumulative_unique = np.zeros(len(frames), dtype=int)
    ever_touched: set[int] = set()

    for i, frame in enumerate(frames):
        times[i] = frame["t"]
        hull_areas[i] = frame.get("a", 0.0)

        touching = frame.get("c") or []
        contacts[i] = len(touching)
        ever_touched.update(touching)
        cumulative_unique[i] = len(ever_touched)

    return times, hull_areas, contacts, cumulative_unique


# --------------------------------------------------------------------------- plotting


def build_figure(path: Path, data: dict, times, hull_areas, contacts, cumulative):
    header = data["header"]
    n_agents = int(header.get("agentCount", 0))
    has_contacts = bool(header.get("contactsRecorded"))

    fig, (ax_hull, ax_wall) = plt.subplots(
        2, 1, figsize=(11, 7.2), sharex=True, layout="constrained",
        gridspec_kw={"height_ratios": [1, 1]},
    )

    title = header.get("fileName") or path.stem
    subtitle = (
        f"{header.get('swarmType', 'unknown')}   ·   {n_agents} agents   ·   "
        f"perception {header.get('perceptionRadius', 0):.2f}   ·   "
        f"maxSpeed {header.get('maxSpeed', 0):.2f}   ·   friction {header.get('friction', 0):.2f}"
    )
    fig.suptitle(f"{title}\n{subtitle}", fontsize=11, ha="left", x=0.01)

    # ---- hull area
    has_hull = bool(header.get("hullAreaRecorded"))
    trim = header.get("hullTrimFraction", 0)
    hull_label = f"trimmed hull area (trim {trim:.2f})" if has_hull else "trimmed hull area"
    ax_hull.plot(times, hull_areas, color="#1f77b4", linewidth=1.8, label=hull_label)
    if len(hull_areas):
        ax_hull.axhline(hull_areas[0], color="#1f77b4", linestyle="--", linewidth=1,
                        alpha=0.5, label=f"frame 0 = {hull_areas[0]:.1f} u²")

    r = float(header.get("perceptionRadius", 0) or 0)
    if r > 0 and n_agents > 0:
        critical = n_agents * math.pi * r * r / 4.51
        if 0 < critical < max(hull_areas.max() * 3, 1):
            ax_hull.axhline(critical, color="#d62728", linestyle=":", linewidth=1.2,
                            label=f"A_c = {critical:.1f} u² (connectivity)")

    ax_hull.set_ylabel("hull area  (u²)")
    ax_hull.grid(alpha=0.25, linewidth=0.6)
    ax_hull.legend(loc="upper right", fontsize=8, framealpha=0.9)
    ax_hull.set_ylim(bottom=0)

    # ---- wall contacts
    if has_contacts:
        ax_wall.fill_between(times, contacts, color="#ff7f0e", alpha=0.35, step="post")
        ax_wall.step(times, contacts, where="post", color="#d95f02", linewidth=1.4,
                     label="agents touching a wall")

        ax_cum = ax_wall.twinx()
        ax_cum.plot(times, cumulative, color="#6a3d9a", linewidth=1.5,
                    label="cumulative unique agents")
        ax_cum.set_ylabel("unique agents that touched", color="#6a3d9a")
        ax_cum.tick_params(axis="y", labelcolor="#6a3d9a")
        ax_cum.set_ylim(0, max(n_agents, 1))

        handles = ax_wall.get_legend_handles_labels()[0] + ax_cum.get_legend_handles_labels()[0]
        labels = ax_wall.get_legend_handles_labels()[1] + ax_cum.get_legend_handles_labels()[1]
        ax_wall.legend(handles, labels, loc="upper right", fontsize=8, framealpha=0.9)
        ax_wall.set_ylim(0, max(int(contacts.max()) + 1, 2))
    else:
        ax_wall.text(0.5, 0.5,
                     "this recording has no wall contact data\n"
                     "re-record with capture enabled",
                     transform=ax_wall.transAxes, ha="center", va="center",
                     fontsize=10, color="#888888")
        ax_wall.set_yticks([])

    ax_wall.set_ylabel("agents in contact")
    ax_wall.set_xlabel("time  (s)")
    ax_wall.grid(alpha=0.25, linewidth=0.6)
    ax_wall.set_xlim(times[0], times[-1] if len(times) > 1 else times[0] + 1)

    end_reason = header.get("endReason")
    if end_reason:
        for ax in (ax_hull, ax_wall):
            ax.axvline(times[-1], color="#444444", linewidth=1, alpha=0.6)
        ax_hull.annotate(f"ended: {end_reason}", xy=(times[-1], ax_hull.get_ylim()[1]),
                         xytext=(-6, -12), textcoords="offset points",
                         ha="right", fontsize=8, color="#444444")

    return fig


def plot(path: Path, data: dict, times, hull_areas, contacts, cumulative, out_dir: Path):
    """Builds the figure and writes it. Notebooks should call build_figure instead."""
    fig = build_figure(path, data, times, hull_areas, contacts, cumulative)
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / f"{path.stem}.png"
    fig.savefig(out_path, dpi=150)
    plt.close(fig)
    return out_path


def write_csv(path: Path, times, hull_areas, contacts, cumulative, out_dir: Path) -> Path:
    out_dir.mkdir(parents=True, exist_ok=True)
    out_path = out_dir / f"{path.stem}.csv"
    with out_path.open("w", newline="") as fh:
        writer = csv.writer(fh)
        writer.writerow(["time_s", "hull_area", "agents_touching_wall", "cumulative_unique_touched"])
        for row in zip(times, hull_areas, contacts, cumulative):
            writer.writerow([f"{row[0]:.4f}", f"{row[1]:.4f}", row[2], row[3]])
    return out_path


# --------------------------------------------------------------------------- main


def main() -> int:
    # Only force the headless backend when run as a script. Importing this module from a notebook
    # must leave the interactive backend alone, or nothing renders inline.
    matplotlib.use("Agg")

    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("input", type=Path, help="a trajectory .json, or a folder of them")
    parser.add_argument("--out", type=Path, default=None, help="output folder (default: <input>/figures)")
    parser.add_argument("--csv", action="store_true", help="also write the series as CSV")
    args = parser.parse_args()

    if args.input.is_dir():
        files = sorted(p for p in args.input.glob("*.json")
                       if p.stem != "batch_config" and not p.stem.endswith("_config"))
    else:
        files = [args.input]

    if not files:
        print(f"No trajectory files found in {args.input}", file=sys.stderr)
        return 1

    out_dir = args.out or (args.input if args.input.is_dir() else args.input.parent) / "figures"

    for path in files:
        try:
            data = load_trajectory(path)
        except (ValueError, json.JSONDecodeError) as exc:
            print(f"  skipped {path.name}: {exc}")
            continue

        times, hull_areas, contacts, cumulative = analyse(data)
        png = plot(path, data, times, hull_areas, contacts, cumulative, out_dir)

        header = data["header"]
        ratio = hull_areas[-1] / hull_areas[0] if hull_areas[0] > 0 else 0.0

        print(f"{path.name}")
        print(f"   {len(times)} frames over {times[-1]:.2f}s, {header.get('agentCount')} agents")
        if header.get("hullAreaRecorded"):
            print(f"   hull {hull_areas[0]:.1f} -> {hull_areas[-1]:.1f} u²  (ratio {ratio:.2f}, "
                  f"trim {header.get('hullTrimFraction', 0):.2f})")
        else:
            print("   no hull area in this recording, hull panel will be flat")

        if header.get("contactsRecorded"):
            print(f"   contacts at threshold {header.get('contactDistance', 0):.3f} u: "
                  f"peak {int(contacts.max())} at once, "
                  f"{int(cumulative[-1])} of {header.get('agentCount')} agents touched")
        else:
            print("   no wall contact data in this recording, wall panel skipped")

        print(f"   wrote {png}")

        if args.csv:
            print(f"   wrote {write_csv(path, times, hull_areas, contacts, cumulative, out_dir)}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
