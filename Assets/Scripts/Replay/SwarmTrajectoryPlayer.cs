using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

#if UNITY_EDITOR
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
#endif

/// <summary>How the reference dispersion is drawn in the scene.</summary>
public enum ReferenceOutlineMode
{
    /// <summary>
    /// The reference's own trimmed hull at its plateau frame, in world space. The reference began
    /// from the identical spawn layout, so this is directly comparable: where the replayed hull
    /// covers it, the swarm has genuinely spread that far in that direction.
    /// </summary>
    RecordedHull,

    /// <summary>
    /// The same outline translated onto the replayed swarm's centroid, which removes the drift
    /// between the two runs and leaves a pure size and shape comparison.
    /// </summary>
    CentredOnSwarm,

    /// <summary>
    /// A circle of the same area as the reference plateau. An area gauge only.
    ///
    /// Do not read it as a shape to match. By the isoperimetric inequality a circle is the most
    /// compact shape enclosing a given area, so a hull of exactly equal area still extends beyond
    /// the ring in some directions and falls inside it in others. Measured on this project's own
    /// references at R 1.90, the hull reaches 1.10-1.22x the ring radius at its furthest vertex and
    /// 0.84-0.95x at its nearest, with over half the vertices outside. "Touching the ring" is
    /// therefore not the moment the areas match.
    /// </summary>
    EqualAreaCircle,
}

/// <summary>Replays recorded agent poses in the scene or in two matched comparison rows, restoring live state on exit.</summary>
[RequireComponent(typeof(UI))]
public class SwarmTrajectoryPlayer : MonoBehaviour
{
    // Found at runtime from the same GameObject, so there is nothing to assign in the Inspector.
    private UI uiController;
    private SwarmManager swarmManager;

    [Header("Source")]
    [Tooltip("Folder to list recordings from. Absolute, or relative to Assets, with or without a leading Assets/.")]
    public string recordingsFolder = "SimulationRecordings/Trajectories";

    [Header("Comparison screen")]
    public bool enterReplayOnStart = false;
    [Tooltip("Replay the selected run in the top row and its matching zero-randomness recording below, at the same time and world scale.")]
    public bool compareReferenceReplay = false;
    [Tooltip("Optional recording name to open on entry, without .json.")]
    public string initialRecording = "";

    [Header("Toggle")]
    [Tooltip("Enters and leaves replay mode.")]
    public Key toggleKey = Key.R;

    [Header("Panel")]
    public bool showPanel = true;
    [Tooltip("Hide or show the replay panel without changing playback.")]
    public Key panelToggleKey = Key.X;

    [Header("Replay Video Recording")]
    public string videoSaveFolder = "SimulationRecordings/ReplayVideos";
    [Range(1, 120)] public int videoFrameRate = 60;
    [Min(16)] public int videoWidth = 1280;
    [Min(16)] public int videoHeight = 720;
    public bool hidePanelInVideo = true;
    public Key recordVideoKey = Key.F9;

    [Header("Playback")]
    public bool playOnLoad = true;
    public bool loop = true;
    [Range(0.1f, 4f)] public float speed = 1f;
    [Tooltip("Interpolate between captured frames. Off shows exactly the captured poses.")]
    public bool interpolate = false;
    public bool applyRotation = true;

    [Header("Scrubbing")]
    public bool scrollWheelScrubs = true;
    [Tooltip("Frames moved per wheel notch, and per arrow press with shift held.")]
    public int framesPerScrollNotch = 5;
    public bool keyboardScrubs = true;

    [Header("Scene State")]
    [Tooltip("Activate the goal area and disable the walls the recording had, restoring your live selection on exit.")]
    public bool matchRecordedSceneState = true;

    [Header("Gizmos")]
    [Tooltip("Draw each replay agent's perception radius, using the radius recorded with the run.")]
    public bool showPerceptionRadius = true;
    [Tooltip("Draw the trimmed convex hull of the replayed swarm, recomputed as you scrub.")]
    public bool showHull = true;
    public Color hullColor = new Color(0.15f, 0.75f, 1f, 1f);
    public Color baselineHullColor = new Color(0.15f, 0.75f, 1f, 0.3f);
    public float hullLineWidth = 0.05f;

    [Header("Reference dispersion (the same layout, without randomness)")]
    [Tooltip("When replaying a run that had random movement, look up the zero-randomness run recorded from the SAME spawn layout at the same perception radius and max speed, and show the hull area it settled at. Gives the replay a target to be judged against.")]
    public bool showReferenceHull = true;

    // References are always taken from the folder the loaded recording sits in. There is
    // deliberately no setting for this: a batch records its zero-randomness runs alongside its
    // randomised ones, so the clip's own folder is the only correct place to look. A configurable
    // path only created a second thing to get wrong, and pointed at a stale folder by default.

    [Tooltip("Seconds the replayed hull must stay at or past the reference area before it counts as reached, matching the dwell the recorder applies.")]
    public float referenceDwellTime = 0.5f;

    [Tooltip("RecordedHull: the reference's actual hull at its plateau, where it actually was. CentredOnSwarm: that same outline slid onto the replayed swarm, for size and shape only. EqualAreaCircle: a ring of the same area — area only, and NOT a shape the swarm should match.")]
    public ReferenceOutlineMode referenceOutline_Mode = ReferenceOutlineMode.RecordedHull;

    [Tooltip("Rebuild the index instead of reusing reference_hull_index.json in the folder. Turn on after re-recording references.")]
    public bool rebuildReferenceIndex = false;

    public Color referenceHullColor = new Color(1f, 0.78f, 0.25f, 0.85f);
    [Range(0f, 0.5f)] public float hullTrimFraction = 0.1f;

    [Header("Trails")]
    public bool showTrails = false;
    public int trailFrames = 60;
    public float trailWidth = 0.03f;
    public Color trailColor = new Color(0.2f, 0.7f, 1f, 0.55f);

    public bool IsReplaying { get; private set; }
    public int CurrentFrame => currentFrame;
    public SwarmTrajectory Trajectory => trajectory;

    private SwarmTrajectory trajectory;
    private SwarmTrajectory referenceTrajectory;
    private SwarmReplayComparisonView comparisonView;
    private float PlaybackDuration => compareReferenceReplay && referenceTrajectory != null
        ? Mathf.Max(trajectory.Duration, referenceTrajectory.Duration) : trajectory.Duration;
    private readonly List<Transform> spawned = new List<Transform>();
    private readonly List<LineRenderer> trails = new List<LineRenderer>();
    private Transform container;

    private LineRenderer hullLine;
    private LineRenderer baselineHullLine;
    private readonly List<Vector2> positionBuffer = new List<Vector2>();
    private readonly List<Vector2> hullBuffer = new List<Vector2>();
    private readonly List<Vector2> baselineHull = new List<Vector2>();
    private float currentHullArea;
    private float baselineHullArea;

    // Reference dispersion for the run on screen: the area its no-randomness twin settled at.
    private ReferenceHullIndex referenceIndex;
    private string indexedFolder;      // folder the current index was built from
    private string loadedPath;         // file the current trajectory came from
    private ReferenceHullIndex.Entry referenceEntry;
    private LineRenderer referenceHullLine;
    private readonly List<Vector2> referenceOutline = new List<Vector2>();

    // The reference's hull at its plateau, in the coordinates it was recorded in. Read once when a
    // recording is loaded; the outline is small even though the file it came from is not.
    private readonly List<Vector2> referenceHullAtPlateau = new List<Vector2>();
    private Vector2 referenceHullCentre;
    private string referenceStatus = "";
    private int referenceCrossFrame = -1;
    private float referenceCrossTime;

    private bool isPlaying;
    private float playhead;
    private int currentFrame;

    private string[] availableFiles = new string[0];
    private int selectedFileIndex = -1;
    private Vector2 fileScroll;
    private Vector2 panelScroll;
    private string status = "";

    // Live state captured on entry so it can be put back on exit.
    private bool savedShowUI;
    private bool savedManagerEnabled;
    private bool savedDensityMonitorEnabled;
    private readonly List<GameObject> hiddenLiveAgents = new List<GameObject>();

    public bool IsRecordingVideo { get; private set; }
    public string LastVideoPath { get; private set; }
    private bool PanelVisible => showPanel && !(IsRecordingVideo && hidePanelInVideo);
    private string videoStatus = "";

#if UNITY_EDITOR
    private RecorderController videoController;
    private RecorderControllerSettings videoControllerSettings;
    private MovieRecorderSettings videoRecorderSettings;
    private Coroutine videoRoutine;
    private ReplayVideoSettings videoExport;
    private string videoOutputStem;
    private int videoFramesRendered;
    private float videoLastFrameTime;
    private bool videoStarted;
    private bool videoCompleted;
    private float beforeVideoPlayhead;
    private bool beforeVideoPlaying;
#endif

