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

## Notebooks

Three notebooks in `Notebooks/`, all importing the scripts rather than copying them, so there is
still one implementation of every measure.

| Notebook | Pairs with | For |
|---|---|---|
| `plot_trajectory.ipynb` | `plot_trajectory.py` | one run at a time: figure, series table, rate of change, save |
| `plot_grid.ipynb` | `plot_grid.py` | a whole sweep: grids, normalised grids, summary heatmaps, re-arranged axes, subsets |
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
