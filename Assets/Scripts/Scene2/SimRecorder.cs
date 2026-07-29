using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
#endif

public enum SwarmParameterToRecord
{
    Cohesion,
    Separation,
    Alignment,
    Friction,
    RandomMovement,
    OverlapAvoidance,
    SafetyDistance,
    EnvAvoidance,
    ObstacleRadius,
    MaxSpeed,
    PerceptionRad
}

public enum RecordingEndCondition
{
    /// <summary>Always record for recordingTimePerSim seconds.</summary>
    FixedDuration,
    /// <summary>Stop as soon as goalAreaAgentPercent of the agents are inside the goal area, or at recordingTimePerSim — whichever comes first.</summary>
    TargetAreaReached
}

public class SimRecorder : MonoBehaviour
{
    /// <summary>
    /// Per motion type recording rules: how long to let the swarm settle before the recorder
    /// starts, and what ends the recording.
    /// </summary>
    [System.Serializable]
    public class MotionTypeRecordingSettings
    {
        public SwarmType swarmType;

        [Tooltip("Seconds of simulation to run before the recorder starts, letting the swarm settle into formation. This warm-up is NOT captured in the video. Only applies while recording.")]
        public float recordingStartDelay = 0f;

        [Tooltip("Names of walls to disable for this motion type, e.g. Wall1. Must match the object names in the UI 'Wall Setup' list. All other walls stay enabled.")]
        public List<string> wallsToDisable = new List<string>();

        [Tooltip("FixedDuration: always record the full duration. TargetAreaReached: stop early once the percentage below is inside this type's goal area.")]
        public RecordingEndCondition endCondition = RecordingEndCondition.FixedDuration;

        [Tooltip("Percentage of agents that must be inside this type's goal area to end the recording early.")]
        [Range(0f, 100f)]
        public float goalAreaAgentPercent = 90f;

        [Tooltip("Optional per-type maximum recording length. Leave at 0 to use the global recordingTimePerSim.")]
        public float overrideRecordingTime = 0f;
    }

    [System.Serializable]
    public class SimulationConfig
    {
        public string fileName;
        public string variedParameter;
        public float variedParameterValue;
        public string parameter1;
        public float parameter1Value;
        public string parameter2;
        public float parameter2Value;
        public string parameter3;
        public float parameter3Value;
        public string swarmType;
        public string obstacleName;
        public string obstacleSpawnLocationName;
        public float perceptionRadius;
        public float cohesion;
        public float separation;
        public float alignment;
        public float friction;
        public float randomMovement;
        public float overlapAvoidance;
        public float safetyDistance;
        public float envAvoidance;
        public float obstacleRadius;
        public float maxSpeed;
        public int numAgents;

        // Goal area / end condition outcome, filled in after the recording window closes.
        public string endReason;                     // "goal-area" or "timeout"
        public string endConditionUsed;              // end condition configured for this motion type
        public float recordingStartDelay;            // un-recorded settling time before capture began
        public string wallsDisabled;                 // walls switched off for this motion type
        public float recordedDuration;               // seconds of motion recorded
        public float maxRecordingTime;               // duration cap that applied to this sim
        public string goalAreaName;
        public float goalAreaAgentPercentThreshold;  // 0 when the condition was FixedDuration
        public int agentsInsideGoalAreaAtEnd;
        public float percentInsideGoalAreaAtEnd;
        public float videoFinishedOverlaySeconds;    // length of the "Video Finished" card
        public float totalClipDuration;              // recordedDuration + overlay
    }

    [System.Serializable]
    private class BatchConfig
    {
        public string batchType;
        public string folderName;
        public string timestamp;
        public float recordingTimePerSim;
        public string endConditionsPerMotionType;
        public float videoFinishedOverlayDuration;
        public string saveFolder;
        public SimulationConfig[] simulations;
    }

    public UI uiController;
    public SwarmManager swarmManager;

    [Header("Recording Settings")]
    [Tooltip("Maximum length of each recording. Acts as a timeout when the goal area end condition is enabled.")]
    public float recordingTimePerSim = 20f;
    public string saveFolder = "SimulationRecordings";

    [Header("Recording Rules (per motion type)")]
    [Tooltip("Start delay and end condition for each motion type. A type with no entry records immediately for the full duration.")]
    public List<MotionTypeRecordingSettings> motionTypeRecordingSettings = new List<MotionTypeRecordingSettings>
    {
        new MotionTypeRecordingSettings { swarmType = SwarmType.Flocking, recordingStartDelay = 2f, endCondition = RecordingEndCondition.TargetAreaReached, goalAreaAgentPercent = 90f, wallsToDisable = new List<string> { "Wall3", "Wall4" } },
        new MotionTypeRecordingSettings { swarmType = SwarmType.Densification, recordingStartDelay = 0f, endCondition = RecordingEndCondition.TargetAreaReached, goalAreaAgentPercent = 90f },
        new MotionTypeRecordingSettings { swarmType = SwarmType.Random, recordingStartDelay = 0f, endCondition = RecordingEndCondition.FixedDuration, goalAreaAgentPercent = 90f },
        new MotionTypeRecordingSettings { swarmType = SwarmType.Dispersion, recordingStartDelay = 0f, endCondition = RecordingEndCondition.FixedDuration, goalAreaAgentPercent = 90f }
    };

    [Header("End Of Video Overlay")]
    [Tooltip("Show a full screen black card with centred text for the last moments of every recording.")]
    public bool showVideoFinishedOverlay = true;
    [Tooltip("Text shown in the centre of the black card.")]
    public string videoFinishedText = "Video Finished";
    [Tooltip("How long the black card stays on screen. Appended after the motion, so total clip length is motion + this.")]
    public float videoFinishedOverlayDuration = 0.5f;
    [Tooltip("Font size of the centred text.")]
    public int videoFinishedFontSize = 72;

    [Header("Parameter 1 Modification")]
    public SwarmParameterToRecord parameterToRecord1 = SwarmParameterToRecord.PerceptionRad;
    public float param1Start = 0.15f;
    public float param1Step = 5.0f;
    public int param1Iterations = 4;

    [Header("Parameter 2 Modification")]
    public SwarmParameterToRecord parameterToRecord2 = SwarmParameterToRecord.RandomMovement;
    public float param2Start = 0.0f;
    public float param2Step = 20.0f;
    public int param2Iterations = 4;

    [Header("SwarmType + Parameter Batch")]
    public List<SwarmType> swarmTypesToRecord = new List<SwarmType> { SwarmType.Flocking, SwarmType.Densification, SwarmType.Random, SwarmType.Dispersion };
    public SwarmParameterToRecord swarmTypeBatchParameter = SwarmParameterToRecord.MaxSpeed;
    public float swarmTypeParamStart = 1.0f;
    public float swarmTypeParamStep = 1.0f;
    public int swarmTypeParamIterations = 4;