    public bool StartReplayVideoRecording()
    {
#if UNITY_EDITOR
        if (IsRecordingVideo) return false;
        if (!Application.isPlaying || !isActiveAndEnabled || !IsReplaying || trajectory == null || trajectory.FrameCount == 0)
        {
            videoStatus = "Load a replay in Play mode before recording.";
            return false;
        }
        if (videoFrameRate < 1 || videoFrameRate > 120 || videoWidth < 16 || videoHeight < 16 ||
            videoWidth % 2 != 0 || videoHeight % 2 != 0 || speed <= 0f || float.IsNaN(speed) || float.IsInfinity(speed))
        {
            videoStatus = "Use 1–120 fps, even image dimensions of at least 16 pixels, and a positive playback speed.";
            return false;
        }

        beforeVideoPlayhead = playhead;
        beforeVideoPlaying = isPlaying;
        try
        {
            string folder = (videoSaveFolder ?? "").Trim();
            if (string.IsNullOrEmpty(folder)) throw new System.ArgumentException("Choose a video save folder.");
            if (!Path.IsPathRooted(folder))
            {
                if (folder.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase)) folder = folder.Substring(7);
                folder = Path.Combine(Application.dataPath, folder);
            }
            Directory.CreateDirectory(folder);
            string sourceName = string.IsNullOrEmpty(loadedPath) ? "replay" : Path.GetFileNameWithoutExtension(loadedPath);
            string stem = Path.Combine(folder, $"{sourceName}_replay_{System.DateTime.Now:yyyyMMdd_HHmmss_fff}");
            videoOutputStem = stem;
            int suffix = 1;
            while (File.Exists(videoOutputStem + ".mp4") || File.Exists(videoOutputStem + "_settings.json"))
                videoOutputStem = stem + "_" + suffix++;

            ResolveReferenceDispersion();
            ComputeBaselineHull();
            videoExport = new ReplayVideoSettings
            {
                sourceTrajectory = loadedPath,
                referenceTrajectory = compareReferenceReplay && referenceEntry != null ? referenceEntry.sourcePath : null,
                recordedAtUtc = System.DateTime.UtcNow.ToString("o"),
                frameRate = videoFrameRate, width = videoWidth, height = videoHeight,
                playbackSpeed = speed, replayDuration = PlaybackDuration,
                interpolate = interpolate, applyRotation = applyRotation,
                showPerceptionRadius = showPerceptionRadius, showHull = showHull,
                showReferenceHull = showReferenceHull, referenceOutlineMode = referenceOutline_Mode.ToString(),
                compareReferenceReplay = compareReferenceReplay, showTrails = showTrails,
                trailFrames = trailFrames, trailWidth = trailWidth, trailColor = trailColor,
                hullTrimFraction = hullTrimFraction, hullLineWidth = hullLineWidth,
                hullColor = hullColor, baselineHullColor = baselineHullColor, referenceHullColor = referenceHullColor,
                panelIncluded = showPanel && !hidePanelInVideo
            };
            videoFramesRendered = 0;
            videoLastFrameTime = 0f;
            videoStarted = false;
            videoCompleted = false;
            IsRecordingVideo = true;
            isPlaying = false;
            playhead = 0f;
            BuildAgents();
            ApplyPlayhead();

            videoControllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            videoControllerSettings.ExitPlayMode = false;
            videoControllerSettings.FrameRate = videoFrameRate;
            videoControllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
            videoControllerSettings.SetRecordModeToManual();
            videoRecorderSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            videoRecorderSettings.name = "Replay Video";
            videoRecorderSettings.Enabled = true;
            videoRecorderSettings.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            videoRecorderSettings.OutputFile = videoOutputStem;
            videoRecorderSettings.CaptureAudio = false;
            videoRecorderSettings.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = videoWidth, OutputHeight = videoHeight
            };
            videoControllerSettings.AddRecorderSettings(videoRecorderSettings);
            videoController = new RecorderController(videoControllerSettings);
            videoController.PrepareRecording();
            videoStatus = $"Recording replay… {recordVideoKey} stops and saves.";
            videoRoutine = StartCoroutine(RecordReplayVideo());
            return true;
        }
        catch (System.Exception error)
        {
            FinishReplayVideo();
            videoStatus = $"Could not record: {error.Message}";
            Debug.LogError($"[Replay] {videoStatus}");
            return false;
        }
#else
        videoStatus = "Replay video recording is available in the Unity Editor.";
        return false;
#endif
    }

    public void StopReplayVideoRecording()
    {
#if UNITY_EDITOR
        if (!IsRecordingVideo) return;
        Coroutine routine = videoRoutine;
        videoRoutine = null;
        if (routine != null) StopCoroutine(routine);
        if (IsRecordingVideo) FinishReplayVideo();
#endif
    }

#if UNITY_EDITOR
    private IEnumerator RecordReplayVideo()
    {
        try
        {
            // Prepare the recorder and opening pose for a whole frame before the first capture.
            yield return null;
            if (!videoController.StartRecording())
            {
                videoStatus = "The video encoder could not start. Check the Unity Console.";
                yield break;
            }
            videoStarted = true;
            // Recorder holds its first frame for a resolution change; keep the opening pose during that wait.
            yield return new WaitForEndOfFrame();
            int frames = Mathf.CeilToInt(videoExport.replayDuration * videoExport.frameRate / videoExport.playbackSpeed) + 1;
            for (int frame = 0; frame < frames; frame++)
            {
                if (!videoController.IsRecording()) throw new System.InvalidOperationException("The video encoder stopped before replay ended.");
                // Step after the previous render so perception rings update before the next render.
                playhead = Mathf.Min(videoExport.replayDuration, frame * videoExport.playbackSpeed / videoExport.frameRate);
                ApplyPlayhead();
                yield return new WaitForEndOfFrame();
                videoFramesRendered = frame + 1;
                videoLastFrameTime = playhead;
            }
            // Recorder captures at end of frame too; let it capture the final pose before stopping.
            yield return null;
            videoCompleted = true;
        }
        finally
        {
            FinishReplayVideo();
        }
    }

    private void FinishReplayVideo()
    {
        if (!IsRecordingVideo) return;
        bool save = videoStarted && videoFramesRendered > 0;
        try
        {
            videoController?.StopRecording();
            if (save)
            {
                string path = videoOutputStem + ".mp4";
                if (!File.Exists(path) || new FileInfo(path).Length == 0)
                    throw new IOException("The encoder did not create a video file.");
                videoExport.completed = videoCompleted;
                videoExport.frames = videoFramesRendered;
                videoExport.replayEndTime = videoLastFrameTime;
                playhead = videoLastFrameTime;
                ApplyPlayhead();
                File.WriteAllText(videoOutputStem + "_settings.json", JsonUtility.ToJson(videoExport, true));
                LastVideoPath = path;
                videoStatus = $"Saved{(videoCompleted ? "" : " partial replay")}: {Path.GetFileName(path)}";
                Debug.Log($"[Replay] {videoStatus}\n{path}");
            }
            else
            {
                if (!string.IsNullOrEmpty(videoOutputStem) && File.Exists(videoOutputStem + ".mp4"))
                    File.Delete(videoOutputStem + ".mp4");
                if (videoStarted) videoStatus = "Recording canceled before the first frame.";
                playhead = beforeVideoPlayhead;
                isPlaying = beforeVideoPlaying;
            }
        }
        catch (System.Exception error)
        {
            videoStatus = $"Recording could not be saved: {error.Message}";
            Debug.LogError($"[Replay] {videoStatus}");
        }
        finally
        {
            IsRecordingVideo = false;
            videoRoutine = null;
            videoController = null;
            if (videoControllerSettings != null) Destroy(videoControllerSettings);
            if (videoRecorderSettings != null) Destroy(videoRecorderSettings);
            videoControllerSettings = null;
            videoRecorderSettings = null;
        }
    }

    /// <summary>Stores the source and selected display settings alongside an exported replay video.</summary>
    [System.Serializable]
    private sealed class ReplayVideoSettings
    {
        public string sourceTrajectory;
        public string referenceTrajectory;
        public string recordedAtUtc;
        public int frameRate, width, height, frames;
        public float playbackSpeed, replayDuration, replayEndTime;
        public bool completed, panelIncluded, interpolate, applyRotation;
        public bool showPerceptionRadius, showHull, showReferenceHull, showTrails, compareReferenceReplay;
        public string referenceOutlineMode;
        public int trailFrames;
        public float trailWidth, hullTrimFraction, hullLineWidth;
        public Color trailColor, hullColor, baselineHullColor, referenceHullColor;
    }
