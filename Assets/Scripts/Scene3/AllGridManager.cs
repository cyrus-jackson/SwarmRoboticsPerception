using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Displays every recorded spawn layout for one condition — one motion type at one speed,
/// perception radius and randomness — in a synchronized video grid.
///
/// The same grid as <see cref="FlockGridManager"/>, which is left in place and unchanged, with the
/// motion type chosen in the Inspector instead of fixed to flocking. Clips are matched by reading
/// their file names, so any motion type SimRecorder produces works, with whatever number of spawn
/// layouts that condition happens to have.
/// </summary>
[DisallowMultipleComponent]
public class AllGridManager : MonoBehaviour
{
    [Header("Recording condition")]
    [Tooltip("Which motion type to show. The grid rebuilds when this changes, so it can be switched during Play.")]
    public SwarmType motionType = SwarmType.Flocking;

    [Min(0.01f)] public float maxSpeed = 3f;
    [Min(0.01f)] public float perceptionRadius = 4f;

    [Tooltip("Randomness to match, as recorded in the file name. Tick 'Any Randomness' below to ignore it.")]
    [Min(0f)] public float randomMovement = 0f;

    [Tooltip("Ignore randomness when matching, for a folder that holds only one value of it anyway.")]
    public bool anyRandomness = false;

    [Tooltip("Insist on exactly this many layouts and fail otherwise. 0 shows however many are there, which is what suits a batch not recorded with forty layouts.")]
    [Min(0)] public int expectedLayouts = 0;

    [Header("Source")]
    public string folderPath = "Assets/SimulationRecordings/Combinations/20260908_013309_flocking_40";

    [Header("Layout (optional)")]
    [Tooltip("Leave empty to create a screen-filling Canvas automatically.")]
    public RectTransform gridContainer;
    [Range(1, 40)] public int columns = 8;
    [Min(0)] public float spacing = 6f;

    [Header("Playback")]
    public bool loop = true;
    [Min(0)] public float restartPause = 0.5f;
    [Tooltip("Wall-clock seconds allowed to prepare or seek the videos before showing an error.")]
    [Min(1)] public float prepareTimeout = 60f;

    [Header("Grid recording")]
    [Tooltip("Record one complete iteration as a 1080p MP4 in the project's Grid Recordings folder. Checking during playback restarts the grid first. Resets when finished; Unity Editor only.")]
    public bool recordOneIteration;

    [Header("Render textures")]
    [Min(16)] public int renderWidth = 480;
    [Min(16)] public int renderHeight = 270;

    /// <summary>Tracks the video resources and playback state owned by one layout cell.</summary>
    private sealed class Cell
    {
        public VideoPlayer player;
        public RenderTexture texture;
        public TextMeshProUGUI label;
        public bool ready;
        public bool finished;
        public bool seeking;
    }

    private readonly List<Cell> cells = new List<Cell>();
    private GameObject generatedRoot;
    private RectTransform content;
    private UnityEngine.UI.GridLayoutGroup grid;
    private TextMeshProUGUI heading;
    private float loadedSpeed;
    private float loadedPerception;
    private string loadedFolder;
    private SwarmType loadedMotionType;
    private float loadedRandom;
    private bool loadedAnyRandom;
    private int loadedExpectedLayouts;
    private float waitingSince;
    private float finishedAt = -1;
    private bool waiting;
    private bool paused;
    private bool failed;
    private bool built;
    private Vector2 lastSize;
    private int lastColumns;
    private float lastSpacing;
    private bool recordToggleState;
    private bool recordingPending;
    private int recordingEndFrame = -1;
#if UNITY_EDITOR
    private FlockGridCapture capture;
#endif

    private void Start() => BuildGrid();

