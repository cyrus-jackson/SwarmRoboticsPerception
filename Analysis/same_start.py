#!/usr/bin/env python3
"""
Same-start runs: what the recordings contain, and what the swarm did on each shared layout.

Every clip in a same-start batch begins from one of a small set of saved spawn layouts, so a run
with random movement has a partner with none that started from the identical arrangement of agents.
That pairing is what makes the comparison here a within-layout one: any difference between the two
traces is the parameter, not the spawn.

Figures, in the order the argument is built:

    inventory()          what conditions and radii the folder actually holds
    layout_figure()      the shared starting positions, one panel per layout
    hull_figure()        hull area against time, one panel per layout
    contacts_figure()    agents that have touched a wall, one panel per layout
    rate_figure()        d(hull)/dt, the same series differentiated
    reference_figure()   each randomised run over its own no-randomness partner

Every figure builder returns a matplotlib Figure and writes nothing, so a notebook can render
inline and a script can save. Series are read straight from the recordings — hull area and wall
contacts are both computed in Unity at capture time.

Usage
-----
    python3 same_start.py ../Assets/SimulationRecordings/MatchedStart_PerceptionRad
    python3 same_start.py FOLDER --out figures --maxspeed 1.5
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

from plot_grid import load_runs

STRIDE = 5  # x, y, rotation, vx, vy

RANDOM_COLOUR = {0: "#404040", 40: "#1f77b4", 80: "#d62728"}
RANDOM_STYLE = {0: "-", 40: "-", 80: "--"}


# --------------------------------------------------------------------------- loading


def load_folder(parent: Path) -> list[dict]:
    """Every run under a parent folder, with its condition attached."""
    folders = [p for p in sorted(parent.iterdir()) if p.is_dir()]
    if not folders and list(parent.glob("*.json")):
        folders = [parent]

    runs = []
    for folder in folders:
        for run in load_runs(folder):
            h = run["header"]
            run["batch"] = folder.name
            run["swarmType"] = h.get("swarmType", "unknown")
            run["layout"] = h.get("spawnLayoutId") or "unknown"
            run["random"] = round(float(run["randomMovement"]))
            run["speed"] = round(float(run["maxSpeed"]), 1)
            run["R"] = round(float(run["perceptionRadius"]), 2)
            runs.append(run)

    if not runs:
        raise SystemExit(f"no recordings found under {parent}")

    return runs


def load_layouts(runs: list[dict], parent: Path) -> dict:
    """
    The saved spawn layouts, as {layoutId: (N, 2) array}.

    Prefers the layout files written at spawn time. Where one is missing, falls back to frame 0 of
    a zero-randomness run on that layout — those move only after the rules act, so their first
    recorded frame still sits on the spawn.
    """
    positions = {}

    for folder in (parent / "SpawnLayouts", parent.parent / "SpawnLayouts"):
        if not folder.is_dir():
            continue
        for path in sorted(folder.glob("*.json")):
            data = json.loads(path.read_text())
            flat = data.get("positions") or []
            if flat:
                positions[data.get("layoutId", path.stem)] = np.array(flat).reshape(-1, 2)

    for run in runs:
        if run["layout"] in positions or run["random"] != 0:
            continue
        first = run["frames"][0] if "frames" in run else None
        if first is not None:
            positions[run["layout"]] = np.array(first["v"]).reshape(-1, STRIDE)[:, :2]

    return positions


def frame_zero(run: dict) -> np.ndarray | None:
    """Agent positions on the first recorded frame, read back from the file."""
    data = json.loads(run["path"].read_text())
    frames = data.get("frames") or []
    if not frames:
        return None
    return np.array(frames[0]["v"]).reshape(-1, STRIDE)[:, :2]


# --------------------------------------------------------------------------- inventory


def inventory(runs: list[dict]) -> dict:
    """What the folder holds: conditions, radii, layouts, and any holes in the design."""
    conditions = defaultdict(list)
    for r in runs:
        conditions[(r["swarmType"], r["random"], r["speed"])].append(r)

    rows = []
    for (swarm_type, random, speed), group in sorted(conditions.items()):
        radii = sorted({r["R"] for r in group})
        layouts = sorted({r["layout"] for r in group})
        rows.append({
            "motion": swarm_type, "random": random, "maxSpeed": speed,
            "clips": len(group), "radii": len(radii), "layouts": len(layouts),
            "R_min": radii[0], "R_max": radii[-1],
            "batches": len({r["batch"] for r in group}),
        })

    all_layouts = sorted({r["layout"] for r in runs})
    per_condition = {k: {r["layout"] for r in v} for k, v in conditions.items()}
    shared = set.intersection(*per_condition.values()) if per_condition else set()

    return {
        "rows": rows,
        "conditions": conditions,
        "randoms": sorted({r["random"] for r in runs}),
        "speeds": sorted({r["speed"] for r in runs}),
        "radii": sorted({r["R"] for r in runs}),
        "layouts": all_layouts,
        "shared_layouts": sorted(shared),
        "fully_shared": len(shared) == len(all_layouts),
    }


def describe(inv: dict) -> str:
    """The inventory as text, for a script or a notebook cell."""
    lines = [
        f"random movement : {', '.join(f'{v:g}' for v in inv['randoms'])}",
        f"max speed       : {', '.join(f'{v:g}' for v in inv['speeds'])}",
        f"perception radii: {len(inv['radii'])} levels, "
        f"{inv['radii'][0]:g} to {inv['radii'][-1]:g}",
        f"                  {', '.join(f'{v:g}' for v in inv['radii'])}",
        f"spawn layouts   : {len(inv['layouts'])}"
        + ("  (all shared by every condition)" if inv["fully_shared"]
           else f"  ({len(inv['shared_layouts'])} shared by every condition)"),
        "",
        f"   {'motion':13} {'random':>7} {'maxSpeed':>9} {'clips':>6} {'layouts':>8} "
        f"{'radii':>6}  R range",
    ]
    for row in inv["rows"]:
        lines.append(
            f"   {row['motion']:13} {row['random']:7g} {row['maxSpeed']:9.1f} {row['clips']:6d} "
            f"{row['layouts']:8d} {row['radii']:6d}  {row['R_min']:g}-{row['R_max']:g}")

    return "\n".join(lines)


# --------------------------------------------------------------------------- helpers


def select(runs: list[dict], **where) -> list[dict]:
    """Filter runs by any of layout, random, speed, R, swarmType."""
    out = runs
    for key, value in where.items():
        if value is None:
            continue
        wanted = value if isinstance(value, (list, tuple, set)) else [value]
        out = [r for r in out if r[key] in wanted]
    return out


def _layout_grid(layouts, title, ylabel, figsize_per=3.2, height=3.4):
    n = len(layouts)
    fig, axes = plt.subplots(1, n, figsize=(figsize_per * n + 1.8, height),
                             squeeze=False, sharex=True, sharey=True, layout="constrained")
    axes = axes[0]
    for ax, layout in zip(axes, layouts):
        ax.set_title(_short(layout), fontsize=9.5, pad=4)
        ax.grid(alpha=0.22, linewidth=0.6)
    axes[0].set_ylabel(ylabel, fontsize=9)
    fig.suptitle(title, fontsize=12, ha="left", x=0.01)
    return fig, axes


def _short(layout_id: str) -> str:
    return layout_id.split("_layout_")[-1] if "_layout_" in layout_id else layout_id


def _radius_colours(radii):
    cmap = plt.cm.viridis(np.linspace(0.12, 0.9, max(len(radii), 1)))
    return {R: cmap[i] for i, R in enumerate(radii)}


def _smooth_derivative(times, areas, window_seconds=1.0):
    """
    Rate of change of hull area, over a rolling window rather than between adjacent frames.

    Frame-to-frame differences at 60 fps are dominated by one agent crossing the trim boundary, so
    a raw difference is mostly noise. Averaging over a second leaves the trend. This is the same
    quantity steady_state.find_plateau tests against its tolerance.
    """
    if len(times) < 3:
        return times, np.zeros_like(times)

    dt = float(np.median(np.diff(times))) or 1e-3
    w = max(2, int(round(window_seconds / dt)))
    if len(times) <= w:
        return times, np.zeros_like(times)

    slope = (areas[w:] - areas[:-w]) / (times[w:] - times[:-w])
    return times[w:], slope


# --------------------------------------------------------------------------- figures


def layout_figure(runs: list[dict], layouts: dict, show_frame_zero: bool = True):
    """The shared starting positions, one panel per layout."""
    ids = sorted(layouts)
    fig, axes = _layout_grid(ids, "Shared spawn layouts   ·   every condition starts from these",
                             "y", figsize_per=3.0, height=3.6)

    for ax, layout in zip(axes, ids):
        pts = layouts[layout]
        ax.scatter(pts[:, 0], pts[:, 1], s=26, marker="x", linewidths=1.3,
                   color="#222222", label="saved layout", zorder=4)

        if show_frame_zero:
            for random in sorted({r["random"] for r in runs}):
                sample = select(runs, layout=layout, random=random)
                if not sample:
                    continue
                first = frame_zero(sample[0])
                if first is None:
                    continue
                ax.scatter(first[:, 0], first[:, 1], s=52, facecolors="none", linewidths=1.1,
                           edgecolors=RANDOM_COLOUR.get(random, "#888888"),
                           label=f"frame 0, random {random}", zorder=3)

        ax.set_aspect("equal")
        ax.set_xlabel("x", fontsize=9)

    handles, labels = axes[0].get_legend_handles_labels()
    seen, uniq = set(), []
    for h, l in zip(handles, labels):
        if l not in seen:
            seen.add(l)
            uniq.append((h, l))
    fig.legend(*zip(*uniq), loc="outside right upper", fontsize=8, frameon=False)
    return fig


def hull_figure(runs: list[dict], speed: float, randoms=None, radii=None, normalise=False):
    """Hull area against time, one panel per layout, coloured by perception radius."""
    return _series_grid(runs, speed, randoms, radii,
                        series=lambda r: (r["areas"] / r["areas"][0]) if normalise else r["areas"],
                        ylabel="hull area / frame 0" if normalise else "hull area  (u²)",
                        title=f"Hull area over time   ·   maxSpeed {speed:g}"
                              + ("   ·   normalised to frame 0" if normalise else ""))


def contacts_figure(runs: list[dict], speed: float, randoms=None, radii=None, cumulative=True):
    """Wall touches, one panel per layout. Cumulative unique agents by default."""
    if cumulative:
        series = lambda r: r["unique"] / max(int(r["header"].get("agentCount", 1)), 1)
        ylabel = "agents that have touched a wall"
    else:
        series = lambda r: r["contacts"]
        ylabel = "agents touching a wall now"

    return _series_grid(runs, speed, randoms, radii, series=series, ylabel=ylabel,
                        percent=cumulative,
                        title=f"Wall contact over time   ·   maxSpeed {speed:g}"
                              + ("   ·   cumulative, share of swarm" if cumulative else ""))


def rate_figure(runs: list[dict], speed: float, randoms=None, radii=None, window=3.0):
    """
    d(hull)/dt, one panel per layout. Where the swarm is still spreading, and how fast.

    The window defaults to 3 s rather than the 1 s used for plateau detection. A randomised hull
    rattles by tens of u²/s frame to frame, and at 1 s thirty-two overlaid traces are a solid band
    with no readable structure. Three seconds keeps the trend and drops the rattle. Pass a subset
    of `radii` when comparing individual runs closely.
    """
    def series(run):
        _, slope = _smooth_derivative(run["times"], run["areas"], window)
        return slope

    def times(run):
        t, _ = _smooth_derivative(run["times"], run["areas"], window)
        return t

    fig = _series_grid(runs, speed, randoms, radii, series=series, times=times,
                       ylabel="d(hull)/dt  (u²/s)",
                       title=f"Rate of hull growth   ·   maxSpeed {speed:g}   ·   "
                             f"{window:g}s window",
                       zero_line=True)
    return fig


def _series_grid(runs, speed, randoms, radii, series, ylabel, title,
                 times=None, percent=False, zero_line=False):
    """Shared layout: one panel per spawn layout, radius as colour, random level as line style."""
    subset = select(runs, speed=speed, random=randoms, R=radii)
    if not subset:
        raise SystemExit(f"no runs at maxSpeed {speed}")

    layouts = sorted({r["layout"] for r in subset})
    present_radii = sorted({r["R"] for r in subset})
    present_random = sorted({r["random"] for r in subset})
    colour = _radius_colours(present_radii)

    fig, axes = _layout_grid(layouts, title, ylabel, figsize_per=3.3, height=3.8)

    for ax, layout in zip(axes, layouts):
        for run in sorted(select(subset, layout=layout), key=lambda r: (r["random"], r["R"])):
            y = series(run)
            x = times(run) if times else run["times"]
            ax.plot(x, y, lw=1.2, color=colour[run["R"]],
                    ls=RANDOM_STYLE.get(run["random"], "-"),
                    alpha=0.5 if run["random"] == 0 else 0.85)
        if zero_line:
            ax.axhline(0, color="#888888", lw=0.9)
        ax.set_xlabel("time  (s)", fontsize=9)

    if percent:
        axes[0].yaxis.set_major_formatter(lambda v, p: f"{v:.0%}")

    handles = [plt.Line2D([], [], color=colour[R], lw=2.2, label=f"R {R:g}")
               for R in present_radii]
    handles.append(plt.Line2D([], [], color="none", label=""))
    handles += [plt.Line2D([], [], color="#333333", lw=1.8,
                           ls=RANDOM_STYLE.get(v, "-"),
                           alpha=0.55 if v == 0 else 1.0, label=f"random {v}")
                for v in present_random]
    fig.legend(handles=handles, loc="outside right upper", fontsize=8, frameon=False)
    return fig


def reference_figure(runs: list[dict], speed: float, radii=None, mode: str = "overlay"):
    """
    Each randomised run against its own no-randomness partner on the same layout.

    mode="overlay" draws both traces. mode="ratio" draws randomised / reference, which is only
    meaningful where both ran to a common time, so it is clipped to the shorter of the two.
    """
    subset = select(runs, speed=speed, R=radii)
    reference = {(r["layout"], r["R"]): r for r in subset if r["random"] == 0}
    randomised = [r for r in subset if r["random"] > 0]
    if not reference or not randomised:
        raise SystemExit(f"need both a reference and a randomised run at maxSpeed {speed}")

    layouts = sorted({r["layout"] for r in randomised})
    present_radii = sorted({r["R"] for r in randomised})
    colour = _radius_colours(present_radii)

    ylabel = "hull area  (u²)" if mode == "overlay" else "hull area ÷ no-randomness"
    title = (f"Randomised runs over their no-randomness partner   ·   maxSpeed {speed:g}"
             if mode == "overlay" else
             f"Dispersion relative to no randomness, same layout   ·   maxSpeed {speed:g}")

    fig, axes = _layout_grid(layouts, title, ylabel, figsize_per=3.3, height=3.8)

    for ax, layout in zip(axes, layouts):
        for run in sorted(select(randomised, layout=layout), key=lambda r: (r["random"], r["R"])):
            ref = reference.get((layout, run["R"]))
            if ref is None:
                continue

            if mode == "overlay":
                ax.plot(run["times"], run["areas"], lw=1.4, color=colour[run["R"]],
                        ls=RANDOM_STYLE.get(run["random"], "-"))
            else:
                span = min(run["times"][-1], ref["times"][-1])
                grid = np.linspace(0, span, 200)[1:]
                a = np.interp(grid, run["times"], run["areas"])
                b = np.interp(grid, ref["times"], ref["areas"])
                ax.plot(grid, a / np.maximum(b, 1e-9), lw=1.4, color=colour[run["R"]],
                        ls=RANDOM_STYLE.get(run["random"], "-"))

        if mode == "overlay":
            for run in sorted(select(subset, layout=layout, random=0), key=lambda r: r["R"]):
                if run["R"] in colour:
                    ax.plot(run["times"], run["areas"], lw=2.4, color=colour[run["R"]],
                            alpha=0.30, zorder=1)
        else:
            ax.axhline(1, color="#888888", lw=1.0)

        ax.set_xlabel("time  (s)", fontsize=9)

    handles = [plt.Line2D([], [], color=colour[R], lw=2.2, label=f"R {R:g}")
               for R in present_radii]
    handles.append(plt.Line2D([], [], color="none", label=""))
    handles += [plt.Line2D([], [], color="#333333", lw=1.8, ls=RANDOM_STYLE.get(v, "-"),
                           label=f"random {v}")
                for v in sorted({r["random"] for r in randomised})]
    if mode == "overlay":
        handles.append(plt.Line2D([], [], color="#333333", lw=3, alpha=0.3,
                                  label="no randomness"))
    fig.legend(handles=handles, loc="outside right upper", fontsize=8, frameon=False)
    return fig


def summary_figure(runs: list[dict], speed: float):
    """Peak hull area against perception radius, one line per random level, averaged over layouts."""
    subset = select(runs, speed=speed)
    fig, ax = plt.subplots(1, 2, figsize=(11.5, 4.2), layout="constrained")

    for random in sorted({r["random"] for r in subset}):
        radii = sorted({r["R"] for r in select(subset, random=random)})
        peak, spread = [], []
        for R in radii:
            v = [r["areas"].max() for r in select(subset, random=random, R=R)]
            peak.append(np.mean(v))
            spread.append(np.std(v))
        ax[0].errorbar(radii, peak, yerr=spread, marker="o", ms=4, lw=1.8, capsize=3,
                       color=RANDOM_COLOUR.get(random, "#888888"), label=f"random {random}")

    ax[0].set_title("A  peak hull area reached", fontsize=10, loc="left")
    ax[0].set_ylabel("hull area  (u²)")

    for random in sorted({r["random"] for r in subset if r["random"] > 0}):
        radii, ratio = [], []
        for R in sorted({r["R"] for r in select(subset, random=random)}):
            pairs = []
            for run in select(subset, random=random, R=R):
                ref = select(subset, random=0, R=R, layout=run["layout"])
                if ref:
                    pairs.append(run["areas"].max() / max(ref[0]["areas"].max(), 1e-9))
            if pairs:
                radii.append(R)
                ratio.append(np.mean(pairs))
        ax[1].plot(radii, ratio, marker="o", ms=4, lw=1.8,
                   color=RANDOM_COLOUR.get(random, "#888888"), label=f"random {random}")

    ax[1].axhline(1, color="#888888", lw=1)
    ax[1].set_title("B  peak hull ÷ its no-randomness partner", fontsize=10, loc="left")
    ax[1].set_ylabel("×")

    for a in ax:
        a.set_xlabel("perception radius")
        a.grid(alpha=0.22, lw=0.6)
        a.legend(fontsize=9, frameon=False)

    fig.suptitle(f"Same-start summary   ·   maxSpeed {speed:g}   ·   mean over layouts",
                 fontsize=12, ha="left", x=0.01)
    return fig


# --------------------------------------------------------------------------- main


def main() -> int:
    matplotlib.use("Agg")

    parser = argparse.ArgumentParser(description=__doc__,
                                     formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("parent", type=Path, help="folder holding the same-start batches")
    parser.add_argument("--out", type=Path, default=None, help="where to write PNGs")
    parser.add_argument("--maxspeed", type=float, default=None,
                        help="only this max speed (default: every one present)")
    args = parser.parse_args()

    if not args.parent.is_dir():
        print(f"{args.parent} is not a folder", file=sys.stderr)
        return 1

    runs = load_folder(args.parent)
    inv = inventory(runs)
    print(describe(inv))

    out = args.out or (args.parent / "figures")
    out.mkdir(parents=True, exist_ok=True)

    layouts = load_layouts(runs, args.parent)
    if layouts:
        layout_figure(runs, layouts).savefig(out / "spawn_layouts.png", dpi=150)

    speeds = [args.maxspeed] if args.maxspeed else inv["speeds"]
    for speed in speeds:
        tag = f"ms{speed:g}".replace(".", "p")
        hull_figure(runs, speed).savefig(out / f"{tag}_hull.png", dpi=150)
        contacts_figure(runs, speed).savefig(out / f"{tag}_walls.png", dpi=150)
        rate_figure(runs, speed).savefig(out / f"{tag}_rate.png", dpi=150)
        reference_figure(runs, speed, mode="ratio").savefig(out / f"{tag}_vs_reference.png", dpi=150)
        summary_figure(runs, speed).savefig(out / f"{tag}_summary.png", dpi=150)
        plt.close("all")

    print(f"\nwrote figures to {out}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
