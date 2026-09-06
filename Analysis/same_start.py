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
import steady_state as ss

STRIDE = 5  # x, y, rotation, vx, vy

RANDOM_COLOUR = {0: "#404040", 40: "#1f77b4", 80: "#d62728"}
RANDOM_STYLE = {0: "-", 40: "-", 80: "--"}


# --------------------------------------------------------------------------- loading


def load_folder(parent: Path) -> list[dict]:
    """
    Every run under a parent folder, with its condition attached.

    The parent itself is always searched, not only when it has no subfolders. The old rule —
    "use subfolders, or the parent if there are none" — broke the moment anything created a
    subfolder next to the recordings. Running this script once with the default `--out` writes
    `<input>/figures`, so the second run on the same batch folder found one subfolder, ignored
    every recording beside it, and died with "no recordings found" on a folder full of them.
    """
    folders = [p for p in sorted(parent.iterdir()) if p.is_dir()]
    if list(parent.glob("*.json")):
        folders.append(parent)

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


def find_spawn_layouts_folder(parent: Path, levels: int = 4) -> Path | None:
    """
    Locate SimulationRecordings/SpawnLayouts by walking up from the batch folder.

    How far up it sits depends on where the batch was written. A matched-start batch lands in
    SimulationRecordings/MatchedStart_X/<timestamp>/, a Combinations one in
    SimulationRecordings/Combinations/<timestamp>/, and pointing the script at a parent rather than
    a single batch shifts it again. Checking only one or two levels quietly found nothing and the
    spawn figure went missing without an error.
    """
    here = parent.resolve()
    for _ in range(levels + 1):
        candidate = here / "SpawnLayouts"
        if candidate.is_dir():
            return candidate
        if here.parent == here:
            break
        here = here.parent
    return None


def load_layouts(runs: list[dict], parent: Path) -> dict:
    """
    The saved spawn layouts, as {layoutId: (N, 2) array}.

    Prefers the layout files written at spawn time. Where one is missing, falls back to frame 0 of
    a zero-randomness run on that layout — with no random movement the swarm only moves once the
    rules act, so its first recorded frame still sits on the spawn.
    """
    positions = {}

    folder = find_spawn_layouts_folder(parent)
    if folder is not None:
        for path in sorted(folder.glob("*.json")):
            try:
                data = json.loads(path.read_text())
            except json.JSONDecodeError:
                continue
            flat = data.get("positions") or []
            if flat:
                positions[data.get("layoutId", path.stem)] = np.array(flat).reshape(-1, 2)

    # Keep only the layouts these runs actually used, so pointing at one small batch does not
    # scatter the figure with every layout ever saved.
    used = {r["layout"] for r in runs}
    positions = {k: v for k, v in positions.items() if k in used}

    # Fallback reads the file, because load_runs deliberately does not keep frame data in memory.
    for run in runs:
        if run["layout"] in positions or run["random"] != 0:
            continue
        first = frame_zero(run)
        if first is not None:
            positions[run["layout"]] = first

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


