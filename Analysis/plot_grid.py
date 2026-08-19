#!/usr/bin/env python3
"""
Compare a whole parameter sweep on one page.

A sweep varies three things at once — random movement, perception radius and max speed — which is
one dimension too many for a single pair of axes. The layout used here is small multiples: the two
coarser factors become the grid rows and columns, and the third is overlaid inside every panel as
coloured lines. All runs then sit on shared axes, so a difference is a distance you can see rather
than something you have to hold in your head between figures.

Produces, per motion type:

    <type>_hull_grid.png       hull area against time, every run
    <type>_contacts_grid.png   agents touching a wall against time, every run
    <type>_summary.png         the sweep reduced to scalars, as heatmaps

Parameters come from each recording's header, so nothing is parsed out of filenames.

Usage
-----
    python3 plot_grid.py FOLDER/
    python3 plot_grid.py FOLDER/ --normalise
    python3 plot_grid.py FOLDER/ --rows maxSpeed --cols randomMovement --hue perceptionRadius
"""

from __future__ import annotations

import argparse
import json
import sys
from collections import defaultdict
from pathlib import Path

import matplotlib
import matplotlib.pyplot as plt
import numpy as np

FACTORS = ("randomMovement", "perceptionRadius", "maxSpeed")

SHORT = {
    "randomMovement": "random",
    "perceptionRadius": "perception",
    "maxSpeed": "max speed",
}


def load_runs(folder: Path) -> list[dict]:
    runs = []
    for path in sorted(folder.glob("*.json")):
        if path.stem == "batch_config" or path.stem.endswith("_config"):
            continue

        data = json.loads(path.read_text())
        header = data.get("header", {})
        frames = data.get("frames", [])
        if not frames:
            continue

        times = np.array([f["t"] for f in frames])
        areas = np.array([f.get("a", 0.0) for f in frames])

        contacts = np.array([len(f.get("c") or []) for f in frames])
        unique = np.empty(len(frames), dtype=int)
        seen: set[int] = set()
        for i, f in enumerate(frames):
            seen.update(f.get("c") or [])
            unique[i] = len(seen)

        runs.append({
            "path": path,
            "header": header,
            "times": times,
            "areas": areas,
            "contacts": contacts,
            "unique": unique,
            # Rounded so float noise like 0.15000000596 groups cleanly.
            **{k: round(float(header.get(k, 0.0)), 4) for k in FACTORS},
        })

    return runs


def choose_axes(runs: list[dict], rows: str | None, cols: str | None, hue: str | None):
    """Assign the three factors to grid rows, grid columns and line colour."""
    levels = {f: sorted({r[f] for r in runs}) for f in FACTORS}
    varying = [f for f in FACTORS if len(levels[f]) > 1]

    chosen = [x for x in (rows, cols, hue) if x]
    for name in chosen:
        if name not in FACTORS:
            raise SystemExit(f"unknown factor {name!r}, expected one of {FACTORS}")

    # Default: the factor with fewest levels becomes columns, most becomes the overlay.
    remaining = [f for f in (varying or list(FACTORS)) if f not in chosen]
    remaining.sort(key=lambda f: len(levels[f]))

    if cols is None:
        cols = remaining.pop(0) if remaining else FACTORS[2]
    if rows is None:
        rows = remaining.pop(0) if remaining else FACTORS[1]
    if hue is None:
        hue = remaining.pop(0) if remaining else FACTORS[0]

    return rows, cols, hue, levels