#endif

    private void DrawVideoRecording()
    {
        Section("Record replay video");
#if UNITY_EDITOR
        if (IsRecordingVideo)
        {
            if (GUILayout.Button($"Stop and save ({recordVideoKey})", buttonStyle)) StopReplayVideoRecording();
        }
        else
        {
            hidePanelInVideo = GUILayout.Toggle(hidePanelInVideo, " Hide panel in video", toggleStyle);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && trajectory != null;
            if (GUILayout.Button($"Record replay video ({recordVideoKey})", buttonStyle)) StartReplayVideoRecording();
            GUI.enabled = enabled;
            GUILayout.Label("Records one pass from the start using the selected visual options and speed.", mutedStyle);
            GUILayout.Label($"{videoWidth} × {videoHeight} · {videoFrameRate} fps · folder: {videoSaveFolder}", mutedStyle);
        }
#else
        GUILayout.Label("Video recording is available in the Unity Editor.", mutedStyle);
#endif
        if (!string.IsNullOrEmpty(videoStatus)) GUILayout.Label(videoStatus, statusStyle);
        Separator();
    }

    private readonly List<ReplaySceneState> savedSceneState = new List<ReplaySceneState>();
    private Camera replayCamera;
    private TrajectoryCamera savedCamera;

    private void CaptureSceneState()
    {
        savedSceneState.Clear();
        foreach (GameObject root in gameObject.scene.GetRootGameObjects())
            foreach (Transform target in root.GetComponentsInChildren<Transform>(true))
                savedSceneState.Add(new ReplaySceneState(target));
        replayCamera = Camera.main;
        if (replayCamera != null)
        {
            savedCamera = new TrajectoryCamera
            {
                position = replayCamera.transform.position,
                rotation = replayCamera.transform.rotation,
                orthographic = replayCamera.orthographic,
                orthographicSize = replayCamera.orthographicSize,
                fieldOfView = replayCamera.fieldOfView,
                backgroundColor = replayCamera.backgroundColor
            };
        }
    }

    private void RestoreSceneState()
    {
        foreach (ReplaySceneState state in savedSceneState) state.Restore();
        ApplyCamera(savedCamera);
    }

    private void ApplyCamera(TrajectoryCamera framing)
    {
        if (framing == null || replayCamera == null) return;
        replayCamera.transform.SetPositionAndRotation(framing.position, framing.rotation);
        replayCamera.orthographic = framing.orthographic;
        replayCamera.orthographicSize = framing.orthographicSize;
        replayCamera.fieldOfView = framing.fieldOfView;
        replayCamera.backgroundColor = framing.backgroundColor;
    }

    private void ApplyGeometry(List<TrajectoryGeometry> geometry)
    {
        var candidates = new HashSet<Transform>();
        if (uiController.walls != null)
            foreach (Transform wall in uiController.walls) if (wall != null) candidates.Add(wall);
        if (uiController.obstacles != null)
            foreach (Transform obstacle in uiController.obstacles) if (obstacle != null) candidates.Add(obstacle);
        if (uiController.spawnAreas != null)
            foreach (Transform area in uiController.spawnAreas) if (area != null) candidates.Add(area);
        Transform[] areas = { uiController.circleSpawnArea, uiController.densificationSpawnArea,
            uiController.flockingSpawnArea, uiController.flockingGoalArea, uiController.densificationGoalArea,
            uiController.randomGoalArea, uiController.dispersionGoalArea };
        foreach (Transform area in areas) if (area != null) candidates.Add(area);
        foreach (ReplaySceneState state in savedSceneState)
        {
            Transform target = state.Target;
            if (target == null || target.gameObject == uiController.agentPrefab) continue;
            if (target.name.Contains("Obstacle") && target.GetComponent<Collider2D>() != null)
                candidates.Add(target);
        }
        foreach (Transform candidate in candidates) candidate.gameObject.SetActive(false);
        foreach (TrajectoryGeometry shape in geometry)
        {
            if (shape == null) continue;
            ReplaySceneState state = savedSceneState.Find(item => item.Target != null && item.Target.name == shape.name);
            if (state == null)
            {
                Debug.LogWarning($"[Replay] Recorded geometry '{shape.name}' is missing from this scene.");
                continue;
            }
            Transform target = state.Target;
            target.gameObject.SetActive(true);
            if (shape.transformRecorded)
            {
                SetWorldScale(target, shape.scale);
                target.SetPositionAndRotation(shape.position, shape.orientation);
                continue;
            }
            // Legacy files carry collider geometry only; retain the original scene's sprite and material.
            target.rotation = Quaternion.AngleAxis(shape.rotation, Vector3.forward);
            BoxCollider2D box = target.GetComponent<BoxCollider2D>();
            CircleCollider2D circle = target.GetComponent<CircleCollider2D>();
            Vector2 offset = Vector2.zero;
            Vector3 scale = target.lossyScale;
            if (shape.shape == TrajectoryGeometry.ShapeBox && box != null)
            {
                scale.x = Mathf.Sign(scale.x) * shape.width / Mathf.Max(0.0001f, box.size.x);
                scale.y = Mathf.Sign(scale.y) * shape.height / Mathf.Max(0.0001f, box.size.y);
                offset = box.offset;
            }
            else if (shape.shape == TrajectoryGeometry.ShapeCircle && circle != null)
            {
                float factor = shape.radius / Mathf.Max(0.0001f, circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
                scale.x *= factor;
                scale.y *= factor;
                offset = circle.offset;
            }
            SetWorldScale(target, scale);
            Vector3 centreOffset = target.TransformVector(offset);
            target.position = new Vector3(shape.x - centreOffset.x, shape.y - centreOffset.y, target.position.z);
        }
    }

    private static void SetWorldScale(Transform target, Vector3 scale)
    {
        Vector3 parentScale = target.parent != null ? target.parent.lossyScale : Vector3.one;
        target.localScale = new Vector3(scale.x / parentScale.x, scale.y / parentScale.y, scale.z / parentScale.z);
    }

    /// <summary>Retains the original transform and activation of a scene object for replay exit or clip changes.</summary>
    private sealed class ReplaySceneState
    {
        public Transform Target { get; }
        private readonly Vector3 position;
        private readonly Quaternion rotation;
        private readonly Vector3 scale;
        private readonly bool active;

        public ReplaySceneState(Transform target)
        {
            Target = target;
            position = target.localPosition;
            rotation = target.localRotation;
            scale = target.localScale;
            active = target.gameObject.activeSelf;
        }

        public void Restore()
        {
            if (Target == null) return;
            Target.localPosition = position;
            Target.localRotation = rotation;
            Target.localScale = scale;
            Target.gameObject.SetActive(active);
        }
    }

    void Awake()
    {
        ResolveWiring();
    }

    void Start()
    {
        if (enterReplayOnStart) EnterReplay();
    }

    private void ResolveWiring()
    {
        if (uiController == null) uiController = GetComponent<UI>();
        if (swarmManager == null && uiController != null) swarmManager = uiController.swarmManager;
    }

    void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            if (IsReplaying) ExitReplay();
            else EnterReplay();
        }

        if (IsReplaying && Keyboard.current != null && Keyboard.current[panelToggleKey].wasPressedThisFrame)
            showPanel = !showPanel;

        if (IsReplaying && Keyboard.current != null && Keyboard.current[recordVideoKey].wasPressedThisFrame)
        {
            if (IsRecordingVideo) StopReplayVideoRecording();
            else StartReplayVideoRecording();
        }

        if (IsRecordingVideo || !IsReplaying || trajectory == null || trajectory.FrameCount == 0) return;

        HandleScrubInput();

        if (isPlaying)
        {
            playhead += Time.unscaledDeltaTime * speed;

            if (playhead >= PlaybackDuration)
            {
                if (loop && PlaybackDuration > 0f) playhead %= PlaybackDuration;
                else { playhead = PlaybackDuration; isPlaying = false; }
            }
        }

        ApplyPlayhead();
    }

    // ------------------------------------------------------------- mode switching

    public void EnterReplay() => EnterReplay(true);

    private void EnterReplay(bool loadInitial)
    {
        if (IsReplaying) return;

        ResolveWiring();
        if (uiController == null)
        {
            Debug.LogError("[Replay] No UI component found; cannot enter replay.");
            return;
        }

        // Stash live state, then quiet everything that would fight the playback.
        savedShowUI = uiController.showUI;
        savedManagerEnabled = swarmManager != null && swarmManager.enabled;

        CaptureSceneState();
        if (uiController.enabled && !enterReplayOnStart) uiController.SetMotion(false);
        uiController.showUI = false;

        if (swarmManager != null)
        {
            swarmManager.enabled = false;

            if (swarmManager.densityMonitor != null)
            {
                // Otherwise it keeps sampling the hidden live agents and draws a contradictory outline.
                savedDensityMonitorEnabled = swarmManager.densityMonitor.enabled;
                swarmManager.densityMonitor.enabled = false;
            }
        }

        HideLiveAgents();

        IsReplaying = true;
        RefreshFileList();

        if (loadInitial && availableFiles.Length > 0 && trajectory == null)
        {
            int initialIndex = System.Array.IndexOf(availableFiles, initialRecording);
            if (!string.IsNullOrEmpty(initialRecording) && initialIndex < 0)
                status = $"Recording not found: {initialRecording}";
            else LoadByIndex(initialIndex >= 0 ? initialIndex : 0);
        }
        else if (loadInitial && trajectory != null) { BuildAgents(); ApplyRecordedSceneState(); ApplyPlayhead(); }

        Debug.Log("[Replay] Entered replay mode.");
    }

    public void ExitReplay()
    {
        if (!IsReplaying) return;

        StopReplayVideoRecording();
        DestroyReplayAgents();
        RestoreLiveAgents();

        if (uiController != null)
        {
            uiController.showUI = savedShowUI;

            if (matchRecordedSceneState)
            {
                RestoreSceneState();
            }
        }

        if (swarmManager != null)
        {
            if (swarmManager.densityMonitor != null) swarmManager.densityMonitor.enabled = savedDensityMonitorEnabled;
            if (uiController.enabled && !enterReplayOnStart) uiController.SetMotion(savedManagerEnabled);
            else swarmManager.enabled = savedManagerEnabled;
        }

        IsReplaying = false;
        isPlaying = false;
        Debug.Log("[Replay] Left replay mode.");
    }

    private void HideLiveAgents()
    {
        hiddenLiveAgents.Clear();
        if (swarmManager == null || swarmManager.agents == null) return;

        foreach (GameObject agent in swarmManager.agents)
        {
            if (agent == null || !agent.activeSelf) continue;
            agent.SetActive(false);
            hiddenLiveAgents.Add(agent);
        }
    }

    private void RestoreLiveAgents()
    {
        foreach (GameObject agent in hiddenLiveAgents)
        {
            if (agent != null) agent.SetActive(true);
        }
        hiddenLiveAgents.Clear();
    }

    /// <summary>
    /// Puts the scene into the state the recording was made in: the goal area of the recorded
    /// motion type shown, and the walls it had disabled switched off.
    /// </summary>
    private void ApplyRecordedSceneState()
    {
        if (!matchRecordedSceneState || trajectory == null || uiController == null) return;

        TrajectoryHeader h = trajectory.header;

        RestoreSceneState();
        HideLiveAgents();
        if (h.cameraRecorded) ApplyCamera(h.camera);
        if (h.geometry != null && h.geometry.Count > 0)
        {
            ApplyGeometry(h.geometry);
            return;
        }
        uiController.EnableAllWalls();
        if (!string.IsNullOrEmpty(h.wallsDisabled))
        {
            List<string> names = new List<string>(h.wallsDisabled.Split(','));
            for (int i = 0; i < names.Count; i++) names[i] = names[i].Trim();
            uiController.ApplyWallsDisabledByName(names);
        }

        if (!string.IsNullOrEmpty(h.swarmType) &&
            System.Enum.TryParse(h.swarmType, out SwarmType recordedType))
        {
            uiController.ShowGoalAreaForType(recordedType);
        }
    }

    // ------------------------------------------------------------- loading

    public void RefreshFileList()
    {
        string folder = AbsoluteFolder();
        if (!Directory.Exists(folder))
        {
            availableFiles = new string[0];
            status = $"Folder not found:\n{folder}";
            return;
        }

        List<string> names = new List<string>();
        int skippedConfigs = 0;

        foreach (string p in Directory.GetFiles(folder, "*.json"))
        {
            string name = Path.GetFileNameWithoutExtension(p);
            if (!IsTrajectoryFile(name)) { skippedConfigs++; continue; }
            names.Add(name);
        }

        names.Sort();
        availableFiles = names.ToArray();

        if (availableFiles.Length > 0) status = $"{availableFiles.Length} recordings found";
        else status = skippedConfigs > 0
            ? "No trajectory files here, only config.\nThis batch predates trajectory capture."
            : $"No .json recordings in\n{folder}";
    }

    public void LoadByIndex(int index)
    {
        if (index < 0 || index >= availableFiles.Length) return;
        selectedFileIndex = index;
        LoadByName(availableFiles[index]);
    }

    public void LoadByName(string fileNameWithoutExtension)
    {
        LoadFromPath(Path.Combine(AbsoluteFolder(), fileNameWithoutExtension + ".json"));
    }

    /// <summary>Loads any trajectory by absolute path, e.g. the one SimRecorder just wrote.</summary>
    public void LoadFromPath(string path)
    {
        if (IsRecordingVideo) return;
        SwarmTrajectory loaded = SwarmTrajectory.Load(path);
        if (loaded == null) { status = $"Failed to load {Path.GetFileName(path)}"; return; }

        if (loaded.FrameCount == 0 || loaded.AgentCount == 0)
        {
            status = $"{Path.GetFileNameWithoutExtension(path)} is not a trajectory file";
            return;
        }

        if (!IsReplaying) EnterReplay(false);
        trajectory = loaded;
        loadedPath = path;
        BuildAgents();
        ApplyRecordedSceneState();
        ComputeBaselineHull();
        ResolveReferenceDispersion();

        playhead = 0f;
        currentFrame = 0;
        isPlaying = playOnLoad;
        ApplyPlayhead();

        status = $"{trajectory.header.fileName}: {trajectory.FrameCount} frames, " +
                 $"{trajectory.Duration:F2}s, {trajectory.AgentCount} agents";
        Debug.Log($"[Replay] Loaded {status}");
    }

    public string AbsoluteFolder()
    {
        string folder = (recordingsFolder ?? "").Trim().Replace('\\', '/').TrimEnd('/');
        if (Path.IsPathRooted(folder)) return folder;
        if (folder.Equals("Assets", System.StringComparison.OrdinalIgnoreCase)) return Application.dataPath;
        if (folder.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase)) folder = folder.Substring(7);
        return Path.Combine(Application.dataPath, folder);
    }

    private static bool IsTrajectoryFile(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (name.Equals("batch_config", System.StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Equals("reference_hull_index", System.StringComparison.OrdinalIgnoreCase)) return false;
        return !name.EndsWith("_config", System.StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------- agents

    private void BuildAgents()
    {
        DestroyReplayAgents();

        if (!IsReplaying || trajectory == null || compareReferenceReplay) return;

        GameObject prefab = uiController != null ? uiController.agentPrefab : null;
        if (prefab == null)
        {
            status = "UI has no agent prefab assigned";
            Debug.LogError("[Replay] UI.agentPrefab is not set; nothing to spawn.");
            return;
        }

        if (container == null)
        {
            container = new GameObject("ReplayAgents").transform;
            container.position = Vector3.zero;
            container.rotation = Quaternion.identity;
            container.localScale = Vector3.one;
            container.gameObject.SetActive(false);
        }

        for (int i = 0; i < trajectory.AgentCount; i++)
        {
            GameObject agent = Instantiate(prefab, container);
            agent.name = "Replay_" + trajectory.GetAgentName(i);
            Strip(agent);

            if (showPerceptionRadius)
            {
                PerceptionVisualizer pv = agent.AddComponent<PerceptionVisualizer>();
                pv.radius = trajectory.header.perceptionRadius;
                pv.segments = 48;
                pv.lineWidth = 0.02f;
            }

            spawned.Add(agent.transform);
            trails.Add(showTrails ? CreateTrail(agent) : null);
        }

        ApplyPlayhead();
        container.gameObject.SetActive(true);
        EnsureHullRenderers();
    }

    private void DestroyReplayAgents()
    {
        if (container != null)
        {
            container.gameObject.SetActive(false);
            Destroy(container.gameObject);
            container = null;
        }
        spawned.Clear();
        trails.Clear();

        if (hullLine != null) Destroy(hullLine.gameObject);
        if (baselineHullLine != null) Destroy(baselineHullLine.gameObject);
        if (referenceHullLine != null) Destroy(referenceHullLine.gameObject);
        hullLine = null;
        baselineHullLine = null;
        referenceHullLine = null;
    }

    /// <summary>Removes anything that would move or collide, so the replay is purely the data.</summary>
    private void Strip(GameObject agent)
    {
        // Quiet the entire hierarchy before activating the clone; Destroy is deferred by Unity.
        foreach (MonoBehaviour script in agent.GetComponentsInChildren<MonoBehaviour>(true))
        {
            script.enabled = false;
            Destroy(script);
        }
        foreach (Rigidbody2D body in agent.GetComponentsInChildren<Rigidbody2D>(true))
        {
            body.simulated = false;
            Destroy(body);
        }
        foreach (Collider2D collider in agent.GetComponentsInChildren<Collider2D>(true))
        {
            collider.enabled = false;
            Destroy(collider);
        }
        foreach (LineRenderer line in agent.GetComponentsInChildren<LineRenderer>(true))
            line.enabled = false;
    }

    private LineRenderer CreateTrail(GameObject agent)
    {
        GameObject trailObj = new GameObject("Trail");
        trailObj.transform.SetParent(agent.transform, false);

        LineRenderer line = trailObj.AddComponent<LineRenderer>();
        ConfigureLine(line, trailColor, trailWidth, false);
        line.startColor = new Color(trailColor.r, trailColor.g, trailColor.b, 0f);
        return line;
    }

    private void EnsureHullRenderers()
    {
        if (hullLine == null)
        {
            GameObject go = new GameObject("ReplayHull");
            go.transform.SetParent(transform, false);
            hullLine = go.AddComponent<LineRenderer>();
            ConfigureLine(hullLine, hullColor, hullLineWidth, true);
        }

        if (baselineHullLine == null)
        {
            GameObject go = new GameObject("ReplayHullBaseline");
            go.transform.SetParent(transform, false);
            baselineHullLine = go.AddComponent<LineRenderer>();
            ConfigureLine(baselineHullLine, baselineHullColor, hullLineWidth, true);
        }

        if (referenceHullLine == null)
        {
            GameObject go = new GameObject("ReplayHullReference");
            go.transform.SetParent(transform, false);
            referenceHullLine = go.AddComponent<LineRenderer>();
            ConfigureLine(referenceHullLine, referenceHullColor, hullLineWidth, true);
        }
    }

    private void ConfigureLine(LineRenderer line, Color color, float width, bool loopLine)
    {
        line.useWorldSpace = true;
        line.loop = loopLine;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.widthMultiplier = width;
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.positionCount = 0;
        line.startColor = color;
        line.endColor = color;
    }

    // ------------------------------------------------------------- playback

    private void HandleScrubInput()
    {
        if (scrollWheelScrubs && Mouse.current != null)
        {
            float wheel = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(wheel) > 0.01f)
            {
                StepFrames((wheel > 0f ? 1 : -1) * Mathf.Max(1, framesPerScrollNotch));
            }
        }

        if (!keyboardScrubs || Keyboard.current == null) return;

        int step = Keyboard.current.shiftKey.isPressed ? Mathf.Max(1, framesPerScrollNotch) : 1;

        if (Keyboard.current.leftArrowKey.isPressed) StepFrames(-step);
        if (Keyboard.current.rightArrowKey.isPressed) StepFrames(step);
        if (Keyboard.current.spaceKey.wasPressedThisFrame) TogglePlay();
        if (Keyboard.current.homeKey.wasPressedThisFrame) SeekToFrame(0);
        if (Keyboard.current.endKey.wasPressedThisFrame) SeekToTime(PlaybackDuration);
    }

    private void ApplyPlayhead()
    {
        if (trajectory == null || trajectory.FrameCount == 0) return;

        currentFrame = trajectory.FrameIndexAtTime(playhead);

        if (compareReferenceReplay)
        {
            currentHullArea = AreaAtFrame(currentFrame);
            return;
        }
        if (spawned.Count == 0) return;

        int nextFrame = Mathf.Min(currentFrame + 1, trajectory.FrameCount - 1);
        float blend = 0f;

        if (interpolate && nextFrame != currentFrame)
        {
            float span = trajectory.frames[nextFrame].t - trajectory.frames[currentFrame].t;
            if (span > 0.0001f) blend = Mathf.Clamp01((playhead - trajectory.frames[currentFrame].t) / span);
        }

        positionBuffer.Clear();

        int count = Mathf.Min(spawned.Count, trajectory.AgentCount);
        for (int i = 0; i < count; i++)
        {
            Transform t = spawned[i];
            if (t == null) continue;

            Vector2 position = trajectory.GetPosition(currentFrame, i);
            float rotation = trajectory.GetRotation(currentFrame, i);

            if (blend > 0f)
            {
                position = Vector2.Lerp(position, trajectory.GetPosition(nextFrame, i), blend);
                rotation = Mathf.LerpAngle(rotation, trajectory.GetRotation(nextFrame, i), blend);
            }

            float z = t.position.z;
            bool active = true;
            if (trajectory.header.agentStatesRecorded)
            {
                int state = i * SwarmTrajectory.StateStride;
                List<float> values = trajectory.frames[currentFrame].s;
                z = values[state];
                t.localScale = new Vector3(values[state + 1], values[state + 2], values[state + 3]);
                active = values[state + 4] > 0f;
            }
            t.gameObject.SetActive(active);
            t.position = new Vector3(position.x, position.y, z);
            if (applyRotation) t.rotation = Quaternion.AngleAxis(rotation, Vector3.forward);

            if (active) positionBuffer.Add(position);
            UpdateTrail(i);
        }

        UpdateHull();
    }

    private void UpdateTrail(int agentIndex)
    {
        if (agentIndex >= trails.Count) return;
        LineRenderer line = trails[agentIndex];
        if (line == null) return;

        if (!showTrails) { line.positionCount = 0; return; }

        int first = Mathf.Max(0, currentFrame - Mathf.Max(1, trailFrames));
        int points = currentFrame - first + 1;
        line.positionCount = points;

        for (int f = 0; f < points; f++)
        {
            Vector2 p = trajectory.GetPosition(first + f, agentIndex);
            line.SetPosition(f, new Vector3(p.x, p.y, 0.01f));
        }
    }

    /// <summary>Recomputes the trimmed hull of the replayed swarm for the current frame.</summary>
    private void UpdateHull()
    {
        currentHullArea = SwarmDensityMetrics.TrimmedHull(positionBuffer, hullTrimFraction, hullBuffer, null);
        UpdateReferenceOutline();

        EnsureHullRenderers();
        DrawPolygon(hullLine, showHull ? hullBuffer : null, hullColor);
        DrawPolygon(baselineHullLine, showHull ? baselineHull : null, baselineHullColor);
        DrawPolygon(referenceHullLine,
                    showHull && showReferenceHull ? referenceOutline : null, referenceHullColor);
    }

    /// <summary>
    /// Finds the dispersion this run would have reached without randomness, and when it got there.
    ///
    /// The run on screen started from a saved spawn layout. Somewhere in the recordings folder is
    /// the run that began from that identical layout at the same perception radius and max speed
    /// with random movement switched off. That run's plateau hull area is the honest comparison
    /// point: it is how far this particular arrangement of agents spreads under separation alone,
    /// with spawn luck held fixed rather than averaged away.
    ///
    /// Only meaningful for a randomised run. A reference is its own answer, so it is left alone.
    /// </summary>
    private void ResolveReferenceDispersion()
    {
        referenceEntry = null;
        referenceTrajectory = null;
        referenceOutline.Clear();
        referenceHullAtPlateau.Clear();
        referenceCrossFrame = -1;
        referenceCrossTime = 0f;
        referenceStatus = "";

        if (trajectory == null) return;
        if (comparisonView == null) comparisonView = new SwarmReplayComparisonView();
        comparisonView.SetTrajectories(trajectory, null);
        if (!showReferenceHull && !compareReferenceReplay) return;

        TrajectoryHeader h = trajectory.header;

        // A zero-randomness clip is its own answer. This is the most common reason the panel shows
        // nothing, and it is not a fault: 130 of the 390 clips in a three-level batch are
        // references. Load one with random movement to see a comparison.
        if (Mathf.Abs(h.randomMovement) <= 0.001f)
        {
            referenceStatus = "this clip has randomMovement 0, so it IS a reference — "
                            + "load a randomised clip to compare against it";
            return;
        }

        if (string.IsNullOrEmpty(h.spawnLayoutId))
        {
            referenceStatus = "this clip records no spawnLayoutId, so there is no layout to match";
            return;
        }

        ReferenceHullIndex index = EnsureReferenceIndex();
        if (index == null || index.Count == 0)
        {
            referenceStatus = "no reference video found in this clip's folder";
            return;
        }

        if (!index.TryGet(h.spawnLayoutId, h.perceptionRadius, h.maxSpeed,
                          out ReferenceHullIndex.Entry entry))
        {
            referenceStatus = $"no reference video found for layout "
                            + $"{entryLayoutShort(h.spawnLayoutId)}, R {h.perceptionRadius:F2}, "
                            + $"maxSpeed {h.maxSpeed:F2}";
            return;
        }

        referenceEntry = entry;
        LoadReferenceHullOutline();
        comparisonView.SetTrajectories(trajectory, referenceTrajectory);
        if (referenceTrajectory == null)
        {
            if (string.IsNullOrEmpty(referenceStatus)) referenceStatus = "the reference trajectory could not be loaded";
            referenceEntry = null;
            return;
        }
        FindReferenceCrossing();

        if (!entry.HasDirection)
        {
            referenceStatus = $"reference barely moved ({entry.Growth:F2}x its spawn area), " +
                              "so this target is inside the noise of where it started";
        }
        else if (!entry.settled)
        {
            referenceStatus = "reference never settled, area is a lower bound";
        }
    }

    /// <summary>
    /// The first frame at which the replayed hull holds at or past the reference area for the dwell
    /// time. Mirrors the rule the recorder applies, so the marker lands where a clip using this
    /// target would actually have ended.
    /// </summary>
    private void FindReferenceCrossing()
    {
        referenceCrossFrame = -1;
        if (referenceEntry == null || trajectory == null || trajectory.FrameCount == 0) return;

        float target = referenceEntry.plateauArea;
        if (target <= 0f) return;

        // Direction is a property of the reference, not of this run's own frame 0. Comparing the
        // target against the replayed run's start flips the test to a contraction check whenever
        // the target lands inside the spread of starting areas, which scored eight of these
        // recordings as "never reached" while they were expanding tenfold.
        bool growing = referenceEntry.Growing;

        int heldFrom = -1;

        for (int f = 0; f < trajectory.FrameCount; f++)
        {
            float area = AreaAtFrame(f);
            bool met = growing ? area >= target : area <= target;

            if (!met) { heldFrom = -1; continue; }
            if (heldFrom < 0) heldFrom = f;

            if (trajectory.frames[f].t - trajectory.frames[heldFrom].t >= Mathf.Max(0f, referenceDwellTime))
            {
                referenceCrossFrame = f;
                referenceCrossTime = trajectory.frames[f].t;
                return;
            }
        }
    }

    /// <summary>
    /// Hull area on a frame, preferring the value Unity recorded at capture time so the replay
    /// reports the number the recorder was deciding on. Older files without it are recomputed.
    /// </summary>
    private float AreaAtFrame(int frame)
    {
        if (trajectory.HasHullArea) return trajectory.GetHullArea(frame);

        List<Vector2> points = new List<Vector2>(trajectory.AgentCount);
        for (int i = 0; i < trajectory.AgentCount; i++) points.Add(trajectory.GetPosition(frame, i));
        return SwarmDensityMetrics.TrimmedHullArea(points, hullTrimFraction);
    }

    /// <summary>Trailing part of a layout id, for a readable status line.</summary>
    private static string entryLayoutShort(string layoutId)
    {
        if (string.IsNullOrEmpty(layoutId)) return "?";
        int at = layoutId.LastIndexOf("_layout_", System.StringComparison.Ordinal);
        return at >= 0 ? layoutId.Substring(at + 8) : layoutId;
    }

    /// <summary>
    /// Indexes the references sitting in the same folder as the loaded recording.
    ///
    /// Rebuilt whenever the folder changes, because it follows whichever clip is open.
    /// </summary>
    private ReferenceHullIndex EnsureReferenceIndex()
    {
        if (string.IsNullOrEmpty(loadedPath)) return null;

        string folder = Path.GetDirectoryName(loadedPath);
        if (string.IsNullOrEmpty(folder)) return null;

        if (referenceIndex != null && !rebuildReferenceIndex && folder == indexedFolder)
        {
            return referenceIndex;
        }

        Debug.Log($"[Replay] Indexing reference runs in this clip's folder: '{folder}'");

        referenceIndex = ReferenceHullIndex.Build(folder, useCache: !rebuildReferenceIndex);
        indexedFolder = folder;
        rebuildReferenceIndex = false;
        return referenceIndex;
    }

    /// <summary>Loads the matching reference trajectory and its hull at the plateau frame.</summary>
    private void LoadReferenceHullOutline()
    {
        referenceHullAtPlateau.Clear();
        referenceHullCentre = Vector2.zero;

        if (referenceEntry == null || string.IsNullOrEmpty(referenceEntry.sourcePath)) return;
        if (!File.Exists(referenceEntry.sourcePath)) return;

        SwarmTrajectory reference = SwarmTrajectory.Load(referenceEntry.sourcePath);
        if (reference == null || reference.FrameCount == 0) return;

        TrajectoryHeader h = trajectory.header;
        TrajectoryHeader r = reference.header;
        if (!string.Equals(h.swarmType, r.swarmType, System.StringComparison.OrdinalIgnoreCase) ||
            h.spawnLayoutId != r.spawnLayoutId || h.agentCount != r.agentCount ||
            Mathf.Abs(r.randomMovement) > 0.001f ||
            Mathf.Abs(h.perceptionRadius - r.perceptionRadius) > ReferenceHullIndex.RadiusTolerance ||
            Mathf.Abs(h.maxSpeed - r.maxSpeed) > ReferenceHullIndex.SpeedTolerance ||
            (!string.IsNullOrEmpty(h.spawnLayoutFingerprint) &&
             h.spawnLayoutFingerprint != r.spawnLayoutFingerprint))
        {
            referenceStatus = "the reference does not match this recording's motion, spawn layout or settings";
            referenceEntry = null;
            return;
        }
        referenceTrajectory = reference;

        int frame = Mathf.Clamp(referenceEntry.plateauFrame, 0, reference.FrameCount - 1);

        List<Vector2> points = new List<Vector2>(reference.AgentCount);
        for (int i = 0; i < reference.AgentCount; i++) points.Add(reference.GetPosition(frame, i));

        SwarmDensityMetrics.TrimmedHull(points, hullTrimFraction, referenceHullAtPlateau, null);

        for (int i = 0; i < referenceHullAtPlateau.Count; i++) referenceHullCentre += referenceHullAtPlateau[i];
        if (referenceHullAtPlateau.Count > 0) referenceHullCentre /= referenceHullAtPlateau.Count;
    }

    /// <summary>Builds the outline drawn in the scene, per the chosen mode.</summary>
    private void UpdateReferenceOutline()
    {
        referenceOutline.Clear();

        if (referenceEntry == null || referenceEntry.plateauArea <= 0f) return;

        if (referenceOutline_Mode != ReferenceOutlineMode.EqualAreaCircle &&
            referenceHullAtPlateau.Count >= 3)
        {
            if (referenceOutline_Mode == ReferenceOutlineMode.RecordedHull)
            {
                referenceOutline.AddRange(referenceHullAtPlateau);
                return;
            }

            // CentredOnSwarm: same polygon, slid onto the replayed swarm so the drift between the
            // two runs does not muddle a comparison that is only about size and shape.
            Vector2 offset = SwarmCentroid() - referenceHullCentre;
            for (int i = 0; i < referenceHullAtPlateau.Count; i++)
            {
                referenceOutline.Add(referenceHullAtPlateau[i] + offset);
            }
            return;
        }

        // Area-only fallback. See ReferenceOutlineMode.EqualAreaCircle: this is not a shape to match.
        if (positionBuffer.Count == 0) return;

        float radius = Mathf.Sqrt(referenceEntry.plateauArea / Mathf.PI);
        Vector2 centre = SwarmCentroid();
        const int segments = 64;

        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            referenceOutline.Add(centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
        }
    }

    private Vector2 SwarmCentroid()
    {
        if (positionBuffer.Count == 0) return Vector2.zero;

        Vector2 centre = Vector2.zero;
        for (int i = 0; i < positionBuffer.Count; i++) centre += positionBuffer[i];
        return centre / positionBuffer.Count;
    }

    private void ComputeBaselineHull()
    {
        baselineHull.Clear();
        baselineHullArea = 0f;
        if (trajectory == null || trajectory.FrameCount == 0) return;

        List<Vector2> first = new List<Vector2>();
        for (int i = 0; i < trajectory.AgentCount; i++) first.Add(trajectory.GetPosition(0, i));

        baselineHullArea = SwarmDensityMetrics.TrimmedHull(first, hullTrimFraction, baselineHull, null);
    }

    private void DrawPolygon(LineRenderer line, List<Vector2> polygon, Color color)
    {
        if (line == null) return;

        if (polygon == null || polygon.Count < 3) { line.positionCount = 0; return; }

        line.startColor = color;
        line.endColor = color;
        line.widthMultiplier = hullLineWidth;
        line.positionCount = polygon.Count;
        for (int i = 0; i < polygon.Count; i++)
        {
            line.SetPosition(i, new Vector3(polygon[i].x, polygon[i].y, 0.02f));
        }
    }

    public void Play() { if (!IsRecordingVideo) isPlaying = true; }
    public void Pause() { if (!IsRecordingVideo) isPlaying = false; }
    public void TogglePlay() { if (!IsRecordingVideo) isPlaying = !isPlaying; }

    public void SeekToFrame(int frame)
    {
        if (IsRecordingVideo) return;
        if (trajectory == null || trajectory.FrameCount == 0) return;
        playhead = trajectory.frames[Mathf.Clamp(frame, 0, trajectory.FrameCount - 1)].t;
        ApplyPlayhead();
    }

    public void StepFrames(int delta)
    {
        if (IsRecordingVideo) return;
        if (trajectory == null || trajectory.FrameCount == 0) return;
        isPlaying = false;
        SwarmTrajectory timeline = compareReferenceReplay && referenceTrajectory != null &&
            referenceTrajectory.Duration > trajectory.Duration ? referenceTrajectory : trajectory;
        int frame = Mathf.Clamp(timeline.FrameIndexAtTime(playhead) + delta, 0, timeline.FrameCount - 1);
        SeekToTime(timeline.frames[frame].t);
    }

    public void SeekToTime(float time)
    {
        if (IsRecordingVideo) return;
        if (trajectory == null || trajectory.FrameCount == 0) return;
        playhead = Mathf.Clamp(time, 0f, PlaybackDuration);
        ApplyPlayhead();
    }

    // ------------------------------------------------------------- panel

    private GUIStyle panelStyle, titleStyle, sectionStyle, labelStyle, valueStyle,
                     mutedStyle, buttonStyle, toggleStyle, separatorStyle, statusStyle;
    private Texture2D panelTexture, separatorTexture;

    private static readonly Color Ink = new Color(0.93f, 0.94f, 0.96f);
    private static readonly Color Muted = new Color(0.62f, 0.66f, 0.72f);
    private static readonly Color Accent = new Color(0.42f, 0.78f, 1f);
    private static readonly Color Warn = new Color(1f, 0.48f, 0.42f);

    private const float PanelWidth = 550f;
    private const float LabelColumn = 132f;

    private void EnsureStyles()
    {
        if (panelStyle != null) return;

        panelTexture = SolidTexture(new Color(0.09f, 0.10f, 0.13f, 0.93f));
        separatorTexture = SolidTexture(new Color(1f, 1f, 1f, 0.13f));

        panelStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = panelTexture },
            padding = new RectOffset(16, 16, 14, 16),
            border = new RectOffset(0, 0, 0, 0)
        };

        titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 15, fontStyle = FontStyle.Bold, richText = true,
            normal = { textColor = Ink }, margin = new RectOffset(0, 0, 0, 2)
        };

        sectionStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11, fontStyle = FontStyle.Bold, richText = true,
            normal = { textColor = Accent }, margin = new RectOffset(0, 0, 8, 4)
        };

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12, normal = { textColor = Muted }, margin = new RectOffset(0, 0, 2, 2)
        };

        valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12, fontStyle = FontStyle.Bold, richText = true,
            normal = { textColor = Ink }, margin = new RectOffset(0, 0, 2, 2)
        };

        mutedStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11, richText = true, wordWrap = true,
            normal = { textColor = Muted }, margin = new RectOffset(0, 0, 2, 2)
        };

        statusStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 11, richText = true, wordWrap = true,
            normal = { textColor = Ink }, margin = new RectOffset(0, 0, 0, 6)
        };

        buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 12, fixedHeight = 24, margin = new RectOffset(0, 4, 2, 2)
        };

        toggleStyle = new GUIStyle(GUI.skin.toggle)
        {
            fontSize = 12, normal = { textColor = Ink }, onNormal = { textColor = Ink },
            hover = { textColor = Ink }, onHover = { textColor = Ink },
            margin = new RectOffset(0, 0, 3, 3)
        };

        separatorStyle = new GUIStyle
        {
            normal = { background = separatorTexture },
            margin = new RectOffset(0, 0, 8, 6),
            fixedHeight = 1
        };
    }

    private static Texture2D SolidTexture(Color color)
    {
        Texture2D t = new Texture2D(1, 1);
        t.SetPixel(0, 0, color);
        t.Apply();
        return t;
    }

    private void Separator()
    {
        GUILayout.Box(GUIContent.none, separatorStyle, GUILayout.ExpandWidth(true), GUILayout.Height(1));
    }

    private void Section(string title)
    {
        Separator();
        GUILayout.Label(title.ToUpperInvariant(), sectionStyle);
    }

    /// <summary>Aligned label and value on one line, which is what makes a wall of numbers readable.</summary>
    private void Row(string label, string value, Color? valueColor = null)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(LabelColumn));

        if (valueColor.HasValue)
        {
            GUIStyle tinted = new GUIStyle(valueStyle) { normal = { textColor = valueColor.Value } };
            GUILayout.Label(value, tinted);
        }
        else
        {
            GUILayout.Label(value, valueStyle);
        }

        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    /// <summary>Two label/value pairs side by side, for the compact parameter grid.</summary>
    private void RowPair(string labelA, string valueA, string labelB, string valueB)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(labelA, labelStyle, GUILayout.Width(96));
        GUILayout.Label(valueA, valueStyle, GUILayout.Width(56));
        GUILayout.Space(10);
        GUILayout.Label(labelB, labelStyle, GUILayout.Width(96));
        GUILayout.Label(valueB, valueStyle, GUILayout.Width(56));
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();
    }

    void OnGUI()
    {
        if (!IsReplaying) return;

        EnsureStyles();

        if (compareReferenceReplay && trajectory != null && comparisonView != null)
        {
            float left = PanelVisible ? PanelWidth + 28f : 14f;
            comparisonView.Draw(new Rect(left, 12f,
                Mathf.Max(1f, Screen.width - left - 14f), Mathf.Max(1f, Screen.height - 24f)),
                playhead, interpolate, showHull, showPerceptionRadius, showTrails, trailFrames,
                hullTrimFraction, hullColor, referenceHullColor, referenceStatus, titleStyle, mutedStyle);
        }

        if (!PanelVisible) return;

        float height = Mathf.Min(Screen.height - 24f, 760f);
        GUILayout.BeginArea(new Rect(14, 12, PanelWidth, height), GUIContent.none, panelStyle);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"<color=#6bc6ff>REPLAY</color>  <size=11><color=#9aa4b0>press {toggleKey} for live</color></size>", titleStyle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button($"Hide ({panelToggleKey})", buttonStyle, GUILayout.Width(84f))) showPanel = false;
        GUILayout.EndHorizontal();
        panelScroll = GUILayout.BeginScrollView(panelScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

        GUILayout.Label(status, statusStyle);
        DrawVideoRecording();

        bool controlsEnabled = GUI.enabled;
        GUI.enabled = controlsEnabled && !IsRecordingVideo;
        if (trajectory != null && trajectory.FrameCount > 0)
        {
            DrawTransport();
            DrawRecordedParameters();
            DrawHullReadout();
        }

        DrawFileList();
        GUI.enabled = controlsEnabled;

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void DrawTransport()
    {
        Section("Playback");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button(isPlaying ? "Pause" : "Play", buttonStyle, GUILayout.Width(74))) TogglePlay();
        if (GUILayout.Button("|<", buttonStyle, GUILayout.Width(40))) SeekToFrame(0);
        if (GUILayout.Button("-1", buttonStyle, GUILayout.Width(40))) StepFrames(-1);
        if (GUILayout.Button("+1", buttonStyle, GUILayout.Width(40))) StepFrames(1);
        if (GUILayout.Button(">|", buttonStyle, GUILayout.Width(40))) SeekToTime(PlaybackDuration);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        Row("Frame", $"{currentFrame} / {trajectory.FrameCount - 1}");
        Row("Time", $"{playhead:F2} s  of  {PlaybackDuration:F2} s");

        GUILayout.Space(2);
        float scrubbed = GUILayout.HorizontalSlider(playhead, 0f, PlaybackDuration);
        if (!Mathf.Approximately(scrubbed, playhead))
        {
            isPlaying = false;
            playhead = scrubbed;
            ApplyPlayhead();
        }

        GUILayout.Space(6);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Speed", labelStyle, GUILayout.Width(48));
        GUILayout.Label($"{speed:F2}x", valueStyle, GUILayout.Width(46));
        speed = GUILayout.HorizontalSlider(speed, 0.1f, 4f);
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        loop = GUILayout.Toggle(loop, " Loop", toggleStyle);
        interpolate = GUILayout.Toggle(interpolate, " Interpolate between frames", toggleStyle);
        showHull = GUILayout.Toggle(showHull, " Show convex hull", toggleStyle);

        bool hasSceneAgents = uiController != null && uiController.agentPrefab != null;
        bool comparisonWanted = hasSceneAgents ? GUILayout.Toggle(compareReferenceReplay,
            " Compare with zero-randomness replay (2 rows)", toggleStyle) : compareReferenceReplay;
        if (comparisonWanted != compareReferenceReplay)
        {
            compareReferenceReplay = comparisonWanted;
            ResolveReferenceDispersion();
            BuildAgents();
            playhead = Mathf.Min(playhead, PlaybackDuration);
            ApplyPlayhead();
        }

        bool perceptionWanted = GUILayout.Toggle(showPerceptionRadius, " Show perception radius", toggleStyle);
        bool trailsWanted = GUILayout.Toggle(showTrails, " Show trails", toggleStyle);
        if (perceptionWanted != showPerceptionRadius || trailsWanted != showTrails)
        {
            showPerceptionRadius = perceptionWanted;
            showTrails = trailsWanted;
            BuildAgents();
            ApplyPlayhead();
        }

        if (showTrails)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Trail", labelStyle, GUILayout.Width(48));
            GUILayout.Label($"{trailFrames}", valueStyle, GUILayout.Width(46));
            trailFrames = Mathf.RoundToInt(GUILayout.HorizontalSlider(trailFrames, 5, 600));
            GUILayout.EndHorizontal();
        }
    }

    /// <summary>The recorded settings, as static text: these are history, not controls.</summary>
    private void DrawRecordedParameters()
    {
        TrajectoryHeader h = trajectory.header;

        Section("Recorded settings");

        Row("Motion type", string.IsNullOrEmpty(h.swarmType) ? "unknown" : h.swarmType, Accent);
        Row("Agents", h.agentSpawnShortfall
                ? $"{h.agentCount} of {h.requestedAgentCount}  (short)"
                : $"{h.agentCount}",
            h.agentSpawnShortfall ? Warn : (Color?)null);

        GUILayout.Space(4);
        RowPair("Cohesion", $"{h.cohesion:F2}", "Separation", $"{h.separation:F2}");
        RowPair("Alignment", $"{h.alignment:F2}", "Friction", $"{h.friction:F2}");
        RowPair("Random mvmt", $"{h.randomMovement:F2}", "Overlap avoid", $"{h.overlapAvoidance:F2}");
        RowPair("Safety dist", $"{h.safetyDistance:F2}", "Env avoid", $"{h.envAvoidance:F2}");
        RowPair("Perception", $"{h.perceptionRadius:F2}", "Obstacle rad", $"{h.obstacleRadius:F2}");
        RowPair("Max speed", $"{h.maxSpeed:F2}", "Sim step",
            h.simulationStep > 0f ? $"{h.simulationStep:F4}" : "Per frame");

        GUILayout.Space(4);
        if (!string.IsNullOrEmpty(h.obstacleName)) Row("Obstacle", h.obstacleName);
        if (!string.IsNullOrEmpty(h.goalAreaName)) Row("Goal area", h.goalAreaName);
        if (!string.IsNullOrEmpty(h.wallsDisabled)) Row("Walls off", h.wallsDisabled, Warn);

        if (!string.IsNullOrEmpty(h.endReason))
        {
            string ended = $"{h.endReason}  at {h.duration:F2} s";
            Row("Ended", ended, h.endReason == "timeout" ? Muted : Accent);
            if (!string.IsNullOrEmpty(h.endConditionUsed)) Row("Rule", h.endConditionUsed);
        }

        if (!string.IsNullOrEmpty(h.recordedAtUtc))
        {
            GUILayout.Space(2);
            GUILayout.Label($"captured {h.recordedAtUtc} UTC", mutedStyle);
        }
    }

    /// <summary>Hull area as it evolves through the replay, against the opening frame.</summary>
    private void DrawHullReadout()
    {
        if (trajectory.HasContactData)
        {
            Section("Wall contacts");

            int touching = trajectory.ContactCount(currentFrame);
            int unique = trajectory.UniqueAgentsTouchedBy(currentFrame);

            Row("Touching now", $"{touching}", touching > 0 ? Warn : (Color?)null);
            Row("Touched so far", $"{unique} of {trajectory.AgentCount}",
                unique > 0 ? Warn : (Color?)null);
            Row("Threshold", $"{trajectory.header.contactDistance:F3} u");
        }

        Section("Convex hull, this frame");

        float ratio = baselineHullArea > 0.0001f ? currentHullArea / baselineHullArea : 0f;
        Row("Area", $"{currentHullArea:F2} u²");
        Row("Frame 0", $"{baselineHullArea:F2} u²");
        Row("Ratio", $"{ratio:F2}", ratio < 1f ? Accent : Warn);

        float r = trajectory.header.perceptionRadius;
        int n = trajectory.AgentCount;
        if (r > 0f && currentHullArea > 0.0001f && n > 0)
        {
            float degree = (n / currentHullArea) * Mathf.PI * r * r;
            float criticalArea = n * Mathf.PI * r * r / 4.51f;
            bool connected = degree >= 4.51f;

            GUILayout.Space(4);
            Row("Mean degree", $"{degree:F1}   crit 4.51");
            Row("State", connected ? "connected" : "fragmenting", connected ? Accent : Warn);
            Row("Area / A_c", $"{(currentHullArea / criticalArea):F2}x   A_c {criticalArea:F1}");
        }

        DrawReferenceDispersion();
    }

    /// <summary>
    /// How this run compares with the same layout run without randomness.
    ///
    /// Everything here is paired: the reference began from the identical spawn, so the ratio is a
    /// within-layout number with spawn luck divided out rather than averaged away.
    /// </summary>
    private void DrawReferenceDispersion()
    {
        if (!showReferenceHull) return;

        Section("Vs. no randomness, same layout");

        if (referenceEntry == null)
        {
            Row("Reference", string.IsNullOrEmpty(referenceStatus) ? "not resolved" : referenceStatus, Muted);
            return;
        }

        Row("Target area", $"{referenceEntry.plateauArea:F2} u²",
            referenceEntry.settled ? Accent : Warn);
        Row("From layout", referenceEntry.layoutId, Muted);
        Row("Settled at", referenceEntry.settled
                ? $"{referenceEntry.plateauTime:F2} s into the reference"
                : "never settled, lower bound",
            referenceEntry.settled ? (Color?)null : Warn);

        GUILayout.Space(4);

        float share = referenceEntry.plateauArea > 0.0001f
            ? currentHullArea / referenceEntry.plateauArea
            : 0f;
        Row("This frame", $"{share:F2}x the reference", share >= 1f ? Accent : Warn);

        if (referenceCrossFrame >= 0)
        {
            bool passed = currentFrame >= referenceCrossFrame;
            Row("Reaches it at", $"{referenceCrossTime:F2} s   frame {referenceCrossFrame}",
                passed ? Accent : Muted);

            if (!passed && GUILayout.Button("Jump to that frame", buttonStyle))
            {
                SeekToFrame(referenceCrossFrame);
            }
        }
        else
        {
            Row("Reaches it at", $"never, in {trajectory.Duration:F2} s", Warn);
        }

        GUILayout.Space(4);

        string drawn;
        switch (referenceOutline_Mode)
        {
            case ReferenceOutlineMode.RecordedHull:
                drawn = referenceHullAtPlateau.Count >= 3
                    ? "the reference's own hull, where it was"
                    : "outline unavailable, showing area ring";
                break;
            case ReferenceOutlineMode.CentredOnSwarm:
                drawn = referenceHullAtPlateau.Count >= 3
                    ? "reference hull, moved onto this swarm"
                    : "outline unavailable, showing area ring";
                break;
            default:
                drawn = "equal-area ring — size only, not a shape";
                break;
        }
        Row("Gold outline", drawn, Muted);

        if (!string.IsNullOrEmpty(referenceStatus))
        {
            GUILayout.Space(2);
            Row("Note", referenceStatus, Warn);
        }
    }

    private void DrawFileList()
    {
        Section("Recordings");

        SimRecorder recorder = uiController != null ? uiController.GetComponent<SimRecorder>() : null;

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Refresh", buttonStyle, GUILayout.Width(76))) RefreshFileList();
        if (recorder != null && !string.IsNullOrEmpty(recorder.LastTrajectoryPath))
        {
            if (GUILayout.Button("Load last capture", buttonStyle)) LoadFromPath(recorder.LastTrajectoryPath);
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        fileScroll = GUILayout.BeginScrollView(fileScroll, GUIStyle.none, GUI.skin.verticalScrollbar,
                                               GUILayout.Height(132));
        for (int i = 0; i < availableFiles.Length; i++)
        {
            bool selected = i == selectedFileIndex;
            if (GUILayout.Toggle(selected, " " + availableFiles[i], toggleStyle) && !selected) LoadByIndex(i);
        }
        GUILayout.EndScrollView();

        Separator();
        GUILayout.Label($"wheel or arrows scrub  ·  shift for {framesPerScrollNotch} frames  ·  " +
                        $"space plays  ·  {toggleKey} returns to live", mutedStyle);
    }

    private void OnDisable()
    {
        if (IsReplaying) ExitReplay();
    }

    private void OnDestroy()
    {
        StopReplayVideoRecording();
        DestroyReplayAgents();
        comparisonView?.Dispose();
    }
}

