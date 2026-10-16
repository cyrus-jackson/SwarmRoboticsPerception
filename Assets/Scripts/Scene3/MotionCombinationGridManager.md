# 32-video motion-combination grid

Open `Assets/Scenes/FlockGrid.unity` or **Tools → Flocking → Open 32-Video Motion Grid**. On the `FlockGridManager` object, use **Motion Combination Grid Manager**:

- **Folder Path**: the folder containing the MP4 recordings directly. Absolute paths, `Assets/...` paths, and paths relative to Assets are accepted. Subfolders are not searched.
- **Repeat**: the recorded repetition number, starting at 1. For example, 3 selects files ending in `_repeat_03.mp4`.
- **Motion Order**: drag the four entries into the desired top-to-bottom row order. Keep each motion type exactly once.

Press Play to display all 32 videos together: four motion rows, eight combinations per row. The default source is `Assets/SimulationRecordings/Combinations/20261009_113950`, with rows Flocking, Densification, Expansion, and Random. The display labels **Densification** and **Expansion** correspond to **DensificationV2** and **Dispersion** in Motion Order and the source filenames. The sequential **All Grid Batch Manager** is disabled to let the new component control playback.

Each row sorts left to right by randomness, then perception radius, then speed, ascending. This gives all combinations of two randomness levels, two radii, and two speeds. Each cell labels its motion type, noise, perception, and speed. For Random motion, recorded noise values 20 and 60 display as 0 and 40 respectively. Video selection and ordering use the original recorded values.

The grid header shows only **Layout 01** (using the selected repeat number), including during recording. Playback and recording status remain available in the Inspector; errors are still shown on the grid.

Changing Folder Path, Repeat, or Motion Order during Play rebuilds the complete grid. Set these before Play and save the scene to retain them. Missing repeats, incomplete rows, duplicate combinations, or mismatched speed/radius values show an error instead of a partial comparison.

Playback settings are on **All Grid Manager**: **Loop**, **Restart Pause**, **Prepare Timeout**, texture size, and spacing. Its component menu provides **Pause Grid**, **Resume Grid**, and **Restart Grid**. All clips prepare before starting; shorter clips hold their last frame until the longest finishes, and the whole grid then restarts if Loop is enabled. The grid is for visual comparison; simultaneous video decoders can drop frames under load.

To record the complete 32-video grid, use the **Recording** section on **Motion Combination Grid Manager**:

- Set **Output Folder Path** to the destination folder. Absolute paths and paths relative to the project folder are accepted. The default is `Grid Recordings/Motion Combinations`. The folder is created if necessary, and an existing folder can be reused.
- Enable **Record One Iteration**, then press Play. You can also check it during playback or use the component menu's **Record One Iteration** action; the grid returns to the start before recording.

Capture begins after all videos prepare and stops when the longest clip finishes, even with Loop disabled. It saves one 1920×1080, 30 fps MP4 with the selected repeat number and a unique timestamp in its filename. The checkbox resets after capture; later playback loops are not recorded. **Status** displays the saved file path.

Unchecking the checkbox, rebuilding the grid, disabling the component, or leaving Play mode interrupts an active capture and preserves a file ending in `_partial.mp4`. Output folder changes apply to the next recording. Recording requires Play mode in the Unity Editor and uses the project's installed Unity Recorder package.