def plateau_figure(runs: list[dict], speed: float, radius: float, random_overlay: int = 40,
                   slope_tolerance: float = 0.02, hold_seconds: float = 3.0,
                   window_seconds: float = 1.0):
    """
    Shows exactly where A*(R) comes from, on one perception radius.

    A* is built only from the zero-randomness runs. Each one is plateau-detected on its own and
    contributes one number; A*(R) is the mean of those, and its spread is the spread across spawn
    layouts. The randomised runs never feed into it — they are only ever measured against it.

    Three panels, left to right:

      A  the five reference hulls, with the detected plateau marked on each and A* as a level
      B  the rolling slope those markers came from, against the tolerance band it must stay inside
      C  the five plateau values that were averaged, and the mean with its spread
    """
    references = sorted(select(runs, speed=speed, random=0, R=radius), key=lambda r: r["layout"])
    if not references:
        raise SystemExit(f"no zero-randomness runs at maxSpeed {speed}, R {radius}")

    plateaus = [ss.find_plateau(r["times"], r["areas"],
                                slope_tolerance=slope_tolerance,
                                hold_seconds=hold_seconds,
                                window_seconds=window_seconds)
                for r in references]
    astar = float(np.mean([p["area"] for p in plateaus]))
    spread = float(np.std([p["area"] for p in plateaus]))

    fig, ax = plt.subplots(1, 3, figsize=(15, 4.6), layout="constrained",
                           gridspec_kw={"width_ratios": [1.5, 1.5, 1]})

    colours = plt.cm.viridis(np.linspace(0.15, 0.85, len(references)))

    # ---- A: hull area, plateau marked
    for run, p, c in zip(references, plateaus, colours):
        ax[0].plot(run["times"], run["areas"], lw=1.6, color=c, label=f"layout {_short(run['layout'])}")
        ax[0].plot(p["time"], p["area"], marker="o", ms=7, color=c,
                   markeredgecolor="black", markeredgewidth=0.8, zorder=5)

    ax[0].axhline(astar, color="black", lw=1.6, ls="--")
    ax[0].axhspan(astar - spread, astar + spread, color="black", alpha=0.08)
    ax[0].annotate(f"A* = {astar:.1f} u²  ± {spread:.1f}", (0.98, astar), xycoords=("axes fraction", "data"),
                   ha="right", va="bottom", fontsize=9)

    if random_overlay:
        for run in select(runs, speed=speed, random=random_overlay, R=radius):
            ax[0].plot(run["times"], run["areas"], lw=1.0, color="#d62728", alpha=0.35)
        ax[0].plot([], [], lw=1.4, color="#d62728", alpha=0.6,
                   label=f"random {random_overlay} (not used for A*)")

    ax[0].set_title("A  reference hulls, plateau marked", fontsize=10, loc="left")
    ax[0].set_ylabel("hull area  (u²)")
    ax[0].legend(fontsize=8, frameon=False, loc="lower right")

    # ---- B: the slope the detector actually tests
    for run, p, c in zip(references, plateaus, colours):
        t, slope = _smooth_derivative(run["times"], run["areas"], window_seconds)
        ax[1].plot(t, slope, lw=1.3, color=c)
        ax[1].plot(p["time"], 0, marker="o", ms=7, color=c,
                   markeredgecolor="black", markeredgewidth=0.8, zorder=5)

    # Tolerance is a fraction of each run's own peak area, so one setting works at any scale.
    limit = slope_tolerance * float(np.mean([r["areas"].max() for r in references]))
    ax[1].axhspan(-limit, limit, color="#2ca02c", alpha=0.15)
    ax[1].axhline(0, color="#888888", lw=0.9)
    ax[1].annotate(f"±{limit:.1f} u²/s tolerance\n(2% of peak area)", (0.97, 0.92),
                   xycoords="axes fraction", ha="right", va="top", fontsize=8, color="#2a7")
    ax[1].annotate(f"must stay inside for {hold_seconds:g}s", (0.97, 0.72),
                   xycoords="axes fraction", ha="right", va="top", fontsize=8, color="#555")

    ax[1].set_title(f"B  d(hull)/dt over a {window_seconds:g}s window", fontsize=10, loc="left")
    ax[1].set_ylabel("u²/s")

    # ---- C: the five values that were averaged
    xs = np.arange(len(plateaus))
    ax[2].scatter(xs, [p["area"] for p in plateaus], s=70, c=colours,
                  edgecolors="black", linewidths=0.8, zorder=4)
    ax[2].axhline(astar, color="black", lw=1.6, ls="--")
    ax[2].axhspan(astar - spread, astar + spread, color="black", alpha=0.10)
    ax[2].set_xticks(xs)
    ax[2].set_xticklabels([_short(r["layout"]) for r in references], fontsize=8)
    ax[2].set_title(f"C  A* = mean of these five\n{astar:.1f} ± {spread:.1f} u²  "
                    f"(cv {spread / max(astar, 1e-9):.1%})", fontsize=10, loc="left")
    ax[2].set_ylabel("plateau area  (u²)")
    ax[2].set_xlabel("spawn layout")

    for a in ax[:2]:
        a.set_xlabel("time  (s)")
    for a in ax:
        a.grid(alpha=0.22, lw=0.6)

    settled = sum(1 for p in plateaus if p["reached"])
    fig.suptitle(f"How A* is measured   ·   maxSpeed {speed:g}   ·   perception {radius:g}   ·   "
                 f"{settled}/{len(plateaus)} references settled",
                 fontsize=12, ha="left", x=0.01)
    return fig


# --------------------------------------------------------------------------- clusters