    [Header("Combinations Batch")]
    public List<SwarmType> combinationSwarmTypes = new List<SwarmType> { SwarmType.Flocking, SwarmType.Densification, SwarmType.Random, SwarmType.Dispersion };
    public SwarmParameterToRecord combinationParameter1 = SwarmParameterToRecord.RandomMovement;
    public float[] combinationParam1Values = new float[] { 0.0f, 32.0f, 64.0f };
    public SwarmParameterToRecord combinationParameter2 = SwarmParameterToRecord.PerceptionRad;
    public float[] combinationParam2Values = new float[] { 0.15f, 3.15f, 40.15f };
    public SwarmParameterToRecord combinationParameter3 = SwarmParameterToRecord.MaxSpeed;
    public float[] combinationParam3Values = new float[] { 1.5f, 3.0f };

    [Header("Single Parameter Batch")]
    public SwarmParameterToRecord singleBatchParameter = SwarmParameterToRecord.MaxSpeed;
    public float singleParamStart = 0.15f;
    public float singleParamStep = 2.8f;
    public int singleParamIterations = 16;

    [Header("Obstacle Batch (Obstacle List + 1 Parameter)")]
    public SwarmParameterToRecord obstacleBatchParameter = SwarmParameterToRecord.RandomMovement;
    public float obstacleParamStart = 1.3f;
    public float obstacleParamStep = 0.4f;
    public int obstacleParamIterations = 4;

    private bool isRecording = false;
    private float currentParam1DisplayValue = 0f;
    private float currentParam2DisplayValue = 0f;
    private string currentObstacleDisplayName = "";
    private string currentSpawnLocationDisplayName = "";
    private bool isObstacleBatchMode = false;
    private bool isObstacleSpawnBatchMode = false;
    private bool isSingleParameterBatchMode = false;
    private bool isSwarmTypeBatchMode = false;
    private bool hideOverlayText = false;
    private SwarmType currentSwarmTypeDisplay;

    // Outcome of the most recent recording window.
    private float lastRecordedDuration;
    private string lastEndReason = "timeout";
    private string lastGoalAreaName;
    private int lastAgentsInsideGoalArea;
    private float lastPercentInsideGoalArea;
    private SwarmType lastSwarmType;
    private RecordingEndCondition lastEndConditionUsed;
    private float lastEndConditionPercent;
    private float lastMaxRecordingTime;
    private float lastOverlayDuration;
    private float lastStartDelay;
    private string lastWallsDisabled = "";

    // "Video Finished" card state.
    private bool videoFinishedOverlayActive = false;
    private Texture2D blackOverlayTexture;

    /// <summary>
    /// Returns the recording rules configured for a motion type, or a no-delay / FixedDuration
    /// default when the type has no entry in the list.
    /// </summary>
    public MotionTypeRecordingSettings GetSettingsFor(SwarmType type)
    {
        if (motionTypeRecordingSettings != null)
        {
            foreach (MotionTypeRecordingSettings entry in motionTypeRecordingSettings)
            {
                if (entry != null && entry.swarmType == type) return entry;
            }
        }

        return new MotionTypeRecordingSettings { swarmType = type, recordingStartDelay = 0f, endCondition = RecordingEndCondition.FixedDuration };
    }

    /// <summary>
    /// Applies the current motion type's recording rules that must be in place before capture:
    /// disables the walls listed for the type, then lets the swarm run un-recorded for the start
    /// delay so it can settle into formation. Call after motion is enabled and before
    /// StartRecording().
    /// </summary>
    private IEnumerator PrepareRecordingForMotionType()
    {
        SwarmType swarmType = CurrentSwarmType;
        MotionTypeRecordingSettings settings = GetSettingsFor(swarmType);

        // Walls: every wall is re-enabled first, then this type's list is disabled.
        lastWallsDisabled = "";
        if (uiController != null)
        {
            List<string> disabledWalls = uiController.ApplyWallsDisabledByName(settings.wallsToDisable);
            lastWallsDisabled = disabledWalls.Count > 0 ? string.Join(", ", disabledWalls) : "";

            if (disabledWalls.Count > 0)
            {
                Debug.Log($"[SimRecorder] {swarmType}: disabled walls — {lastWallsDisabled}.");
            }
        }

        lastStartDelay = 0f;
        if (settings.recordingStartDelay <= 0f) yield break;

        Debug.Log($"[SimRecorder] {swarmType}: settling for {settings.recordingStartDelay:F2}s before the recorder starts.");

        float delayTimer = 0f;
        while (delayTimer < settings.recordingStartDelay)
        {
            yield return new WaitForEndOfFrame();
            delayTimer += Time.deltaTime;
        }

        lastStartDelay = delayTimer;
    }

    /// <summary>
    /// Human readable summary of the per-motion-type recording rules, stored in batch_config.json.
    /// e.g. "Flocking:delay 3.0s,TargetAreaReached@90%; Random:delay 1.0s,FixedDuration".
    /// </summary>
    private string DescribeEndConditions()
    {
        if (motionTypeRecordingSettings == null || motionTypeRecordingSettings.Count == 0) return "FixedDuration (no per-type entries)";

        List<string> parts = new List<string>();
        foreach (MotionTypeRecordingSettings entry in motionTypeRecordingSettings)
        {
            if (entry == null) continue;

            string description = entry.endCondition == RecordingEndCondition.TargetAreaReached
                ? $"{entry.swarmType}:delay {entry.recordingStartDelay:F1}s,TargetAreaReached@{entry.goalAreaAgentPercent:F0}%"
                : $"{entry.swarmType}:delay {entry.recordingStartDelay:F1}s,FixedDuration";

            if (entry.overrideRecordingTime > 0f)
            {
                description += $" (max {entry.overrideRecordingTime:F1}s)";
            }

            if (entry.wallsToDisable != null && entry.wallsToDisable.Count > 0)
            {
                description += $" [walls off: {string.Join("/", entry.wallsToDisable)}]";
            }

            parts.Add(description);
        }

        return string.Join("; ", parts);
    }

    /// <summary>The motion type currently loaded in the scene.</summary>
    private SwarmType CurrentSwarmType => uiController != null ? uiController.SelectedSwarmType : currentSwarmTypeDisplay;