/// <summary>Draws two recorded swarms using a shared world frame and timeline, without requiring scene prefabs.</summary>
internal sealed class SwarmReplayComparisonView
{
    private SwarmTrajectory primary;
    private SwarmTrajectory reference;
    private Vector2 minimum;
    private Vector2 maximum;
    private Texture2D agentTexture;
    private readonly List<Vector2> positions = new List<Vector2>();
    private readonly List<Vector2> hull = new List<Vector2>();
    private readonly List<List<Vector2>> primaryGeometry = new List<List<Vector2>>();
    private readonly List<List<Vector2>> referenceGeometry = new List<List<Vector2>>();

    public void SetTrajectories(SwarmTrajectory run, SwarmTrajectory zeroRandomness)
    {
        primary = run;
        reference = zeroRandomness;
        minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        IncludeTrajectory(primary, primaryGeometry);
        IncludeTrajectory(reference, referenceGeometry);
        if (primary == null) { minimum = -Vector2.one; maximum = Vector2.one; }
        Vector2 padding = Vector2.Max((maximum - minimum) * 0.04f, Vector2.one * 0.5f);
        minimum -= padding;
        maximum += padding;
    }

    private void IncludeTrajectory(SwarmTrajectory clip, List<List<Vector2>> geometry)
    {
        geometry.Clear();
        if (clip == null) return;
        // Bounds cover the entire pair, so neither playback nor row changes the apparent scale.
        for (int f = 0; f < clip.FrameCount; f++)
            for (int i = 0; i < clip.AgentCount; i++) IncludePoint(clip.GetPosition(f, i));
        if (clip.header.geometry == null) return;
        foreach (TrajectoryGeometry shape in clip.header.geometry)
        {
            List<Vector2> outline = shape.BuildOutline();
            geometry.Add(outline);
            foreach (Vector2 point in outline) IncludePoint(point);
        }
    }