def has_clusters(run: dict) -> bool:
    """True when this recording carries per-frame group sizes."""
    return bool(run["header"].get("clustersRecorded")) and len(run.get("clusters", [])) > 0


def cluster_series(run: dict, min_size: int = 1):
    """
    Group count and largest-group share over time.

    `min_size` ignores stragglers: at 1 a lone agent is its own group, which is the paper's
    reading ("the swarm loses one or more robots"); at 2 only real groups are counted, which is
    usually what you want when asking how many flocks formed.
    """
    counts, largest = [], []
    total = max(int(run["header"].get("agentCount", 1)), 1)

    for sizes in run["clusters"]:
        kept = [s for s in sizes if s >= min_size]
        counts.append(len(kept))
        largest.append((kept[0] if kept else 0) / total)

    return np.asarray(counts), np.asarray(largest)


def fragmentation_summary(run: dict, min_size: int = 2, settle_from: float = 0.5) -> dict:
    """
    One row per run: how fragmented it was, and whether the split lasted.

    A group count on a single frame is not a finding — agents drift in and out of range constantly,
    so a swarm can register a split for a few frames and rejoin. `held_fraction` is the share of
    the settled part of the clip spent fragmented, which is what separates a real split from a
    flicker.
    """
    counts, largest = cluster_series(run, min_size)
    if len(counts) == 0:
        return {}

    times = run["times"]
    late = times >= times[-1] * settle_from

    fragmented = counts > 1
    first = float(times[np.argmax(fragmented)]) if fragmented.any() else None

    return {
        "groups_final": int(counts[-1]),
        "groups_max": int(counts.max()),
        "groups_median_late": float(np.median(counts[late])),
        "largest_share_final": float(largest[-1]),
        "first_split_s": first,
        "held_fraction": float(np.mean(fragmented[late])),
        "ever_fragmented": bool(fragmented.any()),
    }


def cluster_figure(runs: list[dict], speed: float, radii=None, min_size: int = 2):
    """Group count over time, one panel per random level, coloured by perception radius."""
    subset = [r for r in select(runs, speed=speed, R=radii) if has_clusters(r)]
    if not subset:
        raise SystemExit("no recordings with cluster data — re-record with captureClusters on")

    randoms = sorted({r["random"] for r in subset})
    present = sorted({r["R"] for r in subset})
    colour = _radius_colours(present)

    fig, axes = plt.subplots(1, len(randoms), figsize=(4.6 * len(randoms) + 1.8, 4.0),
                             sharex=True, sharey=True, squeeze=False, layout="constrained")
    axes = axes[0]

    for ax, random in zip(axes, randoms):
        for R in present:
            sel = select(subset, random=random, R=R)
            if not sel:
                continue

            span = min(float(r["times"][-1]) for r in sel)
            grid = np.linspace(0, span, 600)
            stack = np.asarray([np.interp(grid, r["times"], cluster_series(r, min_size)[0])
                                for r in sel])

            ax.plot(grid, stack.mean(axis=0), lw=1.6, color=colour[R])
            ax.fill_between(grid, stack.min(axis=0), stack.max(axis=0),
                            color=colour[R], alpha=0.12, linewidth=0)

        ax.axhline(1, color="#888888", lw=1.0, ls="--")
        ax.set_title(f"random {random}", fontsize=10)
        ax.set_xlabel("time  (s)", fontsize=9)
        ax.grid(alpha=0.22, lw=0.6)

    axes[0].set_ylabel(f"groups of {min_size}+ agents", fontsize=9)

    handles = [plt.Line2D([], [], color=colour[R], lw=2.2, label=f"R {R:g}") for R in present]
    fig.legend(handles=handles, loc="outside right upper", fontsize=8, frameon=False,
               title="perception", title_fontsize=9)

    fig.suptitle(f"Group count over time   ·   maxSpeed {speed:g}   ·   mean of layouts, "
                 f"min–max shaded   ·   dashed line = intact", fontsize=12, ha="left", x=0.01)
    return fig