    [ContextMenu("Build / Reload Grid")]
    public void BuildGrid()
    {
        if (!Application.isPlaying) return;
        ClearGrid();
        recordToggleState = recordOneIteration;
        recordingPending = recordOneIteration;
        loadedSpeed = maxSpeed;
        loadedPerception = perceptionRadius;
        loadedFolder = folderPath;
        loadedMotionType = motionType;
        loadedRandom = randomMovement;
        loadedAnyRandom = anyRandomness;
        loadedExpectedLayouts = expectedLayouts;
        built = true;
        failed = false;
        paused = false;
        finishedAt = -1;
        CreateContainer();
        try
        {
            string[] recordings = AllRecordingCatalog.FindRecordings(
                ResolveFolder(folderPath),
                motionType.ToString(),
                maxSpeed,
                perceptionRadius,
                anyRandomness ? double.NaN : randomMovement,
                expectedLayouts);
            for (int i = 0; i < recordings.Length; i++) CreateCell(recordings[i], i);
            UpdateLayout();
            waiting = true;
            waitingSince = Time.realtimeSinceStartup;
            SetHeading($"Preparing {recordings.Length} recordings…");
            foreach (Cell cell in cells) cell.player.Prepare();
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private void Update()
    {
        if (!built) return;
        // Any part of the condition changing means a different set of clips, so rebuild rather
        // than leave the previous grid showing under a new heading.
        if (!loadedSpeed.Equals(maxSpeed) ||
            !loadedPerception.Equals(perceptionRadius) ||
            loadedFolder != folderPath ||
            loadedMotionType != motionType ||
            !loadedRandom.Equals(randomMovement) ||
            loadedAnyRandom != anyRandomness ||
            loadedExpectedLayouts != expectedLayouts)
        {
            BuildGrid();
            return;
        }
        UpdateLayout();
        if (failed || cells.Count == 0) return;
        if (recordOneIteration != recordToggleState)
        {
            recordToggleState = recordOneIteration;
            recordingPending = recordOneIteration;
            if (recordOneIteration)
            {
                paused = false;
                if (!waiting) RestartGrid();
            }
            else
            {
                FinishCapture(false);
            }
        }
        if (waiting)
        {
            if (Time.realtimeSinceStartup - waitingSince > Mathf.Max(1, prepareTimeout))
            {
                Fail("Timed out preparing/seeking the videos. Check the Console and reload the grid.");
                return;
            }
            foreach (Cell cell in cells)
                if (!cell.ready || cell.seeking) return;
            waiting = false;
            SetHeading(paused ? "Paused" : "Playing");
            if (!paused)
            {
                if (recordingPending && !BeginCapture()) return;
                PlayUnfinished();
            }
        }
        if (paused) return;
        foreach (Cell cell in cells)
            if (!cell.finished) return;
        if (IsCapturing)
        {
            // Keep the final composite visible for a rendered frame before closing the encoder.
            if (recordingEndFrame < 0) recordingEndFrame = Time.frameCount + 2;
            if (Time.frameCount < recordingEndFrame) return;
            FinishCapture(true);
        }
        if (!loop) return;
        if (finishedAt < 0) finishedAt = Time.realtimeSinceStartup;
        if (Time.realtimeSinceStartup - finishedAt >= Mathf.Max(0, restartPause)) RestartGrid();
    }

    [ContextMenu("Pause Grid")]
    public void PauseGrid()
    {
        paused = true;
        foreach (Cell cell in cells) cell.player.Pause();
        if (!failed) SetHeading("Paused");
    }

    [ContextMenu("Resume Grid")]
    public void ResumeGrid()
    {
        if (failed) return;
        paused = false;
        finishedAt = -1;
        if (!waiting)
        {
            if (recordingPending && !BeginCapture()) return;
            PlayUnfinished();
        }
        SetHeading(waiting ? "Preparing…" : "Playing");
    }

    [ContextMenu("Restart Grid")]
    public void RestartGrid()
    {
        if (failed || cells.Count == 0 || waiting) return;
        FinishCapture(false);
        waiting = true;
        waitingSince = Time.realtimeSinceStartup;
        finishedAt = -1;
        foreach (Cell cell in cells)
        {
            cell.player.Pause();
            cell.finished = false;
            cell.seeking = cell.player.frame != 0;
            if (cell.seeking) cell.player.frame = 0;
        }
        SetHeading("Returning all layouts to the start…");
    }

    private void PlayUnfinished()
    {
        foreach (Cell cell in cells)
            if (!cell.finished) cell.player.Play();
    }

    private bool IsCapturing
    {
        get
        {
#if UNITY_EDITOR
            return capture != null;
#else
            return false;
#endif
        }
    }

    private bool BeginCapture()
    {
        recordingPending = false;
        recordingEndFrame = -1;
#if UNITY_EDITOR
        try
        {
            capture = new FlockGridCapture();
            capture.Start(loadedSpeed, loadedPerception, loadedMotionType.ToString().ToLowerInvariant());
            SetHeading("Recording one iteration");
            Debug.Log($"[AllGridManager] Recording one iteration to {capture.FinalPath}", this);
            return true;
        }
        catch (Exception exception)
        {
            Fail($"Could not start grid recording: {exception.Message}");
            return false;
        }
#else
        recordOneIteration = recordToggleState = false;
        Fail("Grid recording requires Play mode in the Unity Editor.");
        return false;
#endif
    }

    private void FinishCapture(bool complete)
    {
        if (!IsCapturing) return;
        recordOneIteration = recordToggleState = recordingPending = false;
        recordingEndFrame = -1;
#if UNITY_EDITOR
        FlockGridCapture finishedCapture = capture;
        capture = null;
        try
        {
            string path = finishedCapture.Finish(complete);
            if (path != null)
                Debug.Log($"[AllGridManager] {(complete ? "Saved complete grid iteration" : "Saved interrupted grid recording (partial)")}: {path}", this);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[AllGridManager] Could not finish grid recording: {exception.Message}", this);
        }
#endif
    }

    private void CreateContainer()
    {
        generatedRoot = new GameObject("AllGrid", typeof(RectTransform));
        RectTransform root = (RectTransform)generatedRoot.transform;
        if (gridContainer == null)
        {
            root.SetParent(transform, false);
            Canvas canvas = generatedRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            UnityEngine.UI.CanvasScaler scaler = generatedRoot.AddComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            if (Camera.allCamerasCount == 0)
            {
                GameObject cameraObject = new GameObject("Grid Background Camera");
                cameraObject.transform.SetParent(root, false);
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.cullingMask = 0;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
            }
        }
        else
        {
            root.SetParent(gridContainer, false);
            Stretch(root);
            generatedRoot.AddComponent<UnityEngine.UI.LayoutElement>().ignoreLayout = true;
        }
        UnityEngine.UI.Image background = generatedRoot.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.04f, 0.05f, 0.07f, 1);
        background.raycastTarget = false;
        heading = CreateLabel("Condition", root, 22);
        RectTransform titleRect = heading.rectTransform;
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = Vector2.one;
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.offsetMin = new Vector2(12, -48);
        titleRect.offsetMax = new Vector2(-12, -4);

        content = (RectTransform)new GameObject("Layouts", typeof(RectTransform)).transform;
        content.SetParent(root, false);
        Stretch(content);
        content.offsetMin = new Vector2(12, 12);
        content.offsetMax = new Vector2(-12, -52);
        grid = content.gameObject.AddComponent<UnityEngine.UI.GridLayoutGroup>();
        grid.startCorner = UnityEngine.UI.GridLayoutGroup.Corner.UpperLeft;
        grid.startAxis = UnityEngine.UI.GridLayoutGroup.Axis.Horizontal;
        grid.childAlignment = TextAnchor.MiddleCenter;
        grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
        lastSize = Vector2.zero;
        Canvas.ForceUpdateCanvases();
    }

    private void CreateCell(string path, int layout)
    {
        GameObject element = new GameObject($"Layout_{layout:00}", typeof(RectTransform));
        element.transform.SetParent(content, false);
        UnityEngine.UI.Image background = element.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.10f, 0.12f, 0.15f, 1);
        background.raycastTarget = false;
        Cell cell = new Cell();
        cells.Add(cell);
        cell.label = CreateLabel("Layout", element.transform, 16);
        cell.label.text = $"Layout {layout:00}";
        RectTransform labelRect = cell.label.rectTransform;
        labelRect.anchorMin = new Vector2(0, 1);
        labelRect.anchorMax = Vector2.one;
        labelRect.pivot = new Vector2(0.5f, 1);
        labelRect.offsetMin = new Vector2(2, -24);
        labelRect.offsetMax = new Vector2(-2, 0);

        GameObject area = new GameObject("VideoArea", typeof(RectTransform));
        area.transform.SetParent(element.transform, false);
        Stretch((RectTransform)area.transform);
        ((RectTransform)area.transform).offsetMax = new Vector2(0, -24);
        GameObject view = new GameObject("Video", typeof(RectTransform));
        view.transform.SetParent(area.transform, false);
        UnityEngine.UI.RawImage image = view.AddComponent<UnityEngine.UI.RawImage>();
        image.raycastTarget = false;
        UnityEngine.UI.AspectRatioFitter fitter = view.AddComponent<UnityEngine.UI.AspectRatioFitter>();
        fitter.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = (float)Mathf.Max(16, renderWidth) / Mathf.Max(16, renderHeight);

        cell.texture = new RenderTexture(Mathf.Max(16, renderWidth), Mathf.Max(16, renderHeight), 0, RenderTextureFormat.ARGB32);
        cell.texture.Create();
        image.texture = cell.texture;
        cell.player = element.AddComponent<VideoPlayer>();
        cell.player.playOnAwake = false;
        cell.player.isLooping = false;
        cell.player.source = VideoSource.Url;
        cell.player.url = new Uri(path).AbsoluteUri;
        cell.player.renderMode = VideoRenderMode.RenderTexture;
        cell.player.targetTexture = cell.texture;
        cell.player.aspectRatio = VideoAspectRatio.FitInside;
        cell.player.audioOutputMode = VideoAudioOutputMode.None;
        cell.player.waitForFirstFrame = true;
        cell.player.timeUpdateMode = VideoTimeUpdateMode.GameTime;
        cell.player.skipOnDrop = true;
        cell.player.prepareCompleted += player => cell.ready = true;
        cell.player.seekCompleted += player => cell.seeking = false;
        cell.player.loopPointReached += player =>
        {
            cell.finished = true;
            cell.label.text = $"Layout {layout:00} · ended";
        };
        cell.player.started += player => cell.label.text = $"Layout {layout:00}";
        cell.player.errorReceived += (player, message) =>
        {
            cell.label.text = $"Layout {layout:00} · error";
            cell.label.color = new Color(1, 0.45f, 0.4f);
            Fail($"Layout {layout:00}: {message}");
        };
    }