def grid_figure(runs, rows, cols, hue, levels, metric: str, normalise: bool, title: str):
    row_values = levels[rows]
    col_values = levels[cols]
    hue_values = levels[hue]

    colours = plt.cm.viridis(np.linspace(0.15, 0.85, max(len(hue_values), 1)))
    colour_of = {v: colours[i] for i, v in enumerate(hue_values)}

    fig, axes = plt.subplots(
        len(row_values), len(col_values),
        figsize=(4.6 * len(col_values) + 1.6, 2.9 * len(row_values) + 1.6),
        sharex=True, sharey=True, squeeze=False, layout="constrained",
    )

    lookup = defaultdict(list)
    for run in runs:
        lookup[(run[rows], run[cols])].append(run)

    y_max = 0.0
    x_max = 0.0

    for r, row_value in enumerate(row_values):
        for c, col_value in enumerate(col_values):
            ax = axes[r][c]

            for run in sorted(lookup[(row_value, col_value)], key=lambda x: x[hue]):
                if metric == "hull":
                    series = run["areas"]
                    if normalise and series[0] > 1e-9:
                        series = series / series[0]
                else:
                    series = run["contacts"]

                ax.plot(run["times"], series, linewidth=1.5,
                        color=colour_of[run[hue]], label=f"{run[hue]:g}")

                # A dot at the end marks how the clip finished.
                reason = run["header"].get("endReason", "")
                ax.plot(run["times"][-1], series[-1], marker="o" if reason == "timeout" else "*",
                        markersize=5 if reason == "timeout" else 9,
                        color=colour_of[run[hue]], zorder=5)

                y_max = max(y_max, float(np.max(series)))
                x_max = max(x_max, float(run["times"][-1]))

            ax.grid(alpha=0.22, linewidth=0.6)
            ax.set_title(f"{SHORT[rows]} {row_value:g}   ·   {SHORT[cols]} {col_value:g}",
                         fontsize=9.5, pad=4)

    for r in range(len(row_values)):
        label = "hull area / frame 0" if (metric == "hull" and normalise) else (
            "hull area  (u²)" if metric == "hull" else "agents touching a wall")
        axes[r][0].set_ylabel(label, fontsize=9)
    for c in range(len(col_values)):
        axes[-1][c].set_xlabel("time  (s)", fontsize=9)

    if y_max > 0:
        axes[0][0].set_ylim(0, y_max * 1.08)
    if x_max > 0:
        axes[0][0].set_xlim(0, x_max)

    handles = [plt.Line2D([], [], color=colour_of[v], linewidth=2, label=f"{v:g}") for v in hue_values]
    handles.append(plt.Line2D([], [], color="#555555", marker="o", linestyle="", label="ended: timeout"))
    handles.append(plt.Line2D([], [], color="#555555", marker="*", linestyle="", markersize=9,
                              label="ended: rule met"))
    fig.legend(handles=handles, title=SHORT[hue], loc="outside right upper", fontsize=9,
               title_fontsize=9, frameon=False)

    fig.suptitle(title, fontsize=12, ha="left", x=0.01)
    return fig


def summary_figure(runs, rows, cols, hue, levels, title: str):
    """The sweep boiled down to one number per run, as heatmaps."""
    row_values, col_values, hue_values = levels[rows], levels[cols], levels[hue]

    measures = [
        ("final hull / frame 0", lambda r: r["areas"][-1] / r["areas"][0] if r["areas"][0] > 1e-9 else np.nan, "viridis"),
        ("peak agents on a wall", lambda r: float(r["contacts"].max()), "magma"),
        ("unique agents on a wall", lambda r: float(r["unique"][-1]), "magma"),
        ("duration (s)", lambda r: float(r["times"][-1]), "cividis"),
    ]

    fig, axes = plt.subplots(
        len(measures), len(hue_values),
        figsize=(2.9 * len(hue_values) + 2.4, 2.5 * len(measures) + 1.2),
        squeeze=False, layout="constrained",
    )

    index = {(r[rows], r[cols], r[hue]): r for r in runs}

    for m, (name, fn, cmap) in enumerate(measures):
        grids = []
        for h in hue_values:
            grid = np.full((len(row_values), len(col_values)), np.nan)
            for i, rv in enumerate(row_values):
                for j, cv in enumerate(col_values):
                    run = index.get((rv, cv, h))
                    if run is not None:
                        grid[i, j] = fn(run)
            grids.append(grid)

        finite = np.concatenate([g[np.isfinite(g)] for g in grids]) if grids else np.array([0.0])
        vmin, vmax = (float(finite.min()), float(finite.max())) if finite.size else (0.0, 1.0)

        for h_i, h in enumerate(hue_values):
            ax = axes[m][h_i]
            im = ax.imshow(grids[h_i], cmap=cmap, vmin=vmin, vmax=vmax, aspect="auto")

            for i in range(len(row_values)):
                for j in range(len(col_values)):
                    value = grids[h_i][i, j]
                    if np.isfinite(value):
                        # Contrast against the cell rather than assuming a light background.
                        shade = (value - vmin) / (vmax - vmin + 1e-12)
                        ax.text(j, i, f"{value:.2f}", ha="center", va="center", fontsize=8.5,
                                color="white" if shade < 0.55 else "black")

            ax.set_xticks(range(len(col_values)), [f"{v:g}" for v in col_values], fontsize=8)
            ax.set_yticks(range(len(row_values)), [f"{v:g}" for v in row_values], fontsize=8)
            if m == 0:
                ax.set_title(f"{SHORT[hue]} {h:g}", fontsize=10)
            if h_i == 0:
                ax.set_ylabel(f"{name}\n\n{SHORT[rows]}", fontsize=9)
            if m == len(measures) - 1:
                ax.set_xlabel(SHORT[cols], fontsize=9)

        fig.colorbar(im, ax=axes[m].tolist(), fraction=0.025, pad=0.01)

    fig.suptitle(title, fontsize=12, ha="left", x=0.01)
    return fig