def cluster_size_figure(runs: list[dict], speed: float, radii=None, frame: str = "final"):
    """
    Distribution of group sizes, as a stacked picture of where the agents ended up.

    Each bar is one condition; the segments are the groups, largest at the bottom. A single full
    bar means the swarm stayed intact; many thin segments mean it shattered.
    """
    subset = [r for r in select(runs, speed=speed, R=radii) if has_clusters(r)]
    if not subset:
        raise SystemExit("no recordings with cluster data")

    randoms = sorted({r["random"] for r in subset})
    present = sorted({r["R"] for r in subset})

    fig, axes = plt.subplots(1, len(randoms), figsize=(4.4 * len(randoms) + 1.4, 4.2),
                             sharey=True, squeeze=False, layout="constrained")
    axes = axes[0]

    for ax, random in zip(axes, randoms):
        for x, R in enumerate(present):
            sel = select(subset, random=random, R=R)
            if not sel:
                continue

            index = -1 if frame == "final" else len(sel[0]["clusters"]) // 2
            sizes = sel[0]["clusters"][index]

            bottom = 0
            for rank, s in enumerate(sizes):
                ax.bar(x, s, bottom=bottom, width=0.75,
                       color=plt.cm.tab20(rank % 20), edgecolor="white", linewidth=0.6)
                if s >= 3:
                    ax.text(x, bottom + s / 2, str(s), ha="center", va="center", fontsize=7.5)
                bottom += s

        ax.set_xticks(range(len(present)))
        ax.set_xticklabels([f"{R:g}" for R in present], fontsize=8, rotation=45)
        ax.set_title(f"random {random}", fontsize=10)
        ax.set_xlabel("perception radius", fontsize=9)
        ax.grid(alpha=0.22, lw=0.6, axis="y")

    axes[0].set_ylabel("agents, stacked by group", fontsize=9)
    fig.suptitle(f"Group sizes at the {frame} frame   ·   maxSpeed {speed:g}   ·   one layout",
                 fontsize=12, ha="left", x=0.01)
    return fig


# --------------------------------------------------------------- dispersion vs randomness


def savgol_drift(times, areas, window_seconds: float = 3.0, order: int = 2):
    """
    d(hull)/dt estimated with a Savitzky-Golay filter instead of a difference over a window.

    A boxcar difference is a low-order estimator: it averages the noise but leaves the derivative
    itself rattling frame to frame. Savitzky-Golay fits a low-order polynomial across the window and
    reads the derivative off the fit, which on this data gives the same drift with about a quarter
    of the jitter:

        boxcar 3 s          late mean 2.42 u2/s   roughness 0.453
        Savitzky-Golay 3 s  late mean 2.58 u2/s   roughness 0.117

    It also needs no scipy at capture time if it ever moves into Unity: for a fixed window and
    order the filter is a constant FIR kernel, so the coefficients can be baked in.
    """
    from scipy.signal import savgol_filter

    times = np.asarray(times, dtype=float)
    areas = np.asarray(areas, dtype=float)
    if len(times) < 5:
        return np.zeros_like(areas)

    dt = float(np.median(np.diff(times))) or 1e-3
    window = max(order + 2, int(round(window_seconds / dt)) | 1)
    window = min(window, len(areas) - (1 - len(areas) % 2))
    if window <= order + 1:
        return np.zeros_like(areas)

    return savgol_filter(areas, window, order, deriv=1, delta=dt)


def dispersion_end(run: dict, fraction: float = 0.15, hold_seconds: float = 3.0,
                   window_seconds: float = 3.0) -> dict:
    """
    When the systematic spreading stops and what is left is random motion.

    `find_plateau` asks whether the slope is small in absolute area units. On a randomised run that
    question can never be answered yes: the drift settles to about 2.4 u2/s while the noise on the
    slope is about 11 u2/s, so |slope| keeps crossing any tolerance set near zero, and 50-80% of
    randomised runs are reported as never settling even after they have clearly stopped dispersing.

    This asks a different question: has the drift collapsed *relative to its own peak*? Dispersion
    produces a large early drift that decays; diffusion leaves a small residual one. Normalising by
    the run's own peak drift makes the test scale-free and immune to the noise floor.

    Returns the moment the drift first stays under `fraction` of its peak for `hold_seconds`.

    Important: this marks the end of the systematic trend, not the end of all growth. After it
    fires, a randomised hull still creeps up a further 15-45% by the timeout, because bounded
    diffusion keeps spreading the swarm. It answers "is dispersion still driving this?", not
    "has the hull stopped changing?".
    """
    times = np.asarray(run["times"], dtype=float)
    areas = np.asarray(run["areas"], dtype=float)
    drift = savgol_drift(times, areas, window_seconds)

    # Peak taken over the first half, so a late noise spike cannot inflate the reference scale.
    early = drift[times <= times[-1] * 0.5]
    peak = float(np.max(early)) if len(early) else 0.0

    out = {"fires": False, "time": None, "index": None, "drift": drift, "peak_drift": peak,
           "limit": fraction * peak}
    if peak <= 0:
        return out

    dt = float(np.median(np.diff(times))) or 1e-3
    need = max(1, int(round(hold_seconds / dt)))
    streak = 0

    for i, value in enumerate(drift):
        if value <= out["limit"]:
            streak += 1
            if streak >= need:
                out.update(fires=True, index=i - need + 1, time=float(times[i - need + 1]))
                return out
        else:
            streak = 0

    return out


