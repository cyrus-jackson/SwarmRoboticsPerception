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
    TargetAreaReached,
    /// <summary>Stop once the swarm's spread reaches densityTargetRatio of its value at recording start, or at recordingTimePerSim — whichever comes first.</summary>
    DensityRatioReached,
    /// <summary>
    /// Stop once the trimmed hull area reaches an absolute target in world units, rather than a
    /// ratio to where this run happened to start. Use when several conditions must end at the same
    /// degree of dispersion so that the endpoint is held constant and only the motion differs.
    /// </summary>
    AbsoluteHullAreaReached,
    /// <summary>
    /// Stop once the spread stops changing — the rate of change of the metric stays near zero for
    /// a sustained stretch, rather than the value reaching some level.
    ///
    /// This is the right rule for a run with no random movement, which spreads and then holds. It
    /// is not dependable with randomness: the hull keeps wobbling by several u²/s after the swarm
    /// has stopped spreading, so the slope never stays quiet and the clip would run to the timeout.
    /// </summary>
    SpreadSettled,
    /// <summary>
    /// Stop once the swarm has broken into, or merged down to, a target number of groups.
    ///
    /// Groups are the connected components of the perception graph: two agents are joined when one
    /// is inside the other's perception radius, and a chain of such links forms one group. An agent
    /// that can see nobody is a group of one, so a swarm of 40 agents that all sit out of range of
    /// each other counts as 40 groups.
    ///
    /// Which direction the test runs is taken from the count when recording starts, so the same
    /// setting works for a flock coalescing (many groups down to a few) and for a swarm fragmenting
    /// (one group up to several).
    /// </summary>
    ClusterCountReached,
    /// <summary>Stop once wallContactAgentPercent of unique spawned agents have touched any wall during capture.</summary>
    WallContactReached,
    /// <summary>Random only: record for the mean motion duration of the other completed clips in this batch.</summary>
    AverageOtherMotionDuration,
    /// <summary>Stop when local spacing is stable for a sustained period, with an optional low-speed requirement.</summary>
    MotionSettled
}

/// <summary>What <c>densityTargetRatio</c> is a ratio of.</summary>
public enum DensityRatioBaseline
{
    /// <summary>
    /// The metric measured at the moment capture began — this run's own starting spread.
    ///
    /// Right for Densification, which is asked to shrink to some fraction of where it started.
    /// </summary>
    RecordingStart,

    /// <summary>
    /// The settled value of the zero-randomness run from the same layout, perception radius and
    /// max speed.
    ///
    /// Right for Dispersion. Without randomness the swarm spreads and then stops, so that plateau
    /// is what separation alone achieves from this exact arrangement of agents. Measuring a
    /// randomised run against its own starting area instead answers a different and less useful
    /// question, because every run starts from the same small spawn regardless of radius.
    ///
    /// Combinations batches measure missing references before capture, including when each clip
    /// uses a fresh layout. Other recording modes require the reference to be recorded first.
    /// </summary>
    NoRandomnessReference
}

