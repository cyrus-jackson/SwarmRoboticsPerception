# Analysis

Python tooling for the trajectory recordings written by `SwarmTrajectoryRecorder`.

## plot_trajectory.py

Plots two series against time for a run:

- **Trimmed convex hull area** — read from the recording. Unity computes it at capture time with
  `SwarmDensityMetrics.cs`, the same code the density end condition uses.
- **Wall contacts** — how many agents are touching a wall on each frame, plus a running count of
  how many distinct agents have touched one at any point.

### One implementation, not two

Both series are computed in Unity and stored per frame. Nothing is recomputed in Python, so the
plot cannot drift from what the recorder actually decided on, and there is no second copy of the
hull algorithm to keep in step.

`validate_hull.py` is the exception, and deliberately so: it recomputes the areas with
`scipy.spatial.ConvexHull` and reports the difference. That is a second implementation used for
verification rather than production.

```bash
python3 validate_hull.py path/to/recording.json
```

Expect relative errors around 1e-4. They come from the recording being written at three decimal
places, not from the algorithm — the largest relative error always lands on the smallest area,
which is the signature of rounding. A real algorithmic fault would show a large error on a large
area. `--tolerance` sets the pass threshold, default 2e-3.

One scipy gotcha the script handles: in 2D, `ConvexHull.volume` is the **area** and
`ConvexHull.area` is the **perimeter**.

### Where wall contacts come from

Straight out of the recording. `SwarmTrajectoryRecorder` tests each agent against the walls that
were **active for that run** and stores the touching agent indices per frame, so contacts already
account for walls a motion type had disabled. Nothing is recomputed here.

The threshold Unity used is in the header as `contactDistance` (agent radius plus skin), and the
figure reports it. If you want a different threshold, change it on the recorder and re-record.

Note this is a geometric test rather than a physics event: agents are moved by writing to
`transform.position`, so Unity's collision callbacks never see a proper sweep.

Recordings made before contact capture have no `contactsRecorded` flag. Those still plot the hull
panel, and the wall panel says so instead of guessing.

### Usage

```bash
# one recording
python3 plot_trajectory.py path/to/recording.json

# a whole batch folder, with the series written out as CSV too
python3 plot_trajectory.py ../Assets/SimulationRecordings/SwarmType_vs_MaxSpeed/20260816_120000/ \
    --out output --csv
```

Figures are written as PNG, one per recording. `--csv` additionally writes the underlying series
(`time_s`, `hull_area`, `agents_touching_wall`, `cumulative_unique_touched`) for use elsewhere.

### Options

| Flag | Default | Meaning |
|---|---|---|
| `--out` | `<input>/figures` | Output folder |
| `--csv` | off | Also write the series as CSV |

The hull trim fraction is no longer a flag: it is fixed at capture time and reported in the header
as `hullTrimFraction`, so the figure always states the value that produced the data.

### Reading the hull panel

The dashed line is the frame-0 area, the baseline the Unity density rule compares against. The
dotted red line, when shown, is the critical area for connectivity,
`A_c = N·πR²/4.51` — above it the perception graph tends to fragment, below it the swarm holds
together as one. See `../Docs/SwarmDensityReference.pdf` for where that comes from.

## plot_grid.py

Compares a whole sweep on one page. Three varying factors do not fit on one pair of axes, so the
two coarser ones become grid rows and columns and the third is overlaid inside each panel as
coloured lines. Every run sits on shared axes, which turns a comparison into a distance you can
see rather than something to remember between figures.

```bash
python3 plot_grid.py ../Assets/SimulationRecordings/Combinations/20260817_004421/
python3 plot_grid.py FOLDER/ --normalise
python3 plot_grid.py FOLDER/ --rows maxSpeed --cols randomMovement --hue perceptionRadius
```

Per motion type it writes:

| File | What it shows |
|---|---|
| `<type>_hull_grid.png` | hull area against time, all runs |
| `<type>_contacts_grid.png` | agents touching a wall against time, all runs |
| `<type>_summary.png` | the sweep reduced to scalars, as heatmaps |