def run_statistic(run: dict, statistic: str = "plateau") -> tuple[float, bool]:
    """
    One number summarising how far a run dispersed, plus whether it can be trusted.

    "plateau" is where the hull settled, the same measure A* uses. "peak" is the largest area the
    hull ever reached. They are not interchangeable: on a settled reference they agree to within
    a rounding error, but on a randomised run that never settles the peak sits 8-10% above the
    plateau, so plotting one condition's peak beside another's plateau silently compares two
    different quantities.

    The second return value is False when the run never plateaued, in which case the number is a
    lower bound rather than a settled value.
    """
    if statistic == "peak":
        return float(run["areas"].max()), True

    p = ss.find_plateau(run["times"], run["areas"])
    return float(p["area"]), bool(p["reached"])


def _mean_over_layouts(sel, metric: str, n_agents: int = 40, points: int = 900):
    """
    Mean of a series across the layouts of one condition, on a common time grid.

    Clips differ in length by a few frames, so they are interpolated onto a shared grid running to
    the shortest of them rather than padded — extending the mean past where a run ended would show
    a trend built from fewer and fewer runs.
    """
    if not sel:
        return None, None, None

    span = min(float(r["times"][-1]) for r in sel)
    grid = np.linspace(0, span, points)

    stack = []
    for run in sel:
        if metric == "walls":
            series = run["unique"] / max(int(run["header"].get("agentCount", n_agents)), 1)
        else:
            series = run["areas"]
        stack.append(np.interp(grid, run["times"], series))

    stack = np.asarray(stack)
    return grid, stack.mean(axis=0), stack.std(axis=0)


def mean_series_figure(runs: list[dict], speed: float, metric: str = "hull", radii=None):
    """
    Mean series across the five layouts, one panel per random level, coloured by radius.

    The per-layout grids elsewhere show every run; this collapses the layouts so the effect of the
    parameter is readable on its own. The shaded band is ±1 sd across layouts, which is the spread
    the paired design is meant to control for.
    """
    subset = select(runs, speed=speed, R=radii)
    randoms = sorted({r["random"] for r in subset})
    present = sorted({r["R"] for r in subset})
    colour = _radius_colours(present)

    fig, axes = plt.subplots(1, len(randoms), figsize=(4.6 * len(randoms) + 1.8, 4.0),
                             sharex=True, sharey=True, squeeze=False, layout="constrained")
    axes = axes[0]

    for ax, random in zip(axes, randoms):
        for R in present:
            grid, mean, sd = _mean_over_layouts(select(subset, random=random, R=R), metric)
            if grid is None:
                continue
            ax.plot(grid, mean, lw=1.6, color=colour[R])
            ax.fill_between(grid, mean - sd, mean + sd, color=colour[R], alpha=0.15, linewidth=0)

        ax.set_title(f"random {random}", fontsize=10)
        ax.set_xlabel("time  (s)", fontsize=9)
        ax.grid(alpha=0.22, lw=0.6)

    label = "agents that have touched a wall" if metric == "walls" else "hull area  (u²)"
    axes[0].set_ylabel(label, fontsize=9)
    if metric == "walls":
        axes[0].yaxis.set_major_formatter(lambda v, p: f"{v:.0%}")

    handles = [plt.Line2D([], [], color=colour[R], lw=2.2, label=f"R {R:g}") for R in present]
    fig.legend(handles=handles, loc="outside right upper", fontsize=8, frameon=False,
               title="perception", title_fontsize=9)

    what = "Wall contact" if metric == "walls" else "Hull area"
    fig.suptitle(f"{what} over time   ·   maxSpeed {speed:g}   ·   mean of 5 layouts, ±1 sd",
                 fontsize=12, ha="left", x=0.01)
    return fig