    /// <summary>
    /// Runs a single recording window using the end condition of the current motion type: either
    /// the full duration, or an early stop once that type's arrival percentage is inside its goal
    /// area. The duration always acts as the upper bound. A "Video Finished" card is then held on
    /// screen (and captured into the clip) before the recorder is stopped.
    /// </summary>
    private IEnumerator RunRecordingWindow()
    {
        SwarmType swarmType = CurrentSwarmType;
        MotionTypeRecordingSettings settings = GetSettingsFor(swarmType);
        GoalArea goalArea = ResolveGoalArea();

        float maxTime = settings.overrideRecordingTime > 0f ? settings.overrideRecordingTime : recordingTimePerSim;
        bool wantsGoalCondition = settings.endCondition == RecordingEndCondition.TargetAreaReached;
        bool goalConditionEnabled = wantsGoalCondition && goalArea != null && settings.goalAreaAgentPercent > 0f;

        lastEndReason = "timeout";
        lastGoalAreaName = goalArea != null ? goalArea.name : null;
        lastSwarmType = swarmType;
        lastEndConditionUsed = settings.endCondition;
        lastEndConditionPercent = wantsGoalCondition ? settings.goalAreaAgentPercent : 0f;
        lastMaxRecordingTime = maxTime;

        if (wantsGoalCondition && goalArea == null)
        {
            Debug.LogWarning($"[SimRecorder] {swarmType} is set to TargetAreaReached but has no goal area assigned; falling back to the {maxTime:F2}s duration.");
        }

        float timer = 0f;
        while (timer < maxTime)
        {
            yield return new WaitForEndOfFrame();
            timer += Time.deltaTime;

            if (goalConditionEnabled && goalArea.IsPercentReached(settings.goalAreaAgentPercent))
            {
                lastEndReason = "goal-area";
                Debug.Log($"[SimRecorder] {swarmType}: goal area '{goalArea.name}' reached {goalArea.AgentsInside}/{goalArea.TrackedAgents} agents ({goalArea.PercentInside:F1}% >= {settings.goalAreaAgentPercent:F1}%) after {timer:F2}s — ending recording early.");
                break;
            }
        }

        lastRecordedDuration = timer;
        lastAgentsInsideGoalArea = goalArea != null ? goalArea.AgentsInside : 0;
        lastPercentInsideGoalArea = goalArea != null ? goalArea.PercentInside : 0f;

        if (lastEndReason == "timeout")
        {
            string goalSuffix = goalArea != null
                ? $" — goal area '{goalArea.name}' held {lastAgentsInsideGoalArea}/{goalArea.TrackedAgents} agents ({lastPercentInsideGoalArea:F1}%)."
                : ".";
            Debug.Log($"[SimRecorder] {swarmType}: recording hit the {maxTime:F2}s duration{goalSuffix}");
        }

        // Hold the "Video Finished" card while still recording so it is baked into the clip.
        lastOverlayDuration = 0f;
        if (showVideoFinishedOverlay && videoFinishedOverlayDuration > 0f)
        {
            videoFinishedOverlayActive = true;

            float overlayTimer = 0f;
            while (overlayTimer < videoFinishedOverlayDuration)
            {
                yield return new WaitForEndOfFrame();
                overlayTimer += Time.deltaTime;
            }

            videoFinishedOverlayActive = false;
            lastOverlayDuration = overlayTimer;
        }
    }

    /// <summary>The goal area currently in play, preferring the one UI.cs selected for the swarm type.</summary>
    private GoalArea ResolveGoalArea()
    {
        if (uiController != null && uiController.ActiveGoalArea != null) return uiController.ActiveGoalArea;
        if (swarmManager != null) return swarmManager.goalArea;
        return null;
    }

    /// <summary>Copies the outcome of the last recording window into a simulation config entry.</summary>
    private void ApplyRecordingOutcome(SimulationConfig config)
    {
        if (config == null) return;

        config.endReason = lastEndReason;
        config.recordedDuration = lastRecordedDuration;
        config.goalAreaName = lastGoalAreaName;
        config.endConditionUsed = lastEndConditionUsed.ToString();
        config.recordingStartDelay = lastStartDelay;
        config.wallsDisabled = lastWallsDisabled;
        config.goalAreaAgentPercentThreshold = lastEndConditionPercent;
        config.maxRecordingTime = lastMaxRecordingTime;
        config.agentsInsideGoalAreaAtEnd = lastAgentsInsideGoalArea;
        config.percentInsideGoalAreaAtEnd = lastPercentInsideGoalArea;
        config.videoFinishedOverlaySeconds = lastOverlayDuration;
        config.totalClipDuration = lastRecordedDuration + lastOverlayDuration;

        if (string.IsNullOrEmpty(config.swarmType))
        {
            config.swarmType = lastSwarmType.ToString();
        }
    }

    /// <summary>Full screen black card with centred text, captured into the recording.</summary>
    private void DrawVideoFinishedOverlay()
    {
        if (blackOverlayTexture == null)
        {
            blackOverlayTexture = new Texture2D(1, 1);
            blackOverlayTexture.SetPixel(0, 0, Color.black);
            blackOverlayTexture.Apply();
        }

        Rect fullScreen = new Rect(0, 0, Screen.width, Screen.height);

        GUI.depth = -1000; // draw in front of everything else
        Color previousColor = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(fullScreen, blackOverlayTexture);

        GUIStyle centeredStyle = new GUIStyle
        {
            fontSize = Mathf.Max(1, videoFinishedFontSize),
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            wordWrap = false
        };
        centeredStyle.normal.textColor = Color.white;

        GUI.Label(fullScreen, videoFinishedText, centeredStyle);
        GUI.color = previousColor;
    }

    void OnGUI()
    {
        if (isRecording && !hideOverlayText)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 36;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.black;

            string displayText;
            if (isObstacleSpawnBatchMode)
            {
                displayText = $"Obstacle: {currentObstacleDisplayName} | Spawn: {currentSpawnLocationDisplayName}";
            }
            else if (isObstacleBatchMode)
            {
                displayText = $"Obstacle: {currentObstacleDisplayName} | {obstacleBatchParameter}: {currentParam1DisplayValue:F2}";
            }
            else if (isSingleParameterBatchMode)
            {
                displayText = $"{singleBatchParameter}: {currentParam1DisplayValue:F2}";
            }
            else if (isSwarmTypeBatchMode)
            {
                displayText = $"Type: {currentSwarmTypeDisplay} | {swarmTypeBatchParameter}: {currentParam1DisplayValue:F2}";
            }
            else
            {
                displayText = $"{parameterToRecord1}: {currentParam1DisplayValue:F2} | {parameterToRecord2}: {currentParam2DisplayValue:F2}";
            }

            GoalArea overlayGoalArea = ResolveGoalArea();
            if (overlayGoalArea != null)
            {
                displayText += $" | In area: {overlayGoalArea.AgentsInside}/{overlayGoalArea.TrackedAgents}";
            }

            GUI.Label(new Rect(22, 22, 1000, 50), displayText, new GUIStyle(style) { normal = { textColor = Color.white } });
            GUI.Label(new Rect(20, 20, 1000, 50), displayText, style);
        }

