# Motion presentation

Open `Assets/Scenes/MotionPresentation.unity` or **Tools → Flocking → Open Motion Presentation**. Select **Motion Presentation Manager**.

The scene shows one motion at a time. A fullscreen title card appears first, followed by eight videos in two side-by-side 2×2 grids with the motion name at the top. The left grid is labelled **Noise Absent** and the right grid **Noise Present**; within each group, perception increases down the rows and speed increases across the columns. The small **Layout 01** footnote appears at the bottom during the videos.

The default frame is **1920×720**, fitting the two video rows closely while keeping every source video fully visible and preserving its proportions. The Unity Game view switches to this resolution during playback and restores its previous resolution when the component is disabled.

The default sequence is **FLOCKING → DENSIFICATION → EXPANSION → RANDOM**. The title cards use the requested display names; **Motion Order** continues to use the original motion enum names. Random's recorded noise 20/60 displays as 0/40. The videos label perception 1.5 as **Perception (Small)** and perception 4 as **Perception (Large)**, with speed 1.5 labelled **Speed (Slow)** and speed 3 **Speed (Fast)**. Other perception and speed values retain their numeric labels. There is no “ended” suffix.

In the Inspector:

- **Folder Path** selects the source folder. It accepts an absolute path or a path relative to Assets.
- **Repeat** selects `_repeat_01.mp4`, `_repeat_02.mp4`, etc. Keep the four motion types exactly once in **Motion Order**, and reorder them as desired.
- **Title Duration** controls each fullscreen title card (default: 2 seconds).
- **Loop** repeats the complete sequence during playback. Disable it to hold the last comparison screen after all sections finish.
- **Record One Iteration** records all four title cards and all four comparison screens into one 30 fps MP4. **Output Folder Path** accepts an absolute path or a path relative to the project folder. Existing folders can be reused. **Output Width** and **Output Height** control the frame size (default: 1920×720); use even values.

The first eight source videos prepare before the first title and before capture starts, so the initial preparation message is not included in the recording. Each section destroys its completed players and releases their textures before the next section prepares during its title card, keeping only eight videos prepared at a time. Only the current section's eight videos play. Shorter clips hold their final frame until all eight finish and the next section is ready, then the next title appears. Completion checks both the end-of-clip signal and the decoded final frame, so a missing native playback callback cannot stall the sequence. At the end of the sequence, capture stops and the checkbox resets even when Loop is enabled. The Inspector's **Last Recording Path** shows the completed file.

The presentation enables background playback while its component is active, so switching to another app does not pause the sequence. Disabling it restores the previous background playback setting.

To record during playback, check **Record One Iteration** or use the component menu's action with that name; the whole sequence restarts from the first title. Changing the folder, repeat, or motion order also rebuilds the sequence. Interrupting a capture retains a file ending in `_partial.mp4`. The component menu also provides **Build / Reload Presentation**, **Pause Presentation**, and **Resume Presentation**. Pausing while recording includes the pause in the exported movie.

Recording uses the installed Unity Recorder package and requires Play mode in the Unity Editor. Each motion is flushed as a separate temporary recording to bound native encoder memory. At the end, FFmpeg joins the four sections into one MP4 without re-encoding and removes the temporary files. FFmpeg must be installed (available on this computer). If joining fails, the recorded sections are preserved in the output folder for recovery. This scene is separate from the existing 32-video FlockGrid scene.
