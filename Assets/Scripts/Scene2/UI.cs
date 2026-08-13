using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public enum SwarmType
{
    Flocking,
    Densification,
    Random,
    Dispersion
}

public enum AgentSpawnType
{
    Grid,
    Random,
    Spiral,
    Chain
}

public class UI : MonoBehaviour
{
    [Header("Swarm Setup")]
    public GameObject agentPrefab;
    public SwarmManager swarmManager;

    [Tooltip("List of possible spawn areas in the scene")]
    public Transform[] spawnAreas;

    [Header("Obstacle Setup")]
    [Tooltip("Obstacles in the scene. Only element 0 is treated as the default obstacle.")]
    public List<Transform> obstacles = new List<Transform>();

    [Header("Obstacle Spawn Setup")]
    [Tooltip("Possible spawn locations for the default obstacle.")]
    public List<Transform> obstacleSpawnLocations = new List<Transform>();

    [Tooltip("List of possible common fates in the scene")]
    public Transform[] commonFates;

    [Header("Dispersion Setup")]
    [Tooltip("The circular spawn area for Dispersion type")]
    public Transform circleSpawnArea;

    [Header("Densification Setup")]
    [Tooltip("The spawn area specifically for Densification type")]
    public Transform densificationSpawnArea;
    [Tooltip("The common fate specifically for Densification type")]
    public Transform densificationCommonFate;

    [Header("Flocking Setup")]
    [Tooltip("The spawn area specifically for Flocking type")]
    public Transform flockingSpawnArea;
    [Tooltip("The common fate specifically for Flocking type")]
    public Transform flockingCommonFate;

    [Header("Density Rule")]
    [Tooltip("Density monitor used by the density end condition. Added to the SwarmManager object automatically if left empty.")]
    public SwarmDensityMonitor densityMonitor;
    [Tooltip("Target ratio shown and tuned in the play mode panel. Below 1 means contract to that fraction of the starting spread, above 1 means expand to that multiple.")]
    public float densityTargetRatio = 0.4f;
    [Tooltip("Show the live density readout in the panel while playing outside of a recording.")]
    public bool showDensityReadout = true;

    [Header("Wall Setup")]
    [Tooltip("Walls in the scene. Recording rules can disable individual walls by name per motion type.")]
    public List<Transform> walls = new List<Transform>();
    [Tooltip("Optional parent (e.g. the 'Walls' object). Its children are added to the wall list on Start.")]
    public Transform wallsRoot;

    [Header("Goal Area Setup (per motion type)")]
    [Tooltip("Target region for Flocking. Agents inside are counted; can end a recording early.")]
    public Transform flockingGoalArea;
    [Tooltip("Target region for Densification.")]
    public Transform densificationGoalArea;
    [Tooltip("Target region for Random.")]
    public Transform randomGoalArea;
    [Tooltip("Target region for Dispersion.")]
    public Transform dispersionGoalArea;
    [Tooltip("Hide the inactive goal areas so only the current type's area is visible in the recording.")]
    public bool hideInactiveGoalAreas = true;

    public bool showUI = true;
    private bool isRunning = false;
    List<GameObject> agentsInRange = new List<GameObject>();

    private int uiNumberOfAgents = 40;
    private List<GameObject> activeAgents = new List<GameObject>();

    // UI Configuration values
    private SwarmType selectedSwarmType = SwarmType.Densification;
    private AgentSpawnType selectedSpawnType = AgentSpawnType.Grid;
    private int selectedSpawnAreaIndex = 0;
    private int selectedCommonFateIndex = 0;

    private float uiCohesion = 5.0f;
    private float uiSeparation = 1.0f;
    private float uiAlignment = 2.0f;
    private float uiFriction = 0.9f;
    private float uiRandomMvmt = 0.0f;
    private float uiNeighbourSpread = 1.0f;

    private float uiOverlapAvoid = 20.0f;
    private float uiSafetyDist = 0.2f;
    private float uiEnvAvoid = 20.0f;

    private float uiPerceptionRad = 0.2f;
    private float uiObstacleRad = 0.1f;
    private float uiMaxSpeed = 1.5f;
    private bool uiShowPerceptionRadius = false;
    private bool uiShowDensityArea = false;
    private Vector2 scrollPosition;
    private Texture2D bgTexture;

    void Start()
    {
        if (swarmManager != null)
            swarmManager.enabled = false;

        CollectWallsFromRoot();
        EnsureDensityMonitor();
        ApplyPreset(selectedSwarmType);
        ResetScene();
    }

    /// <summary>Finds or adds the density monitor on the SwarmManager object and links it up.</summary>
    private void EnsureDensityMonitor()
    {
        if (swarmManager == null) return;

        if (densityMonitor == null)
        {
            densityMonitor = swarmManager.GetComponent<SwarmDensityMonitor>();
            if (densityMonitor == null)
            {
                densityMonitor = swarmManager.gameObject.AddComponent<SwarmDensityMonitor>();
            }
        }

        densityMonitor.swarmManager = swarmManager;
        swarmManager.densityMonitor = densityMonitor;
    }

    /// <summary>Adds the children of wallsRoot to the wall list, skipping duplicates.</summary>
    private void CollectWallsFromRoot()
    {
        if (wallsRoot == null) return;

        if (walls == null) walls = new List<Transform>();

        foreach (Transform child in wallsRoot)
        {
            if (child != null && !walls.Contains(child))
            {
                walls.Add(child);
            }
        }
    }

    /// <summary>Re-enables every wall in the list.</summary>
    public void EnableAllWalls()
    {
        if (walls == null) return;

        foreach (Transform wall in walls)
        {
            if (wall != null) wall.gameObject.SetActive(true);
        }
    }