    private void IncludePoint(Vector2 point)
    {
        minimum = Vector2.Min(minimum, point);
        maximum = Vector2.Max(maximum, point);
    }

    public void Draw(Rect screen, float time, bool interpolate, bool showHull, bool showPerception,
        bool showTrails, int trailFrames, float trim, Color primaryColor, Color referenceColor,
        string missingReason, GUIStyle title, GUIStyle caption)
    {
        if (primary == null) return;
        EnsureAgentTexture();
        float rowHeight = Mathf.Max(1f, (screen.height - 10f) * 0.5f);
        Rect top = new Rect(screen.x, screen.y, screen.width, rowHeight);
        Rect bottom = new Rect(screen.x, screen.y + rowHeight + 10f, screen.width, rowHeight);
        DrawRow(top, primary, primaryGeometry, time, interpolate, showHull, showPerception,
            showTrails, trailFrames, trim, primaryColor,
            $"{primary.header.swarmType} · random {primary.header.randomMovement:0.##}", title, caption);
        if (reference != null)
        {
            DrawRow(bottom, reference, referenceGeometry, time, interpolate, showHull, showPerception,
                showTrails, trailFrames, trim, referenceColor, "Zero-randomness reference · random 0", title, caption);
        }
        else
        {
            Fill(bottom, new Color(0.07f, 0.08f, 0.10f));
            GUI.Label(new Rect(bottom.x + 12f, bottom.y + 8f, bottom.width - 24f, 24f),
                "Zero-randomness reference unavailable", title);
            GUI.Label(new Rect(bottom.x + 12f, bottom.y + 40f, bottom.width - 24f, 80f),
                string.IsNullOrEmpty(missingReason) ? "Choose a randomized recording with a matching spawn layout." : missingReason, caption);
        }
    }

