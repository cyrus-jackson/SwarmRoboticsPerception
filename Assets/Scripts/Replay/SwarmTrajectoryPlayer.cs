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

    [Header("Reference dispersion (the same layout, without randomness)")]
    [Tooltip("When replaying a run that had random movement, look up the zero-randomness run recorded from the SAME spawn layout at the same perception radius and max speed, and show the hull area it settled at. Gives the replay a target to be judged against.")]
    public bool showReferenceHull = true;

    [Tooltip("Folder of recordings to take reference runs from, relative to the project root. Scanned recursively; only the zero-randomness runs are indexed.")]
    public string referenceHullFolder = "Assets/SimulationRecordings/MatchedStart_PerceptionRad";

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
        if (referenceHullLine != null) Destroy(referenceHullLine.gameObject);
        hullLine = null;
        baselineHullLine = null;
        referenceHullLine = null;
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
        referenceOutline.Clear();
        referenceHullAtPlateau.Clear();
        referenceCrossFrame = -1;
        referenceCrossTime = 0f;
        referenceStatus = "";

        if (!showReferenceHull || trajectory == null) return;

        TrajectoryHeader h = trajectory.header;

        if (Mathf.Abs(h.randomMovement) <= 0.001f)
        {
            referenceStatus = "this run IS the reference";
            return;
        }

        if (string.IsNullOrEmpty(h.spawnLayoutId))
        {
            referenceStatus = "no spawn layout recorded, nothing to match";
            return;
        }

        ReferenceHullIndex index = EnsureReferenceIndex();
        if (index == null || index.Count == 0)
        {
            referenceStatus = "no reference recordings indexed";
            return;
        }

        if (!index.TryGet(h.spawnLayoutId, h.perceptionRadius, h.maxSpeed,
                          out ReferenceHullIndex.Entry entry))
        {
            referenceStatus = $"none for R {h.perceptionRadius:F2}, maxSpeed {h.maxSpeed:F2}";
            return;
        }

        referenceEntry = entry;
        LoadReferenceHullOutline();
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

    private ReferenceHullIndex EnsureReferenceIndex()
    {
        if (referenceIndex != null && !rebuildReferenceIndex) return referenceIndex;

        referenceIndex = ReferenceHullIndex.Build(ResolveProjectPath(referenceHullFolder),
                                                  useCache: !rebuildReferenceIndex);
        rebuildReferenceIndex = false;
        return referenceIndex;
    }

    /// <summary>
    /// Project-relative path to absolute. Application.dataPath already ends in "Assets", so a path
    /// beginning "Assets/" would otherwise resolve to "Assets/Assets/...".
    /// </summary>
    private static string ResolveProjectPath(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;

        string trimmed = path.Trim().Replace('\\', '/').TrimEnd('/');
        if (Path.IsPathRooted(trimmed)) return trimmed;
        if (trimmed.Equals("Assets", System.StringComparison.OrdinalIgnoreCase)) return Application.dataPath;
        if (trimmed.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(7);

        return Path.Combine(Application.dataPath, trimmed);
    }

    /// <summary>
    /// Reads the reference's own hull at its plateau, so the scene can show the actual shape the
    /// swarm settled into rather than a stand-in for its area.
    ///
    /// Loading the whole reference recording for one frame is heavy, but it happens once when a
    /// recording is opened, and only the resulting outline is kept.
    /// </summary>
    private void LoadReferenceHullOutline()
    {
        referenceHullAtPlateau.Clear();
        referenceHullCentre = Vector2.zero;

        if (referenceEntry == null || string.IsNullOrEmpty(referenceEntry.sourcePath)) return;
        if (!File.Exists(referenceEntry.sourcePath)) return;

        SwarmTrajectory reference = SwarmTrajectory.Load(referenceEntry.sourcePath);
        if (reference == null || reference.FrameCount == 0) return;

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
}
