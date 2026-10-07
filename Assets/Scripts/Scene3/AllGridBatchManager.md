# All Grid Batch Manager

`AllGridBatchManager` drives an `AllGridManager`, playing and optionally recording grids of existing source videos.

## Automatic folder playback and recording

Leave **Auto Detect Folder** enabled and supply **Folder Path**. No motion selection, fixed parameter values, expected layout count, or column count is needed in this mode. Those fields apply only to manual selection. **Motion Sources** overrides are also ignored in automatic mode; the supplied folder is the source. Absolute paths and paths beginning with `Assets/` are supported. Only MP4s directly in that folder are loaded.

The manager reads `batch_config.json` when present:

- **Matched Start:** one comparison grid per starting layout and fixed condition, with every swept value in ascending numeric order. The same layout appearing at eight Cohesion values gives eight cells, not one deduplicated layout.
- **Single / two parameter sweeps:** swept values appear together, while different motion types and fixed conditions remain separate.
- **Combinations:** each full simulation condition gets its own grid of the available layouts or repetitions. The number of cells is detected automatically.
- **Other SimRecorder batches:** the recorded parameter names supply cell labels; different fixed conditions and obstacle settings get separate grids.

Without a manifest, recognized combination filenames are grouped by motion, noise, speed, and perception. Other MP4s still play as a grid, labeled by filename. The manager does not require a particular filename format for generic playback. A supplied manifest must have unique entries and every referenced video must exist; errors stop selection before capture starts.

For `Assets/SimulationRecordings/MatchedStart_Cohesion/20261007_114900`, automatic mode selects **one 4 × 2 grid with eight DensificationV2 videos**. Cells are labeled **Cohesion 2, 2.5, 3, 3.5, 4, 4.5, 5, 5.5**. The shared settings are Noise 40, Speed 1.5, and Perception 1.5. With **Record Grids** enabled, the output is one MP4 in `Grid Recordings/20261007_114900/`.

Enter Play mode with **Record On Start** enabled to start automatically. Disable **Record Grids** to play without exporting; existing output folders do not block playback. The **Start Batch Playback** context command also starts without recording. Capture retains the existing policy of skipping a source when its output folder already exists. Automatic filenames describe the motion and comparison rather than claiming that the first video's speed and perception apply to every cell.

## Manual condition selection

Disable **Auto Detect Folder** to use the original condition-based workflow below. Leave **Record Grids** enabled to export each selected grid.

1. Add **All Grid Batch Manager** to the same GameObject as your **All Grid Manager**. Adding it to a new GameObject automatically adds the required grid manager. Use only one active grid in the scene.
2. Set **Folder Path** to the batch folder containing the MP4 files directly. Leave it empty to use the folder configured on All Grid Manager. Absolute paths and paths relative to `Assets` are supported.
3. If motion types live in separate folders, add an entry per motion to **Motion Sources**, with its motion type and folder. Other detected motions use the shared folder. Child folders, including `NoRandomnessReferences`, are not searched.
4. Keep **Expected Layouts = 10** and **Columns = 5** for a 5 × 2 grid. Render texture size, spacing, optional grid container, and preparation timeout are configured on All Grid Manager.
5. Leave **Record On Start** enabled and enter Play mode in the Unity Editor. Alternatively, disable it and use the component's **Start Batch Recording** context-menu command during Play mode.

### Record every combination manually

Enable **All Motion Types** and select **Parameter = All Combinations**. The manager detects motion types from the source MP4 filenames and records only those present. It supports DensificationV2 separately from Densification. A folder with just one motion works without changing Selected Motion Type. When present, the existing motions retain the order Random, Dispersion, Flocking, then Densification, followed by DensificationV2 and any additional enum motion types. Within each motion, it visits existing combinations in ascending randomness, speed, then perception order. It records each combination once, without inventing combinations missing from the source folder. The fixed-value fields are ignored in this mode.

Missing motions in the shared folder are skipped. Explicit Motion Sources overrides must contain clips for their assigned motion. The manager validates the entire selection before beginning: each condition must contain exactly the expected number of distinct numbered layouts, with no duplicate layout indices. An empty or unrecognizable shared folder, an explicit override without its assigned motion, missing parameters in filenames, or incomplete grids stop the batch with a Console error. Numbered `repeat_01` suffixes are also accepted, though those represent repetitions rather than matched spawn layouts.

For `Assets/SimulationRecordings/Combinations/20261006_164023`, set **Folder Path** to that path, keep **All Motion Types** enabled, choose **All Combinations**, and set **Expected Layouts = 10**. This folder contains 80 DensificationV2 clips: two randomness values (0 and 40), two speeds (1.5 and 3), and two perception radii (1.5 and 4), with ten repetitions per condition. The manager queues eight DensificationV2 grid videos, each containing ten source clips. Clear any Motion Sources overrides left over from other batches if you want to use only this folder.

### Record one parameter of one motion manually

Disable **All Motion Types**, choose **Selected Motion Type**, then select:

| Parameter | Values swept | Values held fixed |
| --- | --- | --- |
| Random | Every available randomness value | Fixed Speed and Fixed Perception |
| Speed | Every available speed value | Fixed Random and Fixed Perception |
| Perception | Every available perception value | Fixed Random and Fixed Speed |

For example, select **Flocking**, **Speed**, **Fixed Random = 0**, and **Fixed Perception = 4** to record one grid for each existing flocking speed at those other values. The fixed value for the parameter being swept is ignored. Choose **All Combinations** with All Motion Types disabled to record every combination for just the selected motion.

## Progress, output, and stopping

Progress appears on the batch component and in the Console. Each job prepares all clips, begins recording, plays every layout once, and saves after the last layout finishes. Shorter clips hold their final frame while the remaining clips finish. The next job starts only after export succeeds. The final grid remains visible when the batch completes.

Output is 1920 × 1080, 30 fps MP4 in the project's `Grid Recordings/<source folder name>` folder, using the existing Unity Recorder capture implementation. For example, source `Assets/SimulationRecordings/Combinations/20261006_164023` writes all eight videos to `Grid Recordings/20261006_164023/`. Names include motion, randomness, speed, perception, timestamp, and a unique suffix.

If that destination already exists, the source batch is skipped before preparing videos or starting capture. An empty or partially recorded destination also counts as existing. For multiple source folders, only existing destinations are skipped; new destinations are recorded. The destination is created when its first job begins, and all jobs from that source in the current batch may write into it. Source folders with the same final name cannot be recorded together because they would share a destination. Standalone All Grid Manager recordings follow the same destination and existence check.

Use **Stop Batch Recording** in the component context menu to stop. Disabling the batch component also stops it. An interrupted current video is kept with a `_partial` suffix; completed videos remain intact. Preparation, playback, and export errors stop the batch instead of advancing. Starting again skips existing destination folders; there is no resume feature. To deliberately re-record a source, move or remove its output folder first.

Configure the batch before starting. Batch selection fields are read when the queue is built. Do not change the underlying All Grid Manager condition, column count, loop setting, or recording toggle during a batch. Its manual pause and resume commands remain available, but pausing during capture also records the pause.
