using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Video;

/// <summary>Presents motion title cards and eight-video comparisons, optionally recording the full sequence as one movie.</summary>
[DisallowMultipleComponent]
public sealed class MotionPresentationManager : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Folder containing the MP4s directly. Accepts absolute paths or paths relative to Assets.")]
    public string folderPath = "Assets/SimulationRecordings/Combinations/20261009_113950";
    [Tooltip("Recording repeat to select. Displayed as a small Layout footnote.")]
    [Min(1)] public int repeat = 1;

    [Header("Sequence")]
    public SwarmType[] motionOrder =
    {
        SwarmType.Flocking, SwarmType.DensificationV2, SwarmType.Dispersion, SwarmType.Random
    };
    [Min(0.1f)] public float titleDuration = 2f;
    public bool loop = true;
    [Min(0)] public float restartPause = 0.5f;
    [Min(1)] public float prepareTimeout = 60f;

    [Header("Recording")]
    [Tooltip("Record all four title cards and motion sections into one 30 fps QuickTime MOV with H.264 compression. Checking during playback restarts the sequence. Unity Editor only.")]
    public bool recordOneIteration;
    [Tooltip("Destination folder, absolute or relative to the project folder. Existing folders can be reused.")]
    public string outputFolderPath = "Grid Recordings/Motion Presentation";
    [Tooltip("Use an even width and height. The wide default fits two video rows without large top and bottom margins.")]
    [Min(64)] public int outputWidth = 1920;
    [Min(64)] public int outputHeight = 720;

    [Header("Appearance")]
    [Min(0)] public float cellSpacing = 12f;
    [Min(0)] public float groupSpacing = 48f;
    [Min(16)] public int renderWidth = 960;
    [Min(16)] public int renderHeight = 540;

    [Header("Progress")]
    [SerializeField] private string status = "Ready";
    [SerializeField] private string lastRecordingPath;
    public string Status => status;
    public string LastRecordingPath => lastRecordingPath;

    private enum Stage { Preparing, Title, Videos, Finishing, Complete, Failed }

    /// <summary>Owns one video decoder, texture, and end-of-clip state.</summary>
    private sealed class Cell
    {
        public VideoPlayer player;
        public RenderTexture texture;
        public bool ready;
        public bool finished;
        public bool released;
        public long lastFrame = -1;
        public float stalledFor;
    }

    /// <summary>Groups one motion's eight clips into two noise-level comparison grids.</summary>
    private sealed class Section
    {
        public GameObject root;
        public string title;
        public readonly List<Cell> cells = new List<Cell>();
        public readonly List<UnityEngine.UI.GridLayoutGroup> grids = new List<UnityEngine.UI.GridLayoutGroup>();
        public readonly List<TextMeshProUGUI> noiseLabels = new List<TextMeshProUGUI>();
        public bool preparing;
        public float preparingSince;
    }

    private readonly List<Section> sections = new List<Section>();
    private GameObject generatedRoot;
    private GameObject titlePanel;
    private TextMeshProUGUI title;
    private TextMeshProUGUI footer;
    private Stage stage;
    private int sectionIndex;
    private float elapsed;
    private float preparingSince;
    private int finalFrame;
    private bool built;
    private bool previousRunInBackground;
    private bool paused;
    private bool recordToggleState;
    private bool recordingPending;
    private string recordingFolder;
    private string loadedFolder;
    private int loadedRepeat;
    private int loadedWidth;
    private int loadedHeight;
    private SwarmType[] loadedOrder;
#if UNITY_EDITOR
    private MotionPresentationCapture capture;
    private uint previousPreviewWidth;
    private uint previousPreviewHeight;
    private bool previewSizeOverridden;