    private void DrawRow(Rect row, SwarmTrajectory clip, List<List<Vector2>> geometry, float time,
        bool interpolate, bool showHull, bool showPerception, bool showTrails, int trailFrames,
        float trim, Color color, string heading, GUIStyle title, GUIStyle caption)
    {
        Fill(row, new Color(0.07f, 0.08f, 0.10f));
        GUI.BeginGroup(row);
        GUI.Label(new Rect(12f, 6f, row.width - 24f, 24f), heading, title);
        Rect plot = new Rect(12f, 34f, Mathf.Max(1f, row.width - 24f), Mathf.Max(1f, row.height - 64f));
        Vector2 size = maximum - minimum;
        float scale = Mathf.Min(plot.width / size.x, plot.height / size.y);
        Vector2 centre = (maximum + minimum) * 0.5f;
        GUI.BeginClip(plot);
        Vector2 plotCentre = plot.size * 0.5f;
        foreach (List<Vector2> outline in geometry)
            DrawOutline(outline, centre, plotCentre, scale, new Color(0.4f, 0.44f, 0.49f), 1f);

        float clipTime = Mathf.Min(time, clip.Duration);
        int frame = clip.FrameIndexAtTime(clipTime);
        int next = Mathf.Min(frame + 1, clip.FrameCount - 1);
        float span = clip.frames[next].t - clip.frames[frame].t;
        float blend = interpolate && span > 0f ? Mathf.Clamp01((clipTime - clip.frames[frame].t) / span) : 0f;
        positions.Clear();
        for (int i = 0; i < clip.AgentCount; i++)
        {
            Vector2 position = Vector2.Lerp(clip.GetPosition(frame, i), clip.GetPosition(next, i), blend);
            positions.Add(position);
            Vector2 pixel = Project(position, centre, plotCentre, scale);
            if (showPerception)
                DrawCircle(pixel, clip.header.perceptionRadius * scale, new Color(color.r, color.g, color.b, 0.16f));
            if (showTrails)
            {
                int first = Mathf.Max(0, frame - Mathf.Max(1, trailFrames));
                for (int f = first; f < frame; f++)
                    DrawLine(Project(clip.GetPosition(f, i), centre, plotCentre, scale),
                        Project(clip.GetPosition(f + 1, i), centre, plotCentre, scale),
                        new Color(color.r, color.g, color.b, 0.35f), 1f);
            }
            float radius = Mathf.Max(2f, (clip.header.agentRadius > 0f ? clip.header.agentRadius : 0.156f) * scale);
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(pixel.x - radius, pixel.y - radius, radius * 2f, radius * 2f), agentTexture);
            GUI.color = previous;
        }
        float area = SwarmDensityMetrics.TrimmedHull(positions, trim, hull, null);
        if (showHull) DrawOutline(hull, centre, plotCentre, scale, color, 1.5f);
        GUI.EndClip();
        string held = time >= clip.Duration ? " · holding final frame" : "";
        GUI.Label(new Rect(12f, row.height - 26f, row.width - 24f, 24f),
            $"{clipTime:F2} / {clip.Duration:F2} s · frame {frame} · area {area:F2} u² · R {clip.header.perceptionRadius:0.##} · speed {clip.header.maxSpeed:0.##}{held}", caption);
        GUI.EndGroup();
    }

    private static Vector2 Project(Vector2 point, Vector2 centre, Vector2 pixelCentre, float scale)
    {
        Vector2 offset = (point - centre) * scale;
        return pixelCentre + new Vector2(offset.x, -offset.y);
    }

    private static void DrawOutline(List<Vector2> points, Vector2 centre, Vector2 pixelCentre,
        float scale, Color color, float width)
    {
        if (points.Count < 2) return;
        for (int i = 0; i < points.Count; i++)
            DrawLine(Project(points[i], centre, pixelCentre, scale),
                Project(points[(i + 1) % points.Count], centre, pixelCentre, scale), color, width);
    }

    private static void DrawCircle(Vector2 centre, float radius, Color color)
    {
        const int segments = 48;
        Vector2 previous = centre + Vector2.right * radius;
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            Vector2 next = centre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
            DrawLine(previous, next, color, 1f);
            previous = next;
        }
    }

    private static void DrawLine(Vector2 from, Vector2 to, Color color, float width)
    {
        Matrix4x4 previous = GUI.matrix;
        Vector2 delta = to - from;
        GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, from);
        Fill(new Rect(from.x, from.y - width * 0.5f, delta.magnitude, width), color);
        GUI.matrix = previous;
    }

    private static void Fill(Rect rect, Color color)
    {
        Color previous = GUI.color;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void EnsureAgentTexture()
    {
        if (agentTexture != null) return;
        const int size = 32;
        agentTexture = new Texture2D(size, size, TextureFormat.RGBA32, false);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), Vector2.one * (size * 0.5f));
                agentTexture.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(size * 0.5f - distance)));
            }
        agentTexture.Apply();
    }

    public void Dispose()
    {
        if (agentTexture != null) Object.Destroy(agentTexture);
        agentTexture = null;
    }
}
