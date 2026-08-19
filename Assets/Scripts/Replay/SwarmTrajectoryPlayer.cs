using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// In-scene replay of a run captured by SwarmTrajectoryRecorder. Lives on the same object as UI in
/// the simulation scene and takes over when the toggle key is pressed, so nothing has to be
/// reassigned: the agent prefab, swarm manager, goal areas and walls all come from the UI.
///
/// While replaying, the live simulation is paused and its agents hidden, and a separate set of
/// replay agents is driven straight from the file. Poses are read, never simulated, so scrubbing
/// backwards costs exactly what scrubbing forwards does. Leaving replay restores everything.
/// </summary>
[RequireComponent(typeof(UI))]
public class SwarmTrajectoryPlayer : MonoBehaviour
{
    // Found at runtime from the same GameObject, so there is nothing to assign in the Inspector.
    private UI uiController;
    private SwarmManager swarmManager;

    [Header("Source")]
    [Tooltip("Folder to list recordings from. Absolute, or relative to Assets, with or without a leading Assets/.")]
    public string recordingsFolder = "SimulationRecordings/Trajectories";

    [Header("Toggle")]
    [Tooltip("Enters and leaves replay mode.")]
    public Key toggleKey = Key.R;

    [Header("Playback")]
    public bool playOnLoad = true;
    public bool loop = true;
    [Range(0.1f, 4f)] public float speed = 1f;
    [Tooltip("Interpolate between captured frames. Off shows exactly the captured poses.")]
    public bool interpolate = true;
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

    void Awake()
    {
        ResolveWiring();
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

        if (!IsReplaying || trajectory == null || trajectory.FrameCount == 0) return;

        HandleScrubInput();

        if (isPlaying)
        {
            playhead += Time.deltaTime * speed;

            if (playhead >= trajectory.Duration)
            {
                if (loop) playhead = 0f;
                else { playhead = trajectory.Duration; isPlaying = false; }
            }
        }

        ApplyPlayhead();
    }

    // ------------------------------------------------------------- mode switching

    public void EnterReplay()
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

        uiController.SetMotion(false);
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

        if (availableFiles.Length > 0 && trajectory == null) LoadByIndex(0);
        else if (trajectory != null) { BuildAgents(); ApplyRecordedSceneState(); ApplyPlayhead(); }