#endif

    private void OnEnable()
    {
        previousRunInBackground = Application.runInBackground;
        Application.runInBackground = true;
    }

    private void Start() => BuildPresentation();

    private void Update()
    {
        if (!built || loadedFolder != folderPath || loadedRepeat != repeat ||
            loadedWidth != outputWidth || loadedHeight != outputHeight ||
            !(loadedOrder ?? Array.Empty<SwarmType>()).SequenceEqual(motionOrder ?? Array.Empty<SwarmType>()))
        {
            BuildPresentation();
            return;
        }
        if (recordOneIteration != recordToggleState)
        {
            recordToggleState = recordOneIteration;
            if (recordOneIteration)
            {
                BuildPresentation();
                return;
            }
            recordingPending = false;
            FinishCapture(false);
        }
        if (paused || stage == Stage.Failed) return;
        switch (stage)
        {
            case Stage.Preparing:
                if (sections[0].cells.All(cell => cell.ready))
                    ShowTitle(0);
                else if (Time.realtimeSinceStartup - preparingSince > Mathf.Max(1, prepareTimeout))
                    Fail("Timed out preparing videos. Check the Console and reload the presentation.");
                break;
            case Stage.Title:
                if (recordingPending)
                {
                    BeginCapture();
                    return;
                }
                Section current = sections[sectionIndex];
                PrepareSection(sectionIndex);
                if (!current.cells.All(cell => cell.ready))
                {
                    if (Time.realtimeSinceStartup - current.preparingSince > Mathf.Max(1, prepareTimeout))
                        Fail($"Timed out preparing {current.title}. Check the Console and reload the presentation.");
                    return;
                }
                elapsed += Time.unscaledDeltaTime;
                if (elapsed >= Mathf.Max(0.1f, titleDuration)) ShowVideos();
                break;
            case Stage.Videos:
                bool allFinished = true;
                foreach (Cell cell in sections[sectionIndex].cells)
                {
                    if (HasFinished(cell)) continue;
                    allFinished = false;
                    if (cell.player.frame != cell.lastFrame)
                    {
                        cell.lastFrame = cell.player.frame;
                        cell.stalledFor = 0;
                    }
                    else cell.stalledFor += Time.unscaledDeltaTime;
                    // Native capture can leave a decoder paused even though the presentation is running.
                    if (cell.player.isPaused && cell.stalledFor >= 0.5f)
                    {
                        cell.player.Play();
                        cell.stalledFor = 0;
                    }
                }
                if (!allFinished) return;
                if (sectionIndex + 1 < sections.Count)
                {
                    ShowTitle(sectionIndex + 1);
                }
                else
                {
                    stage = Stage.Finishing;
                    // Let the final composite reach the recorder before closing its encoder.
                    finalFrame = Time.frameCount + 2;
                }
                break;
            case Stage.Finishing:
                if (Time.frameCount < finalFrame) return;
                FinishCapture(true);
                if (stage == Stage.Failed) return;
                stage = Stage.Complete;
                elapsed = 0;
                status = string.IsNullOrEmpty(lastRecordingPath) ? "Sequence complete" : $"Saved: {lastRecordingPath}";
                break;
            case Stage.Complete:
                elapsed += Time.unscaledDeltaTime;
                if (loop && elapsed >= Mathf.Max(0, restartPause)) BuildPresentation();
                break;
        }
    }

    private void LateUpdate()
    {
        float gap = Mathf.Max(0, cellSpacing);
        foreach (Section section in sections)
        for (int group = 0; group < section.grids.Count; group++)
        {
            UnityEngine.UI.GridLayoutGroup grid = section.grids[group];
            RectTransform panel = (RectTransform)grid.transform.parent;
            panel.offsetMin = new Vector2(group == 0 ? 0 : Mathf.Max(0, groupSpacing) / 2, 0);
            panel.offsetMax = new Vector2(group == 0 ? -Mathf.Max(0, groupSpacing) / 2 : 0, -56);
            Vector2 size = panel.rect.size;
            float width = Mathf.Max(1, (size.x - gap) / 2);
            float height = Mathf.Max(32, Mathf.Min((size.y - 48 - gap) / 2,
                32 + width * Mathf.Max(16, renderHeight) / Mathf.Max(16, renderWidth)));
            float gridHeight = height * 2 + gap;
            RectTransform gridRect = (RectTransform)grid.transform;
            gridRect.anchorMin = new Vector2(0, 0.5f);
            gridRect.anchorMax = new Vector2(1, 0.5f);
            gridRect.offsetMin = new Vector2(0, -gridHeight / 2 - 24);
            gridRect.offsetMax = new Vector2(0, gridHeight / 2 - 24);
            RectTransform noiseRect = section.noiseLabels[group].rectTransform;
            noiseRect.anchorMin = new Vector2(0, 0.5f);
            noiseRect.anchorMax = new Vector2(1, 0.5f);
            noiseRect.offsetMin = new Vector2(0, gridHeight / 2 - 16);
            noiseRect.offsetMax = new Vector2(0, gridHeight / 2 + 24);
            grid.spacing = new Vector2(gap, gap);
            grid.cellSize = new Vector2(width, height);
        }
    }

    [ContextMenu("Build / Reload Presentation")]
    public void BuildPresentation()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        bool requested = recordOneIteration;
        ClearPresentation();
        recordOneIteration = recordToggleState = requested;
        recordingPending = requested;
        if (requested) lastRecordingPath = "";
        loadedFolder = folderPath;
        loadedRepeat = repeat;
        loadedWidth = outputWidth;
        loadedHeight = outputHeight;
        loadedOrder = motionOrder == null ? null : (SwarmType[])motionOrder.Clone();
        built = true;
        paused = false;
        stage = Stage.Preparing;
        CreateCanvas();
        try
        {
            if (outputWidth < 64 || outputHeight < 64 || outputWidth % 2 != 0 || outputHeight % 2 != 0)
                throw new ArgumentException("Output Width and Height must be even numbers of at least 64 pixels.");
#if UNITY_EDITOR
            if (!previewSizeOverridden)
            {
                UnityEditor.PlayModeWindow.GetRenderingResolution(out previousPreviewWidth, out previousPreviewHeight);
                previewSizeOverridden = true;
            }
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution((uint)outputWidth, (uint)outputHeight, "Motion Presentation");
#endif
            string[] order = motionOrder == null ? null : Array.ConvertAll(motionOrder, motion => motion.ToString());
            AllRecordingCatalog.Grid selection = MotionCombinationCatalog.Select(ResolveSource(folderPath), repeat, order);
            if (requested)
            {
                if (string.IsNullOrWhiteSpace(outputFolderPath)) throw new ArgumentException("Output Folder Path is empty.");
                string path = outputFolderPath.Trim().Replace('\\', '/');
                recordingFolder = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Application.dataPath, "..", path));
            }
            for (int i = 0; i < MotionCombinationCatalog.MotionCount; i++)
                CreateSection(selection.clips.Skip(i * MotionCombinationCatalog.CombinationsPerMotion)
                    .Take(MotionCombinationCatalog.CombinationsPerMotion).ToArray(), order[i]);
            footer.text = $"Layout {repeat:00}";
            preparingSince = Time.realtimeSinceStartup;
            title.text = "Preparing videos…";
            status = "Preparing the first 8 videos";
            PrepareSection(0);
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    [ContextMenu("Record One Iteration")]
    public void RecordOneIteration()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        recordOneIteration = true;
        BuildPresentation();
    }

    [ContextMenu("Pause Presentation")]
    public void PausePresentation()
    {
        paused = true;
        foreach (Section section in sections)
        foreach (Cell cell in section.cells) if (cell.player != null) cell.player.Pause();
    }

    [ContextMenu("Resume Presentation")]
    public void ResumePresentation()
    {
        paused = false;
        if (stage != Stage.Videos || sections.Count == 0) return;
        foreach (Cell cell in sections[sectionIndex].cells)
            if (!cell.finished) cell.player.Play();
    }

    private void ShowTitle(int index)
    {
        if (index > 0)
        {
#if UNITY_EDITOR
            if (capture != null)
            {
                try
                {
                    capture.FinishSection();
                    recordingPending = true;
                }
                catch (Exception exception)
                {
                    Fail($"Could not finish the motion section: {exception.Message}");
                    return;
                }
            }
#endif
            // Prepare only one section at a time; idle preloaded decoders also consume native resources.
            foreach (Cell cell in sections[index - 1].cells) ReleaseCell(cell);
        }
        sectionIndex = index;
        foreach (Section section in sections) section.root.SetActive(false);
        footer.gameObject.SetActive(false);
        titlePanel.SetActive(true);
        title.text = sections[index].title.ToUpperInvariant();
        elapsed = 0;
        stage = Stage.Title;
        status = $"Title: {sections[index].title}";
    }

    private static bool HasFinished(Cell cell)
    {
        if (cell.finished) return true;
        if (cell.released) return false;
        VideoPlayer player = cell.player;
        if (!cell.ready || player.isPlaying || player.frame < 0 || player.frameCount == 0) return false;
        // Some native decoders stop one frame short and omit loopPointReached. Require a decoded end frame.
        long lastFrame = (long)player.frameCount - 1;
        if (player.frame < Math.Max(0, lastFrame - 1)) return false;
        cell.finished = true;
        return true;
    }

    private void PrepareSection(int index)
    {
        if (index >= sections.Count || sections[index].preparing) return;
        Section section = sections[index];
        section.preparing = true;
        section.preparingSince = Time.realtimeSinceStartup;
        try
        {
            foreach (Cell cell in section.cells) cell.player.Prepare();
        }
        catch (Exception exception)
        {
            Fail($"Could not prepare {section.title}: {exception.Message}");
        }
    }

    private void ShowVideos()
    {
        titlePanel.SetActive(false);
        footer.gameObject.SetActive(true);
        Section section = sections[sectionIndex];
        section.root.SetActive(true);
        foreach (Cell cell in section.cells) cell.player.Play();
        stage = Stage.Videos;
        status = $"Playing {section.title} · 8 videos";
    }

    private void CreateCanvas()
    {
        generatedRoot = new GameObject("Motion Presentation", typeof(RectTransform), typeof(Canvas));
        generatedRoot.transform.SetParent(transform, false);
        Canvas canvas = generatedRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = generatedRoot.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(Mathf.Max(64, outputWidth), Mathf.Max(64, outputHeight));
        scaler.matchWidthOrHeight = 0.5f;
        var background = generatedRoot.AddComponent<UnityEngine.UI.Image>();
        background.color = new Color(0.035f, 0.045f, 0.06f, 1);
        background.raycastTarget = false;
        if (Camera.allCamerasCount == 0)
        {
            var cameraObject = new GameObject("Presentation Background Camera", typeof(Camera));
            cameraObject.transform.SetParent(generatedRoot.transform, false);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.cullingMask = 0;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
        }
        footer = CreateLabel("Layout Footnote", generatedRoot.transform, 16);
        footer.color = new Color(0.65f, 0.69f, 0.74f);
        footer.rectTransform.anchorMax = new Vector2(1, 0);
        footer.rectTransform.offsetMin = new Vector2(24, 8);
        footer.rectTransform.offsetMax = new Vector2(-24, 36);
        footer.gameObject.SetActive(false);
        titlePanel = new GameObject("Title Card", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        titlePanel.transform.SetParent(generatedRoot.transform, false);
        Stretch((RectTransform)titlePanel.transform);
        var titleBackground = titlePanel.GetComponent<UnityEngine.UI.Image>();
        titleBackground.color = background.color;
        titleBackground.raycastTarget = false;
        title = CreateLabel("Motion Title", titlePanel.transform, 84);
        title.fontSizeMin = 18;
        title.fontStyle = FontStyles.Bold;
        title.rectTransform.offsetMin = new Vector2(80, 80);
        title.rectTransform.offsetMax = new Vector2(-80, -80);
    }

    private void CreateSection(AllRecordingCatalog.Clip[] clips, string motion)
    {
        var section = new Section { title = MotionCombinationCatalog.MotionLabel(motion) };
        sections.Add(section);
        section.root = new GameObject(section.title, typeof(RectTransform));
        section.root.transform.SetParent(generatedRoot.transform, false);
        RectTransform rect = (RectTransform)section.root.transform;
        Stretch(rect);
        rect.offsetMin = new Vector2(24, 52);
        rect.offsetMax = new Vector2(-24, -24);
        TextMeshProUGUI heading = CreateLabel("Motion Heading", rect, 36);
        heading.text = section.title.ToUpperInvariant();
        heading.fontStyle = FontStyles.Bold;
        heading.rectTransform.anchorMin = new Vector2(0, 1);
        heading.rectTransform.offsetMin = new Vector2(0, -48);
        heading.rectTransform.offsetMax = Vector2.zero;
        for (int groupIndex = 0; groupIndex < 2; groupIndex++)
        {
            var panel = new GameObject($"Noise Group {groupIndex + 1}", typeof(RectTransform));
            panel.transform.SetParent(rect, false);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = new Vector2(groupIndex * 0.5f, 0);
            panelRect.anchorMax = new Vector2((groupIndex + 1) * 0.5f, 1);
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = new Vector2(0, -56);
            TextMeshProUGUI noise = CreateLabel("Noise", panel.transform, 26);
            section.noiseLabels.Add(noise);
            noise.text = MotionCombinationCatalog.NoiseLabel(clips[groupIndex * 4]) == "0"
                ? "Noise Absent" : "Noise Present";
            noise.rectTransform.anchorMin = new Vector2(0, 1);
            noise.rectTransform.offsetMin = new Vector2(0, -40);
            noise.rectTransform.offsetMax = Vector2.zero;
            var content = new GameObject("2x2 Grid", typeof(RectTransform));
            content.transform.SetParent(panel.transform, false);
            Stretch((RectTransform)content.transform);
            ((RectTransform)content.transform).offsetMax = new Vector2(0, -48);
            var grid = content.AddComponent<UnityEngine.UI.GridLayoutGroup>();
            grid.constraint = UnityEngine.UI.GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 2;
            grid.startCorner = UnityEngine.UI.GridLayoutGroup.Corner.UpperLeft;
            grid.startAxis = UnityEngine.UI.GridLayoutGroup.Axis.Horizontal;
            grid.childAlignment = TextAnchor.MiddleCenter;
            section.grids.Add(grid);
            for (int i = 0; i < 4; i++) CreateCell(section, content.transform, clips[groupIndex * 4 + i]);
        }
        section.root.SetActive(false);
    }

    private void CreateCell(Section section, Transform parent, AllRecordingCatalog.Clip clip)
    {
        string perception = clip.perception == 1.5 ? "(Small)" : clip.perception == 4 ? "(Large)"
            : clip.perception.ToString("0.##", CultureInfo.InvariantCulture);
        string speed = clip.maxSpeed == 1.5 ? "(Slow)" : clip.maxSpeed == 3 ? "(Fast)"
            : clip.maxSpeed.ToString("0.##", CultureInfo.InvariantCulture);
        string label = $"Perception Radius {perception} · Speed {speed}";
        var element = new GameObject("Combination", typeof(RectTransform), typeof(UnityEngine.UI.Image));
        element.transform.SetParent(parent, false);
        element.GetComponent<UnityEngine.UI.Image>().color = new Color(0.075f, 0.09f, 0.115f, 1);
        element.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
        TextMeshProUGUI text = CreateLabel("Parameters", element.transform, 20);
        text.text = label;
        text.rectTransform.anchorMin = new Vector2(0, 1);
        text.rectTransform.offsetMin = new Vector2(4, -32);
        text.rectTransform.offsetMax = new Vector2(-4, 0);
        var area = new GameObject("Video Area", typeof(RectTransform));
        area.transform.SetParent(element.transform, false);
        Stretch((RectTransform)area.transform);
        ((RectTransform)area.transform).offsetMax = new Vector2(0, -32);
        var view = new GameObject("Video", typeof(RectTransform), typeof(UnityEngine.UI.RawImage), typeof(UnityEngine.UI.AspectRatioFitter));
        view.transform.SetParent(area.transform, false);
        var image = view.GetComponent<UnityEngine.UI.RawImage>();
        image.raycastTarget = false;
        var fitter = view.GetComponent<UnityEngine.UI.AspectRatioFitter>();
        fitter.aspectMode = UnityEngine.UI.AspectRatioFitter.AspectMode.FitInParent;
        fitter.aspectRatio = (float)Mathf.Max(16, renderWidth) / Mathf.Max(16, renderHeight);
        var cell = new Cell();
        section.cells.Add(cell);
        cell.texture = new RenderTexture(Mathf.Max(16, renderWidth), Mathf.Max(16, renderHeight), 0, RenderTextureFormat.ARGB32);
        cell.texture.Create();
        image.texture = cell.texture;
        // Keep decoders independent of hidden UI so the next section can prepare during current playback.
        var decoder = new GameObject($"{section.title} Decoder {section.cells.Count}", typeof(VideoPlayer));
        decoder.transform.SetParent(generatedRoot.transform, false);
        cell.player = decoder.GetComponent<VideoPlayer>();
        cell.player.playOnAwake = false;
        cell.player.isLooping = false;
        cell.player.source = VideoSource.Url;
        cell.player.url = new Uri(clip.path).AbsoluteUri;
        cell.player.renderMode = VideoRenderMode.RenderTexture;
        cell.player.targetTexture = cell.texture;
        cell.player.aspectRatio = VideoAspectRatio.FitInside;
        cell.player.audioOutputMode = VideoAudioOutputMode.None;
        cell.player.waitForFirstFrame = true;
        cell.player.timeUpdateMode = VideoTimeUpdateMode.GameTime;
        cell.player.skipOnDrop = true;
        cell.player.prepareCompleted += player => cell.ready = true;
        cell.player.loopPointReached += player => HasFinished(cell);
        cell.player.errorReceived += (player, message) =>
        {
            if (!cell.released) Fail($"{section.title}, {label}: {message}");
        };
    }

    private void BeginCapture()
    {
        recordingPending = false;
#if UNITY_EDITOR
        try
        {
            if (capture == null)
                capture = new MotionPresentationCapture(recordingFolder, loadedRepeat, outputWidth, outputHeight);
            capture.StartSection();
            status = "Recording the full motion sequence";
        }
        catch (Exception exception)
        {
            Fail($"Could not start recording: {exception.Message}");
        }
#else
        Fail("Recording requires Play mode in the Unity Editor.");
#endif
    }

    private void FinishCapture(bool complete)
    {
#if UNITY_EDITOR
        if (capture == null) return;
        MotionPresentationCapture finished = capture;
        capture = null;
        recordOneIteration = recordToggleState = recordingPending = false;
        try
        {
            string path = finished.Finish(complete);
            if (complete) lastRecordingPath = path;
            if (!string.IsNullOrEmpty(path)) Debug.Log($"[MotionPresentationManager] Saved {(complete ? "complete" : "partial")} recording: {path}", this);
        }
        catch (Exception exception)
        {
            Fail($"Could not finish recording: {exception.Message}");
        }
#endif
    }

    private void Fail(string message)
    {
        stage = Stage.Failed;
        recordingPending = recordOneIteration = recordToggleState = false;
        FinishCapture(false);
        foreach (Section section in sections)
        {
            section.root.SetActive(false);
            foreach (Cell cell in section.cells) if (cell.player != null) cell.player.Pause();
        }
        footer.gameObject.SetActive(false);
        titlePanel.SetActive(true);
        title.text = message;
        status = message;
        Debug.LogError($"[MotionPresentationManager] {message}", this);
    }

    private static TextMeshProUGUI CreateLabel(string name, Transform parent, float size)
    {
        var obj = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        obj.transform.SetParent(parent, false);
        var label = obj.GetComponent<TextMeshProUGUI>();
        label.alignment = TextAlignmentOptions.Center;
        label.color = Color.white;
        label.fontSize = label.fontSizeMax = size;
        label.fontSizeMin = 12;
        label.enableAutoSizing = true;
        label.raycastTarget = false;
        Stretch(label.rectTransform);
        return label;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    private static string ResolveSource(string folder)
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
        ClearPresentation();
        Application.runInBackground = previousRunInBackground;
#if UNITY_EDITOR
        if (previewSizeOverridden)
        {
            UnityEditor.PlayModeWindow.SetCustomRenderingResolution(previousPreviewWidth, previousPreviewHeight, "Recording Resolution");
            previewSizeOverridden = false;
        }
#endif
        built = false;
    }

    private void ClearPresentation()
    {
        FinishCapture(false);
        foreach (Section section in sections)
        foreach (Cell cell in section.cells)
            ReleaseCell(cell);
        sections.Clear();
        if (generatedRoot != null)
        {
            generatedRoot.SetActive(false);
            Destroy(generatedRoot);
        }
        generatedRoot = null;
    }

    private static void ReleaseCell(Cell cell)
    {
        cell.released = true;
        if (cell.player != null)
        {
            cell.player.Stop();
            cell.player.targetTexture = null;
            Destroy(cell.player.gameObject);
            cell.player = null;
        }
        if (cell.texture != null)
        {
            cell.texture.Release();
            Destroy(cell.texture);
            cell.texture = null;
        }
    }
}