/// <summary>Records swarm simulations and batches with per-motion stopping rules and run metadata.</summary>
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

        [Tooltip("Seconds to keep recording AFTER the end condition fires, so the clip does not cut the instant the threshold is crossed. This tail IS captured in the video. It does not apply when the clip ended on the duration cap, because there is no event to pad there.")]
        public float recordingEndDelay = 0f;

        [Tooltip("Names of walls to disable for this motion type, e.g. Wall1. Must match the object names in the UI 'Wall Setup' list. All other walls stay enabled.")]
        public List<string> wallsToDisable = new List<string>();

        [Tooltip("FixedDuration: always record the full duration. TargetAreaReached: stop early once the percentage below is inside this type's goal area.")]
        public RecordingEndCondition endCondition = RecordingEndCondition.FixedDuration;

        [Tooltip("Percentage of agents that must be inside this type's goal area to end the recording early.")]
        [Range(0f, 100f)]
        public float goalAreaAgentPercent = 90f;

        [Tooltip("WallContactReached: percentage of unique spawned agents that must touch any wall during capture. Each agent counts once across all walls. Earlier warm-up contacts are cleared; agents still touching a wall when capture starts count. Walls are identified by the Wall tag or a name containing 'wall', including parent objects. 0 disables the condition.")]
        [Range(0f, 100f)]
        public float wallContactAgentPercent = 10f;

        [Tooltip("Which spread measure the density end condition uses for this motion type.")]
        public SwarmDensityMetric densityMetric = SwarmDensityMetric.TrimmedHullArea;

        [Tooltip("What densityTargetRatio is measured against. RecordingStart: the value at the moment capture began, i.e. this run's own starting spread — right for Densification, which shrinks from where it started. NoRandomnessReference: the settled value of the zero-randomness run from the SAME layout, radius and max speed — right for Dispersion, where the no-randomness swarm stops growing after a few seconds and that plateau is the natural yardstick.")]
        public DensityRatioBaseline densityRatioBaseline = DensityRatioBaseline.RecordingStart;

        [Tooltip("Target ratio against whichever baseline is selected above. Below 1 is a contraction test (0.4 = shrunk to 40%), above 1 an expansion test (1.8 = grown to 180%).")]
        public float densityTargetRatio = 0.25f;

        [Tooltip("Target trimmed hull area in world units for AbsoluteHullAreaReached. Whether it is a grow-to or shrink-to test is inferred from the area at recording start.")]
        public float absoluteHullAreaTarget = 390f;

        [Tooltip("Seconds the end condition must hold continuously before the recording stops. Stops an oscillating swarm ending a clip on a momentary spike. 0 fires on the first crossing.")]
        public float endConditionDwellTime = 0.5f;

        [Tooltip("ClusterCountReached: how many groups to stop at. Direction is inferred from the count when recording starts, so 1 ends a flock once it has merged into a single group, and 2 ends a swarm once it has split in two.")]
        public int clusterCountTarget = 2;

        [Tooltip("ClusterCountReached: smallest group that counts. 1 treats a lone agent as its own group, which is how fragmentation is defined in the paper. 2 ignores strays and counts only real groups.")]
        public int minClusterSize = 1;

        [Tooltip("SpreadSettled: slope limit as a fraction of the largest value seen so far, per second. 0.02 means the spread must be changing by under 2% of its peak per second.")]
        public float settleTolerance = 0.02f;

        [Tooltip("SpreadSettled: seconds the slope must stay inside the limit before the run counts as settled. Also used by the no-randomness reference run when the density ratio is measured against it.")]
        public float settleHoldTime = 3f;

        [Tooltip("MotionSettled: seconds of local spacing history to check for stability.")]
        public float motionSettleWindowSeconds = 3f;

        [Tooltip("MotionSettled: maximum spacing range in the window, as a fraction of the starting mean 3-neighbour distance.")]
        [Range(0f, 1f)]
        public float motionSettleSpacingTolerance = 0.01f;

        [Tooltip("MotionSettled: also require every agent to move slowly. Leave off for density equilibrium with residual motion or randomness.")]
        public bool motionSettleRequireLowSpeed = false;

        [Tooltip("MotionSettled: when Require Low Speed is enabled, every agent must move at or below this fraction of Max Speed.")]
        [Range(0f, 1f)]
        public float motionSettleSpeedFraction = 0.02f;

        [Tooltip("MotionSettled: seconds that stable spacing and the optional low-speed check must hold.")]
        public float motionSettleHoldSeconds = 3f;

        [Tooltip("MotionSettled: earliest recording time at which settling can end the clip.")]
        public float motionSettleMinimumRunSeconds = 5f;

        [Tooltip("MotionSettled: spacing reduction required to label the result settled after contraction. Otherwise it is settled without contraction.")]
        [Range(0f, 1f)]
        public float motionSettleContractionFraction = 0.05f;

        [Tooltip("Optional per-type maximum recording length. Leave at 0 to use the global recordingTimePerSim.")]
        public float overrideRecordingTime = 0f;
    }

    /// <summary>Stores the simulation settings and stopping outcome for one recorded clip.</summary>
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
        public int numAgents;            // agents actually spawned
        public int requestedAgents;      // agents asked for
        public bool agentSpawnShortfall; // true when fewer spawned than requested
        public string spawnLayoutId;         // set when the run started from a fixed layout
        public string spawnLayoutFingerprint;// lets two clips be confirmed to share a start
        public string pairedFileName;        // randomised clip <-> its no-randomness reference, when both were saved

        // Goal area / end condition outcome, filled in after the recording window closes.
        public string endReason;                     // "goal-area" or "timeout"
        public string endConditionUsed;              // end condition configured for this motion type
        public float recordingStartDelay;            // un-recorded settling time before capture began
        public float recordingEndDelay;              // recorded tail after the end condition fired, 0 on timeout
        public string wallsDisabled;                 // walls switched off for this motion type
        public string densityMetric;                 // spread measure sampled for this sim
        public float densityBaseline;                // metric value at recording start
        public float densityAtEnd;                   // metric value when the clip ended
        public float densityRatioAtEnd;              // densityAtEnd / densityBaseline
        public float densityTargetRatio;             // 0 when the condition was not DensityRatioReached
        public string densityBaselineSource;         // "recording start" or the no-randomness reference used
        public float densityReferenceValue;          // settled value of the zero-randomness twin, 0 when unused
        public float absoluteHullAreaTarget;         // 0 when the condition was not AbsoluteHullAreaReached
        public int clusterCountTarget;               // 0 when the condition was not ClusterCountReached
        public int clusterCountAtStart;              // groups when recording began
        public int clusterCountAtEnd;                // groups when the clip finished
        public int minClusterSize;                   // smallest group counted
        public float endConditionDwellTime;          // seconds the rule had to hold before firing
        public float recordedDuration;               // motion plus the ending marker
        public float recordedMotionDuration;         // includes the recorded tail, excludes closing frames
        public float averageOtherMotionDuration;
        public int averageOtherMotionSampleCount;
        public float maxRecordingTime;               // duration cap that applied to this sim
        public string goalAreaName;
        public float goalAreaAgentPercentThreshold;  // 0 when the condition was FixedDuration
        public int agentsInsideGoalAreaAtEnd;
        public float percentInsideGoalAreaAtEnd;
        public float wallContactAgentPercentThreshold;
        public int wallContactTrackedAgents;
        public int uniqueAgentsHitWallsAtEnd;
        public float percentAgentsHitWallsAtEnd;
        public bool motionSettleRequireLowSpeed;
        public float motionSettleWindowSeconds;
        public float motionSettleSpacingTolerance;
        public float motionSettleSpeedFraction;
        public float motionSettleHoldSeconds;
        public float motionSettleMinimumRunSeconds;
        public float motionSettleContractionFraction;
        public float settlingSpacingBaseline;
        public float settlingSpacingAtTermination;
        public float settlingSpacingRange;
        public float settlingMaxSpeedAtTermination;
        public float settlingSpeedLimit;
        public bool settledWithContraction;
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
        public int frameRate;
        public float simulationStep;
        public string saveFolder;
        public SimulationConfig[] simulations;
    }

    public UI uiController;
    public SwarmManager swarmManager;

    [Header("Recording Settings")]
    [Tooltip("Maximum length of each recording. Acts as a timeout when the goal area end condition is enabled.")]
    public float recordingTimePerSim = 30f;
    [Tooltip("Capture frame rate. Hénard et al. (2024) presented stimuli at 60 fps.")]
    public int recordingFrameRate = 60;
    [Tooltip("Also write a per-frame trajectory json beside each mp4, replayable with SwarmTrajectoryPlayer.")]
    public bool captureTrajectories = true;
    [Tooltip("Capture resolution. Raw frames are width x height x 4 bytes each and all of them pass through the encoder, so this and the frame rate dominate recording cost.")]
    public int outputWidth = 1280;
    public int outputHeight = 720;
    public string saveFolder = "SimulationRecordings";

    [Header("Recording Rules (per motion type)")]
    [Tooltip("Start delay and end condition for each motion type. Missing types record immediately for the full duration.")]
    public List<MotionTypeRecordingSettings> motionTypeRecordingSettings = new List<MotionTypeRecordingSettings>
    {
        new MotionTypeRecordingSettings { swarmType = SwarmType.Flocking, recordingStartDelay = 1.5f, endCondition = RecordingEndCondition.TargetAreaReached, goalAreaAgentPercent = 90f, wallsToDisable = new List<string> { "Wall3", "Wall4" } },
        new MotionTypeRecordingSettings { swarmType = SwarmType.Densification, recordingStartDelay = 0f, endCondition = RecordingEndCondition.DensityRatioReached, densityRatioBaseline = DensityRatioBaseline.RecordingStart, densityTargetRatio = 0.3f, recordingEndDelay = 3f },
        new MotionTypeRecordingSettings { swarmType = SwarmType.Random, recordingStartDelay = 1.5f, endCondition = RecordingEndCondition.AverageOtherMotionDuration },
        new MotionTypeRecordingSettings { swarmType = SwarmType.Dispersion, recordingStartDelay = 0f, endCondition = RecordingEndCondition.DensityRatioReached, densityRatioBaseline = DensityRatioBaseline.NoRandomnessReference, densityTargetRatio = 1f, recordingEndDelay = 3f },
        CreateDensificationV2RecordingSettings()
    };

    [Tooltip("Draw the parameter and hull area line in the top left of the recorded video. ")]
    public bool showRecordingOverlay = false;

    [Tooltip("Seconds to keep recording with the line reading 'Video Ended', so the last frames of the clip say where it stopped. Added to the clip length. 0 skips it, which means the marker lasts a single frame and is invisible on playback.")]
    public float videoEndedMarkerSeconds = 1f;

    [Header("End Of Video Overlay")]
    [Tooltip("Show a full screen black card with centred text for the last moments of every recording.")]
    public bool showVideoFinishedOverlay = true;
    [Tooltip("Text shown in the centre of the black card.")]
    public string videoFinishedText = "Video Finished";
    [Tooltip("How long the black card stays on screen. Appended after the motion, so total clip length is motion + this.")]
    public float videoFinishedOverlayDuration = 2.0f;
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
    public List<SwarmType> swarmTypesToRecord = new List<SwarmType> { SwarmType.DensificationV2 };
    public SwarmParameterToRecord swarmTypeBatchParameter = SwarmParameterToRecord.Cohesion;
    public float swarmTypeParamStart = 1.0f;
    public float swarmTypeParamStep = 0.5f;
    public int swarmTypeParamIterations = 10;

    [Header("Combinations Batch")]
    [Tooltip("Record every combination once per saved spawn layout, using the layouts named in 'Reuse Spawn Layouts' below. Off means a fresh random spawn per clip. NoRandomnessReference works in either mode: missing references are measured in an unrecorded zero-randomness run from the clip's exact starting layout. Fresh clips are not paired across parameter values.")]
    public bool combinationsUseSpawnLayouts = false;

    [Tooltip("Runs per parameter combination when Use Spawn Layouts is off. Every repetition gets a fresh spawn and, when needed, its own no-randomness reference. With layouts on, each combination instead runs once per layout.")]
    [Min(1)]
    public int combinationsRepetitions = 10;

    [Tooltip("Record the no-randomness reference runs (mp4 + trajectory json) into a NoRandomnessReferences child folder of the batch, with their own batch_config.json. Off measures them unrecorded.")]
    public bool saveNoRandomnessReferences = true;

    public List<SwarmType> combinationSwarmTypes = new List<SwarmType> { SwarmType.DensificationV2 };
    public SwarmParameterToRecord combinationParameter1 = SwarmParameterToRecord.RandomMovement;
    [Tooltip("RandomMovement sweep values are added to a base of 60 for Random motion only. For all other motion types and parameters, values are used directly. This applies to any of the three parameter axes.")]
    public float[] combinationParam1Values = new float[] { 0.0f, 40.0f };
    public SwarmParameterToRecord combinationParameter2 = SwarmParameterToRecord.PerceptionRad;
    public float[] combinationParam2Values = new float[] { 1.5f, 4.0f }; // 0.5f, 1.0f, 1.5f, 1.8f, 2.0f, 2.5f, 3.0f, 3.5f, 4.0f
    // public float[] combinationParam2Values = new float[] { 0.15f };

    public SwarmParameterToRecord combinationParameter3 = SwarmParameterToRecord.MaxSpeed;
    public float[] combinationParam3Values = new float[] { 1.5f, 3.0f };

    [Header("Matched Start Batch (single parameter, identical spawns)")]
    [Tooltip("How many starting layouts to generate for Matched Start Batch, or per motion type in Combinations with Use Spawn Layouts on. The same layouts are reused across every parameter value. Named saved layouts replace this count. Ignored by Combinations when Use Spawn Layouts is off; use Combinations Repetitions there.")]
    public int matchedStartLayouts = 1;

    [Tooltip("Save each layout as JSON under SimulationRecordings/SpawnLayouts so a later batch can reuse the exact same starts.")]
    public bool saveSpawnLayouts = true;

    [Tooltip("Optional. Names of saved layouts to load instead of generating fresh ones, e.g. layout_00. Leave empty to generate.")]
    public List<string> reuseSpawnLayouts = new List<string> { "20261007_114900_layout_00" };

    [Header("Single Parameter Batch")]
    public SwarmParameterToRecord singleBatchParameter = SwarmParameterToRecord.Cohesion;
    public float singleParamStart = 6.0f;
    public float singleParamStep = 0.50f;
    public int singleParamIterations = 8;

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

    /// <summary>True while a clip is being captured, batch or single.</summary>
    public bool IsRecording => isRecording;

    /// <summary>
    /// True when nothing that is only there for the operator should be drawn.
    ///
    /// <see cref="showRecordingOverlay"/> governs this recorder's own status line, but anything
    /// else drawing IMGUI in the scene ends up in the video too, and a keyboard hint is no use to
    /// a viewer watching a recording. Other panels read this rather than each inventing their own
    /// rule, so one switch covers the frame.
    /// </summary>
    public bool OperatorOverlayHidden => isRecording && hideOverlayText;

    // Outcome of the most recent recording window.
    private float lastRecordedDuration;
    private float lastRecordedMotionDuration;
    private float lastAverageOtherMotionDuration;
    private int lastAverageOtherMotionSampleCount;
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
    private float lastEndDelay;
    private float lastWallContactAgentPercentThreshold;
    private int lastWallContactTrackedAgents;
    private int lastUniqueAgentsHitWalls;
    private float lastPercentAgentsHitWalls;
    private readonly Dictionary<GameObject, SwarmAgent> wallContactAgents = new Dictionary<GameObject, SwarmAgent>();
    private readonly HashSet<GameObject> agentsHitWalls = new HashSet<GameObject>();

    // Word at the front of the on-screen line: "Recording" while the window is open, "Video Ended"
    // for the closing frames, so the mp4 says for itself where it stopped.
    private string overlayStatus = "Recording";
    private GUIStyle recordingOverlayStyle;
    private GUIStyle recordingOverlayShadowStyle;

    private int lastClusterCountAtStart;
    private int lastClusterCountAtEnd;
    private int lastClusterCountTarget;
    private int lastMinClusterSize = 1;

    // References are shared only by runs with the same layout, motion rules and density metric.
    private readonly Dictionary<string, float> referenceDensityValues = new Dictionary<string, float>();

    // Metric samples for the run in progress, used to take a settled value at the end of it.
    private readonly List<float> referenceSampleBuffer = new List<float>();

    // Resolved for the run being recorded: 0 when the ratio is against this run's own start.
    private float resolvedReferenceValue;
    private string lastDensityBaselineSource = "recording start";

    private readonly SpreadSettleDetector settleDetector = new SpreadSettleDetector();
    private readonly MotionSettleDetector motionSettleDetector = new MotionSettleDetector();
    private readonly List<Vector2> motionSettlePositions = new List<Vector2>();
    private MotionTypeRecordingSettings lastMotionSettleSettings;
    private string lastWallsDisabled = "";

    // Set while a matched start batch is running, so each clip records which layout it began from.
    private string activeSpawnLayoutId;
    private string activeSpawnLayoutFingerprint;
    private string lastTrajectoryPath;

    /// <summary>Path of the most recent trajectory file written, for the replay player's quick load.</summary>
    public string LastTrajectoryPath => lastTrajectoryPath;
    private string lastDensityMetricName;
    private float lastDensityBaseline;
    private float lastDensityValue;
    private float lastDensityRatio;
    private float lastDensityTargetRatio;
    private float lastAbsoluteAreaTarget;
    private float lastDwellTime;

    // "Video Finished" card state.
    private bool videoFinishedOverlayActive = false;
    private Texture2D blackOverlayTexture;

    private static MotionTypeRecordingSettings CreateDensificationV2RecordingSettings()
    {
        return new MotionTypeRecordingSettings
        {
            swarmType = SwarmType.DensificationV2,
            recordingStartDelay = 0f,
            endCondition = RecordingEndCondition.FixedDuration,
            densityMetric = SwarmDensityMetric.MeanKnnDistance,
            endConditionDwellTime = 0f,
            recordingEndDelay = 0f
        };
    }

    /// <summary>
    /// Returns configured recording rules, with a no-delay / FixedDuration fallback for missing types.
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

        if (type == SwarmType.DensificationV2) return CreateDensificationV2RecordingSettings();

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

        if (settings.recordingStartDelay > 0f)
        {
            Debug.Log($"[SimRecorder] {swarmType}: settling for {settings.recordingStartDelay:F2}s before the recorder starts.");

            float delayTimer = 0f;
            while (delayTimer < settings.recordingStartDelay)
            {
                yield return new WaitForEndOfFrame();
                delayTimer += Time.deltaTime;
            }

            lastStartDelay = delayTimer;
        }

        // Baseline the density metric after settling, so the warm-up contraction is not counted
        // toward the threshold.
        CaptureDensityBaseline(settings);
    }

    /// <summary>
    /// Identifies the layout, motion rules and metric shared by a reference and its partners.
    /// </summary>
    private string ReferenceKey(float perceptionRadius, float maxSpeed)
    {
        MotionTypeRecordingSettings settings = GetSettingsFor(CurrentSwarmType);
        SwarmDensityMonitor monitor = ResolveDensityMonitor();
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0}|{1}|{2:R}|{3:R}|{4:R}|{5:R}|{6:R}|{7:R}|{8:R}|{9:R}|{10:R}|{11:R}|{12}|{13:R}|{14}",
            CurrentSwarmType, $"{activeSpawnLayoutId ?? "none"}:{activeSpawnLayoutFingerprint ?? "none"}",
            perceptionRadius, maxSpeed, swarmManager?.cohesionIntensity, swarmManager?.separationIntensity,
            swarmManager?.alignmentIntensity, swarmManager?.frictionIntensity,
            swarmManager?.overlappingAvoidanceIntensity, swarmManager?.safetyDistance,
            swarmManager?.envObstacleAvoidanceIntensity, swarmManager?.obstacleAvoidanceRadius,
            settings.densityMetric, monitor?.hullTrimFraction, monitor?.knnK);
    }

    /// <summary>
    /// Runs the zero-randomness twin of the clip about to be recorded, from the same layout, and
    /// stores its settled value as that clip's density baseline.
    ///
    /// When <paramref name="referenceFolder"/> is given the run is captured like any other clip — mp4,
    /// trajectory json and a <see cref="SimulationConfig"/> added to <paramref name="savedConfigs"/>
    /// — so the reference itself can be watched and analysed. Otherwise it runs unrecorded.
    /// </summary>
    private IEnumerator PrepareCombinationReference(SpawnLayout layout, string referenceFolder = null,
        string saveFileName = null, List<SimulationConfig> savedConfigs = null,
        System.Func<string, SimulationConfig> buildConfig = null)
    {
        MotionTypeRecordingSettings settings = GetSettingsFor(CurrentSwarmType);
        if (settings.endCondition != RecordingEndCondition.DensityRatioReached ||
            settings.densityRatioBaseline != DensityRatioBaseline.NoRandomnessReference ||
            CurrentRunIsReference)
            yield break;

        string key = ReferenceKey(swarmManager.perceptionRadius, swarmManager.maxSpeed);
        if (referenceDensityValues.TryGetValue(key, out float reference) && reference > 0f)
            yield break;

        if (layout == null || layout.Count == 0 || ResolveDensityMonitor() == null)
            yield break;

        bool save = !string.IsNullOrEmpty(referenceFolder) && !string.IsNullOrEmpty(saveFileName);
        if (save && !Directory.Exists(referenceFolder)) Directory.CreateDirectory(referenceFolder);

        float randomness = swarmManager.randomMovementIntensity;
        float captureDeltaTime = Time.captureDeltaTime;
        Random.State randomState = Random.state;
#if UNITY_EDITOR
        RecorderController referenceRecorder = null;
#endif
        try
        {
            // Match the recording cadence so integration and the reference median use the same clock.
            Time.captureFramerate = recordingFrameRate;
            uiController.SetParameter(SwarmParameterToRecord.RandomMovement, 0f);
            uiController.SpawnFromLayout(layout);
            uiController.SetMotion(true);
            yield return PrepareRecordingForMotionType();

            if (save)
            {
                Debug.Log($"[SimRecorder] Recording no-randomness reference for {layout.layoutId} → {saveFileName}.mp4");
#if UNITY_EDITOR
                referenceRecorder = StartClipRecorder(referenceFolder, saveFileName, "No-Randomness Reference Recorder");
#endif
                yield return RunRecordingWindow(referenceFolder, saveFileName);

#if UNITY_EDITOR
                referenceRecorder.StopRecording();
                referenceRecorder = null;
#endif
                // Built after the window so the config carries the zero randomness actually run.
                SimulationConfig referenceConfig = buildConfig != null ? buildConfig(saveFileName) : null;
                if (referenceConfig != null)
                {
                    ApplyRecordingOutcome(referenceConfig);

                    // The value this run produced, which its randomised partners are measured against.
                    if (referenceDensityValues.TryGetValue(key, out float settledValue) && settledValue > 0f)
                    {
                        referenceConfig.densityReferenceValue = settledValue;
                        referenceConfig.densityBaselineSource = $"this run (settled at {settledValue:F2})";
                    }
                    else
                    {
                        referenceConfig.densityBaselineSource = "this run (did NOT settle — no reference stored)";
                    }
                    savedConfigs?.Add(referenceConfig);
                }

                Debug.Log($"[SimRecorder] Saved no-randomness reference ({lastEndReason}, {lastRecordedDuration:F2}s) → {saveFileName}.mp4");
            }
            else
            {
                Debug.Log($"[SimRecorder] Measuring unrecorded no-randomness reference for {layout.layoutId}.");
                yield return RunRecordingWindow(referenceOnly: true);
            }
        }
        finally
        {
#if UNITY_EDITOR
            // Only still set if the batch was interrupted mid-window.
            if (referenceRecorder != null) referenceRecorder.StopRecording();
#endif
            uiController.SetMotion(false);
            uiController.SetParameter(SwarmParameterToRecord.RandomMovement, randomness);
            uiController.SpawnFromLayout(layout);
            uiController.ApplySettingsToManager();
            Time.captureDeltaTime = captureDeltaTime;
            Random.state = randomState;
        }
    }