        Debug.Log("[Replay] Entered replay mode.");
    }

    public void ExitReplay()
    {
        if (!IsReplaying) return;

        DestroyReplayAgents();
        RestoreLiveAgents();

        if (uiController != null)
        {
            uiController.showUI = savedShowUI;

            if (matchRecordedSceneState)
            {
                // Puts the live goal area and every wall back the way the panel has them.
                uiController.EnableAllWalls();
                uiController.ApplySettingsToManager();
            }
        }

        if (swarmManager != null)
        {
            if (swarmManager.densityMonitor != null) swarmManager.densityMonitor.enabled = savedDensityMonitorEnabled;
            swarmManager.enabled = savedManagerEnabled;
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
        SwarmTrajectory loaded = SwarmTrajectory.Load(path);
        if (loaded == null) { status = $"Failed to load {Path.GetFileName(path)}"; return; }

        if (loaded.FrameCount == 0 || loaded.AgentCount == 0)
        {
            status = $"{Path.GetFileNameWithoutExtension(path)} is not a trajectory file";
            return;
        }

        trajectory = loaded;
        BuildAgents();
        ApplyRecordedSceneState();
        ComputeBaselineHull();

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
        return !name.EndsWith("_config", System.StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------- agents

    private void BuildAgents()
    {
        DestroyReplayAgents();

        if (trajectory == null) return;

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
            container.SetParent(transform, false);
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

        EnsureHullRenderers();
    }

    private void DestroyReplayAgents()
    {
        foreach (Transform t in spawned)
        {
            if (t != null) Destroy(t.gameObject);
        }
        spawned.Clear();
        trails.Clear();

        if (hullLine != null) Destroy(hullLine.gameObject);
        if (baselineHullLine != null) Destroy(baselineHullLine.gameObject);
        hullLine = null;
        baselineHullLine = null;
    }

    /// <summary>Removes anything that would move or collide, so the replay is purely the data.</summary>
    private void Strip(GameObject agent)
    {
        SwarmAgent sim = agent.GetComponent<SwarmAgent>();
        if (sim != null) Destroy(sim);

        PerceptionVisualizer existing = agent.GetComponent<PerceptionVisualizer>();
        if (existing != null) Destroy(existing);

        Rigidbody2D body = agent.GetComponent<Rigidbody2D>();
        if (body != null) Destroy(body);

        foreach (Collider2D c in agent.GetComponents<Collider2D>()) Destroy(c);
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
        if (Keyboard.current.endKey.wasPressedThisFrame) SeekToFrame(trajectory.FrameCount - 1);
    }

    private void ApplyPlayhead()
    {
        if (trajectory == null || trajectory.FrameCount == 0 || spawned.Count == 0) return;

        currentFrame = trajectory.FrameIndexAtTime(playhead);

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

            t.position = new Vector3(position.x, position.y, t.position.z);
            if (applyRotation) t.rotation = Quaternion.AngleAxis(rotation, Vector3.forward);

            positionBuffer.Add(position);
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

        EnsureHullRenderers();
        DrawPolygon(hullLine, showHull ? hullBuffer : null, hullColor);
        DrawPolygon(baselineHullLine, showHull ? baselineHull : null, baselineHullColor);
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

    public void Play() { isPlaying = true; }
    public void Pause() { isPlaying = false; }
    public void TogglePlay() { isPlaying = !isPlaying; }

    public void SeekToFrame(int frame)
    {
        if (trajectory == null || trajectory.FrameCount == 0) return;
        playhead = trajectory.frames[Mathf.Clamp(frame, 0, trajectory.FrameCount - 1)].t;
        ApplyPlayhead();
    }

    public void StepFrames(int delta)
    {
        isPlaying = false;
        SeekToFrame(currentFrame + delta);
    }

    // ------------------------------------------------------------- panel

    private GUIStyle panelStyle, titleStyle, sectionStyle, labelStyle, valueStyle,
                     mutedStyle, buttonStyle, toggleStyle, separatorStyle, statusStyle;
    private Texture2D panelTexture, separatorTexture;

    private static readonly Color Ink = new Color(0.93f, 0.94f, 0.96f);
    private static readonly Color Muted = new Color(0.62f, 0.66f, 0.72f);
    private static readonly Color Accent = new Color(0.42f, 0.78f, 1f);
    private static readonly Color Warn = new Color(1f, 0.48f, 0.42f);

    private const float PanelWidth = 400f;
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

        float height = Mathf.Min(Screen.height - 24f, 760f);
        GUILayout.BeginArea(new Rect(14, 12, PanelWidth, height), GUIContent.none, panelStyle);
        panelScroll = GUILayout.BeginScrollView(panelScroll, GUIStyle.none, GUI.skin.verticalScrollbar);

        GUILayout.Label($"<color=#6bc6ff>REPLAY</color>  <size=11><color=#9aa4b0>press {toggleKey} for live</color></size>", titleStyle);
        GUILayout.Label(status, statusStyle);

        if (trajectory != null && trajectory.FrameCount > 0)
        {
            DrawTransport();
            DrawRecordedParameters();
            DrawHullReadout();
        }

        DrawFileList();

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
        if (GUILayout.Button(">|", buttonStyle, GUILayout.Width(40))) SeekToFrame(trajectory.FrameCount - 1);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(4);
        Row("Frame", $"{currentFrame} / {trajectory.FrameCount - 1}");
        Row("Time", $"{playhead:F2} s  of  {trajectory.Duration:F2} s");

        GUILayout.Space(2);
        float scrubbed = GUILayout.HorizontalSlider(currentFrame, 0, trajectory.FrameCount - 1);
        if (Mathf.RoundToInt(scrubbed) != currentFrame)
        {
            isPlaying = false;
            SeekToFrame(Mathf.RoundToInt(scrubbed));
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
        RowPair("Max speed", $"{h.maxSpeed:F2}", "Sim step", $"{h.simulationStep:F4}");

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
}