    private void UpdateLayout()
    {
        if (content == null) return;
        Vector2 size = content.rect.size;
        int cols = Mathf.Clamp(columns, 1, Mathf.Max(1, cells.Count));
        float gap = Mathf.Max(0, spacing);
        if (size == lastSize && cols == lastColumns && gap == lastSpacing) return;
        lastSize = size;
        lastColumns = cols;
        lastSpacing = gap;
        int rows = Mathf.CeilToInt((float)Mathf.Max(1, cells.Count) / cols);
        grid.constraintCount = cols;
        grid.spacing = new Vector2(gap, gap);
        grid.cellSize = new Vector2(Mathf.Max(1, (size.x - gap * (cols - 1)) / cols),
                                    Mathf.Max(25, (size.y - gap * (rows - 1)) / rows));
    }

    private void SetHeading(string status)
    {
        if (heading == null) return;

        string randomText = loadedAnyRandom ? "any randomness" : $"Random {loadedRandom:0.##}";
        heading.text = $"{loadedMotionType} · Speed {loadedSpeed:0.##} · Perception {loadedPerception:0.##} " +
                       $"· {randomText} · {cells.Count} layouts · {status}";
    }

    private void Fail(string message)
    {
        FinishCapture(false);
        failed = true;
        waiting = false;
        foreach (Cell cell in cells)
            if (cell.player != null) cell.player.Pause();
        SetHeading(message);
        Debug.LogError($"[AllGridManager] {message}", this);
    }