#if UNITY_EDITOR
    /// <summary>Starts an mp4 recorder writing to folder/fileName with this recorder's capture settings.</summary>
    private RecorderController StartClipRecorder(string folder, string fileName, string recorderName)
    {
        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var recorderController = new RecorderController(controllerSettings);

        var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        videoRecorder.name = recorderName;
        videoRecorder.Enabled = true;
        videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        videoRecorder.OutputFile = Path.Combine(folder, fileName);
        videoRecorder.ImageInputSettings = new GameViewInputSettings
        {
            OutputWidth = outputWidth,
            OutputHeight = outputHeight
        };
        videoRecorder.AudioInputSettings.PreserveAudio = false;

        controllerSettings.AddRecorderSettings(videoRecorder);
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = recordingFrameRate;

        recorderController.PrepareRecording();
        recorderController.StartRecording();
        return recorderController;
    }
#endif

    /// <summary>True when the run being recorded is a zero-randomness reference.</summary>
    private bool CurrentRunIsReference =>
        swarmManager != null && Mathf.Abs(swarmManager.randomMovementIntensity) <= 0.001f;

    /// <summary>
    /// Settled value of the run just finished, stored for its randomised partners.
    ///
    /// Takes the median of the last few seconds rather than the final frame: the hull twitches by a
    /// unit or two even when the swarm has stopped, and a single frame would carry that into every
    /// clip that later uses this as a target.
    /// </summary>
    private void StoreReferenceValue(float perceptionRadius, float maxSpeed, float tailSeconds = 3f)
    {
        if (referenceSampleBuffer.Count == 0) return;

        int wanted = Mathf.Clamp(Mathf.RoundToInt(tailSeconds * recordingFrameRate),
                                 1, referenceSampleBuffer.Count);

        List<float> tail = referenceSampleBuffer.GetRange(referenceSampleBuffer.Count - wanted, wanted);
        tail.Sort();
        float settled = tail.Count % 2 == 1
            ? tail[tail.Count / 2]
            : 0.5f * (tail[tail.Count / 2 - 1] + tail[tail.Count / 2]);

        string key = ReferenceKey(perceptionRadius, maxSpeed);
        referenceDensityValues[key] = settled;

        Debug.Log($"[SimRecorder] Reference for {key} settled at {settled:F2} " +
                  $"(median of the last {wanted} samples). Randomised runs at these settings will " +
                  $"use it as their densityTargetRatio baseline.");
    }

    /// <summary>
    /// The density rule, against whichever baseline this motion type selected.
    ///
    /// With RecordingStart this is the monitor's own ratio test. With NoRandomnessReference the
    /// denominator is the settled value of the zero-randomness twin instead, so a target of 1.0
    /// means "spread as far as the swarm gets with no randomness at all" and 1.5 means "half as far
    /// again". Direction follows the ratio in both cases: below 1 is a contraction test.
    /// </summary>
    private bool IsDensityRatioReached(SwarmDensityMonitor monitor, MotionTypeRecordingSettings settings)
    {
        if (monitor == null || settings.densityTargetRatio <= 0f) return false;

        if (resolvedReferenceValue <= 0f)
        {
            return monitor.IsRatioReached(settings.densityTargetRatio);
        }

        float target = settings.densityTargetRatio * resolvedReferenceValue;
        return settings.densityTargetRatio < 1f
            ? monitor.CurrentValue <= target
            : monitor.CurrentValue >= target;
    }

    // Reused so counting the groups every frame allocates nothing.
    private readonly List<int> clusterSizeBuffer = new List<int>();

    /// <summary>
    /// Number of groups the swarm is currently in.
    ///
    /// Groups come from <see cref="SwarmClusterMetrics"/>, which joins two agents when one is inside
    /// the other's perception radius and takes the connected components of that graph. The same
    /// edge test the agents themselves use to pick neighbours, so the count reflects what the swarm
    /// can actually communicate across.
    /// </summary>
    private int CountClusters(int minClusterSize)
    {
        if (swarmManager == null) return 0;

        SwarmClusterMetrics.ClusterSizes(swarmManager.agents, swarmManager.perceptionRadius,
                                         clusterSizeBuffer, swarmManager.AgentColliders,
                                         !swarmManager.useDensificationV2);

        return minClusterSize <= 1
            ? clusterSizeBuffer.Count
            : SwarmClusterMetrics.CountAtLeast(clusterSizeBuffer, minClusterSize);
    }

    /// <summary>Points the monitor at this motion type's metric and takes the reference measurement.</summary>
    private void CaptureDensityBaseline(MotionTypeRecordingSettings settings)
    {
        SwarmDensityMonitor monitor = ResolveDensityMonitor();
        if (monitor == null) return;

        monitor.metric = settings.densityMetric;
        monitor.CaptureBaseline(swarmManager != null ? swarmManager.agents : null);
    }

    /// <summary>The density monitor in play, preferring the one the UI provisioned.</summary>
    private SwarmDensityMonitor ResolveDensityMonitor()
    {
        if (uiController != null && uiController.densityMonitor != null) return uiController.densityMonitor;
        if (swarmManager != null) return swarmManager.densityMonitor;
        return null;
    }

    private void ResetWallContactTracking(bool enabled)
    {
        wallContactAgents.Clear();
        agentsHitWalls.Clear();
        lastWallContactTrackedAgents = 0;
        lastUniqueAgentsHitWalls = 0;
        lastPercentAgentsHitWalls = 0f;

        if (!enabled || swarmManager == null || swarmManager.agents == null) return;

        foreach (GameObject agent in swarmManager.agents)
        {
            if (agent == null || wallContactAgents.ContainsKey(agent)) continue;
            SwarmAgent script = agent.GetComponent<SwarmAgent>();
            if (script != null) script.ResetWallContactTracking();
            wallContactAgents.Add(agent, script);
        }

        lastWallContactTrackedAgents = wallContactAgents.Count;
    }

    private void UpdateWallContactOutcome()
    {
        foreach (KeyValuePair<GameObject, SwarmAgent> agent in wallContactAgents)
        {
            if (agent.Value != null && agent.Value.HasHitWall) agentsHitWalls.Add(agent.Key);
        }

        lastUniqueAgentsHitWalls = agentsHitWalls.Count;
        lastPercentAgentsHitWalls = lastWallContactTrackedAgents > 0
            ? 100f * lastUniqueAgentsHitWalls / lastWallContactTrackedAgents
            : 0f;
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

            string rule;
            switch (entry.endCondition)
            {
                case RecordingEndCondition.TargetAreaReached:
                    rule = $"TargetAreaReached@{entry.goalAreaAgentPercent:F0}%";
                    break;
                case RecordingEndCondition.WallContactReached:
                    rule = $"WallContactReached@{entry.wallContactAgentPercent:F0}% unique agents";
                    break;
                case RecordingEndCondition.AverageOtherMotionDuration:
                    rule = "AverageOtherMotionDuration[Random only]";
                    break;
                case RecordingEndCondition.DensityRatioReached:
                    rule = $"DensityRatioReached[{entry.densityMetric}]@{entry.densityTargetRatio:F2}";
                    break;
                case RecordingEndCondition.AbsoluteHullAreaReached:
                    rule = $"AbsoluteHullAreaReached@{entry.absoluteHullAreaTarget:F0}u2";
                    break;

                case RecordingEndCondition.ClusterCountReached:
                    rule = $"ClusterCountReached@{entry.clusterCountTarget} groups of "
                         + $"{entry.minClusterSize}+";
                    break;

                case RecordingEndCondition.MotionSettled:
                    rule = $"MotionSettled[3NN range <= {entry.motionSettleSpacingTolerance:P0} of start over {entry.motionSettleWindowSeconds:F1}s, " +
                           (entry.motionSettleRequireLowSpeed ? $"speed <= {entry.motionSettleSpeedFraction:P0} of max, " : "density equilibrium, ") +
                           $"held {entry.motionSettleHoldSeconds:F1}s, min {entry.motionSettleMinimumRunSeconds:F1}s]";
                    break;
                case RecordingEndCondition.SpreadSettled:
                    rule = $"SpreadSettled@{entry.settleTolerance:P0}/s held {entry.settleHoldTime:F1}s";
                    break;
                default:
                    rule = "FixedDuration";
                    break;
            }

            bool usesAverageDuration = entry.endCondition == RecordingEndCondition.AverageOtherMotionDuration;
            string tail = !usesAverageDuration && entry.recordingEndDelay > 0f
                ? $",tail {entry.recordingEndDelay:F1}s" : "";
            string description = $"{entry.swarmType}:delay {entry.recordingStartDelay:F1}s,{rule}{tail}";

            if (!usesAverageDuration && entry.overrideRecordingTime > 0f)
            {
                description += $" (max {entry.overrideRecordingTime:F1}s)";
            }

            if (!usesAverageDuration && entry.endCondition != RecordingEndCondition.MotionSettled && entry.endConditionDwellTime > 0f)
            {
                description += $" (dwell {entry.endConditionDwellTime:F2}s)";
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

    private bool ValidateSingleMotionRecording()
    {
        if (GetSettingsFor(CurrentSwarmType).endCondition != RecordingEndCondition.AverageOtherMotionDuration)
            return true;

        Debug.LogError("[SimRecorder] AverageOtherMotionDuration requires Random in a Combinations or " +
                       "Swarm Type + Parameter batch with at least one other motion type.");
        return false;
    }

    private bool TryGetBatchRecordingOrder(List<SwarmType> types, out List<SwarmType> order)
    {
        order = types != null ? new List<SwarmType>(types) : new List<SwarmType>();
        if (order.Count == 0) return false;

        foreach (SwarmType type in order)
        {
            if (type != SwarmType.Random &&
                GetSettingsFor(type).endCondition == RecordingEndCondition.AverageOtherMotionDuration)
            {
                Debug.LogError($"[SimRecorder] AverageOtherMotionDuration is only available for Random, not {type}.");
                return false;
            }
        }

        if (!order.Contains(SwarmType.Random) ||
            GetSettingsFor(SwarmType.Random).endCondition != RecordingEndCondition.AverageOtherMotionDuration)
            return true;

        if (!order.Exists(type => type != SwarmType.Random))
        {
            Debug.LogError("[SimRecorder] Random needs another motion type in this batch to calculate its average duration.");
            return false;
        }

        // All source clips must finish before Random; keep the configured order among other types.
        order.RemoveAll(type => type == SwarmType.Random);
        foreach (SwarmType type in types)
            if (type == SwarmType.Random) order.Add(type);
        return true;
    }

    private static bool TryGetOtherMotionAverage(List<SimulationConfig> simulations, out float duration, out int sampleCount)
    {
        double total = 0d;
        sampleCount = 0;
        if (simulations != null)
        {
            foreach (SimulationConfig clip in simulations)
            {
                if (clip == null || string.IsNullOrEmpty(clip.endReason) ||
                    string.IsNullOrEmpty(clip.swarmType) || clip.swarmType == SwarmType.Random.ToString() ||
                    clip.recordedMotionDuration < 0f || float.IsNaN(clip.recordedMotionDuration) ||
                    float.IsInfinity(clip.recordedMotionDuration)) continue;

                total += clip.recordedMotionDuration;
                sampleCount++;
            }
        }

        duration = sampleCount > 0 ? (float)(total / sampleCount) : 0f;
        return sampleCount > 0;
    }

    /// <summary>Runs a motion window and adds the configured closing frames for recorded clips.</summary>
    private IEnumerator RunRecordingWindow(string outputFolder = null, string outputFileName = null,
        bool referenceOnly = false, List<SimulationConfig> batchSimulations = null)
    {
        // Trajectory capture runs for exactly the recorded window, so the json lines up with the mp4.
        SwarmTrajectoryRecorder trajectory = referenceOnly ? null : ResolveTrajectoryRecorder();
        if (captureTrajectories && trajectory != null)
        {
            trajectory.StartCapture(outputFileName);
            trajectory.SetRunContext(lastWallsDisabled, activeSpawnLayoutId, activeSpawnLayoutFingerprint);
        }

        SwarmType swarmType = CurrentSwarmType;
        MotionTypeRecordingSettings settings = GetSettingsFor(swarmType);
        GoalArea goalArea = ResolveGoalArea();
        SwarmDensityMonitor densityMonitor = ResolveDensityMonitor();

        float maxTime = settings.overrideRecordingTime > 0f ? settings.overrideRecordingTime : recordingTimePerSim;
        lastAverageOtherMotionDuration = 0f;
        lastAverageOtherMotionSampleCount = 0;
        bool averageDurationEnabled = swarmType == SwarmType.Random &&
                                      settings.endCondition == RecordingEndCondition.AverageOtherMotionDuration &&
                                      TryGetOtherMotionAverage(batchSimulations, out lastAverageOtherMotionDuration,
                                          out lastAverageOtherMotionSampleCount);
        if (averageDurationEnabled)
        {
            maxTime = lastAverageOtherMotionDuration;
            Debug.Log($"[SimRecorder] Random: recording for the average motion duration {maxTime:F2}s " +
                      $"of {lastAverageOtherMotionSampleCount} other clips in this batch.");
        }
        bool wantsGoalCondition = settings.endCondition == RecordingEndCondition.TargetAreaReached;
        bool goalConditionEnabled = wantsGoalCondition && goalArea != null && settings.goalAreaAgentPercent > 0f;
        bool wantsWallContactCondition = settings.endCondition == RecordingEndCondition.WallContactReached;
        ResetWallContactTracking(wantsWallContactCondition);
        lastWallContactAgentPercentThreshold = wantsWallContactCondition
            ? Mathf.Clamp(settings.wallContactAgentPercent, 0f, 100f)
            : 0f;
        bool wallContactConditionEnabled = wantsWallContactCondition && lastWallContactTrackedAgents > 0 &&
                                           lastWallContactAgentPercentThreshold > 0f;
        bool wantsDensityCondition = settings.endCondition == RecordingEndCondition.DensityRatioReached;
        bool densityConditionEnabled = wantsDensityCondition && densityMonitor != null && settings.densityTargetRatio > 0f;
        bool wantsAbsoluteArea = settings.endCondition == RecordingEndCondition.AbsoluteHullAreaReached;
        bool absoluteAreaEnabled = wantsAbsoluteArea && densityMonitor != null && settings.absoluteHullAreaTarget > 0f;

        // Resolve what densityTargetRatio is a ratio of, for this specific run.
        float perceptionRadius = swarmManager != null ? swarmManager.perceptionRadius : 0f;
        float runMaxSpeed = swarmManager != null ? swarmManager.maxSpeed : 0f;
        bool isReferenceRun = CurrentRunIsReference;

        resolvedReferenceValue = 0f;
        lastDensityBaselineSource = "recording start";
        referenceSampleBuffer.Clear();

        // A run that will serve as the reference has to stop when its spread stops changing, not
        // when it reaches some ratio of its own spawn area. Measured against its own start, a
        // dispersing swarm passes any ratio above 1 within a second or two and the clip ends while
        // the agents are still visibly spreading — which is exactly what the reference must not do,
        // since its settled value is what every randomised run is then judged against.
        bool referenceMustSettle = wantsDensityCondition && isReferenceRun &&
                                   settings.densityRatioBaseline == DensityRatioBaseline.NoRandomnessReference;

        // Cluster rule: count the groups now so the direction of the test is known. A flock starts
        // scattered and merges, a swarm starts whole and splits, and the same setting has to serve
        // both without the user restating which way it goes.
        bool wantsClusterCondition = settings.endCondition == RecordingEndCondition.ClusterCountReached;
        bool clusterConditionEnabled = wantsClusterCondition && swarmManager != null;
        int clusterBaseline = 0;
        bool clusterMerging = false;

        if (clusterConditionEnabled)
        {
            clusterBaseline = CountClusters(settings.minClusterSize);
            clusterMerging = settings.clusterCountTarget <= clusterBaseline;

            if (settings.clusterCountTarget == clusterBaseline)
            {
                clusterConditionEnabled = false;
                Debug.LogError(
                    $"[SimRecorder] {swarmType}: clusterCountTarget is {settings.clusterCountTarget}, " +
                    $"which is already the number of groups at recording start. The rule would fire " +
                    $"on the first frame. Pick a target the swarm has to reach — lower to end when it " +
                    $"merges, higher to end when it splits. Recording to the {maxTime:F2}s duration.");
            }
            else
            {
                Debug.Log($"[SimRecorder] {swarmType}: {clusterBaseline} groups at start, ending at " +
                          $"{settings.clusterCountTarget} " +
                          $"({(clusterMerging ? "merging" : "splitting")}, groups of " +
                          $"{settings.minClusterSize}+ agents).");
            }
        }

        if (wantsClusterCondition && swarmManager == null)
        {
            Debug.LogWarning($"[SimRecorder] {swarmType} is set to ClusterCountReached but there is no " +
                             $"swarm manager; falling back to the {maxTime:F2}s duration.");
        }

        bool wantsSettle = settings.endCondition == RecordingEndCondition.SpreadSettled || referenceMustSettle;
        bool settleEnabled = wantsSettle && densityMonitor != null;

        if (settleEnabled)
        {
            settleDetector.Reset();
            settleDetector.tolerance = Mathf.Max(0.0001f, settings.settleTolerance);
            settleDetector.holdSeconds = Mathf.Max(0f, settings.settleHoldTime);

            if (referenceMustSettle)
            {
                // The ratio rule is replaced for this run, not applied alongside it.
                densityConditionEnabled = false;
                Debug.Log($"[SimRecorder] {swarmType}: this is the no-randomness reference, so it runs " +
                          $"until its spread settles (slope under {settings.settleTolerance:P0} of peak " +
                          $"per second for {settings.settleHoldTime:F1}s) rather than to a ratio.");
            }
        }

        if (wantsSettle && densityMonitor == null)
        {
            Debug.LogWarning($"[SimRecorder] {swarmType} needs the spread settling rule but no density " +
                             $"monitor was found; falling back to the {maxTime:F2}s duration.");
        }

        // A ratio measured against this run's own start is satisfied the instant recording begins
        // when the target is 1, because the value and its baseline are the same number at that
        // moment. The clip then ends after only the dwell, with the swarm still at its spawn.
        // Against the no-randomness reference a target of 1 is perfectly meaningful — "spread as
        // far as the reference did" — so the guard applies only to the RecordingStart baseline.
        const float RatioMargin = 0.05f;
        if (wantsDensityCondition &&
            settings.densityRatioBaseline == DensityRatioBaseline.RecordingStart &&
            Mathf.Abs(settings.densityTargetRatio - 1f) < RatioMargin)
        {
            densityConditionEnabled = false;
            Debug.LogError(
                $"[SimRecorder] {swarmType}: densityTargetRatio is {settings.densityTargetRatio:F2} " +
                $"against the recording-start baseline, which is already true on the first frame — " +
                $"the ratio starts at exactly 1. Every clip would end after just the dwell with the " +
                $"swarm still at its spawn. Use a target clearly above 1 to grow (1.5, 2.0) or below " +
                $"it to shrink (0.4), or set 'Ratio Measured Against' to NoRandomnessReference, where " +
                $"1.0 means 'spread as far as the run without randomness'. Recording to the " +
                $"{maxTime:F2}s duration instead.");
        }

        if (wantsDensityCondition &&
            settings.densityRatioBaseline == DensityRatioBaseline.NoRandomnessReference &&
            !isReferenceRun)
        {
            string key = ReferenceKey(perceptionRadius, runMaxSpeed);

            if (referenceDensityValues.TryGetValue(key, out float reference) && reference > 0f)
            {
                resolvedReferenceValue = reference;
                lastDensityBaselineSource = $"no-randomness reference {reference:F2}";
            }
            else
            {
                // Falling back silently would put this clip on a different scale from the rest of
                // the batch, which is the whole thing the reference baseline exists to prevent.
                densityConditionEnabled = false;
                lastDensityBaselineSource = "MISSING reference";
                Debug.LogWarning(
                    $"[SimRecorder] {swarmType}: densityTargetRatio is set to measure against the " +
                    $"no-randomness reference, but none has been recorded for {key}. Record the " +
                    $"randomMovement = 0 runs before the randomised ones in the same batch " +
                    $"(put 0 first in Combination Param 1 Values). Falling back to the " +
                    $"{maxTime:F2}s duration for this clip.");
            }
        }

        bool wantsMotionSettle = settings.endCondition == RecordingEndCondition.MotionSettled;
        bool motionSettleEnabled = wantsMotionSettle && swarmManager != null;
        lastMotionSettleSettings = null;
        motionSettleDetector.Reset(0f);
        float motionSpacing = 0f;
        float nextMotionSpacingSample = 0f;
        float motionSpeedLimit = runMaxSpeed * Mathf.Clamp01(settings.motionSettleSpeedFraction);
        if (motionSettleEnabled)
        {
            motionSpacing = MeasureSettlingSpacing();
            motionSettleEnabled = motionSettlePositions.Count >= 2;
            if (motionSettleEnabled)
            {
                motionSettleDetector.requireLowSpeed = settings.motionSettleRequireLowSpeed;
                motionSettleDetector.windowSeconds = Mathf.Max(0.01f, settings.motionSettleWindowSeconds);
                motionSettleDetector.spacingTolerance = Mathf.Clamp01(settings.motionSettleSpacingTolerance);
                motionSettleDetector.holdSeconds = Mathf.Max(0f, settings.motionSettleHoldSeconds);
                motionSettleDetector.minimumRunSeconds = Mathf.Max(0f, settings.motionSettleMinimumRunSeconds);
                motionSettleDetector.contractionFraction = Mathf.Clamp01(settings.motionSettleContractionFraction);
                motionSettleDetector.Reset(motionSpacing);
                motionSettleDetector.Push(0f, motionSpacing, MaximumAgentSpeed(), motionSpeedLimit);
                lastMotionSettleSettings = settings;
                clusterBaseline = CountClusters(settings.minClusterSize);
            }
        }
        if (wantsMotionSettle && !motionSettleEnabled)
        {
            Debug.LogWarning($"[SimRecorder] {swarmType}: MotionSettled needs at least two agents " +
                             $"and a swarm manager; falling back to the {maxTime:F2}s duration.");
        }

        // MotionSettled owns its combined dwell; other rules retain their existing dwell.
        float dwellRequired = averageDurationEnabled || wantsMotionSettle
            ? 0f : Mathf.Max(0f, settings.endConditionDwellTime);
        float heldFor = 0f;

        lastEndReason = "timeout";
        // Reset per run: a clip that ends on the duration cap has no tail, and leaving the previous
        // run's value in place would report one it never recorded.
        lastEndDelay = 0f;
        overlayStatus = "Recording";
        lastGoalAreaName = goalArea != null ? goalArea.name : null;
        lastSwarmType = swarmType;
        lastEndConditionUsed = settings.endCondition;
        lastEndConditionPercent = wantsGoalCondition ? settings.goalAreaAgentPercent : 0f;
        lastMaxRecordingTime = maxTime;
        lastAbsoluteAreaTarget = wantsAbsoluteArea ? settings.absoluteHullAreaTarget : 0f;
        lastDwellTime = motionSettleEnabled ? motionSettleDetector.holdSeconds : dwellRequired;

        if (wantsGoalCondition && goalArea == null)
        {
            Debug.LogWarning($"[SimRecorder] {swarmType} is set to TargetAreaReached but has no goal area assigned; falling back to the {maxTime:F2}s duration.");
        }

        if (wantsWallContactCondition && !wallContactConditionEnabled)
        {
            Debug.LogWarning($"[SimRecorder] {swarmType}: WallContactReached needs spawned agents and a " +
                             $"positive agent percentage; falling back to the {maxTime:F2}s duration.");
        }

        if (wantsDensityCondition && densityMonitor == null)
        {
            Debug.LogWarning($"[SimRecorder] {swarmType} is set to DensityRatioReached but no density monitor was found; falling back to the {maxTime:F2}s duration.");
        }

        if (wantsAbsoluteArea && densityMonitor == null)
        {
            Debug.LogWarning($"[SimRecorder] {swarmType} is set to AbsoluteHullAreaReached but no density monitor was found; falling back to the {maxTime:F2}s duration.");
        }

        float timer = 0f;
        while (timer < maxTime)
        {
            yield return new WaitForEndOfFrame();
            timer += Time.deltaTime;

            if (wallContactConditionEnabled) UpdateWallContactOutcome();

            // A zero-randomness run is a reference for everything else at these settings, so keep
            // its metric as it goes. Sampled during the recorded window only, which is the same
            // span the clip and the trajectory cover.
            if (isReferenceRun && densityMonitor != null && densityMonitor.HasSampled)
            {
                referenceSampleBuffer.Add(densityMonitor.CurrentValue);
            }

            // Evaluate whichever rule this motion type uses, then apply the dwell requirement to it.
            bool conditionMet = false;
            string reason = null;
            string detail = null;

            // Fed every frame so the slope is measured over real elapsed time, whatever the frame rate.
            bool spreadSettled = false;
            if (settleEnabled && densityMonitor.HasSampled)
            {
                spreadSettled = settleDetector.Push(timer, densityMonitor.CurrentValue);
            }

            bool motionSettled = false;
            if (motionSettleEnabled)
            {
                if (timer >= nextMotionSpacingSample)
                {
                    motionSpacing = MeasureSettlingSpacing();
                    nextMotionSpacingSample = timer + Mathf.Min(0.1f, motionSettleDetector.windowSeconds / 2f);
                }
                motionSettled = motionSettleDetector.Push(timer, motionSpacing,
                    MaximumAgentSpeed(), motionSpeedLimit);
            }

            if (motionSettled)
            {
                conditionMet = true;
                string settledPrefix = motionSettleDetector.requireLowSpeed ? "settled" : "density-settled";
                reason = settledPrefix + (motionSettleDetector.HasContracted
                    ? "-after-contraction" : "-without-contraction");
                detail = $"local spacing settled at {motionSettleDetector.Spacing:F3} " +
                         $"(start {motionSettleDetector.Baseline:F3}, range {motionSettleDetector.SpacingRange:F3}), " +
                         (motionSettleDetector.requireLowSpeed
                             ? $"maximum speed {motionSettleDetector.MaxAgentSpeed:F3} <= {motionSpeedLimit:F3}, "
                             : $"residual maximum speed {motionSettleDetector.MaxAgentSpeed:F3} (not a stop constraint), ") +
                         $"held {motionSettleDetector.HeldFor:F2}s";
            }
            else if (spreadSettled)
            {
                conditionMet = true;
                reason = "spread-settled";
                detail = $"{densityMonitor.MetricName} stopped changing — {settleDetector.Describe()}";
            }
            else if (clusterConditionEnabled)
            {
                int groups = CountClusters(settings.minClusterSize);
                bool met = clusterMerging
                    ? groups <= settings.clusterCountTarget
                    : groups >= settings.clusterCountTarget;

                if (met)
                {
                    conditionMet = true;
                    reason = "cluster-count";
                    detail = $"{groups} groups " +
                             $"({(clusterMerging ? "merged down from" : "split up from")} {clusterBaseline}) " +
                             $"reached target {settings.clusterCountTarget} — " +
                             $"{SwarmClusterMetrics.Describe(clusterSizeBuffer)}";
                }
            }
            else if (goalConditionEnabled && goalArea.IsPercentReached(settings.goalAreaAgentPercent))
            {
                conditionMet = true;
                reason = "goal-area";
                detail = $"goal area '{goalArea.name}' holding {goalArea.AgentsInside}/{goalArea.TrackedAgents} agents " +
                         $"({goalArea.PercentInside:F1}% >= {settings.goalAreaAgentPercent:F1}%)";
            }
            else if (wallContactConditionEnabled && lastPercentAgentsHitWalls >= lastWallContactAgentPercentThreshold)
            {
                conditionMet = true;
                reason = "wall-contact";
                detail = $"{lastUniqueAgentsHitWalls}/{lastWallContactTrackedAgents} unique agents touched a wall " +
                         $"({lastPercentAgentsHitWalls:F1}% >= {lastWallContactAgentPercentThreshold:F1}%)";
            }
            else if (densityConditionEnabled && IsDensityRatioReached(densityMonitor, settings))
            {
                conditionMet = true;
                reason = "density-ratio";

                if (resolvedReferenceValue > 0f)
                {
                    float target = settings.densityTargetRatio * resolvedReferenceValue;
                    detail = $"{densityMonitor.MetricName} {densityMonitor.CurrentValue:F3} vs the " +
                             $"no-randomness reference {resolvedReferenceValue:F3} " +
                             $"= ratio {densityMonitor.CurrentValue / resolvedReferenceValue:F2} " +
                             $"(target {settings.densityTargetRatio:F2}, i.e. {target:F2})";
                }
                else
                {
                    detail = $"{densityMonitor.MetricName} {densityMonitor.CurrentValue:F3} / baseline {densityMonitor.Baseline:F3} " +
                             $"= ratio {densityMonitor.Ratio:F2} (target {settings.densityTargetRatio:F2})";
                }
            }
            else if (absoluteAreaEnabled && densityMonitor.IsHullAreaReached(settings.absoluteHullAreaTarget))
            {
                conditionMet = true;
                reason = "absolute-area";
                detail = $"hull area {densityMonitor.LastHullArea:F1} u² reached target {settings.absoluteHullAreaTarget:F1} u² " +
                         $"(started at {densityMonitor.BaselineHullArea:F1})";
            }

            if (conditionMet)
            {
                heldFor += Time.deltaTime;

                if (heldFor >= dwellRequired)
                {
                    lastEndReason = reason;
                    string held = dwellRequired > 0f ? $", held {heldFor:F2}s" : "";
                    Debug.Log($"[SimRecorder] {swarmType}: {detail} after {timer:F2}s{held} — ending recording early.");

                    // Keep rolling for the tail so the clip does not cut on the exact frame the
                    // rule fired. Recorded, unlike the start delay, and counted into the duration.
                    if (!referenceOnly && settings.recordingEndDelay > 0f)
                    {
                        Debug.Log($"[SimRecorder] {swarmType}: recording a further " +
                                  $"{settings.recordingEndDelay:F2}s after the rule fired.");

                        float tail = 0f;
                        while (tail < settings.recordingEndDelay)
                        {
                            yield return new WaitForEndOfFrame();
                            tail += Time.deltaTime;
                            timer += Time.deltaTime;
                            if (wallContactConditionEnabled) UpdateWallContactOutcome();
                        }

                        lastEndDelay = tail;
                    }

                    break;
                }
            }
            else
            {
                // Fell back below the threshold, so the dwell has to start again.
                heldFor = 0f;
            }
        }

        lastRecordedMotionDuration = timer;
        if (averageDurationEnabled)
        {
            lastEndReason = "average-other-motion-duration";
        }

        // Mark the closing frames while the recorder is still running, so the clip itself shows
        // where it stopped. A single frame at 60 fps is invisible on playback, hence the hold.
        overlayStatus = "Video Ended";

        if (!referenceOnly && videoEndedMarkerSeconds > 0f)
        {
            float marker = 0f;
            while (marker < videoEndedMarkerSeconds)
            {
                yield return new WaitForEndOfFrame();
                marker += Time.deltaTime;
                timer += Time.deltaTime;
                if (wallContactConditionEnabled) UpdateWallContactOutcome();
            }
        }

        lastRecordedDuration = timer;

        // Store the settled value now the window has closed, so the randomised runs that follow in
        // this batch can be measured against it.
        if (isReferenceRun && (!referenceMustSettle || lastEndReason == "spread-settled") &&
            (!wantsMotionSettle || motionSettleDetector.IsSettled))
        {
            StoreReferenceValue(perceptionRadius, runMaxSpeed);
        }

        // Counted for every run, not only when the cluster rule is in use: it is cheap and it makes
        // every clip's config say how fragmented the swarm ended up.
        lastClusterCountAtStart = clusterBaseline;
        lastClusterCountAtEnd = CountClusters(settings.minClusterSize);
        lastMinClusterSize = settings.minClusterSize;
        lastClusterCountTarget = wantsClusterCondition ? settings.clusterCountTarget : 0;

        lastAgentsInsideGoalArea = goalArea != null ? goalArea.AgentsInside : 0;
        lastPercentInsideGoalArea = goalArea != null ? goalArea.PercentInside : 0f;
        lastDensityMetricName = densityMonitor != null ? densityMonitor.MetricName : null;
        lastDensityBaseline = densityMonitor != null ? densityMonitor.Baseline : 0f;
        lastDensityValue = densityMonitor != null ? densityMonitor.CurrentValue : 0f;
        lastDensityRatio = densityMonitor != null ? densityMonitor.Ratio : 0f;
        lastDensityTargetRatio = wantsDensityCondition ? settings.densityTargetRatio : 0f;

        if (lastEndReason == "timeout")
        {
            string goalSuffix = goalArea != null
                ? $" — goal area '{goalArea.name}' held {lastAgentsInsideGoalArea}/{goalArea.TrackedAgents} agents ({lastPercentInsideGoalArea:F1}%)."
                : ".";
            string settleSuffix = motionSettleEnabled
                ? $" Spacing range {motionSettleDetector.SpacingRange:F4}/{motionSettleDetector.spacingTolerance * motionSettleDetector.Baseline:F4} " +
                  $"over {motionSettleDetector.windowSeconds:F1}s; hold {motionSettleDetector.HeldFor:F2}/{motionSettleDetector.holdSeconds:F2}s; " +
                  $"max speed {motionSettleDetector.MaxAgentSpeed:F3}/{motionSettleDetector.SpeedLimit:F3} " +
                  $"(required: {motionSettleDetector.requireLowSpeed})."
                : string.Empty;
            Debug.Log($"[SimRecorder] {swarmType}: recording hit the {maxTime:F2}s duration{goalSuffix}{settleSuffix}");
        }

        // Hold the "Video Finished" card while still recording so it is baked into the clip.
        lastOverlayDuration = 0f;
        if (!referenceOnly && showVideoFinishedOverlay && videoFinishedOverlayDuration > 0f)
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

        lastTrajectoryPath = null;
        if (captureTrajectories && trajectory != null && trajectory.IsCapturing)
        {
            trajectory.SetOutcome(lastEndReason, lastEndConditionUsed.ToString(), lastDensityTargetRatio);

            lastTrajectoryPath = string.IsNullOrEmpty(outputFolder)
                ? trajectory.StopCaptureAndSaveToManualFolder(outputFileName)
                : trajectory.StopCaptureAndSave(outputFolder, outputFileName);
        }
    }

    private float MeasureSettlingSpacing()
    {
        SwarmDensityMetrics.CollectPositions(swarmManager.agents, motionSettlePositions);
        return SwarmDensityMetrics.MeanKnnDistance(motionSettlePositions, 3);
    }

    private float MaximumAgentSpeed()
    {
        GameObject[] agents = swarmManager.agents;
        SwarmAgent[] scripts = swarmManager.AgentScripts;
        if (agents == null) return float.PositiveInfinity;

        float maximum = 0f;
        int count = 0;
        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] == null) continue;
            SwarmAgent agent = i < scripts.Length ? scripts[i] : null;
            if (agent == null) return float.PositiveInfinity;
            float speed = agent.Velocity.magnitude;
            if (float.IsNaN(speed) || float.IsInfinity(speed)) return float.PositiveInfinity;
            maximum = Mathf.Max(maximum, speed);
            count++;
        }
        return count >= 2 ? maximum : float.PositiveInfinity;
    }

    /// <summary>The trajectory recorder in play, preferring the one the UI provisioned.</summary>
    private SwarmTrajectoryRecorder ResolveTrajectoryRecorder()
    {
        if (uiController != null && uiController.trajectoryRecorder != null) return uiController.trajectoryRecorder;
        if (swarmManager != null) return swarmManager.GetComponent<SwarmTrajectoryRecorder>();
        return null;
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
        config.recordedMotionDuration = lastRecordedMotionDuration;
        config.averageOtherMotionDuration = lastAverageOtherMotionDuration;
        config.averageOtherMotionSampleCount = lastAverageOtherMotionSampleCount;
        config.goalAreaName = lastGoalAreaName;
        config.endConditionUsed = lastEndConditionUsed.ToString();
        config.recordingStartDelay = lastStartDelay;
        config.recordingEndDelay = lastEndDelay;
        config.wallsDisabled = lastWallsDisabled;
        config.densityMetric = lastDensityMetricName;
        config.densityBaseline = lastDensityBaseline;
        config.densityAtEnd = lastDensityValue;
        config.densityRatioAtEnd = lastDensityRatio;
        config.densityTargetRatio = lastDensityTargetRatio;
        config.densityBaselineSource = lastDensityBaselineSource;
        config.densityReferenceValue = resolvedReferenceValue;
        config.absoluteHullAreaTarget = lastAbsoluteAreaTarget;
        config.clusterCountTarget = lastClusterCountTarget;
        config.clusterCountAtStart = lastClusterCountAtStart;
        config.clusterCountAtEnd = lastClusterCountAtEnd;
        config.minClusterSize = lastMinClusterSize;
        config.endConditionDwellTime = lastDwellTime;
        config.wallContactAgentPercentThreshold = lastWallContactAgentPercentThreshold;
        config.wallContactTrackedAgents = lastWallContactTrackedAgents;
        config.uniqueAgentsHitWallsAtEnd = lastUniqueAgentsHitWalls;
        config.percentAgentsHitWallsAtEnd = lastPercentAgentsHitWalls;

        if (uiController != null)
        {
            config.requestedAgents = uiController.RequestedAgentCount;
            config.agentSpawnShortfall = uiController.AgentSpawnShortfall;

            if (config.agentSpawnShortfall)
            {
                Debug.LogWarning($"[SimRecorder] '{config.fileName}' recorded with only " +
                                 $"{config.numAgents} of {config.requestedAgents} agents.");
            }
        }
        config.goalAreaAgentPercentThreshold = lastEndConditionPercent;
        config.maxRecordingTime = lastMaxRecordingTime;
        config.agentsInsideGoalAreaAtEnd = lastAgentsInsideGoalArea;
        config.percentInsideGoalAreaAtEnd = lastPercentInsideGoalArea;
        MotionTypeRecordingSettings motionSettings = lastMotionSettleSettings;
        config.motionSettleRequireLowSpeed = motionSettings != null && motionSettleDetector.requireLowSpeed;
        config.motionSettleWindowSeconds = motionSettings != null ? motionSettleDetector.windowSeconds : 0f;
        config.motionSettleSpacingTolerance = motionSettings != null ? motionSettleDetector.spacingTolerance : 0f;
        config.motionSettleSpeedFraction = motionSettings != null ? Mathf.Clamp01(motionSettings.motionSettleSpeedFraction) : 0f;
        config.motionSettleHoldSeconds = motionSettings != null ? motionSettleDetector.holdSeconds : 0f;
        config.motionSettleMinimumRunSeconds = motionSettings != null ? motionSettleDetector.minimumRunSeconds : 0f;
        config.motionSettleContractionFraction = motionSettings != null ? motionSettleDetector.contractionFraction : 0f;
        config.settlingSpacingBaseline = motionSettings != null ? motionSettleDetector.Baseline : 0f;
        config.settlingSpacingAtTermination = motionSettings != null ? motionSettleDetector.Spacing : 0f;
        config.settlingSpacingRange = motionSettings != null ? motionSettleDetector.SpacingRange : 0f;
        config.settlingMaxSpeedAtTermination = motionSettings != null ? motionSettleDetector.MaxAgentSpeed : 0f;
        config.settlingSpeedLimit = motionSettings != null ? motionSettleDetector.SpeedLimit : 0f;
        config.settledWithContraction = motionSettings != null && motionSettleDetector.IsSettled && motionSettleDetector.HasContracted;
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
            if (recordingOverlayStyle == null)
            {
                recordingOverlayStyle = new GUIStyle
                {
                    fontSize = 36,
                    fontStyle = FontStyle.Bold,
                    wordWrap = true
                };
                recordingOverlayStyle.normal.textColor = Color.black;
                recordingOverlayShadowStyle = new GUIStyle(recordingOverlayStyle);
                recordingOverlayShadowStyle.normal.textColor = Color.white;
            }

            // Read from the manager rather than the per-batch display fields, so the line is the
            // values actually in force whichever batch type is running.
            float cohesion = swarmManager != null ? swarmManager.cohesionIntensity : 0f;
            float perception = swarmManager != null ? swarmManager.perceptionRadius : 0f;
            float speed = swarmManager != null ? swarmManager.maxSpeed : 0f;
            float noise = swarmManager != null ? swarmManager.randomMovementIntensity : 0f;

            // Distinct agents that have reached this motion type's goal area at any point, not the
            // number standing in it right now. A running total is what "reached" means, and it never
            // drops when an agent drifts back out. Shows "-" rather than 0/0 when no goal area is
            // assigned, so an unwired area is not mistaken for a swarm that never arrived.
            GoalArea overlayGoalArea = ResolveGoalArea();
            string reached = overlayGoalArea != null
                ? $"{overlayGoalArea.AgentsEverInside}/{overlayGoalArea.TrackedAgents}"
                : "-";

            // Groups the swarm is currently in, by the same perception-graph rule the cluster end
            // condition uses, so the clip shows what that rule is acting on.
            MotionTypeRecordingSettings overlaySettings = GetSettingsFor(CurrentSwarmType);
            string groups = swarmManager != null
                ? $"{CountClusters(overlaySettings.minClusterSize)}"
                : "-";

            string displayText = $"{overlayStatus} | Perception: {perception:F2} " +
                                 $"| Groups: {groups} | Agents reached: {reached}";
            if (overlaySettings.endCondition == RecordingEndCondition.WallContactReached)
            {
                displayText = $"{overlayStatus} | Perception: {perception:F2} | Groups: {groups} " +
                              $"| Agents hit walls: {lastUniqueAgentsHitWalls}/{lastWallContactTrackedAgents} " +
                              $"({lastPercentAgentsHitWalls:F1}%)";
            }

            if (isSingleParameterBatchMode)
            {
                displayText = $"{overlayStatus} | Cohesion: {cohesion:F2} " +
                              $"| Perception: {perception:F2} | Speed: {speed:F2} | Noise: {noise:F2}";
            }

            if (!isSingleParameterBatchMode && overlaySettings.endCondition == RecordingEndCondition.MotionSettled)
            {
                displayText += $"\nSettling | Spacing: {motionSettleDetector.Spacing:F3} " +
                               $"| Range: {motionSettleDetector.SpacingRange:F3}/{motionSettleDetector.spacingTolerance * motionSettleDetector.Baseline:F3} " +
                               (motionSettleDetector.requireLowSpeed
                                   ? $"| Speed: {motionSettleDetector.MaxAgentSpeed:F3}/{motionSettleDetector.SpeedLimit:F3} "
                                   : "| Density equilibrium ") +
                               $"| Held: {motionSettleDetector.HeldFor:F1}/{motionSettleDetector.holdSeconds:F1}s";
            }

            float overlayWidth = Mathf.Max(1f, Screen.width - 42f);
            float overlayHeight = recordingOverlayStyle.CalcHeight(new GUIContent(displayText), overlayWidth);
            GUI.Label(new Rect(22, 22, overlayWidth, overlayHeight), displayText, recordingOverlayShadowStyle);
            GUI.Label(new Rect(20, 20, overlayWidth, overlayHeight), displayText, recordingOverlayStyle);
        }

        // Drawn last so it covers the parameter overlay text as well.
        if (videoFinishedOverlayActive)
        {
            DrawVideoFinishedOverlay();
        }
    }
    public void StartSingleParameterBatchRecording()
    {
        if (!isRecording && ValidateSingleMotionRecording())
        {
            StartCoroutine(SingleParameterBatchRecordCoroutine());
        }
    }

    public void StartMatchedStartBatchRecording()
    {
        if (!isRecording && ValidateSingleMotionRecording())
        {
            StartCoroutine(MatchedStartBatchRecordCoroutine());
        }
    }

    /// <summary>
    /// Sweeps a parameter several times over, each pass starting from an identical set of agent
    /// positions.
    ///
    /// Spawning is random, so an ordinary sweep confounds the parameter with wherever the agents
    /// happened to start. Here each layout is captured once and replayed for every parameter
    /// value, which makes the comparison paired: within a layout, the only thing that differs is
    /// the parameter. Several layouts are used so a conclusion does not rest on one lucky spawn.
    /// </summary>
    /// <summary>
    /// Loads the spawn layouts named in <c>reuseSpawnLayouts</c> from SimulationRecordings/SpawnLayouts.
    ///
    /// Shared by the matched start and Combinations batches so both resolve names identically. A
    /// name that cannot be loaded is reported rather than skipped quietly: a batch silently running
    /// on four layouts instead of five is a hard thing to notice afterwards, and it unbalances
    /// every paired comparison built on it.
    /// </summary>
    private List<SpawnLayout> LoadNamedSpawnLayouts(SwarmType? expectedType = null)
    {
        List<SpawnLayout> layouts = new List<SpawnLayout>();
        if (reuseSpawnLayouts == null) return layouts;

        foreach (string name in reuseSpawnLayouts)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            string path = Path.Combine(SpawnLayout.DefaultFolder(saveFolder), name.Trim() + ".json");
            SpawnLayout loaded = SpawnLayout.Load(path);

            if (loaded == null)
            {
                Debug.LogError($"[SimRecorder] Could not load spawn layout '{name}' from {path}.");
                continue;
            }

            // Each motion type spawns in its own area, so a layout captured for one type places
            // agents where a different type never starts. It still runs, which is exactly why this
            // has to be said out loud rather than left to be noticed in the data.
            if (expectedType.HasValue && !string.IsNullOrEmpty(loaded.swarmType) &&
                !loaded.swarmType.Equals(expectedType.Value.ToString(),
                                         System.StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning($"[SimRecorder] Layout '{loaded.layoutId}' was captured for " +
                                 $"{loaded.swarmType} in '{loaded.spawnAreaName}', but this batch " +
                                 $"records {expectedType.Value}, which spawns somewhere else. The " +
                                 $"agents will start at the {loaded.swarmType} positions. Capture " +
                                 $"layouts for {expectedType.Value} instead unless that is deliberate.");
            }

            layouts.Add(loaded);
            Debug.Log($"[SimRecorder] Reusing layout {loaded} from {path}");
        }

        return layouts;
    }

    /// <summary>
    /// The named layouts that belong to one motion type.
    ///
    /// Each motion type spawns in its own area, so a batch sweeping several types needs a set per
    /// type rather than one shared set. Layouts captured for a different type are skipped here
    /// rather than warned about: when the batch covers several types a mismatch is the ordinary
    /// case, not a mistake, and whatever is left over belongs to one of the other types.
    /// </summary>
    private List<SpawnLayout> LoadNamedSpawnLayoutsFor(SwarmType type)
    {
        List<SpawnLayout> matching = new List<SpawnLayout>();
        if (reuseSpawnLayouts == null) return matching;

        foreach (string name in reuseSpawnLayouts)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            string path = Path.Combine(SpawnLayout.DefaultFolder(saveFolder), name.Trim() + ".json");
            SpawnLayout loaded = SpawnLayout.Load(path);

            if (loaded == null)
            {
                Debug.LogError($"[SimRecorder] Could not load spawn layout '{name}' from {path}.");
                continue;
            }

            // A layout saved before the motion type was recorded has nothing to match against.
            // Taking it is better than discarding the only layouts an older project has, but it
            // is worth saying, because it is the one route back to the old mixed-up behaviour.
            if (string.IsNullOrEmpty(loaded.swarmType))
            {
                Debug.LogWarning($"[SimRecorder] Layout '{loaded.layoutId}' does not record which " +
                                 $"motion type it was captured for, so it is being used for {type}. " +
                                 $"Re-capture it if the agents start in the wrong place.");
                matching.Add(loaded);
                continue;
            }

            if (!loaded.swarmType.Equals(type.ToString(), System.StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            matching.Add(loaded);
            Debug.Log($"[SimRecorder] Reusing layout {loaded} for {type} from {path}");
        }

        return matching;
    }

    /// <summary>
    /// Generates fresh spawn layouts for the motion type currently selected, and saves them.
    ///
    /// Used when a Combinations batch is set to run on layouts but none are named. Generating is
    /// the right thing here: the new layouts are shared by every condition inside this batch, so
    /// the batch is internally paired. What they will not do is line up with layouts from an
    /// earlier batch, which is why it is logged rather than done quietly.
    /// </summary>
    private IEnumerator CaptureFreshLayouts(string timestampFolder, List<SpawnLayout> into)
    {
        int wanted = Mathf.Max(1, matchedStartLayouts);

        for (int l = 0; l < wanted; l++)
        {
            uiController.ResetScene();
            yield return null;   // let the spawn settle before snapshotting it

            SpawnLayout layout = uiController.CaptureSpawnLayout($"{timestampFolder}_layout_{l:D2}");
            into.Add(layout);

            if (saveSpawnLayouts)
            {
                string path = Path.Combine(SpawnLayout.DefaultFolder(saveFolder), layout.layoutId + ".json");
                layout.Save(path);
                Debug.Log($"[SimRecorder] Captured {layout}, fingerprint {layout.Fingerprint()} -> {path}");
            }
        }
    }

    private IEnumerator MatchedStartBatchRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = false;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = true;
        isSwarmTypeBatchMode = false;

        if (uiController == null || swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing uiController or swarmManager; cannot start matched start batch.");
            isRecording = false;
            isSingleParameterBatchMode = false;
            yield break;
        }

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = $"MatchedStart_{singleBatchParameter}";
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath)) Directory.CreateDirectory(targetFolderPath);

        uiController.showUI = false;

        List<SimulationConfig> simulations = new List<SimulationConfig>();
        List<SpawnLayout> layouts = new List<SpawnLayout>();

        // Either load the layouts named in the Inspector, or generate fresh ones.
        if (reuseSpawnLayouts != null && reuseSpawnLayouts.Count > 0)
        {
            layouts.AddRange(LoadNamedSpawnLayouts(CurrentSwarmType));

            if (layouts.Count == 0)
            {
                Debug.LogError("[SimRecorder] None of the named layouts could be loaded; aborting.");
                uiController.showUI = true;
                isRecording = false;
                isSingleParameterBatchMode = false;
                yield break;
            }
        }
        else
        {
            for (int l = 0; l < Mathf.Max(1, matchedStartLayouts); l++)
            {
                // A fresh random spawn becomes this layout's fixed starting arrangement.
                uiController.ResetScene();
                yield return null;   // let the spawn settle before snapshotting it

                SpawnLayout layout = uiController.CaptureSpawnLayout($"{timestampFolder}_layout_{l:D2}");
                layouts.Add(layout);

                if (saveSpawnLayouts)
                {
                    string path = Path.Combine(SpawnLayout.DefaultFolder(saveFolder), layout.layoutId + ".json");
                    layout.Save(path);
                    Debug.Log($"[SimRecorder] Captured {layout}, fingerprint {layout.Fingerprint()} -> {path}");
                }
            }
        }

        int clipIndex = 0;
        int totalClips = layouts.Count * Mathf.Max(1, singleParamIterations);
        Debug.Log($"[SimRecorder] Matched start batch: {layouts.Count} layouts x " +
                  $"{singleParamIterations} values of {singleBatchParameter} = {totalClips} clips.");

        foreach (SpawnLayout layout in layouts)
        {
            Debug.Log($"[SimRecorder] === Layout {layout.layoutId} ({layout.Count} agents, " +
                      $"fingerprint {layout.Fingerprint()}) ===");

            for (int i = 0; i < singleParamIterations; i++)
            {
                float currentParam = singleParamStart + (i * singleParamStep);
                currentParam1DisplayValue = currentParam;

                uiController.SetParameter(singleBatchParameter, currentParam);

                // The whole point: identical positions every time, only the parameter differs.
                activeSpawnLayoutId = layout.layoutId;
                activeSpawnLayoutFingerprint = layout.Fingerprint();

                uiController.SpawnFromLayout(layout);
                uiController.SetMotion(true);

                yield return PrepareRecordingForMotionType();

                string fileName = SanitizeFileName(
                    $"{layout.layoutId}_{singleBatchParameter.ToString().ToLower()}_{currentParam:F2}");

                SimulationConfig config = new SimulationConfig
                {
                    fileName = fileName,
                    variedParameter = paramFolderName,
                    variedParameterValue = currentParam,
                    parameter1 = singleBatchParameter.ToString(),
                    parameter1Value = currentParam,
                    parameter2 = "SpawnLayout",
                    parameter2Value = layouts.IndexOf(layout),
                    swarmType = uiController.SelectedSwarmType.ToString(),
                    spawnLayoutId = layout.layoutId,
                    spawnLayoutFingerprint = layout.Fingerprint(),
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
                videoRecorder.name = "Matched Start Recorder";
                videoRecorder.Enabled = true;
                videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
                videoRecorder.OutputFile = Path.Combine(targetFolderPath, fileName);

                videoRecorder.ImageInputSettings = new GameViewInputSettings
                {
                    OutputWidth = outputWidth,
                    OutputHeight = outputHeight
                };

                videoRecorder.AudioInputSettings.PreserveAudio = false;

                controllerSettings.AddRecorderSettings(videoRecorder);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = recordingFrameRate;

                recorderController.PrepareRecording();
                recorderController.StartRecording();
#else
                Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                yield return RunRecordingWindow(targetFolderPath, fileName);
                ApplyRecordingOutcome(config);

#if UNITY_EDITOR
                recorderController.StopRecording();
#endif

                uiController.SetMotion(false);
                clipIndex++;
                Debug.Log($"[SimRecorder] Saved matched start clip {clipIndex}/{totalClips} " +
                          $"({lastEndReason}, {lastRecordedDuration:F2}s) -> {fileName}.mp4");
            }
        }

        BatchConfig batchConfig = new BatchConfig
        {
            batchType = "matched-start",
            folderName = paramFolderName,
            timestamp = timestampFolder,
            recordingTimePerSim = recordingTimePerSim,
            endConditionsPerMotionType = DescribeEndConditions(),
            frameRate = recordingFrameRate,
            simulationStep = swarmManager != null ? swarmManager.simulationStep : 0f,
            videoFinishedOverlayDuration = showVideoFinishedOverlay ? videoFinishedOverlayDuration : 0f,
            saveFolder = saveFolder,
            simulations = simulations.ToArray()
        };

        File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"),
                          JsonUtility.ToJson(batchConfig, true));

        uiController.showUI = true;
        uiController.SetMotion(false);

        activeSpawnLayoutId = null;
        activeSpawnLayoutFingerprint = null;
        isRecording = false;
        isSingleParameterBatchMode = false;
        Debug.Log($"[SimRecorder] Matched start batch finished: {clipIndex} clips from " +
                  $"{layouts.Count} layouts.");
    }

    public void StartBatchRecording()
    {
        if (!isRecording && ValidateSingleMotionRecording())
        {
            StartCoroutine(BatchRecordCoroutine());
        }
    }

    public void StartObstacleBatchRecording()
    {
        if (!isRecording && ValidateSingleMotionRecording())
        {
            StartCoroutine(ObstacleBatchRecordCoroutine());
        }
    }

    public void StartObstacleSpawnLocationBatchRecording()
    {
        if (!isRecording && ValidateSingleMotionRecording())
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
        if (!isRecording && ValidateSingleMotionRecording())
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
        hideOverlayText = !showRecordingOverlay;

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
            OutputWidth = outputWidth,
            OutputHeight = outputHeight
        };

        videoRecorder.AudioInputSettings.PreserveAudio = false;

        controllerSettings.AddRecorderSettings(videoRecorder);
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = recordingFrameRate;

        recorderController.PrepareRecording();
        recorderController.StartRecording();