def reference_peak_figure(runs: list[dict], speed: float, radii=None, statistic: str = "plateau"):
    """
    Randomised hull area against time, with the no-randomness result drawn as a level.

    The horizontal line is where the swarm settles from this very layout set with randomness off,
    so the moment a coloured trace crosses it is the moment that run has dispersed as far as
    separation alone ever gets it. Everything above the line is dispersion randomness bought.
    """
    subset = select(runs, speed=speed)
    if radii is None:
        available = sorted({r["R"] for r in subset})
        radii = [available[i] for i in np.linspace(0, len(available) - 1, 4).astype(int)]

    fig, axes = plt.subplots(1, len(radii), figsize=(3.7 * len(radii) + 1.4, 4.0),
                             sharex=True, squeeze=False, layout="constrained")
    axes = axes[0]

    for ax, R in zip(axes, radii):
        refs = select(subset, random=0, R=R)
        level = float(np.mean([run_statistic(r, statistic)[0] for r in refs])) if refs else np.nan

        for random in sorted({r["random"] for r in subset if r["random"] > 0}):
            grid, mean, sd = _mean_over_layouts(select(subset, random=random, R=R), "hull")
            if grid is None:
                continue
            colour = RANDOM_COLOUR.get(random, "#888888")
            ax.plot(grid, mean, lw=1.7, color=colour, label=f"random {random}")
            ax.fill_between(grid, mean - sd, mean + sd, color=colour, alpha=0.15, linewidth=0)

            crossed = np.flatnonzero(mean >= level)
            if len(crossed):
                ax.plot(grid[crossed[0]], level, marker="v", ms=9, color=colour,
                        markeredgecolor="black", zorder=6)

        ax.axhline(level, color="#404040", lw=1.6, ls="--")
        ax.set_title(f"R {R:g}    no-randomness {level:.0f} u²", fontsize=9.5)
        ax.set_xlabel("time  (s)", fontsize=9)
        ax.grid(alpha=0.22, lw=0.6)

    axes[0].set_ylabel("hull area  (u²)", fontsize=9)
    axes[0].legend(fontsize=8, frameon=False, loc="upper left")

    fig.suptitle(f"Randomised dispersion against the no-randomness level   ·   maxSpeed {speed:g}"
                 f"   ·   marker = crossing", fontsize=12, ha="left", x=0.01)
    return fig


def smoothing_figure(runs: list[dict], speed: float, radius: float, random: int = 40,
                     zoom: tuple[float, float] = (30.0, 50.0)):
    """
    The same derivative estimated four ways, full range and zoomed.

    A boxcar difference averages the noise but leaves the derivative rattling frame to frame.
    Savitzky-Golay fits a low-order polynomial across the window and reads the slope off the fit,
    which recovers the same drift far more cleanly and, being symmetric, adds no lag.
    """
    from scipy.ndimage import gaussian_filter1d

    sel = select(runs, speed=speed, random=random, R=radius)
    if not sel:
        raise SystemExit(f"no runs at maxSpeed {speed}, random {random}, R {radius}")

    run = sel[0]
    t = np.asarray(run["times"], dtype=float)
    a = np.asarray(run["areas"], dtype=float)
    dt = float(np.median(np.diff(t))) or 1e-3

    def boxcar(seconds):
        w = max(2, int(round(seconds / dt)))
        out = np.full(len(a), np.nan)
        out[w:] = (a[w:] - a[:-w]) / (t[w:] - t[:-w])
        return out

    series = [
        ("boxcar 1 s  (current)", boxcar(1.0), "#cccccc", 1.0),
        ("boxcar 3 s", boxcar(3.0), "#999999", 1.1),
        ("Gaussian ~3 s", gaussian_filter1d(a, sigma=(3.0 / dt) / 6.0, order=1,
                                            mode="nearest") / dt, "#2ca02c", 1.3),
        ("Savitzky-Golay 3 s, order 2", savgol_drift(t, a, 3.0, 2), "#1f77b4", 1.8),
    ]

    fig, ax = plt.subplots(1, 2, figsize=(13.5, 4.4), layout="constrained")

    for name, values, colour, lw in series:
        rough = float(np.nanstd(np.diff(values[~np.isnan(values)][-1200:])))
        ax[0].plot(t, values, lw=lw, color=colour, label=f"{name}   (roughness {rough:.2f})")
        ax[1].plot(t, values, lw=lw + 0.2, color=colour)

    for a_ in ax:
        a_.axhline(0, color="#888888", lw=0.9)
        a_.set_xlabel("time  (s)")
        a_.grid(alpha=0.22, lw=0.6)

    ax[0].set_ylabel("d(hull)/dt  (u²/s)")
    ax[0].set_title("A  whole clip", fontsize=10, loc="left")
    ax[0].legend(fontsize=8, frameon=False)
    ax[1].set_xlim(*zoom)
    ax[1].set_title(f"B  zoomed to {zoom[0]:g}-{zoom[1]:g}s, where only randomness is acting",
                    fontsize=10, loc="left")

    fig.suptitle(f"Smoothing the rate of change   ·   maxSpeed {speed:g}, R {radius:g}, "
                 f"random {random}   ·   'roughness' = frame-to-frame jitter of the estimate",
                 fontsize=12, ha="left", x=0.01)
    return fig