    private static TextMeshProUGUI CreateLabel(string name, Transform parent, float fontSize)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI label = obj.AddComponent<TextMeshProUGUI>();
        label.fontSize = fontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.raycastTarget = false;
        label.enableAutoSizing = true;
        label.fontSizeMin = 10;
        label.fontSizeMax = fontSize;
        return label;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static string ResolveFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Recording folder is empty.");
        string path = folder.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(path)) return path;
        if (path.Equals("Assets", StringComparison.OrdinalIgnoreCase)) return Application.dataPath;
        if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) path = path.Substring(7);
        return Path.Combine(Application.dataPath, path);
    }

    private void OnDisable()
    {
        ClearGrid();
        built = false;
    }

    private void OnEnable()
    {
        if (loadedFolder != null && Application.isPlaying) BuildGrid();
    }

    private void ClearGrid()
    {
        FinishCapture(false);
        waiting = false;
        foreach (Cell cell in cells)
        {
            if (cell.player != null)
            {
                cell.player.Stop();
                cell.player.targetTexture = null;
            }
            if (cell.texture != null)
            {
                cell.texture.Release();
                Destroy(cell.texture);
            }
        }
        cells.Clear();
        if (generatedRoot != null)
        {
            generatedRoot.SetActive(false);
            Destroy(generatedRoot);
        }
        content = null;
        heading = null;
        generatedRoot = null;
    }
}