        // Drawn last so it covers the parameter overlay text as well.
        if (videoFinishedOverlayActive)
        {
            DrawVideoFinishedOverlay();
        }
    }
    public void StartSingleParameterBatchRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(SingleParameterBatchRecordCoroutine());
        }
    }

    public void StartBatchRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(BatchRecordCoroutine());
        }
    }

    public void StartObstacleBatchRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(ObstacleBatchRecordCoroutine());
        }
    }

    public void StartObstacleSpawnLocationBatchRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(ObstacleSpawnLocationBatchRecordCoroutine());
        }
    }

    public void StartSwarmTypeParameterBatchRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(SwarmTypeParameterBatchRecordCoroutine());
        }
    }

    public void StartCombinationsRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(CombinationsRecordCoroutine());
        }
    }

    public void StartCurrentSettingsRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(CurrentSettingsRecordCoroutine());
        }
    }

    private IEnumerator CurrentSettingsRecordCoroutine()
    {
        isRecording = true;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = false;
        hideOverlayText = true;

        if (swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing swarmManager; cannot start current settings recording.");
            isRecording = false;
            hideOverlayText = false;
            yield break;
        }

        string currentFolderPath = Path.Combine(Application.dataPath, saveFolder, "Current");
        if (!Directory.Exists(currentFolderPath))
        {
            Directory.CreateDirectory(currentFolderPath);
        }

        string fileName = $"current_{System.DateTime.Now:yyyyMMdd_HHmmss}";

        if (uiController != null)
        {
            uiController.showUI = false;
            uiController.SetMotion(true);
        }

        // Apply this motion type's wall rules, then settle un-recorded before capture starts.
        yield return PrepareRecordingForMotionType();

#if UNITY_EDITOR
        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var recorderController = new RecorderController(controllerSettings);

        var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        videoRecorder.name = "Current Settings Recorder";
        videoRecorder.Enabled = true;
        videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        videoRecorder.OutputFile = Path.Combine(currentFolderPath, fileName);

        videoRecorder.ImageInputSettings = new GameViewInputSettings
        {
            OutputWidth = 1920,
            OutputHeight = 1080
        };

        videoRecorder.AudioInputSettings.PreserveAudio = false;

        controllerSettings.AddRecorderSettings(videoRecorder);
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = 30;

        recorderController.PrepareRecording();
        recorderController.StartRecording();
#else
        Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

        yield return RunRecordingWindow();

#if UNITY_EDITOR
        recorderController.StopRecording();
#endif

        SimulationConfig config = new SimulationConfig
        {
            fileName = fileName,
            variedParameter = "CurrentSettings",
            variedParameterValue = 0f,
            parameter1 = null,
            parameter1Value = 0f,
            parameter2 = null,
            parameter2Value = 0f,
            obstacleName = swarmManager.centralObstacle != null ? swarmManager.centralObstacle.name : null,
            perceptionRadius = swarmManager.perceptionRadius,
            cohesion = swarmManager.cohesionIntensity,
            separation = swarmManager.separationIntensity,
            alignment = swarmManager.alignmentIntensity,
            friction = swarmManager.frictionIntensity,
            randomMovement = swarmManager.randomMovementIntensity,
            overlapAvoidance = swarmManager.overlappingAvoidanceIntensity,
            safetyDistance = swarmManager.safetyDistance,
            envAvoidance = swarmManager.envObstacleAvoidanceIntensity,
            obstacleRadius = swarmManager.obstacleAvoidanceRadius,
            maxSpeed = swarmManager.maxSpeed,
            numAgents = swarmManager.agents != null ? swarmManager.agents.Length : 0
        };

        ApplyRecordingOutcome(config);

        string configJson = JsonUtility.ToJson(config, true);
        File.WriteAllText(Path.Combine(currentFolderPath, $"{fileName}_config.json"), configJson);

        if (uiController != null)
        {
            uiController.SetMotion(false);
            uiController.showUI = true;
        }

        hideOverlayText = false;
        isRecording = false;
        Debug.Log($"[SimRecorder] Saved current settings recording to {currentFolderPath}/{fileName}.mp4");
    }

    private IEnumerator CombinationsRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = true;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = false;

        if (uiController == null || swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing uiController or swarmManager; cannot start combinations recording.");
            isRecording = false;
            hideOverlayText = false;
            yield break;
        }

        if (combinationSwarmTypes == null || combinationSwarmTypes.Count == 0)
        {
            Debug.LogWarning("[SimRecorder] Combination swarm type list is empty; nothing to record.");
            isRecording = false;
            hideOverlayText = false;
            yield break;
        }

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = "Combinations";
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath))
        {
            Directory.CreateDirectory(targetFolderPath);
        }

        uiController.showUI = false;

        List<SimulationConfig> simulations = new List<SimulationConfig>();
        HashSet<string> seenCombinationKeys = new HashSet<string>();

        if (combinationParam1Values == null || combinationParam1Values.Length == 0 || combinationParam2Values == null || combinationParam2Values.Length == 0 || combinationParam3Values == null || combinationParam3Values.Length == 0)
        {
            Debug.LogWarning("[SimRecorder] One or more combination value arrays are empty; nothing to record.");
            isRecording = false;
            hideOverlayText = false;
            yield break;
        }

        int combinationIndex = 0;

        // Upper bound on the number of clips (duplicate combinations are skipped as we go).
        int plannedCombinations = 0;
        foreach (SwarmType plannedType in combinationSwarmTypes)
        {
            plannedCombinations += GetCombinationParam1ValuesForType(plannedType).Length * combinationParam2Values.Length * combinationParam3Values.Length;
        }

        Debug.Log($"[SimRecorder] Combinations batch starting — up to {plannedCombinations} clips across types: {string.Join(", ", combinationSwarmTypes)}.");

        for (int typeIndex = 0; typeIndex < combinationSwarmTypes.Count; typeIndex++)
        {
            SwarmType swarmType = combinationSwarmTypes[typeIndex];
            float[] param1ValuesForType = GetCombinationParam1ValuesForType(swarmType);

            Debug.Log($"[SimRecorder] === Motion type {typeIndex + 1}/{combinationSwarmTypes.Count}: {swarmType} ===");

            for (int param1Index = 0; param1Index < param1ValuesForType.Length; param1Index++)
            {
                float currentParam1 = param1ValuesForType[param1Index];

                for (int param2Index = 0; param2Index < combinationParam2Values.Length; param2Index++)
                {
                    float currentParam2 = combinationParam2Values[param2Index];

                    for (int param3Index = 0; param3Index < combinationParam3Values.Length; param3Index++)
                    {

                        float currentParam3 = combinationParam3Values[param3Index];

                        uiController.SetSwarmType(swarmType);
                        currentSwarmTypeDisplay = swarmType;
                        uiController.SetParameter(combinationParameter1, currentParam1);
                        uiController.SetParameter(combinationParameter2, currentParam2);
                        uiController.SetParameter(combinationParameter3, currentParam3);

                        string combinationKey = $"{swarmType}|{combinationParameter1}|{currentParam1:F4}|{combinationParameter2}|{currentParam2:F4}|{combinationParameter3}|{currentParam3:F4}";
                        string combinationLabel = $"Type: {swarmType} | {combinationParameter1}: {currentParam1:F2} | {combinationParameter2}: {currentParam2:F2} | {combinationParameter3}: {currentParam3:F2}";

                        if (!seenCombinationKeys.Add(combinationKey))
                        {
                            Debug.Log($"[SimRecorder] Skipping duplicate — {combinationLabel}");
                            continue;
                        }

                        Debug.Log($"[SimRecorder] Recording {combinationIndex + 1}/{plannedCombinations} — {combinationLabel}");

                        uiController.ResetScene();
                        uiController.SetMotion(true);

                        // Apply this motion type's wall rules, then settle un-recorded before capture starts.
                        yield return PrepareRecordingForMotionType();

                        string fileName = $"type_{swarmType.ToString().ToLower()}_{combinationParameter1.ToString().ToLower()}_{currentParam1:F2}_{combinationParameter2.ToString().ToLower()}_{currentParam2:F2}_{combinationParameter3.ToString().ToLower()}_{currentParam3:F2}";
                        fileName = SanitizeFileName(fileName);

                        SimulationConfig config = new SimulationConfig
                        {
                            fileName = fileName,
                            variedParameter = paramFolderName,
                            variedParameterValue = combinationIndex,
                            parameter1 = "SwarmType",
                            parameter1Value = typeIndex,
                            parameter2 = combinationParameter1.ToString(),
                            parameter2Value = currentParam1,
                            parameter3 = combinationParameter2.ToString(),
                            parameter3Value = currentParam2,
                            swarmType = swarmType.ToString(),
                            obstacleName = swarmManager.centralObstacle != null ? swarmManager.centralObstacle.name : null,
                            perceptionRadius = swarmManager.perceptionRadius,
                            cohesion = swarmManager.cohesionIntensity,
                            separation = swarmManager.separationIntensity,
                            alignment = swarmManager.alignmentIntensity,
                            friction = swarmManager.frictionIntensity,
                            randomMovement = swarmManager.randomMovementIntensity,
                            overlapAvoidance = swarmManager.overlappingAvoidanceIntensity,
                            safetyDistance = swarmManager.safetyDistance,
                            envAvoidance = swarmManager.envObstacleAvoidanceIntensity,
                            obstacleRadius = swarmManager.obstacleAvoidanceRadius,
                            maxSpeed = swarmManager.maxSpeed,
                            numAgents = swarmManager.agents != null ? swarmManager.agents.Length : 0
                        };

                        simulations.Add(config);

#if UNITY_EDITOR
                        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                        var recorderController = new RecorderController(controllerSettings);

                        var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                        videoRecorder.name = "Combinations Recorder";
                        videoRecorder.Enabled = true;
                        videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
                        videoRecorder.OutputFile = Path.Combine(targetFolderPath, fileName);

                        videoRecorder.ImageInputSettings = new GameViewInputSettings
                        {
                            OutputWidth = 1920,
                            OutputHeight = 1080
                        };

                        videoRecorder.AudioInputSettings.PreserveAudio = false;

                        controllerSettings.AddRecorderSettings(videoRecorder);
                        controllerSettings.SetRecordModeToManual();
                        controllerSettings.FrameRate = 30;

                        recorderController.PrepareRecording();
                        recorderController.StartRecording();
#else
                        Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                        yield return RunRecordingWindow();
                        ApplyRecordingOutcome(config);

#if UNITY_EDITOR
                        recorderController.StopRecording();
#endif

                        uiController.SetMotion(false);
                        Debug.Log($"[SimRecorder] Saved {swarmType} clip {combinationIndex + 1}/{plannedCombinations} ({lastEndReason}, {lastRecordedDuration:F2}s) → {fileName}.mp4");
                        combinationIndex++;
                    }
                }
            }
        }

        BatchConfig batchConfig = new BatchConfig
        {
            batchType = "combinations",
            folderName = paramFolderName,
            timestamp = timestampFolder,
            recordingTimePerSim = recordingTimePerSim,
            endConditionsPerMotionType = DescribeEndConditions(),
            videoFinishedOverlayDuration = showVideoFinishedOverlay ? videoFinishedOverlayDuration : 0f,
            saveFolder = saveFolder,
            simulations = simulations.ToArray()
        };

        string configJson = JsonUtility.ToJson(batchConfig, true);
        File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);

        uiController.showUI = true;
        uiController.SetMotion(false);

        hideOverlayText = false;
        isRecording = false;
        Debug.Log("[SimRecorder] Combinations recording finished.");
    }

    private IEnumerator SingleParameterBatchRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = false;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = true;
        isSwarmTypeBatchMode = false;

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = $"{singleBatchParameter}";
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath))
        {
            Directory.CreateDirectory(targetFolderPath);
        }

        // Hide UI
        if (uiController != null)
        {
            uiController.showUI = false;
        }

        List<SimulationConfig> simulations = new List<SimulationConfig>();

        for (int i = 0; i < singleParamIterations; i++)
        {
            // float currentParam = singleParamStart + Mathf.Pow(singleParamStep, i);
            // if (currentParam == 1f)
            //     currentParam = 0;
            float currentParam = singleParamStart + (i * singleParamStep);
            currentParam1DisplayValue = currentParam;

            // Set parameter via code
            if (uiController != null)
            {
                uiController.SetParameter(singleBatchParameter, currentParam);
            }

            uiController.ResetScene();

            // Start simulation
            uiController.SetMotion(true);

            // Apply this motion type's wall rules, then settle un-recorded before capture starts.
            yield return PrepareRecordingForMotionType();

            string fileName = $"{singleBatchParameter.ToString().ToLower()}_{currentParam:F2}";
            SimulationConfig config = new SimulationConfig
            {
                fileName = fileName,
                variedParameter = paramFolderName,
                variedParameterValue = currentParam,
                parameter1 = singleBatchParameter.ToString(),
                parameter1Value = currentParam,
                parameter2 = null,
                parameter2Value = 0f,
                obstacleName = swarmManager.centralObstacle != null ? swarmManager.centralObstacle.name : null,
                perceptionRadius = swarmManager.perceptionRadius,
                cohesion = swarmManager.cohesionIntensity,
                separation = swarmManager.separationIntensity,
                alignment = swarmManager.alignmentIntensity,
                friction = swarmManager.frictionIntensity,
                randomMovement = swarmManager.randomMovementIntensity,
                overlapAvoidance = swarmManager.overlappingAvoidanceIntensity,
                safetyDistance = swarmManager.safetyDistance,
                envAvoidance = swarmManager.envObstacleAvoidanceIntensity,
                obstacleRadius = swarmManager.obstacleAvoidanceRadius,
                maxSpeed = swarmManager.maxSpeed,
                numAgents = swarmManager.agents != null ? swarmManager.agents.Length : 0
            };

            simulations.Add(config);

#if UNITY_EDITOR
            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var recorderController = new RecorderController(controllerSettings);

            var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            videoRecorder.name = "My Video Recorder";
            videoRecorder.Enabled = true;
            videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            videoRecorder.OutputFile = Path.Combine(targetFolderPath, fileName);

            videoRecorder.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = 1920,
                OutputHeight = 1080
            };

            videoRecorder.AudioInputSettings.PreserveAudio = false;

            controllerSettings.AddRecorderSettings(videoRecorder);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = 30;

            recorderController.PrepareRecording();
            recorderController.StartRecording();