#else
        Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

        yield return RunRecordingWindow(currentFolderPath, fileName);

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
        hideOverlayText = !showRecordingOverlay;
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

        if (!TryGetBatchRecordingOrder(combinationSwarmTypes, out List<SwarmType> recordingTypes))
        {
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
        referenceDensityValues.Clear();

        // No-randomness references are kept apart from the clips they baseline, in a child folder
        // with their own batch_config.json. Keyed by ReferenceKey so a clip that reuses an
        // existing reference (same layout and settings) can still name the file it was judged by.
        string referenceFolderPath = Path.Combine(targetFolderPath, "NoRandomnessReferences");
        List<SimulationConfig> referenceSimulations = new List<SimulationConfig>();
        Dictionary<string, string> referenceFileByKey = new Dictionary<string, string>();

        if (combinationParam1Values == null || combinationParam1Values.Length == 0 || combinationParam2Values == null || combinationParam2Values.Length == 0 || combinationParam3Values == null || combinationParam3Values.Length == 0)
        {
            Debug.LogWarning("[SimRecorder] One or more combination value arrays are empty; nothing to record.");
            isRecording = false;
            hideOverlayText = false;
            yield break;
        }

        // Spawn layouts, one set per motion type. Each type spawns in its own area, so a single
        // shared set would start every type from wherever the first type happens to spawn. A list
        // holding null entries means fresh random spawns, one per repetition of each combination.
        Dictionary<SwarmType, List<SpawnLayout>> layoutsByType =
            new Dictionary<SwarmType, List<SpawnLayout>>();

        foreach (SwarmType layoutType in recordingTypes)
        {
            if (layoutsByType.ContainsKey(layoutType)) continue;

            if (!combinationsUseSpawnLayouts)
            {
                List<SpawnLayout> freshRuns = new List<SpawnLayout>();
                for (int repeat = 0; repeat < Mathf.Max(1, combinationsRepetitions); repeat++)
                    freshRuns.Add(null);
                layoutsByType[layoutType] = freshRuns;
                continue;
            }

            List<SpawnLayout> forType = LoadNamedSpawnLayoutsFor(layoutType);

            if (forType.Count == 0)
            {
                // Nothing named for this type: generate a set for it. They are shared by every
                // condition of this type in this batch, so it is internally paired — it just will
                // not line up with any earlier batch's layouts.
                Debug.Log($"[SimRecorder] No spawn layouts named for {layoutType}. Capturing " +
                          $"{Mathf.Max(1, matchedStartLayouts)} fresh ones in its own spawn area. " +
                          $"These will NOT match layouts from earlier batches — name them in " +
                          $"'Reuse Spawn Layouts' to reuse them.");

                uiController.SetSwarmType(layoutType);
                currentSwarmTypeDisplay = layoutType;
                yield return null;   // let the type switch take effect before spawning into it

                // Per-type prefix, so the four sets in a four-type batch cannot overwrite each
                // other and each name says which type it belongs to.
                yield return CaptureFreshLayouts(
                    $"{timestampFolder}_{layoutType.ToString().ToLower()}", forType);
            }

            if (forType.Count == 0)
            {
                Debug.LogError($"[SimRecorder] Could not obtain any spawn layouts for {layoutType}; aborting.");
                uiController.showUI = true;
                isRecording = false;
                hideOverlayText = false;
                yield break;
            }

            layoutsByType[layoutType] = forType;
        }

        if (combinationsUseSpawnLayouts)
        {
            foreach (KeyValuePair<SwarmType, List<SpawnLayout>> entry in layoutsByType)
            {
                Debug.Log($"[SimRecorder] {entry.Key}: {entry.Value.Count} spawn layouts " +
                          $"({string.Join(", ", entry.Value.ConvertAll(l => l.layoutId))}).");
            }

            Debug.Log("[SimRecorder] Combinations will run every combination of a motion type across " +
                      "that type's own layouts, so the batch can be analysed by matched_start.py.");
        }

        int combinationIndex = 0;
        bool referenceFailed = false;

        // Upper bound on the number of clips (duplicate combinations are skipped as we go).
        int plannedCombinations = 0;
        foreach (SwarmType plannedType in recordingTypes)
        {
            int conditions = combinationParam1Values.Length * combinationParam2Values.Length * combinationParam3Values.Length;

            // Layout counts can differ between types — one may have ten named while another had
            // to capture fresh ones — so the total is summed per type rather than multiplied once.
            plannedCombinations += conditions * layoutsByType[plannedType].Count;
        }

        Debug.Log($"[SimRecorder] Combinations batch starting — up to {plannedCombinations} clips across types: {string.Join(", ", recordingTypes)}.");

        for (int typeIndex = 0; typeIndex < recordingTypes.Count && !referenceFailed; typeIndex++)
        {
            SwarmType swarmType = recordingTypes[typeIndex];

            // This type's own layouts, captured in this type's own spawn area.
            List<SpawnLayout> layouts = layoutsByType[swarmType];

            Debug.Log($"[SimRecorder] === Motion type {typeIndex + 1}/{recordingTypes.Count}: {swarmType} " +
                      $"({layouts.Count} layout{(layouts.Count == 1 ? "" : "s")}) ===");

            for (int param1Index = 0; param1Index < combinationParam1Values.Length && !referenceFailed; param1Index++)
            {
                float currentParam1 = ResolveCombinationValue(swarmType, combinationParameter1, combinationParam1Values[param1Index]);

                for (int param2Index = 0; param2Index < combinationParam2Values.Length && !referenceFailed; param2Index++)
                {
                    float currentParam2 = ResolveCombinationValue(swarmType, combinationParameter2, combinationParam2Values[param2Index]);

                    for (int param3Index = 0; param3Index < combinationParam3Values.Length && !referenceFailed; param3Index++)
                    {

                        float currentParam3 = ResolveCombinationValue(swarmType, combinationParameter3, combinationParam3Values[param3Index]);

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

                        for (int layoutIndex = 0; layoutIndex < layouts.Count; layoutIndex++)
                        {
                            SpawnLayout layout = layouts[layoutIndex];
                            Debug.Log($"[SimRecorder] Recording {combinationIndex + 1}/{plannedCombinations} — {combinationLabel}" +
                                      (layout != null ? $" | layout {layout.layoutId}" : $" | repetition {layoutIndex + 1}/{layouts.Count}"));

                            SpawnLayout runLayout = layout;
                            if (layout != null)
                            {
                                uiController.SpawnFromLayout(layout);
                            }
                            else
                            {
                                uiController.ResetScene();
                                runLayout = uiController.CaptureSpawnLayout(
                                    $"{timestampFolder}_{swarmType.ToString().ToLower()}_clip_{combinationIndex:D4}");
                            }

                            activeSpawnLayoutId = runLayout.layoutId;
                            activeSpawnLayoutFingerprint = runLayout.Fingerprint();
                            uiController.ApplySettingsToManager();

                            string fileName = $"type_{swarmType.ToString().ToLower()}_{combinationParameter1.ToString().ToLower()}_{currentParam1:F2}_{combinationParameter2.ToString().ToLower()}_{currentParam2:F2}_{combinationParameter3.ToString().ToLower()}_{currentParam3:F2}";

                            // Without the layout in the name every layout of a combination would write to
                            // the same file, and only the last would survive.
                            if (layout != null) fileName += $"_{layout.layoutId}";
                            else fileName += $"_repeat_{layoutIndex + 1:D2}";

                            fileName = SanitizeFileName(fileName);

                            // Reads the manager at call time, so the reference's config carries its zero
                            // randomness and the clip's carries the value it actually ran with.
                            int configTypeIndex = typeIndex;
                            int configCombinationIndex = combinationIndex;
                            float configParam1 = currentParam1;
                            float configParam2 = currentParam2;
                            System.Func<string, SimulationConfig> buildConfig = name => new SimulationConfig
                            {
                                fileName = name,
                                variedParameter = paramFolderName,
                                variedParameterValue = configCombinationIndex,
                                parameter1 = "SwarmType",
                                parameter1Value = configTypeIndex,
                                parameter2 = combinationParameter1.ToString(),
                                parameter2Value = combinationParameter1 == SwarmParameterToRecord.RandomMovement ? swarmManager.randomMovementIntensity : configParam1,
                                parameter3 = combinationParameter2.ToString(),
                                parameter3Value = combinationParameter2 == SwarmParameterToRecord.RandomMovement ? swarmManager.randomMovementIntensity : configParam2,
                                swarmType = swarmType.ToString(),
                                spawnLayoutId = activeSpawnLayoutId,
                                spawnLayoutFingerprint = activeSpawnLayoutFingerprint,
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
                                numAgents = swarmManager.agents != null ? swarmManager.agents.Length : 0,
                                requestedAgents = uiController.RequestedAgentCount,
                                agentSpawnShortfall = uiController.AgentSpawnShortfall
                            };

                            // A reference is saved beside the first clip that needs it and named after
                            // that clip. Later clips sharing its layout and settings reuse it by key.
                            string referenceKey = ReferenceKey(swarmManager.perceptionRadius, swarmManager.maxSpeed);
                            string referenceFileName = SanitizeFileName(fileName + "_noRandomRef");
                            int referencesBefore = referenceSimulations.Count;

                            if (saveNoRandomnessReferences)
                            {
                                yield return PrepareCombinationReference(runLayout, referenceFolderPath,
                                    referenceFileName, referenceSimulations, buildConfig);
                            }
                            else
                            {
                                yield return PrepareCombinationReference(runLayout);
                            }

                            if (referenceSimulations.Count > referencesBefore)
                            {
                                SimulationConfig savedReference = referenceSimulations[referenceSimulations.Count - 1];
                                savedReference.pairedFileName = fileName;
                                referenceFileByKey[referenceKey] = referenceFileName;
                            }

                            MotionTypeRecordingSettings settings = GetSettingsFor(swarmType);
                            if (settings.endCondition == RecordingEndCondition.DensityRatioReached &&
                                settings.densityRatioBaseline == DensityRatioBaseline.NoRandomnessReference &&
                                !CurrentRunIsReference &&
                                (!referenceDensityValues.TryGetValue(
                                    ReferenceKey(swarmManager.perceptionRadius, swarmManager.maxSpeed), out float reference) ||
                                 reference <= 0f))
                            {
                                referenceFailed = true;
                                Debug.LogError($"[SimRecorder] No settled no-randomness reference for {runLayout.layoutId}. " +
                                               "Combinations batch stopped before recording this clip. Check the density monitor " +
                                               "and reference settling settings or increase the recording duration cap. " +
                                               "Completed clips will be saved in batch_config.json.");
                                break;
                            }

                            uiController.SetMotion(true);

                            // Apply this motion type's wall rules, then settle un-recorded before capture starts.
                            yield return PrepareRecordingForMotionType();

                            SimulationConfig config = buildConfig(fileName);
                            if (referenceFileByKey.TryGetValue(referenceKey, out string pairedReference))
                                config.pairedFileName = pairedReference;

                            simulations.Add(config);

#if UNITY_EDITOR
                            RecorderController recorderController = StartClipRecorder(targetFolderPath, fileName, "Combinations Recorder");
#else
                            Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                            yield return RunRecordingWindow(targetFolderPath, fileName, batchSimulations: simulations);
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
        }

        activeSpawnLayoutId = null;
        activeSpawnLayoutFingerprint = null;

        BatchConfig batchConfig = new BatchConfig
        {
            batchType = "combinations",
            folderName = paramFolderName,
            timestamp = timestampFolder,
            recordingTimePerSim = recordingTimePerSim,
            endConditionsPerMotionType = DescribeEndConditions(),
            frameRate = recordingFrameRate,
            simulationStep = swarmManager != null ? swarmManager.simulationStep : 0f,
            videoFinishedOverlayDuration = showVideoFinishedOverlay ? videoFinishedOverlayDuration : 0f,
            saveFolder = saveFolder,
            simulations = simulations.ToArray()
        };

        string configJson = JsonUtility.ToJson(batchConfig, true);
        File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);

        // References get their own config in their own folder, so the main batch_config.json keeps
        // listing only the clips shown to participants.
        if (referenceSimulations.Count > 0)
        {
            BatchConfig referenceBatchConfig = JsonUtility.FromJson<BatchConfig>(configJson);
            referenceBatchConfig.batchType = "combinations-no-randomness-references";
            referenceBatchConfig.simulations = referenceSimulations.ToArray();

            if (!Directory.Exists(referenceFolderPath)) Directory.CreateDirectory(referenceFolderPath);
            File.WriteAllText(Path.Combine(referenceFolderPath, "batch_config.json"),
                              JsonUtility.ToJson(referenceBatchConfig, true));
            Debug.Log($"[SimRecorder] Saved {referenceSimulations.Count} no-randomness references → {referenceFolderPath}");
        }

        uiController.showUI = true;
        uiController.SetMotion(false);

        hideOverlayText = false;
        isRecording = false;
        Debug.Log(referenceFailed
            ? "[SimRecorder] Combinations recording stopped: a required reference could not be measured."
            : "[SimRecorder] Combinations recording finished.");
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
                OutputWidth = outputWidth,
                OutputHeight = outputHeight
            };

            videoRecorder.AudioInputSettings.PreserveAudio = false;

            controllerSettings.AddRecorderSettings(videoRecorder);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = recordingFrameRate;

            recorderController.PrepareRecording();
            recorderController.StartRecording();
#else
            Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

            yield return RunRecordingWindow(targetFolderPath, fileName);
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
                frameRate = recordingFrameRate,
                simulationStep = swarmManager != null ? swarmManager.simulationStep : 0f,
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
                    OutputWidth = outputWidth,
                    OutputHeight = outputHeight
                };

                videoRecorder.AudioInputSettings.PreserveAudio = false;

                controllerSettings.AddRecorderSettings(videoRecorder);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = recordingFrameRate;

                recorderController.PrepareRecording();
                recorderController.StartRecording();
#else
                Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                yield return RunRecordingWindow(targetFolderPath, fileName);
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
                frameRate = recordingFrameRate,
                simulationStep = swarmManager != null ? swarmManager.simulationStep : 0f,
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
                    OutputWidth = outputWidth,
                    OutputHeight = outputHeight
                };

                videoRecorder.AudioInputSettings.PreserveAudio = false;

                controllerSettings.AddRecorderSettings(videoRecorder);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = recordingFrameRate;

                recorderController.PrepareRecording();
                recorderController.StartRecording();
#else
                Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                yield return RunRecordingWindow(targetFolderPath, fileName);
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
                frameRate = recordingFrameRate,
                simulationStep = swarmManager != null ? swarmManager.simulationStep : 0f,
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
                    OutputWidth = outputWidth,
                    OutputHeight = outputHeight
                };

                videoRecorder.AudioInputSettings.PreserveAudio = false;

                controllerSettings.AddRecorderSettings(videoRecorder);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = recordingFrameRate;

                recorderController.PrepareRecording();
                recorderController.StartRecording();