Factors are read from each recording's header, never parsed from filenames, and runs are split by
`swarmType` so a batch covering several motion types produces a set of figures each. The axis
assignment is automatic — fewest levels becomes columns, most becomes the coloured overlay — and
`--rows`, `--cols` and `--hue` override it.

A marker at the end of each line shows how the clip finished: a dot for timeout, a star for the
end rule being met. `--normalise` divides the hull series by its frame 0 value, useful when
absolute areas differ so much that shapes are hard to compare.

## steady_state.py

Chooses a perception radius and turns the result into a termination threshold.

The zero-randomness run is the reference: it disperses to a plateau by separation alone, and that
plateau area becomes the stop condition for the randomised runs, so every clip ends at the same
degree of dispersion. Randomness then changes the manner of the motion rather than how far the
swarm got.

```bash
python3 steady_state.py ../Assets/SimulationRecordings/Combinations/Dispersion/
python3 steady_state.py FOLDER/ --budget 0.10
python3 steady_state.py FOLDER/ --target 390       # when would that threshold fire?
```

Three measures per run, all from data already recorded:

| Measure | Why it matters |
|---|---|
| `plateau_area`, `plateau_time`, `settled` | Detected by a rolling slope staying under tolerance, not by taking the last frame, which a still-expanding run would fake |
| `wall_fraction` | Agents that touched a wall by the plateau. Separates a genuine steady state from one the arena imposed |
| `arena_fraction` | Plateau against usable arena. Near 1 means the walls stopped it |

`--budget` sets the acceptable wall-contact fraction, default 20%. `--target` applies a candidate
threshold to every run with the same dwell logic Unity uses, so you can see whether it fires before
the timeout and how much clip duration would vary.

**The trade-off it makes visible:** holding the endpoint constant means duration varies instead.
The verification table reports both so the spread can be stated rather than discovered later.

## matched_start.py

Analyses a set of matched-start batches — several batches sharing the same spawn layouts, one
without random movement and the others with it — and derives the stop threshold from them.

```bash
python3 matched_start.py ../Assets/SimulationRecordings/MatchedStart_PerceptionRad
python3 matched_start.py PARENT --budget 0.10 --dwell 0.5 --csv crossings.csv
```

Point it at the **parent** folder; it discovers the batch subfolders and groups the runs by
`(swarmType, randomMovement, maxSpeed)`, read **per run from each header** rather than averaged
over a folder. Perception radius is deliberately not part of that key — it is the sweep axis
within a condition, so one condition may span several folders and several radii.

Two consequences worth knowing:

- A folder holding more than one condition is **split**, and each part is named for what it holds.
  Pointing the script at `Combinations/` works: 4 folders become 20 conditions.
- A condition split across folders is **merged**. Recording R 0.15-1.90 one evening and
  R 2.15-3.90 the next gives one condition, not two.

It reports, in order of what can invalidate what:

1. **Layout integrity** — do the batches actually share starting layouts, verified by the
   fingerprints recorded in each clip. If not, differences include spawn variation and nothing
   below is a paired comparison.
2. **Convergence** — what fraction of each batch reached a steady state, and whether the hull was
   still growing at the timeout. Only the reference needs to converge, but a plateau measured on a
   still-expanding run is not a plateau.
2b. **Coverage** — which radii each condition actually holds, and which radii a randomised
   condition has without a matching reference (or the reverse). A gap here means a row silently
   disappears from the crossing table.
3. **A\*(R)** — the reference plateau per perception radius, with its spread across layouts.
4. **Crossing** — when each randomised batch reaches A\*(R), in how many of the layouts, and how
   many agents have touched a wall by then.
5. **Recommendation** — the largest radius that fires in every run inside the wall budget, with the
   exact Unity settings to enter.

References are matched to the max speed of the batch they explain, because A\* rises with max
speed.

## same_start.py

The descriptive counterpart to `matched_start.py`. Where that one derives a stop threshold and
makes a recommendation, this one just shows what the same-start recordings contain and what the
swarm did, one panel per shared spawn layout.