    /// <summary>
    /// Enables every wall, then disables the ones whose names match the supplied list
    /// (case-insensitive, surrounding whitespace ignored). Returns the names actually disabled.
    /// </summary>
    public List<string> ApplyWallsDisabledByName(List<string> wallNamesToDisable)
    {
        EnableAllWalls();

        List<string> disabledWalls = new List<string>();
        if (wallNamesToDisable == null || wallNamesToDisable.Count == 0) return disabledWalls;

        foreach (string rawName in wallNamesToDisable)
        {
            if (string.IsNullOrWhiteSpace(rawName)) continue;

            string targetName = rawName.Trim();
            bool matched = false;

            if (walls != null)
            {
                foreach (Transform wall in walls)
                {
                    if (wall == null) continue;

                    if (string.Equals(wall.name, targetName, System.StringComparison.OrdinalIgnoreCase))
                    {
                        wall.gameObject.SetActive(false);
                        disabledWalls.Add(wall.name);
                        matched = true;
                    }
                }
            }

            if (!matched)
            {
                Debug.LogWarning($"UI: No wall named '{targetName}' in the walls list — check the spelling or add it to the Wall Setup list.");
            }
        }

        return disabledWalls;
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.xKey.wasPressedThisFrame)
        {
            showUI = !showUI;
        }

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            ToggleMotion();
        }
    }

    void ApplyPreset(SwarmType type)
    {
        switch (type)
        {
            case SwarmType.Flocking:
                uiCohesion = 2.0f;
                uiSeparation = 1f;
                uiAlignment = 4.0f;
                uiRandomMvmt = 0.0f;
                uiObstacleRad = 0.1f;
                uiSafetyDist = 0.5f;
                uiPerceptionRad = 1.7f;
                break;
            case SwarmType.Densification:
                uiCohesion = 5.0f;
                uiSeparation = 1.0f;
                uiAlignment = 2.0f;
                uiRandomMvmt = 0.0f;
                uiObstacleRad = 0.1f;
                uiSafetyDist = 0.5f;
                uiPerceptionRad = 1.7f;
                break;
            case SwarmType.Random:
                uiCohesion = 0.0f;
                uiSeparation = 0.0f;
                uiAlignment = 0.0f;
                uiObstacleRad = 0.1f;
                uiRandomMvmt = 20.0f;
                uiPerceptionRad = 3.2f;
                break;
            case SwarmType.Dispersion:
                uiCohesion = 0.0f;
                uiSeparation = 5.0f;
                uiAlignment = 0.0f;
                uiRandomMvmt = 0.0f;
                uiObstacleRad = 0.1f;
                uiSafetyDist = 0.5f;
                uiPerceptionRad = 2.0f;
                break;
        }
    }

    void OnGUI()
    {
        if (!showUI) return;

        if (bgTexture == null)
        {
            bgTexture = new Texture2D(1, 1);
            bgTexture.SetPixel(0, 0, new Color(0.9f, 0.9f, 0.9f, 0.4f)); // Light grey, transparent
            bgTexture.Apply();
        }

        GUIStyle bgStyle = new GUIStyle(GUI.skin.box);
        bgStyle.normal.background = bgTexture;

        // Ensure text is black via the skin rather than contentColor to prevent weird tinting
        GUI.contentColor = Color.white;
        GUI.skin.label.normal.textColor = Color.black;
        GUI.skin.toggle.normal.textColor = Color.black;
        GUI.skin.button.normal.textColor = Color.black;

        GUILayout.BeginArea(new Rect(20, 20, 550, Screen.height - 40), bgStyle);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        GUILayout.Label("Swarm Control UI (Press 'X' to hide)", GUI.skin.label);
        GUILayout.Space(10);

        DrawControls();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawControls()
    {
        GUILayout.Label("<b>General Settings</b>");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Type:", GUILayout.Width(80));
        SwarmType newType = (SwarmType)GUILayout.Toolbar((int)selectedSwarmType, System.Enum.GetNames(typeof(SwarmType)));
        if (newType != selectedSwarmType)
        {
            selectedSwarmType = newType;
            ApplyPreset(selectedSwarmType);
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Spawn:", GUILayout.Width(80));
        AgentSpawnType newSpawnType = (AgentSpawnType)GUILayout.Toolbar((int)selectedSpawnType, System.Enum.GetNames(typeof(AgentSpawnType)));
        if (newSpawnType != selectedSpawnType)
        {
            selectedSpawnType = newSpawnType;
            ResetScene();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.Label("<b>Swarm Parameters</b>");

        uiNumberOfAgents = DrawSlider("Agents", uiNumberOfAgents, 4, 200, true);
        uiNeighbourSpread = DrawSlider("Neighbour Spread", uiNeighbourSpread, 0.1f, 5.0f);
        uiCohesion = DrawSlider("Cohesion", uiCohesion, 0, 10);
        uiSeparation = DrawSlider("Separation", uiSeparation, 0, 10);
        uiAlignment = DrawSlider("Alignment", uiAlignment, 0, 10);
        uiFriction = DrawSlider("Friction", uiFriction, 0, 1);
        uiRandomMvmt = DrawSlider("Random Mvmt", uiRandomMvmt, 0, 100);

        GUILayout.Label("<b>Avoidance</b>");
        uiOverlapAvoid = DrawSlider("Overlap Avoid", uiOverlapAvoid, 0, 50);
        uiSafetyDist = DrawSlider("Safety Dist", uiSafetyDist, 0.1f, 5f);
        uiEnvAvoid = DrawSlider("Env Avoid", uiEnvAvoid, 0, 500);

        GUILayout.Label("<b>Perception</b>");
        uiPerceptionRad = DrawSlider("View Radius", uiPerceptionRad, 0.1f, 30f);
        uiObstacleRad = DrawSlider("Obs View Rad", uiObstacleRad, 0, 10);
        uiMaxSpeed = DrawSlider("Max Speed", uiMaxSpeed, 1, 20);

        GUILayout.Space(10); GUILayout.Label("<b>Density End Rule</b>");
        DrawDensityControls();

        GUILayout.Space(10); GUILayout.Label("<b>Visualization</b>");
        uiShowPerceptionRadius = GUILayout.Toggle(uiShowPerceptionRadius, " Show Perception Radius");
        uiShowDensityArea = GUILayout.Toggle(uiShowDensityArea, " Show Density Area");
        // Pushed straight through so the outline also responds while motion is paused.
        if (swarmManager != null) swarmManager.showDensityArea = uiShowDensityArea;
        DrawVideoFinishedToggle();

        GUILayout.Space(10);
        if (GUILayout.Button("Apply Settings to Active Swarm"))
        {
            UpdateSwarmManager();
        }

        if (GUILayout.Button("Reset Scene"))
        {
            ResetScene();
        }

        if (GUILayout.Button(isRunning ? "Pause Motion" : "Play Motion"))
        {
            ToggleMotion();
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Record"))
        {
            SimRecorder recorder = GetComponent<SimRecorder>();
            if (recorder == null) recorder = gameObject.AddComponent<SimRecorder>();

            if (recorder != null)
            {
                recorder.uiController = this;
                if (swarmManager != null) recorder.swarmManager = swarmManager;
                recorder.StartBatchRecording();
            }
        }

        if (GUILayout.Button("Record Current Settings"))
        {
            SimRecorder recorder = GetComponent<SimRecorder>();
            if (recorder == null) recorder = gameObject.AddComponent<SimRecorder>();

            if (recorder != null)
            {
                recorder.uiController = this;
                if (swarmManager != null) recorder.swarmManager = swarmManager;
                recorder.StartCurrentSettingsRecording();
            }
        }

        if (GUILayout.Button("Record Combinations"))
        {
            SimRecorder recorder = GetComponent<SimRecorder>();
            if (recorder == null) recorder = gameObject.AddComponent<SimRecorder>();

            if (recorder != null)
            {
                recorder.uiController = this;
                if (swarmManager != null) recorder.swarmManager = swarmManager;
                recorder.StartCombinationsRecording();
            }
        }

        if (GUILayout.Button("Batch Record Single Parameter"))
        {
            SimRecorder recorder = GetComponent<SimRecorder>();
            if (recorder == null) recorder = gameObject.AddComponent<SimRecorder>();

            if (recorder != null)
            {
                recorder.uiController = this;
                if (swarmManager != null) recorder.swarmManager = swarmManager;
                recorder.StartSingleParameterBatchRecording();
            }
        }

        if (GUILayout.Button("Batch Record SwarmType x Parameter"))
        {
            SimRecorder recorder = GetComponent<SimRecorder>();
            if (recorder == null) recorder = gameObject.AddComponent<SimRecorder>();

            if (recorder != null)
            {
                recorder.uiController = this;
                if (swarmManager != null) recorder.swarmManager = swarmManager;
                recorder.StartSwarmTypeParameterBatchRecording();
            }
        }

        if (GUILayout.Button("Batch Record Obstacles (0th used)"))
        {
            SimRecorder recorder = GetComponent<SimRecorder>();
            if (recorder == null) recorder = gameObject.AddComponent<SimRecorder>();

            if (recorder != null)
            {
                recorder.uiController = this;
                if (swarmManager != null) recorder.swarmManager = swarmManager;
                recorder.StartObstacleBatchRecording();
            }
        }

        if (GUILayout.Button("Batch Record Obstacles x SpawnLocations"))
        {
            SimRecorder recorder = GetComponent<SimRecorder>();
            if (recorder == null) recorder = gameObject.AddComponent<SimRecorder>();

            if (recorder != null)
            {
                recorder.uiController = this;
                if (swarmManager != null) recorder.swarmManager = swarmManager;
                recorder.StartObstacleSpawnLocationBatchRecording();
            }
        }
    }

    float DrawSlider(string label, float val, float min, float max, bool isInt = false)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}: {(isInt ? (int)val : val.ToString("F1"))}", GUILayout.Width(100));
        float result = GUILayout.HorizontalSlider(val, min, max);
        GUILayout.EndHorizontal();
        return isInt ? Mathf.Round(result) : result;
    }

    int DrawSlider(string label, int val, int min, int max, bool isInt = true)
    {
        return (int)DrawSlider(label, (float)val, (float)min, (float)max, true);
    }

    public void SetMotion(bool play)
    {
        isRunning = play;
        if (swarmManager != null)
        {
            UpdateSwarmManager();
            swarmManager.enabled = isRunning;

            // Baseline the density metric at the moment motion starts. SimRecorder re-captures it
            // after the warm-up delay so settling does not count toward the threshold.
            if (isRunning && densityMonitor != null)
            {
                densityMonitor.CaptureBaseline(swarmManager.agents);
            }
        }
    }

    /// <summary>
    /// Toggle for the black "Video Finished" card the recorder appends to every clip. Reads and
    /// writes the recorder's own field so the panel and the Inspector never disagree.
    /// </summary>
    private void DrawVideoFinishedToggle()
    {
        SimRecorder recorder = GetComponent<SimRecorder>();
        if (recorder == null)
        {
            GUILayout.Label(" (no SimRecorder for the Video Finished card)");
            return;
        }

        bool showOverlay = GUILayout.Toggle(recorder.showVideoFinishedOverlay, $" Show \"{recorder.videoFinishedText}\" Overlay");
        if (showOverlay != recorder.showVideoFinishedOverlay)
        {
            recorder.showVideoFinishedOverlay = showOverlay;
        }

        if (showOverlay)
        {
            recorder.videoFinishedOverlayDuration = DrawSlider("Overlay Secs", recorder.videoFinishedOverlayDuration, 0.5f, 5f);
        }
    }

    /// <summary>
    /// Metric selector, threshold slider and live readout for the density end rule. Visible in the
    /// play mode panel so the rule can be tuned without starting a recording.
    /// </summary>
    private void DrawDensityControls()
    {
        if (densityMonitor == null)
        {
            GUILayout.Label("No density monitor assigned.");
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("Metric", GUILayout.Width(110));
        SwarmDensityMetric newMetric = (SwarmDensityMetric)GUILayout.Toolbar(
            (int)densityMonitor.metric,
            new string[] { "Trimmed Hull", "Mean KNN" });
        GUILayout.EndHorizontal();

        if (newMetric != densityMonitor.metric)
        {
            densityMonitor.metric = newMetric;
            // Rebaseline: the two metrics are on different scales.
            if (isRunning) densityMonitor.CaptureBaseline(swarmManager != null ? swarmManager.agents : null);
        }

        densityTargetRatio = DrawSlider("Target Ratio", densityTargetRatio, 0.05f, 3f);

        if (showDensityReadout)
        {
            // Shown whether or not motion is running: while paused the monitor samples itself, so
            // the hull area and mean degree of the current arrangement stay visible.
            GUILayout.Label(densityMonitor.Describe(densityTargetRatio));
            GUILayout.Label(densityMonitor.DescribeConnectivity());
        }

        if (GUILayout.Button("Rebaseline Density"))
        {
            densityMonitor.CaptureBaseline(swarmManager != null ? swarmManager.agents : null);
        }
    }

    public void SetSwarmType(SwarmType type)
    {
        selectedSwarmType = type;
        ApplyPreset(selectedSwarmType);
    }

    /// <summary>The motion type currently selected in the scene.</summary>
    public SwarmType SelectedSwarmType => selectedSwarmType;

    public void SetParameter(SwarmParameterToRecord param, float value)
    {
        switch (param)
        {
            case SwarmParameterToRecord.Cohesion: uiCohesion = value; break;
            case SwarmParameterToRecord.Separation: uiSeparation = value; break;
            case SwarmParameterToRecord.Alignment: uiAlignment = value; break;
            case SwarmParameterToRecord.Friction: uiFriction = value; break;
            case SwarmParameterToRecord.RandomMovement: uiRandomMvmt = value; break;
            case SwarmParameterToRecord.OverlapAvoidance: uiOverlapAvoid = value; break;
            case SwarmParameterToRecord.SafetyDistance: uiSafetyDist = value; break;
            case SwarmParameterToRecord.EnvAvoidance: uiEnvAvoid = value; break;
            case SwarmParameterToRecord.PerceptionRad: uiPerceptionRad = value; break;
            case SwarmParameterToRecord.ObstacleRadius: uiObstacleRad = value; break;
            case SwarmParameterToRecord.MaxSpeed: uiMaxSpeed = value; break;
        }
    }

    void ToggleMotion()
    {
        SetMotion(!isRunning);
    }

    void UpdateSwarmManager()
    {
        if (swarmManager == null) return;

        swarmManager.cohesionIntensity = uiCohesion;
        swarmManager.separationIntensity = uiSeparation;
        swarmManager.alignmentIntensity = uiAlignment;
        swarmManager.frictionIntensity = uiFriction;
        swarmManager.randomMovementIntensity = uiRandomMvmt;

        swarmManager.overlappingAvoidanceIntensity = uiOverlapAvoid;
        swarmManager.safetyDistance = uiSafetyDist;
        swarmManager.envObstacleAvoidanceIntensity = uiEnvAvoid;

        swarmManager.perceptionRadius = uiPerceptionRad;
        swarmManager.obstacleAvoidanceRadius = uiObstacleRad;
        swarmManager.maxSpeed = uiMaxSpeed;

        swarmManager.showPerceptionRadius = uiShowPerceptionRadius;
        swarmManager.showDensityArea = uiShowDensityArea;

        Transform defaultObstacle = GetDefaultObstacle();
        if (defaultObstacle != null)
        {
            // Keep only the default obstacle active to avoid confusion.
            for (int i = 0; i < obstacles.Count; i++)
            {
                if (obstacles[i] != null) obstacles[i].gameObject.SetActive(i == 0);
            }

            swarmManager.centralObstacle = defaultObstacle;
        }
        else
        {
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Count; i++)
                {
                    if (obstacles[i] != null) obstacles[i].gameObject.SetActive(false);
                }
            }
            swarmManager.centralObstacle = null;
        }

        // Common fate handling (kept as-is; only obstacle is unified).
        if (densificationCommonFate != null)
        {
            densificationCommonFate.gameObject.SetActive(selectedSwarmType == SwarmType.Densification);
            if (selectedSwarmType == SwarmType.Densification)
            {
                swarmManager.commonFateTarget = densificationCommonFate;
                swarmManager.commonFateCollider = densificationCommonFate.GetComponent<Collider2D>();
            }
        }

        if (flockingCommonFate != null)
        {
            flockingCommonFate.gameObject.SetActive(selectedSwarmType == SwarmType.Flocking);
            if (selectedSwarmType == SwarmType.Flocking)
            {
                swarmManager.commonFateTarget = flockingCommonFate;
                swarmManager.commonFateCollider = flockingCommonFate.GetComponent<Collider2D>();
            }
        }

        if (selectedSwarmType == SwarmType.Dispersion)
        {
            swarmManager.commonFateTarget = null;
            swarmManager.commonFateCollider = null;
        }
        else if (selectedSwarmType != SwarmType.Densification && selectedSwarmType != SwarmType.Flocking)
        {
            if (commonFates != null && commonFates.Length > 0 && selectedCommonFateIndex < commonFates.Length)
            {
                swarmManager.commonFateTarget = commonFates[selectedCommonFateIndex];
                swarmManager.commonFateCollider = swarmManager.commonFateTarget != null
                    ? swarmManager.commonFateTarget.GetComponent<Collider2D>()
                    : null;
            }
            else
            {
                swarmManager.commonFateTarget = null;
                swarmManager.commonFateCollider = null;
            }
        }

        // Ensure only the relevant common fate is visible.
        if (commonFates != null)
        {
            for (int i = 0; i < commonFates.Length; i++)
            {
                if (commonFates[i] == null) continue;
                bool shouldShow = (selectedSwarmType != SwarmType.Densification && selectedSwarmType != SwarmType.Flocking)
                    && i == selectedCommonFateIndex
                    && swarmManager.commonFateTarget == commonFates[i];
                commonFates[i].gameObject.SetActive(shouldShow);
            }
        }

        ApplyGoalAreaForType();
    }

    /// <summary>
    /// Returns the goal area Transform configured for the given swarm type (may be null).
    /// </summary>
    public Transform GetGoalAreaTransformForType(SwarmType type)
    {
        switch (type)
        {
            case SwarmType.Flocking: return flockingGoalArea;
            case SwarmType.Densification: return densificationGoalArea;
            case SwarmType.Random: return randomGoalArea;
            case SwarmType.Dispersion: return dispersionGoalArea;
        }
        return null;
    }

    /// <summary>The GoalArea component in use for the current swarm type (null if none configured).</summary>
    public GoalArea ActiveGoalArea { get; private set; }

    /// <summary>Name of the active goal area, or null when none is configured.</summary>
    public string ActiveGoalAreaName => ActiveGoalArea != null ? ActiveGoalArea.name : null;

    /// <summary>
    /// Activates only the goal area belonging to the selected swarm type, ensures it carries a
    /// GoalArea component, and hands it to the SwarmManager for per-frame counting.
    /// </summary>
    private void ApplyGoalAreaForType()
    {
        Transform activeGoalAreaTransform = GetGoalAreaTransformForType(selectedSwarmType);

        if (hideInactiveGoalAreas)
        {
            Transform[] allGoalAreas = { flockingGoalArea, densificationGoalArea, randomGoalArea, dispersionGoalArea };
            foreach (Transform area in allGoalAreas)
            {
                if (area == null) continue;
                area.gameObject.SetActive(area == activeGoalAreaTransform);
            }
        }
        else if (activeGoalAreaTransform != null)
        {
            activeGoalAreaTransform.gameObject.SetActive(true);
        }

        if (activeGoalAreaTransform == null)
        {
            ActiveGoalArea = null;
            if (swarmManager != null) swarmManager.goalArea = null;
            return;
        }

        GoalArea goalArea = activeGoalAreaTransform.GetComponent<GoalArea>();
        if (goalArea == null)
        {
            goalArea = activeGoalAreaTransform.gameObject.AddComponent<GoalArea>();
        }

        // Tag logs with the motion type so console output is readable during batch runs.
        goalArea.contextLabel = selectedSwarmType.ToString();
        if (densityMonitor != null) densityMonitor.contextLabel = selectedSwarmType.ToString();

        ActiveGoalArea = goalArea;
        if (swarmManager != null) swarmManager.goalArea = goalArea;
    }

    public void ResetScene()
    {
        isRunning = false;
        if (swarmManager != null)
            swarmManager.enabled = false;

        SyncDefaultObstacleActiveState();
        PlaceDefaultObstacleAtDefaultSpawnLocation();

        // Start every run with all walls present; recording rules disable per motion type afterwards.
        EnableAllWalls();

        foreach (var agent in activeAgents)
        {
            if (agent != null) Destroy(agent);
        }
        activeAgents.Clear();

        Transform activeSpawnArea = null;

        if (selectedSwarmType == SwarmType.Dispersion || selectedSwarmType == SwarmType.Densification || selectedSwarmType == SwarmType.Flocking)
        {
            if (selectedSwarmType == SwarmType.Dispersion)
            {
                activeSpawnArea = circleSpawnArea;
                if (circleSpawnArea != null) circleSpawnArea.gameObject.SetActive(true);
                if (densificationSpawnArea != null) densificationSpawnArea.gameObject.SetActive(false);
                if (flockingSpawnArea != null) flockingSpawnArea.gameObject.SetActive(false);
            }
            else if (selectedSwarmType == SwarmType.Densification)
            {
                activeSpawnArea = densificationSpawnArea;
                if (densificationSpawnArea != null) densificationSpawnArea.gameObject.SetActive(true);
                if (circleSpawnArea != null) circleSpawnArea.gameObject.SetActive(false);
                if (flockingSpawnArea != null) flockingSpawnArea.gameObject.SetActive(false);
            }
            else
            {
                activeSpawnArea = flockingSpawnArea;
                if (flockingSpawnArea != null) flockingSpawnArea.gameObject.SetActive(true);
                if (circleSpawnArea != null) circleSpawnArea.gameObject.SetActive(false);
                if (densificationSpawnArea != null) densificationSpawnArea.gameObject.SetActive(false);
            }

            // disable other spawn areas
            if (spawnAreas != null)
            {
                foreach (var area in spawnAreas)
                {
                    if (area != null) area.gameObject.SetActive(false);
                }
            }
        }
        else
        {
            if (circleSpawnArea != null) circleSpawnArea.gameObject.SetActive(false);
            if (densificationSpawnArea != null) densificationSpawnArea.gameObject.SetActive(false);
            if (flockingSpawnArea != null) flockingSpawnArea.gameObject.SetActive(false);

            if (spawnAreas != null && spawnAreas.Length > 0)
            {
                // enable only the selected standard spawn area
                for (int i = 0; i < spawnAreas.Length; i++)
                {
                    if (spawnAreas[i] != null) spawnAreas[i].gameObject.SetActive(i == selectedSpawnAreaIndex);
                }

                if (selectedSpawnAreaIndex < spawnAreas.Length)
                {
                    activeSpawnArea = spawnAreas[selectedSpawnAreaIndex];
                }
            }
        }

        if (agentPrefab == null || activeSpawnArea == null)
        {
            Debug.LogWarning("UI: Missing agent prefab or active spawn area!");
            return;
        }

        Vector3 center = activeSpawnArea.position;

        Transform activeObstacle = GetDefaultObstacle();

        if (selectedSpawnType == AgentSpawnType.Spiral)
        {
            // Spawn evenly spaced in a filled circle using Fermat's spiral
            float radius = Mathf.Max(activeSpawnArea.lossyScale.x, activeSpawnArea.lossyScale.y) / 2f;
            float goldenAngle = 137.5f * Mathf.Deg2Rad;

            int spawned = 0;
            int i = 0;
            while (spawned < uiNumberOfAgents && i < 10000) // 10000 limit to prevent infinite loops
            {
                // Calculate distance from center to evenly distribute points area wise
                float r = radius * Mathf.Sqrt((float)i / Mathf.Max(1, uiNumberOfAgents - 1));
                float theta = i * goldenAngle;

                float posX = center.x + r * Mathf.Cos(theta);
                float posY = center.y + r * Mathf.Sin(theta);
                Vector3 spawnPos = new Vector3(posX, posY, center.z);

                bool valid = true;
                if (IsTooCloseToObstacle(spawnPos, activeObstacle, uiObstacleRad)) valid = false;

                if (valid)
                {
                    GameObject newAgent = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
                    activeAgents.Add(newAgent);
                    spawned++;
                }
                i++;
            }
        }
        else if (selectedSpawnType == AgentSpawnType.Random)
        {
            // Random spawn
            Vector3 size = activeSpawnArea.lossyScale;
            Vector3 min = center - size / 2f;
            Vector3 max = center + size / 2f;

            int spawned = 0;
            int attempts = 0;

            while (spawned < uiNumberOfAgents && attempts < 10000)
            {
                float posX = Random.Range(min.x, max.x);
                float posY = Random.Range(min.y, max.y);
                Vector3 spawnPos = new Vector3(posX, posY, center.z);

                bool valid = true;
                if (IsTooCloseToObstacle(spawnPos, activeObstacle, uiObstacleRad)) valid = false;

                if (valid)
                {
                    foreach (GameObject agent in activeAgents)
                    {
                        if (Vector3.Distance(agent.transform.position, spawnPos) < uiSafetyDist)
                        {
                            valid = false;
                            break;
                        }
                    }
                }

                if (valid)
                {
                    GameObject newAgent = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
                    activeAgents.Add(newAgent);
                    spawned++;
                }
                attempts++;
            }
        }
        else if (selectedSpawnType == AgentSpawnType.Chain)
        {
            // Chain spawn
            Vector3 size = activeSpawnArea.lossyScale;
            Vector3 min = center - size / 2f;
            Vector3 max = center + size / 2f;
            int spawned = 0;
            int attempts = 0;
            int chainRestarts = 0;

            while (spawned < uiNumberOfAgents && chainRestarts < 100)
            {
                Vector3 spawnPos = Vector3.zero;
                bool valid = true;
                bool insertAtStart = false;

                if (spawned == 0)
                {
                    spawnPos = new Vector3(Random.Range(min.x, max.x), Random.Range(min.y, max.y), center.z);
                    if (IsTooCloseToObstacle(spawnPos, activeObstacle, uiObstacleRad)) valid = false;
                }
                else
                {
                    insertAtStart = (attempts >= 50); // After some failures on one end, start growing out from the FIRST agent
                    int targetIndex = insertAtStart ? 0 : activeAgents.Count - 1;
                    GameObject targetAgent = activeAgents[targetIndex];

                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    float dist = Random.Range(uiSafetyDist, uiPerceptionRad);
                    spawnPos = targetAgent.transform.position + new Vector3(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist, 0);

                    if (spawnPos.x < min.x || spawnPos.x > max.x || spawnPos.y < min.y || spawnPos.y > max.y)
                    {
                        valid = false;
                    }
                    else if (IsTooCloseToObstacle(spawnPos, activeObstacle, uiObstacleRad))
                    {
                        valid = false;
                    }
                    else
                    {
                        for (int i = 0; i < activeAgents.Count; i++)
                        {
                            float d = Vector3.Distance(activeAgents[i].transform.position, spawnPos);
                            if (i == targetIndex)
                            {
                                if (d < uiSafetyDist) { valid = false; break; }
                            }
                            else
                            {
                                if (d <= uiPerceptionRad) { valid = false; break; }
                            }
                        }
                    }
                }

                if (valid)
                {
                    GameObject newAgent = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
                    if (insertAtStart && spawned > 0)
                    {
                        activeAgents.Insert(0, newAgent); // Prepend it so it remains a single chain
                    }
                    else
                    {
                        activeAgents.Add(newAgent);
                    }

                    spawned++;
                    attempts = 0; // Reset attempts for the next agent
                }
                else
                {
                    attempts++;
                    // If we get totally stuck on both ends for a while, scrap the chain and start over
                    if (attempts > 150)
                    {
                        foreach (var a in activeAgents) Destroy(a);
                        activeAgents.Clear();
                        spawned = 0;
                        attempts = 0;
                        chainRestarts++;
                    }
                }
            }
        }
        else
        {
            // Grid spawn
            Vector3 size = activeSpawnArea.lossyScale;
            Vector3 min = center - size / 2f;

            int sideLength = Mathf.CeilToInt(Mathf.Sqrt(uiNumberOfAgents));
            float stepX = sideLength > 1 ? size.x / (sideLength - 1) : 0;
            float stepY = sideLength > 1 ? size.y / (sideLength - 1) : 0;

            int count = 0;
            int attempts = 0;

            // Adjust grid size to potentially find enough valid spots
            int searchSide = sideLength;
            while (count < uiNumberOfAgents && attempts < 100)
            {
                for (int x = 0; x < searchSide; x++)
                {
                    for (int y = 0; y < searchSide; y++)
                    {
                        if (count >= uiNumberOfAgents) break;

                        float posX = searchSide == 1 ? center.x : min.x + (x * stepX);
                        float posY = searchSide == 1 ? center.y : min.y + (y * stepY);
                        Vector3 spawnPos = new Vector3(posX, posY, center.z);

                        bool valid = true;
                        if (IsTooCloseToObstacle(spawnPos, activeObstacle, uiObstacleRad)) valid = false;

                        if (valid)
                        {
                            // we need to make sure we don't spawn in the same place multiple times
                            if (attempts == 0 || activeAgents.Find(a => Vector3.Distance(a.transform.position, spawnPos) < 0.1f) == null)
                            {
                                GameObject newAgent = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
                                activeAgents.Add(newAgent);
                                count++;
                            }
                        }
                    }
                }
                searchSide++;
                stepX = searchSide > 1 ? size.x / (searchSide - 1) : 0;
                stepY = searchSide > 1 ? size.y / (searchSide - 1) : 0;
                attempts++;
            }
        }

        if (swarmManager != null)
        {
            swarmManager.agents = activeAgents.ToArray();
            swarmManager.ResetIntegration();
            UpdateSwarmManager(); // Apply parameters after reset

            // Clear any count carried over from the previous run so it cannot
            // immediately satisfy the goal-area end condition.
            if (ActiveGoalArea != null)
            {
                ActiveGoalArea.ResetTracking();
            }

            if (densityMonitor != null)
            {
                densityMonitor.ResetTracking();
            }
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;

        Transform defaultObstacle = GetDefaultObstacle();
        if (defaultObstacle != null) DrawObstacleGizmos(defaultObstacle, uiObstacleRad);
    }

    private bool IsTooCloseToObstacle(Vector3 point, Transform obstacleTransform, float padding)
    {
        if (obstacleTransform == null) return false;
        padding = Mathf.Max(0f, padding);

        // Prefer colliders (shape-aware).
        Collider2D collider2D = obstacleTransform.GetComponent<Collider2D>();
        if (collider2D != null)
        {
            Vector2 p2 = new Vector2(point.x, point.y);
            if (collider2D.OverlapPoint(p2)) return true;
            Vector2 closest = collider2D.ClosestPoint(p2);
            float dist = Vector2.Distance(p2, closest);
            return dist < padding;
        }

        Collider collider3D = obstacleTransform.GetComponent<Collider>();
        if (collider3D != null)
        {
            if (collider3D.bounds.Contains(point))
            {
                // Fast-path; not perfect for concave meshes but fine for typical primitives.
                // ClosestPoint will still be used for the distance check.
            }
            Vector3 closest = collider3D.ClosestPoint(point);
            float dist = Vector3.Distance(point, closest);
            if (dist == 0f) return true;
            return dist < padding;
        }

        // Fallback: approximate by distance to transform position.
        return Vector3.Distance(point, obstacleTransform.position) < padding;
    }

    private void DrawObstacleGizmos(Transform obstacleTransform, float padding)
    {
        if (obstacleTransform == null) return;

        // 2D colliders
        Collider2D collider2D = obstacleTransform.GetComponent<Collider2D>();
        if (collider2D != null)
        {
            DrawCollider2DGizmos(collider2D, Mathf.Max(0f, padding));
            return;
        }

        // 3D colliders
        Collider collider3D = obstacleTransform.GetComponent<Collider>();
        if (collider3D != null)
        {
            DrawCollider3DGizmos(collider3D, Mathf.Max(0f, padding));
            return;
        }

        // Fallback
        Gizmos.DrawWireSphere(obstacleTransform.position, Mathf.Max(0f, padding));
    }

    private void DrawCollider2DGizmos(Collider2D col, float padding)
    {
        if (col is CircleCollider2D circle)
        {
            Vector3 center = circle.transform.TransformPoint(circle.offset);
            float scale = Mathf.Max(Mathf.Abs(circle.transform.lossyScale.x), Mathf.Abs(circle.transform.lossyScale.y));
            float radius = circle.radius * scale + padding;
            Gizmos.DrawWireSphere(center, radius);
            return;
        }

        if (col is BoxCollider2D box)
        {
            Vector3 center = box.transform.TransformPoint(box.offset);
            Vector3 lossy = box.transform.lossyScale;
            Vector2 sizeWorld = new Vector2(box.size.x * Mathf.Abs(lossy.x), box.size.y * Mathf.Abs(lossy.y));
            Vector3 size = new Vector3(sizeWorld.x + 2f * padding, sizeWorld.y + 2f * padding, 0.01f);

            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, box.transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = old;
            return;
        }

        if (col is CapsuleCollider2D capsule)
        {
            // Approximate capsule by drawing an expanded bounds box.
            Bounds b = capsule.bounds;
            Vector3 size = b.size + new Vector3(2f * padding, 2f * padding, 0.01f);
            Gizmos.DrawWireCube(b.center, size);
            return;
        }

        if (col is PolygonCollider2D poly)
        {
            // Draw the polygon outline.
            for (int p = 0; p < poly.pathCount; p++)
            {
                Vector2[] path = poly.GetPath(p);
                if (path == null || path.Length < 2) continue;

                for (int i = 0; i < path.Length; i++)
                {
                    Vector3 a = poly.transform.TransformPoint(path[i]);
                    Vector3 b = poly.transform.TransformPoint(path[(i + 1) % path.Length]);
                    Gizmos.DrawLine(a, b);
                }
            }

            // Padding visualization: expanded bounds.
            if (padding > 0f)
            {
                Bounds b = poly.bounds;
                Gizmos.DrawWireCube(b.center, b.size + new Vector3(2f * padding, 2f * padding, 0.01f));
            }
            return;
        }

        if (col is EdgeCollider2D edge)
        {
            Vector2[] pts = edge.points;
            if (pts != null && pts.Length >= 2)
            {
                for (int i = 0; i < pts.Length - 1; i++)
                {
                    Vector3 a = edge.transform.TransformPoint(pts[i] + edge.offset);
                    Vector3 b = edge.transform.TransformPoint(pts[i + 1] + edge.offset);
                    Gizmos.DrawLine(a, b);
                }
            }
            if (padding > 0f)
            {
                Bounds b = edge.bounds;
                Gizmos.DrawWireCube(b.center, b.size + new Vector3(2f * padding, 2f * padding, 0.01f));
            }
            return;
        }

        // Generic fallback: bounds.
        {
            Bounds b = col.bounds;
            Gizmos.DrawWireCube(b.center, b.size + new Vector3(2f * padding, 2f * padding, 0.01f));
        }
    }

    private void DrawCollider3DGizmos(Collider col, float padding)
    {
        if (col is SphereCollider sphere)
        {
            Vector3 center = sphere.transform.TransformPoint(sphere.center);
            float scale = Mathf.Max(
                Mathf.Abs(sphere.transform.lossyScale.x),
                Mathf.Abs(sphere.transform.lossyScale.y),
                Mathf.Abs(sphere.transform.lossyScale.z));
            float radius = sphere.radius * scale + padding;
            Gizmos.DrawWireSphere(center, radius);
            return;
        }

        if (col is BoxCollider box)
        {
            Vector3 center = box.transform.TransformPoint(box.center);
            Vector3 lossy = box.transform.lossyScale;
            Vector3 sizeWorld = new Vector3(
                box.size.x * Mathf.Abs(lossy.x),
                box.size.y * Mathf.Abs(lossy.y),
                box.size.z * Mathf.Abs(lossy.z));
            Vector3 size = sizeWorld + Vector3.one * (2f * padding);

            Matrix4x4 old = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(center, box.transform.rotation, Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, size);
            Gizmos.matrix = old;
            return;
        }

        if (col is CapsuleCollider capsule)
        {
            // Approximate by expanded bounds.
            Bounds b = capsule.bounds;
            Gizmos.DrawWireCube(b.center, b.size + Vector3.one * (2f * padding));
            return;
        }

        // MeshCollider or anything else: expanded bounds.
        {
            Bounds b = col.bounds;
            Gizmos.DrawWireCube(b.center, b.size + Vector3.one * (2f * padding));
        }
    }

    private Transform GetDefaultObstacle()
    {
        if (obstacles == null || obstacles.Count == 0) return null;
        return obstacles[0];
    }

    private Transform GetDefaultObstacleSpawnLocation()
    {
        if (obstacleSpawnLocations == null || obstacleSpawnLocations.Count == 0) return null;
        return obstacleSpawnLocations[0];
    }

    public void SetDefaultObstacleSpawnLocation(Transform newDefaultSpawnLocation)
    {
        if (obstacleSpawnLocations == null) obstacleSpawnLocations = new List<Transform>();

        if (newDefaultSpawnLocation == null)
        {
            if (obstacleSpawnLocations.Count > 0) obstacleSpawnLocations[0] = null;
            return;
        }

        obstacleSpawnLocations.Remove(newDefaultSpawnLocation);
        obstacleSpawnLocations.Insert(0, newDefaultSpawnLocation);

        PlaceDefaultObstacleAtDefaultSpawnLocation();
    }

    public void SetDefaultObstacle(Transform newDefaultObstacle)
    {
        if (obstacles == null) obstacles = new List<Transform>();

        if (newDefaultObstacle == null)
        {
            if (obstacles.Count > 0) obstacles[0] = null;
            return;
        }

        // Ensure the requested obstacle becomes index 0.
        obstacles.Remove(newDefaultObstacle);
        obstacles.Insert(0, newDefaultObstacle);

        SyncDefaultObstacleActiveState();
        PlaceDefaultObstacleAtDefaultSpawnLocation();
        if (swarmManager != null) swarmManager.centralObstacle = GetDefaultObstacle();
    }

    private void PlaceDefaultObstacleAtDefaultSpawnLocation()
    {
        Transform obstacle = GetDefaultObstacle();
        Transform spawn = GetDefaultObstacleSpawnLocation();
        if (obstacle == null || spawn == null) return;

        // Always move the obstacle root.
        obstacle.position = spawn.position;
        // obstacle.rotation = spawn.rotation;

        // If any rigidbodies exist on the obstacle hierarchy (common if collider is on a child),
        // sync them too so physics/colliders match the transform you see.
        Rigidbody2D rb2D = obstacle.GetComponent<Rigidbody2D>();
        if (rb2D == null) rb2D = obstacle.GetComponentInChildren<Rigidbody2D>();
        if (rb2D != null)
        {
            Vector3 p = rb2D.transform.position;
            rb2D.position = new Vector2(p.x, p.y);
            rb2D.rotation = rb2D.transform.eulerAngles.z;
            rb2D.linearVelocity = Vector2.zero;
            rb2D.angularVelocity = 0f;
        }

        // Ensure collider bounds update immediately (avoids a one-frame mismatch).
        Physics2D.SyncTransforms();
    }

    private void SyncDefaultObstacleActiveState()
    {
        if (obstacles == null) return;

        for (int i = 0; i < obstacles.Count; i++)
        {
            if (obstacles[i] != null) obstacles[i].gameObject.SetActive(i == 0);
        }
    }
}