def summary_figure(runs: list[dict], speed: float, statistic: str = "plateau"):
    """
    Dispersion against perception radius, one line per random level, averaged over layouts.

    Every line uses the same statistic, and conditions that did not settle are drawn dashed and
    named in the legend, because their value is where the clip stopped rather than where the swarm
    did. Panel B is paired within layout.
    """
    subset = select(runs, speed=speed)
    fig, ax = plt.subplots(1, 2, figsize=(12, 4.4), layout="constrained")

    label = "plateau hull area" if statistic == "plateau" else "peak hull area"
    unsettled = []

    for random in sorted({r["random"] for r in subset}):
        radii = sorted({r["R"] for r in select(subset, random=random)})
        values, spread, settled_fraction = [], [], []

        for R in radii:
            pairs = [run_statistic(r, statistic) for r in select(subset, random=random, R=R)]
            values.append(np.mean([v for v, _ in pairs]))
            spread.append(np.std([v for v, _ in pairs]))
            settled_fraction.append(np.mean([s for _, s in pairs]))

        share = float(np.mean(settled_fraction))
        reliable = share > 0.999
        if not reliable:
            unsettled.append(f"random {random} ({share:.0%} settled)")

        ax[0].errorbar(radii, values, yerr=spread, marker="o", ms=4, lw=1.8, capsize=3,
                       ls="-" if reliable else "--",
                       color=RANDOM_COLOUR.get(random, "#888888"),
                       label=f"random {random}" + ("" if reliable else "  (mostly unsettled)"))

    ax[0].set_title(f"A  {label}", fontsize=10, loc="left")
    ax[0].set_ylabel("hull area  (u²)")

    for random in sorted({r["random"] for r in subset if r["random"] > 0}):
        radii, ratio = [], []
        for R in sorted({r["R"] for r in select(subset, random=random)}):
            pairs = []
            for run in select(subset, random=random, R=R):
                ref = select(subset, random=0, R=R, layout=run["layout"])
                if ref:
                    a, _ = run_statistic(run, statistic)
                    b, _ = run_statistic(ref[0], statistic)
                    pairs.append(a / max(b, 1e-9))
            if pairs:
                radii.append(R)
                ratio.append(np.mean(pairs))
        ax[1].plot(radii, ratio, marker="o", ms=4, lw=1.8,
                   color=RANDOM_COLOUR.get(random, "#888888"), label=f"random {random}")

    ax[1].axhline(1, color="#888888", lw=1)
    ax[1].set_title(f"B  {label} ÷ its no-randomness partner, same layout", fontsize=10, loc="left")
    ax[1].set_ylabel("×")

    for a in ax:
        a.set_xlabel("perception radius")
        a.grid(alpha=0.22, lw=0.6)
        a.legend(fontsize=9, frameon=False)

    note = f"   ·   dashed: {', '.join(unsettled)}" if unsettled else ""
    fig.suptitle(f"Same-start summary   ·   maxSpeed {speed:g}   ·   {label}, mean over layouts{note}",
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