#else
                Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                yield return RunRecordingWindow(targetFolderPath, fileName);
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
                frameRate = recordingFrameRate,
                simulationStep = swarmManager != null ? swarmManager.simulationStep : 0f,
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

        if (!TryGetBatchRecordingOrder(swarmTypesToRecord, out List<SwarmType> recordingTypes))
        {
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
        for (int typeIndex = 0; typeIndex < recordingTypes.Count; typeIndex++)
        {
            SwarmType sType = recordingTypes[typeIndex];
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
                    OutputWidth = outputWidth,
                    OutputHeight = outputHeight
                };

                videoRecorder.AudioInputSettings.PreserveAudio = false;

                controllerSettings.AddRecorderSettings(videoRecorder);
                controllerSettings.SetRecordModeToManual();
                controllerSettings.FrameRate = recordingFrameRate;

                recorderController.PrepareRecording();
                recorderController.StartRecording();
#else
                Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

                yield return RunRecordingWindow(targetFolderPath, fileName, batchSimulations: simulations);
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
                frameRate = recordingFrameRate,
                simulationStep = swarmManager != null ? swarmManager.simulationStep : 0f,
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

    private static float ResolveCombinationValue(SwarmType swarmType, SwarmParameterToRecord parameter, float value)
    {
        return swarmType == SwarmType.Random && parameter == SwarmParameterToRecord.RandomMovement
            ? 20f + value
            : value;
    }
}