#else
            Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

            yield return RunRecordingWindow();
            ApplyRecordingOutcome(config);

#if UNITY_EDITOR
            recorderController.StopRecording();
#endif

            uiController.SetMotion(false);
            Debug.Log($"[SimRecorder] Saved video sequence to {targetFolderPath}/{fileName}.mp4");
        }

        // Write a single config for the whole folder.
        {
            BatchConfig batchConfig = new BatchConfig
            {
                batchType = "single-parameter",
                folderName = paramFolderName,
                timestamp = timestampFolder,
                recordingTimePerSim = recordingTimePerSim,
                endConditionsPerMotionType = DescribeEndConditions(),
                videoFinishedOverlayDuration = showVideoFinishedOverlay ? videoFinishedOverlayDuration : 0f,
                saveFolder = saveFolder,
                simulations = simulations.ToArray()
            };

            string configJson = JsonUtility.ToJson(batchConfig, true);
            File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);
        }

        // Restore UI
        if (uiController != null)
        {
            uiController.showUI = true;
        }

        isRecording = false;
        isSingleParameterBatchMode = false;
        Debug.Log("[SimRecorder] Single parameter batch recording finished.");
    }

    private IEnumerator BatchRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = false;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = false;

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = $"{parameterToRecord1}_vs_{parameterToRecord2}";
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath))
        {
            Directory.CreateDirectory(targetFolderPath);
        }

        // Hide UI
        if (uiController != null)
        {
            uiController.showUI = false;
        }

        List<SimulationConfig> simulations = new List<SimulationConfig>();
        for (int i = 0; i < param1Iterations; i++)
        {
            float currentParam1 = param1Start + (i * param1Step);
            currentParam1DisplayValue = currentParam1;

            for (int j = 0; j < param2Iterations; j++)
            {
                float currentParam2 = param2Start + (j * param2Step);
                float currentParam = swarmTypeParamStart + Mathf.Pow(swarmTypeParamStep, i);
                if (currentParam == 1f)
                    currentParam = 0;
                // float currentParam2 = param2Start + Mathf.Pow(param2Step, j);
                // if (currentParam2 == 1f)
                //     currentParam2 = 0;
                currentParam2DisplayValue = currentParam2;

                // Set parameter via code
                if (uiController != null)
                {
                    uiController.SetParameter(parameterToRecord1, currentParam1);
                    uiController.SetParameter(parameterToRecord2, currentParam2);
                }

                uiController.ResetScene();

                // Start simulation
                uiController.SetMotion(true);

                // Apply this motion type's wall rules, then settle un-recorded before capture starts.
                yield return PrepareRecordingForMotionType();

                string fileName = $"{parameterToRecord1.ToString().ToLower()}_{currentParam1:F2}_{parameterToRecord2.ToString().ToLower()}_{currentParam2:F2}";

                SimulationConfig config = new SimulationConfig
                {
                    fileName = fileName,
                    variedParameter = paramFolderName,
                    variedParameterValue = currentParam1, // Storing one for backward compatibility or change if needed
                    parameter1 = parameterToRecord1.ToString(),
                    parameter1Value = currentParam1,
                    parameter2 = parameterToRecord2.ToString(),
                    parameter2Value = currentParam2,
                    obstacleName = swarmManager.centralObstacle != null ? swarmManager.centralObstacle.name : null,
                    perceptionRadius = swarmManager.perceptionRadius,
                    cohesion = swarmManager.cohesionIntensity,
                    separation = swarmManager.separationIntensity,
                    alignment = swarmManager.alignmentIntensity,
                    friction = swarmManager.frictionIntensity,
                    randomMovement = swarmManager.randomMovementIntensity,
                    overlapAvoidance = swarmManager.overlappingAvoidanceIntensity,
                    safetyDistance = swarmManager.safetyDistance,
                    envAvoidance = swarmManager.envObstacleAvoidanceIntensity,
                    obstacleRadius = swarmManager.obstacleAvoidanceRadius,
                    maxSpeed = swarmManager.maxSpeed,
                    numAgents = swarmManager.agents != null ? swarmManager.agents.Length : 0
                };

                simulations.Add(config);

#if UNITY_EDITOR
                var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var recorderController = new RecorderController(controllerSettings);

                var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                videoRecorder.name = "My Video Recorder";
                videoRecorder.Enabled = true;
                videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
                videoRecorder.OutputFile = Path.Combine(targetFolderPath, fileName);

                videoRecorder.ImageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = 1920,
                    OutputHeight = 1080
                };

                videoRecorder.AudioInputSettings.PreserveAudio = false;

                controllerSettings.AddRecorderSettings(videoRecorder);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = 30;

                recorderController.PrepareRecording();
                recorderController.StartRecording();
#else
                Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                yield return RunRecordingWindow();
                ApplyRecordingOutcome(config);

#if UNITY_EDITOR
                recorderController.StopRecording();
#endif

                uiController.SetMotion(false);
                Debug.Log($"[SimRecorder] Saved video sequence to {targetFolderPath}/{fileName}.mp4");
            }
        }

        // Write a single config for the whole folder.
        {
            BatchConfig batchConfig = new BatchConfig
            {
                batchType = "two-parameter",
                folderName = paramFolderName,
                timestamp = timestampFolder,
                recordingTimePerSim = recordingTimePerSim,
                endConditionsPerMotionType = DescribeEndConditions(),
                videoFinishedOverlayDuration = showVideoFinishedOverlay ? videoFinishedOverlayDuration : 0f,
                saveFolder = saveFolder,
                simulations = simulations.ToArray()
            };

            string configJson = JsonUtility.ToJson(batchConfig, true);
            File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);
        }

        // Restore UI
        if (uiController != null)
        {
            uiController.showUI = true;
        }

        isRecording = false;
        Debug.Log("[SimRecorder] Batch recording finished.");
    }

    private IEnumerator ObstacleBatchRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = false;
        isObstacleBatchMode = true;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = false;

        if (uiController == null || swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing uiController or swarmManager; cannot start obstacle batch recording.");
            isRecording = false;
            isObstacleBatchMode = false;
            yield break;
        }

        if (uiController.obstacles == null || uiController.obstacles.Count == 0)
        {
            Debug.LogWarning("[SimRecorder] UI obstacle list is empty; nothing to record.");
            isRecording = false;
            isObstacleBatchMode = false;
            yield break;
        }

        // Snapshot the obstacle list so we can restore it afterward.
        List<Transform> originalObstacles = new List<Transform>(uiController.obstacles);

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = $"Obstacle_vs_{obstacleBatchParameter}";
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath))
        {
            Directory.CreateDirectory(targetFolderPath);
        }

        // Hide UI
        uiController.showUI = false;

        List<SimulationConfig> simulations = new List<SimulationConfig>();

        for (int obstacleIndex = 0; obstacleIndex < originalObstacles.Count; obstacleIndex++)
        {
            Transform obstacle = originalObstacles[obstacleIndex];
            if (obstacle == null) continue;

            currentObstacleDisplayName = obstacle.name;
            uiController.SetDefaultObstacle(obstacle);

            for (int i = 0; i < obstacleParamIterations; i++)
            {
                float currentParam = obstacleParamStart + (i * obstacleParamStep);
                currentParam1DisplayValue = currentParam;

                uiController.SetParameter(obstacleBatchParameter, currentParam);
                uiController.ResetScene();
                uiController.SetMotion(true);

                // Apply this motion type's wall rules, then settle un-recorded before capture starts.
                yield return PrepareRecordingForMotionType();

                string obstacleSafe = SanitizeFileName(obstacle.name);
                string fileName = $"obstacle_{obstacleSafe}_{obstacleBatchParameter.ToString().ToLower()}_{currentParam:F2}";
                fileName = SanitizeFileName(fileName);

                SimulationConfig config = new SimulationConfig
                {
                    fileName = fileName,
                    variedParameter = $"{paramFolderName}:{obstacleSafe}",
                    variedParameterValue = currentParam,
                    parameter1 = obstacleBatchParameter.ToString(),
                    parameter1Value = currentParam,
                    parameter2 = null,
                    parameter2Value = 0f,
                    obstacleName = obstacle.name,
                    perceptionRadius = swarmManager.perceptionRadius,
                    cohesion = swarmManager.cohesionIntensity,
                    separation = swarmManager.separationIntensity,
                    alignment = swarmManager.alignmentIntensity,
                    friction = swarmManager.frictionIntensity,
                    randomMovement = swarmManager.randomMovementIntensity,
                    overlapAvoidance = swarmManager.overlappingAvoidanceIntensity,
                    safetyDistance = swarmManager.safetyDistance,
                    envAvoidance = swarmManager.envObstacleAvoidanceIntensity,
                    obstacleRadius = swarmManager.obstacleAvoidanceRadius,
                    maxSpeed = swarmManager.maxSpeed,
                    numAgents = swarmManager.agents != null ? swarmManager.agents.Length : 0
                };

                simulations.Add(config);

#if UNITY_EDITOR
                var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var recorderController = new RecorderController(controllerSettings);

                var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                videoRecorder.name = "My Video Recorder";
                videoRecorder.Enabled = true;
                videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
                videoRecorder.OutputFile = Path.Combine(targetFolderPath, fileName);

                videoRecorder.ImageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = 1920,
                    OutputHeight = 1080
                };

                videoRecorder.AudioInputSettings.PreserveAudio = false;

                controllerSettings.AddRecorderSettings(videoRecorder);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = 30;

                recorderController.PrepareRecording();
                recorderController.StartRecording();
#else
                Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                yield return RunRecordingWindow();
                ApplyRecordingOutcome(config);

#if UNITY_EDITOR
                recorderController.StopRecording();
#endif

                uiController.SetMotion(false);
                Debug.Log($"[SimRecorder] Saved obstacle batch video to {targetFolderPath}/{fileName}.mp4");
            }
        }

        // Write a single config for the whole folder.
        {
            BatchConfig batchConfig = new BatchConfig
            {
                batchType = "obstacles+one-parameter",
                folderName = paramFolderName,
                timestamp = timestampFolder,
                recordingTimePerSim = recordingTimePerSim,
                endConditionsPerMotionType = DescribeEndConditions(),
                videoFinishedOverlayDuration = showVideoFinishedOverlay ? videoFinishedOverlayDuration : 0f,
                saveFolder = saveFolder,
                simulations = simulations.ToArray()
            };

            string configJson = JsonUtility.ToJson(batchConfig, true);
            File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);
        }

        // Restore obstacle list and UI
        uiController.obstacles.Clear();
        uiController.obstacles.AddRange(originalObstacles);
        uiController.showUI = true;
        uiController.SetMotion(false);

        isRecording = false;
        isObstacleBatchMode = false;
        Debug.Log("[SimRecorder] Obstacle batch recording finished.");
    }

    private IEnumerator ObstacleSpawnLocationBatchRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = false;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = true;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = false;

        if (uiController == null || swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing uiController or swarmManager; cannot start obstacle/spawn-location batch recording.");
            isRecording = false;
            isObstacleSpawnBatchMode = false;
            yield break;
        }

        if (uiController.obstacles == null || uiController.obstacles.Count == 0)
        {
            Debug.LogWarning("[SimRecorder] UI obstacle list is empty; nothing to record.");
            isRecording = false;
            isObstacleSpawnBatchMode = false;
            yield break;
        }

        if (uiController.obstacleSpawnLocations == null || uiController.obstacleSpawnLocations.Count == 0)
        {
            Debug.LogWarning("[SimRecorder] UI obstacle spawn locations list is empty; nothing to record.");
            isRecording = false;
            isObstacleSpawnBatchMode = false;
            yield break;
        }

        // Snapshot lists so we can restore them afterward.
        List<Transform> originalObstacles = new List<Transform>(uiController.obstacles);
        List<Transform> originalSpawnLocations = new List<Transform>(uiController.obstacleSpawnLocations);

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = "Obstacle_x_SpawnLocation";
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath))
        {
            Directory.CreateDirectory(targetFolderPath);
        }

        // Hide UI
        uiController.showUI = false;

        List<SimulationConfig> simulations = new List<SimulationConfig>();

        for (int obstacleIndex = 0; obstacleIndex < originalObstacles.Count; obstacleIndex++)
        {
            Transform obstacle = originalObstacles[obstacleIndex];
            if (obstacle == null) continue;

            currentObstacleDisplayName = obstacle.name;
            uiController.SetDefaultObstacle(obstacle);

            for (int spawnIndex = 0; spawnIndex < originalSpawnLocations.Count; spawnIndex++)
            {
                Transform spawn = originalSpawnLocations[spawnIndex];
                if (spawn == null) continue;

                currentSpawnLocationDisplayName = spawn.name;
                uiController.SetDefaultObstacleSpawnLocation(spawn);

                uiController.ResetScene();
                uiController.SetMotion(true);

                // Apply this motion type's wall rules, then settle un-recorded before capture starts.
                yield return PrepareRecordingForMotionType();

                string obstacleSafe = SanitizeFileName(obstacle.name);
                string spawnSafe = SanitizeFileName(spawn.name);
                string fileName = $"obstacle_{obstacleSafe}_spawn_{spawnSafe}";
                fileName = SanitizeFileName(fileName);

                SimulationConfig config = new SimulationConfig
                {
                    fileName = fileName,
                    variedParameter = paramFolderName,
                    variedParameterValue = (obstacleIndex * 100f) + spawnIndex,
                    parameter1 = "Obstacle",
                    parameter1Value = obstacleIndex,
                    parameter2 = "SpawnLocation",
                    parameter2Value = spawnIndex,
                    obstacleName = obstacle.name,
                    obstacleSpawnLocationName = spawn.name,
                    perceptionRadius = swarmManager.perceptionRadius,
                    cohesion = swarmManager.cohesionIntensity,
                    separation = swarmManager.separationIntensity,
                    alignment = swarmManager.alignmentIntensity,
                    friction = swarmManager.frictionIntensity,
                    randomMovement = swarmManager.randomMovementIntensity,
                    overlapAvoidance = swarmManager.overlappingAvoidanceIntensity,
                    safetyDistance = swarmManager.safetyDistance,
                    envAvoidance = swarmManager.envObstacleAvoidanceIntensity,
                    obstacleRadius = swarmManager.obstacleAvoidanceRadius,
                    maxSpeed = swarmManager.maxSpeed,
                    numAgents = swarmManager.agents != null ? swarmManager.agents.Length : 0
                };

                simulations.Add(config);

#if UNITY_EDITOR
                var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var recorderController = new RecorderController(controllerSettings);

                var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                videoRecorder.name = "My Video Recorder";
                videoRecorder.Enabled = true;
                videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
                videoRecorder.OutputFile = Path.Combine(targetFolderPath, fileName);

                videoRecorder.ImageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = 1920,
                    OutputHeight = 1080
                };

                videoRecorder.AudioInputSettings.PreserveAudio = false;

                controllerSettings.AddRecorderSettings(videoRecorder);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = 30;

                recorderController.PrepareRecording();
                recorderController.StartRecording();
#else
                Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                yield return RunRecordingWindow();
                ApplyRecordingOutcome(config);

#if UNITY_EDITOR
                recorderController.StopRecording();
#endif

                uiController.SetMotion(false);
                Debug.Log($"[SimRecorder] Saved obstacle/spawn batch video to {targetFolderPath}/{fileName}.mp4");
            }
        }

        // Write a single config for the whole folder.
        {
            BatchConfig batchConfig = new BatchConfig
            {
                batchType = "obstacles+spawn-locations",
                folderName = paramFolderName,
                timestamp = timestampFolder,
                recordingTimePerSim = recordingTimePerSim,
                endConditionsPerMotionType = DescribeEndConditions(),
                videoFinishedOverlayDuration = showVideoFinishedOverlay ? videoFinishedOverlayDuration : 0f,
                saveFolder = saveFolder,
                simulations = simulations.ToArray()
            };

            string configJson = JsonUtility.ToJson(batchConfig, true);
            File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);
        }

        // Restore lists and UI
        uiController.obstacles.Clear();
        uiController.obstacles.AddRange(originalObstacles);
        uiController.obstacleSpawnLocations.Clear();
        uiController.obstacleSpawnLocations.AddRange(originalSpawnLocations);

        uiController.showUI = true;
        uiController.SetMotion(false);

        isRecording = false;
        isObstacleSpawnBatchMode = false;
        Debug.Log("[SimRecorder] Obstacle/spawn-location batch recording finished.");
    }

    private IEnumerator SwarmTypeParameterBatchRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = false;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = true;

        if (uiController == null || swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing uiController or swarmManager; cannot start swarm type batch recording.");
            isRecording = false;
            isSwarmTypeBatchMode = false;
            yield break;
        }

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = $"SwarmType_vs_{swarmTypeBatchParameter}";
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath))
        {
            Directory.CreateDirectory(targetFolderPath);
        }

        // Hide UI
        uiController.showUI = false;

        List<SimulationConfig> simulations = new List<SimulationConfig>();
        for (int typeIndex = 0; typeIndex < swarmTypesToRecord.Count; typeIndex++)
        {
            SwarmType sType = swarmTypesToRecord[typeIndex];
            currentSwarmTypeDisplay = sType;

            for (int i = 0; i < swarmTypeParamIterations; i++)
            {

                float currentParam = swarmTypeParamStart + (i * swarmTypeParamStep);

                // float currentParam = swarmTypeParamStart + Mathf.Pow(swarmTypeParamStep, i);
                // if (currentParam == 1f)
                //     currentParam = 0;
                currentParam1DisplayValue = currentParam;

                uiController.SetSwarmType(sType);
                uiController.SetParameter(swarmTypeBatchParameter, currentParam);

                if (swarmTypeBatchParameter != SwarmParameterToRecord.PerceptionRad)
                {
                    uiController.SetParameter(SwarmParameterToRecord.PerceptionRad, 46f);
                }

                uiController.ResetScene();
                uiController.SetMotion(true);

                // Apply this motion type's wall rules, then settle un-recorded before capture starts.
                yield return PrepareRecordingForMotionType();

                string typeSafe = SanitizeFileName(sType.ToString());
                string fileName = $"type_{typeSafe}_{swarmTypeBatchParameter.ToString().ToLower()}_{currentParam:F2}";
                fileName = SanitizeFileName(fileName);

                SimulationConfig config = new SimulationConfig
                {
                    fileName = fileName,
                    variedParameter = $"{paramFolderName}",
                    variedParameterValue = currentParam,
                    parameter1 = "SwarmType",
                    parameter1Value = typeIndex,
                    parameter2 = swarmTypeBatchParameter.ToString(),
                    parameter2Value = currentParam,
                    swarmType = sType.ToString(),
                    obstacleName = swarmManager.centralObstacle != null ? swarmManager.centralObstacle.name : null,
                    perceptionRadius = swarmManager.perceptionRadius,
                    cohesion = swarmManager.cohesionIntensity,
                    separation = swarmManager.separationIntensity,
                    alignment = swarmManager.alignmentIntensity,
                    friction = swarmManager.frictionIntensity,
                    randomMovement = swarmManager.randomMovementIntensity,
                    overlapAvoidance = swarmManager.overlappingAvoidanceIntensity,
                    safetyDistance = swarmManager.safetyDistance,
                    envAvoidance = swarmManager.envObstacleAvoidanceIntensity,
                    obstacleRadius = swarmManager.obstacleAvoidanceRadius,
                    maxSpeed = swarmManager.maxSpeed,
                    numAgents = swarmManager.agents != null ? swarmManager.agents.Length : 0
                };

                simulations.Add(config);

#if UNITY_EDITOR
                var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
                var recorderController = new RecorderController(controllerSettings);

                var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
                videoRecorder.name = "My Video Recorder";
                videoRecorder.Enabled = true;
                videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
                videoRecorder.OutputFile = Path.Combine(targetFolderPath, fileName);

                videoRecorder.ImageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = 1920,
                    OutputHeight = 1080
                };

                videoRecorder.AudioInputSettings.PreserveAudio = false;

                controllerSettings.AddRecorderSettings(videoRecorder);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = 30;

                recorderController.PrepareRecording();
                recorderController.StartRecording();
#else
                Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                yield return RunRecordingWindow();
                ApplyRecordingOutcome(config);

#if UNITY_EDITOR
                recorderController.StopRecording();
#endif

                uiController.SetMotion(false);
                Debug.Log($"[SimRecorder] Saved swarm type batch video to {targetFolderPath}/{fileName}.mp4");
            }
        }

        // Write a single config for the whole folder.
        {
            BatchConfig batchConfig = new BatchConfig
            {
                batchType = "swarmtype+one-parameter",
                folderName = paramFolderName,
                timestamp = timestampFolder,
                recordingTimePerSim = recordingTimePerSim,
                endConditionsPerMotionType = DescribeEndConditions(),
                videoFinishedOverlayDuration = showVideoFinishedOverlay ? videoFinishedOverlayDuration : 0f,
                saveFolder = saveFolder,
                simulations = simulations.ToArray()
            };

            string configJson = JsonUtility.ToJson(batchConfig, true);
            File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);
        }

        uiController.showUI = true;
        uiController.SetMotion(false);

        isRecording = false;
        isSwarmTypeBatchMode = false;
        Debug.Log("[SimRecorder] Swarm type batch recording finished.");
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unnamed";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }

    private float[] GetCombinationParam1ValuesForType(SwarmType swarmType)
    {
        if (swarmType == SwarmType.Random)
        {
            return new float[] { 60.0f };
        }

        return combinationParam1Values;
    }
}
