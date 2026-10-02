# All Grid Batch Manager

`AllGridBatchManager` drives an `AllGridManager`, recording the entire grid once for each existing parameter combination. It composites existing source videos; it does not run new swarm simulations.

## Setup

1. Add **All Grid Batch Manager** to the same GameObject as your **All Grid Manager**. Adding it to a new GameObject automatically adds the required grid manager. Use only one active grid in the scene.
2. Set **Folder Path** to the batch folder containing the MP4 files directly. Leave it empty to use the folder configured on All Grid Manager. Absolute paths and paths relative to `Assets` are supported.
3. If motion types live in separate folders, add an entry per motion to **Motion Sources**, with its motion type and folder. Other motions use the shared folder. Child folders, including `NoRandomnessReferences`, are not searched.
4. Keep **Expected Layouts = 10** and **Columns = 5** for a 5 × 2 grid. Render texture size, spacing, optional grid container, and preparation timeout are configured on All Grid Manager.
5. Leave **Record On Start** enabled and enter Play mode in the Unity Editor. Alternatively, disable it and use the component's **Start Batch Recording** context-menu command during Play mode.

## Record every combination

Enable **All Motion Types** and select **Parameter = All Combinations**. The manager records Random, Dispersion, Flocking, then Densification. Within each motion, it visits existing combinations in ascending randomness, speed, then perception order. It records each combination once, without inventing combinations missing from the source folder. The fixed-value fields are ignored in this mode.

All four motions must have matching source clips. The manager validates the entire selection before beginning: each condition must contain exactly the expected number of distinct numbered layouts, with no duplicate layout indices. Missing motions, missing parameters in filenames, or incomplete grids stop the batch with a Console error. Numbered `repeat_01` suffixes are also accepted, though those represent repetitions rather than matched spawn layouts.

## Record one parameter of one motion

Disable **All Motion Types**, choose **Selected Motion Type**, then select:

| Parameter | Values swept | Values held fixed |
| --- | --- | --- |
| Random | Every available randomness value | Fixed Speed and Fixed Perception |
| Speed | Every available speed value | Fixed Random and Fixed Perception |
| Perception | Every available perception value | Fixed Random and Fixed Speed |

For example, select **Flocking**, **Speed**, **Fixed Random = 0**, and **Fixed Perception = 4** to record one grid for each existing flocking speed at those other values. The fixed value for the parameter being swept is ignored. Choose **All Combinations** with All Motion Types disabled to record every combination for just the selected motion.

## Progress, output, and stopping

Progress appears on the batch component and in the Console. Each job prepares all clips, begins recording, plays every layout once, and saves after the last layout finishes. Shorter clips hold their final frame while the remaining clips finish. The next job starts only after export succeeds. The final grid remains visible when the batch completes.

Output is 1920 × 1080, 30 fps MP4 in the project's `Grid Recordings` folder, using the existing Unity Recorder capture implementation. Names include motion, randomness, speed, perception, timestamp, and a unique suffix.

Use **Stop Batch Recording** in the component context menu to stop. Disabling the batch component also stops it. An interrupted current video is kept with a `_partial` suffix; completed videos remain intact. Preparation, playback, and export errors stop the batch instead of advancing. Starting again creates a new batch from the beginning; there is no resume or skip-existing feature.

Configure the batch before starting. Batch selection fields are read when the queue is built. Do not change the underlying All Grid Manager condition, column count, loop setting, or recording toggle during a batch. Its manual pause and resume commands remain available, but pausing during capture also records the pause.