def main() -> int:
    # Only force the headless backend when run as a script. Importing this module from a notebook
    # must leave the interactive backend alone, or nothing renders inline.
    matplotlib.use("Agg")

    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("folder", type=Path, help="folder of trajectory recordings")
    parser.add_argument("--out", type=Path, default=None, help="output folder (default: <folder>/figures)")
    parser.add_argument("--rows", default=None, help=f"factor for grid rows, one of {FACTORS}")
    parser.add_argument("--cols", default=None, help="factor for grid columns")
    parser.add_argument("--hue", default=None, help="factor drawn as coloured lines within a panel")
    parser.add_argument("--normalise", action="store_true",
                        help="plot hull area as a ratio to its frame 0 value, so shapes compare "
                             "even when absolute areas differ")
    args = parser.parse_args()

    if not args.folder.is_dir():
        print(f"{args.folder} is not a folder", file=sys.stderr)
        return 1

    runs = load_runs(args.folder)
    if not runs:
        print(f"No trajectory recordings in {args.folder}", file=sys.stderr)
        return 1

    out_dir = args.out or args.folder / "figures"
    out_dir.mkdir(parents=True, exist_ok=True)

    by_type = defaultdict(list)
    for run in runs:
        by_type[run["header"].get("swarmType") or "unknown"].append(run)

    for swarm_type, group in by_type.items():
        rows, cols, hue, levels = choose_axes(group, args.rows, args.cols, args.hue)
        n_agents = group[0]["header"].get("agentCount")

        print(f"{swarm_type}: {len(group)} runs, {n_agents} agents")
        print(f"   rows {SHORT[rows]} {levels[rows]}")
        print(f"   cols {SHORT[cols]} {levels[cols]}")
        print(f"   line {SHORT[hue]} {levels[hue]}")

        stem = swarm_type.lower()
        base = (f"{swarm_type}   ·   {len(group)} runs   ·   {n_agents} agents   ·   "
                f"friction {group[0]['header'].get('friction', 0):.2f}")

        fig = grid_figure(group, rows, cols, hue, levels, "hull", args.normalise,
                          f"{base}\nhull area over time" + (" (normalised to frame 0)" if args.normalise else ""))
        p = out_dir / f"{stem}_hull_grid.png"
        fig.savefig(p, dpi=140)
        plt.close(fig)
        print(f"   wrote {p}")

        fig = grid_figure(group, rows, cols, hue, levels, "contacts", False,
                          f"{base}\nagents touching a wall over time")
        p = out_dir / f"{stem}_contacts_grid.png"
        fig.savefig(p, dpi=140)
        plt.close(fig)
        print(f"   wrote {p}")

        fig = summary_figure(group, rows, cols, hue, levels, f"{base}\nsweep summary")
        p = out_dir / f"{stem}_summary.png"
        fig.savefig(p, dpi=140)
        plt.close(fig)
        print(f"   wrote {p}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
