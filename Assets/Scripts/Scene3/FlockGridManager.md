# FlockGridManager

Open `Assets/Scenes/FlockGrid.unity`, or use **Tools → Flocking → Open 40-Recording Grid**. Select the `FlockGridManager` object, set **Max Speed** and **Perception Radius**, and press Play.

The default source folder contains the 20260908_013309 flocking batch. The manager selects zero-randomness MP4 recordings, lays out layouts 00–39 left to right in an 8-column, 5-row grid, and labels each cell. A Canvas is created automatically; no grid reference or individual video assignments are needed. The saved scene defaults to speed 3 and perception radius 4.

Changing speed or radius during Play mode rebuilds the grid. To retain a choice, set it outside Play mode and save the scene. For another recording batch, change **Folder Path**. Missing or duplicate layouts and mixed batches produce a visible error rather than a partial or ambiguous comparison.

All clips prepare before starting. Shorter clips hold their last frame until every clip finishes, then the group restarts after **Restart Pause**. Disable **Loop** to hold the final frames. Right-click the component header for **Pause Grid**, **Resume Grid**, **Restart Grid**, or **Build → Reload Grid**. Render Width/Height control the per-cell texture resolution; reload after changing them.

Enable **Record One Iteration** to save a 1920×1080, 30 fps MP4 of the whole grid in the project's **Grid Recordings** folder. Capture starts after all clips prepare and stops after the final clip ends, including when Loop is off. The checkbox resets after recording, so later loops are not recorded. Checking it during playback restarts the grid first. Filenames include speed, perception radius, and a timestamp. Unchecking, changing the condition, restarting manually, or leaving Play mode interrupts capture and retains a file with `_partial` in its name. Recording uses the installed Unity Recorder package and is available in the Unity Editor.

This plays the existing recorded videos for visual inspection. It does not rerun flocking or apply a clustering epsilon. Video decoders can drop frames under load, so use the recorded trajectories in the notebook for precise temporal measurements.

The component can also be added to another scene. Its optional **Grid Container** accepts an existing Canvas RectTransform. It owns and cleans up only the objects/textures it creates. Use the dedicated scene to avoid running other video-grid managers simultaneously.