```bash
python3 same_start.py ../Assets/SimulationRecordings/MatchedStart_PerceptionRad
python3 same_start.py FOLDER --out figures --maxspeed 1.5
```

| Function | Figure |
|---|---|
| `inventory` / `describe` | not a figure: the conditions, radii and layouts actually present |
| `layout_figure` | the shared starting positions, saved layout against recorded frame 0 |
| `hull_figure` | hull area against time, one panel per layout, colour = radius |
| `contacts_figure` | share of the swarm that has touched a wall |
| `rate_figure` | d(hull)/dt — whether a run is still spreading |
| `reference_figure` | each randomised run over its own no-randomness partner, `overlay` or `ratio` |
| `summary_figure` | peak hull against radius, and peak relative to the paired reference |

Every builder returns a `Figure` and writes nothing, so the notebook renders inline and the CLI
saves PNGs.

**Why the rate plot exists.** Hull area says where the swarm got; its slope says whether it is
still going. A reference pulses and returns to zero — that return is the plateau. A randomised run
holds a positive slope for the whole clip, so its final area is where the recording stopped rather
than where the swarm settled. The window defaults to 3 s rather than the 1 s
`steady_state.find_plateau` uses, because at 1 s thirty-two overlaid traces are a solid band.

## Notebooks

Three notebooks in `Notebooks/`, all importing the scripts rather than copying them, so there is
still one implementation of every measure.

| Notebook | Pairs with | For |
|---|---|---|
| `plot_trajectory.ipynb` | `plot_trajectory.py` | one run at a time: figure, series table, rate of change, save |
| `plot_grid.ipynb` | `plot_grid.py` | a whole sweep: grids, normalised grids, summary heatmaps, re-arranged axes, subsets |
| `choose_perception.ipynb` | `steady_state.py` | pick a radius from a single sweep, derive the threshold |
| `matched_start.ipynb` | `matched_start.py` | paired batches: A* curve, crossing times, per-layout traces |
| `same_start.ipynb` | `same_start.py` | what the same-start folder holds, and every series per layout |
| `explore_recordings.ipynb` | both | combined tour, including the scipy validation |

`plot_trajectory.ipynb` lists the recordings in a batch with an index, so picking a run is
`files[7]`. It also plots d(hull)/dt to find the moment a clip changes fastest, which is usually
the part worth watching.

`plot_grid.ipynb` shows the sweep as a sortable table first, then the grids. The axis assignment
is automatic but `pg.choose_axes(runs, rows=..., cols=..., hue=...)` re-arranges it, and swapping
the roles often makes a different effect obvious.

Both start with the same `RECORDINGS` path near the top — point that at a batch folder and the
rest follows.

### Running it in VS Code with the PythonAnalysis conda env

```bash
conda activate PythonAnalysis
conda env update -n PythonAnalysis -f environment.yml   # installs ipykernel and the rest
```

Open the notebook in VS Code, click the kernel picker at the top right, and choose the
`PythonAnalysis` interpreter. The env needs `ipykernel` installed for it to appear in that list —
that is the usual reason an environment is missing. If it still does not show, run
`Developer: Reload Window`, or register it explicitly:

```bash
python -m ipykernel install --user --name pythonanalysis --display-name "Python (PythonAnalysis)"
```

The first cell adds `Analysis/` to `sys.path`, so `import plot_grid` works from the `Notebooks/`
subfolder without installing anything.

### One gotcha that was fixed for this

The scripts used to call `matplotlib.use("Agg")` at import, which is right for a headless script
but would have silently suppressed every inline figure in a notebook. That call now happens inside
`main()`, so importing the modules leaves your interactive backend alone. `plot_trajectory` also
gained `build_figure()`, which returns the figure instead of writing a PNG.

## Requirements

```bash
pip install matplotlib numpy pandas   # scripts and notebook
pip install scipy                     # only for validate_hull.py
pip install ipykernel                 # only for the notebook
```

or use `environment.yml` with conda, as above.
