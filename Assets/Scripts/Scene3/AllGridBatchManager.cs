using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Discovers videos in a supplied folder and plays or records comparison grids using batch metadata.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AllGridManager))]
[DefaultExecutionOrder(-100)]
public sealed class AllGridBatchManager : MonoBehaviour
{
    public enum ParameterSelection { AllCombinations, Random, Speed, Perception }

    /// <summary>Overrides the shared source folder for one motion type.</summary>
    [Serializable]
    public sealed class MotionSource
    {
        public SwarmType motionType;
        public string folderPath;
    }

    [Header("Batch")]
    public bool recordOnStart = true;
    [Tooltip("Load the supplied folder automatically. Metadata determines comparison grids and clip labels; unindexed videos use their filenames. Manual condition and layout filters are ignored.")]
    public bool autoDetectFolder = true;
    [Tooltip("Capture each grid once. Disable to play the discovered grids without exporting videos.")]
    public bool recordGrids = true;
    [Header("Manual selection (Auto Detect Folder off)")]
    [Tooltip("Detect and record all motion types present in the source folders, including Densification V2. Disable to use Selected Motion Type.")]
    public bool allMotionTypes = true;
    public SwarmType selectedMotionType = SwarmType.Flocking;
    public ParameterSelection parameter = ParameterSelection.AllCombinations;

    [Header("Fixed values for a single-parameter sweep")]
    [Tooltip("Used unless Random is the selected parameter. Ignored for All Combinations.")]
    [Min(0)] public float fixedRandom = 0f;
    [Tooltip("Used unless Speed is the selected parameter. Ignored for All Combinations.")]
    [Min(0.01f)] public float fixedSpeed = 3f;
    [Tooltip("Used unless Perception is the selected parameter. Ignored for All Combinations.")]
    [Min(0.01f)] public float fixedPerception = 4f;

    [Header("Sources")]
    [Tooltip("Folder containing the source MP4s directly. Empty uses the All Grid Manager's folder. Paths may be absolute or relative to Assets.")]
    public string folderPath = "Assets/SimulationRecordings/MatchedStart_Cohesion/20261007_114900";
    [Tooltip("Optional folder overrides when motion types were recorded in separate batches. Child folders are not searched.")]
    public MotionSource[] motionSources = Array.Empty<MotionSource>();

    [Header("Grid")]
    [Tooltip("Manual selection only. Automatic mode uses all available clips in each grid.")]
    [Min(1)] public int expectedLayouts = 10;
    [Tooltip("Manual selection only. Automatic mode chooses columns from the number of videos.")]
    [Range(1, 40)] public int columns = 5;

    [Header("Progress (read only during a run)")]
    [SerializeField] private string status = "Ready";
    [SerializeField] private int completedRecordings;
    [SerializeField] private int totalRecordings;

    public bool IsRunning { get; private set; }
    public string Status => status;
    public int CompletedRecordings => completedRecordings;
    public int TotalRecordings => totalRecordings;

    /// <summary>Stores the source and parameters of one validated grid recording.</summary>
    private sealed class Job
    {
        public string folder;
        public string outputFolder;
        public SwarmType motion;
        public float random;
        public float speed;
        public float perception;
        public AllRecordingCatalog.Grid selection;
    }

    private readonly List<Job> jobs = new List<Job>();
    private readonly HashSet<string> createdOutputFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private AllGridManager gridManager;
    private int jobIndex;
    private int runLayouts;
    private int runColumns;
    private bool runRecordGrids;

    private void Start()
    {
        if (recordOnStart) StartBatch();
    }

    [ContextMenu("Start Batch Playback")]
    public void StartPlayback()
    {
        if (IsRunning) return;
        recordGrids = false;
        StartBatch();
    }

    [ContextMenu("Start Batch Recording")]
    public void StartBatch()
    {
        if (!Application.isPlaying || IsRunning) return;
        gridManager = GetComponent<AllGridManager>();
        if (string.IsNullOrWhiteSpace(folderPath)) folderPath = gridManager.folderPath;
        completedRecordings = totalRecordings = 0;
        jobs.Clear();
        createdOutputFolders.Clear();
        gridManager.SetBatchOutputFolder(null);
        runLayouts = expectedLayouts;
        runColumns = Mathf.Clamp(columns, 1, 40);
        runRecordGrids = recordGrids;
#if !UNITY_EDITOR
        if (runRecordGrids)
        {
            Fail("Grid recording requires Play mode in the Unity Editor.");
            return;
        }
#endif
        try
        {
            if (!gridManager.isActiveAndEnabled)
                throw new InvalidOperationException("Enable the All Grid Manager before starting the batch.");
            if (!autoDetectFolder && expectedLayouts < 1)
                throw new ArgumentException("Expected Layouts must be at least one.");
            gridManager.StopRecording();
            var skippedSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (autoDetectFolder)
            {
                AddAutomaticJobs(ResolveFolder(folderPath), skippedSources);
            }
            else
            {
                var sourceTypes = new Dictionary<string, string[]>(StringComparer.Ordinal);
                IEnumerable<SwarmType> motions = allMotionTypes
                    ? ((SwarmType[])Enum.GetValues(typeof(SwarmType))).OrderBy(MotionOrder)
                    : new[] { selectedMotionType };
                foreach (SwarmType motion in motions)
                {
                    string source = ResolveSourceFolder(motion, out bool hasOverride);
                    string outputFolder = AllGridManager.GetRecordingFolder(source);
                    if (runRecordGrids && (Directory.Exists(outputFolder) || File.Exists(outputFolder)))
                    {
                        if (skippedSources.Add(source))
                            Debug.Log($"[AllGridBatchManager] Skipped source {source}: output folder already exists at {outputFolder}", this);
                        continue;
                    }
                    if (!sourceTypes.TryGetValue(source, out string[] types))
                    {
                        types = AllRecordingCatalog.MotionTypesIn(source);
                        sourceTypes.Add(source, types);
                    }
                    bool isPresent = types.Any(type =>
                        string.Equals(type, motion.ToString(), StringComparison.OrdinalIgnoreCase));
                    if (!allMotionTypes || hasOverride || isPresent) AddJobs(motion, source);
                }
            }
            if (jobs.Count == 0 && skippedSources.Count > 0)
            {
                status = $"Skipped: output folders already exist for {skippedSources.Count} source folder(s).";
                Debug.Log($"[AllGridBatchManager] {status}", this);
                return;
            }
            if (jobs.Count == 0)
                throw new InvalidDataException("No supported motion types were found in the source MP4 filenames. " +
                    "Use a folder containing recordings named type_<motion>_..., such as type_densificationv2_... .");
            foreach (var destination in jobs.GroupBy(job => job.outputFolder, StringComparer.OrdinalIgnoreCase))
                if (destination.Select(job => job.folder).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
                    throw new InvalidDataException($"Different source folders share the output name {destination.Key}. " +
                        "Use source folders with distinct final names.");
            totalRecordings = jobs.Count;
            jobIndex = 0;
            IsRunning = true;
            BeginJob();
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
    }

    private void LateUpdate()
    {
        if (!IsRunning) return;
        if (gridManager == null || !gridManager.isActiveAndEnabled)
        {
            Fail("The grid manager was disabled during the batch.");
            return;
        }
        if (gridManager.HasFailed)
        {
            Fail(gridManager.LastError);
            return;
        }
        Job job = jobs[jobIndex];
        if (gridManager.folderPath != job.folder || gridManager.motionType != job.motion ||
            gridManager.maxSpeed != job.speed || gridManager.perceptionRadius != job.perception ||
            gridManager.randomMovement != job.random || gridManager.anyRandomness ||
            gridManager.expectedLayouts != runLayouts || gridManager.loop || gridManager.columns != runColumns)
        {
            Fail("Grid settings changed during recording. Start the batch again with the desired settings.");
            return;
        }
        if (runRecordGrids && string.IsNullOrEmpty(gridManager.LastRecordingPath))
        {
            if (!gridManager.recordOneIteration)
                Fail("The current grid recording was interrupted.");
            return;
        }
        if (!runRecordGrids && !gridManager.HasFinishedPlayback) return;

        completedRecordings++;
        jobIndex++;
        if (jobIndex < jobs.Count)
        {
            BeginJob();
            return;
        }
        IsRunning = false;
        gridManager.SetBatchOutputFolder(null);
        status = runRecordGrids ? $"Complete: saved {completedRecordings} grid recordings."
            : $"Complete: played {completedRecordings} grids.";
        Debug.Log($"[AllGridBatchManager] {status}", this);
    }

    [ContextMenu("Stop Batch Recording")]
    public void StopBatch()
    {
        if (!IsRunning) return;
        IsRunning = false;
        if (gridManager != null)
        {
            gridManager.StopRecording();
            gridManager.SetBatchOutputFolder(null);
        }
        status = runRecordGrids ? $"Stopped after {completedRecordings}/{totalRecordings} recordings. Current capture is partial."
            : $"Stopped after {completedRecordings}/{totalRecordings} grids.";
        Debug.Log($"[AllGridBatchManager] {status}", this);
    }

    private string ResolveSourceFolder(SwarmType motion, out bool hasOverride)
    {
        string source = string.IsNullOrWhiteSpace(folderPath) ? gridManager.folderPath : folderPath;
        MotionSource[] overrides = (motionSources ?? Array.Empty<MotionSource>())
            .Where(s => s != null && s.motionType == motion && !string.IsNullOrWhiteSpace(s.folderPath)).ToArray();
        if (overrides.Length > 1)
            throw new ArgumentException($"More than one source folder was supplied for {motion}.");
        hasOverride = overrides.Length == 1;
        if (hasOverride) source = overrides[0].folderPath;
        source = ResolveFolder(source);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Recording folder not found: {source}");
        return source;
    }

    private void AddAutomaticJobs(string source, HashSet<string> skippedSources)
    {
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException($"Recording folder not found: {source}");
        string outputFolder = AllGridManager.GetRecordingFolder(source);
        if (runRecordGrids && (Directory.Exists(outputFolder) || File.Exists(outputFolder)))
        {
            skippedSources.Add(source);
            Debug.Log($"[AllGridBatchManager] Skipped source {source}: output folder already exists at {outputFolder}", this);
            return;
        }
        string manifest = Path.Combine(source, "batch_config.json");
        AllRecordingCatalog.Batch batch = null;
        if (File.Exists(manifest))
        {
            batch = JsonUtility.FromJson<AllRecordingCatalog.Batch>(File.ReadAllText(manifest));
            if (batch == null) throw new InvalidDataException($"Could not read {manifest}.");
        }
        foreach (AllRecordingCatalog.Grid selection in AllRecordingCatalog.PlanGrids(source, batch))
        {
            AllRecordingCatalog.Clip first = selection.clips[0];
            Enum.TryParse(first.swarmType, true, out SwarmType motion);
            jobs.Add(new Job
            {
                folder = source,
                outputFolder = outputFolder,
                motion = motion,
                random = Finite(first.random) && first.random >= 0 ? (float)first.random : 0f,
                speed = Finite(first.maxSpeed) && first.maxSpeed > 0 ? (float)first.maxSpeed : 1f,
                perception = Finite(first.perception) && first.perception > 0 ? (float)first.perception : 1f,
                selection = selection
            });
        }
    }

    private void AddJobs(SwarmType motion, string source)
    {
        List<AllRecordingCatalog.Clip> clips = Directory.EnumerateFiles(source, "*.mp4")
            .Select(AllRecordingCatalog.Parse)
            .Where(c => c != null && string.Equals(c.swarmType, motion.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (clips.Any(c => !Finite(c.random) || c.random < 0 || !Finite(c.maxSpeed) || c.maxSpeed <= 0 ||
                           !Finite(c.perception) || c.perception <= 0))
            throw new InvalidDataException($"{motion} source clips must name randomness, speed and perception: {source}");

        var conditions = clips.Where(MatchesSelection)
            .GroupBy(c => new { c.random, c.maxSpeed, c.perception })
            .OrderBy(g => g.Key.random).ThenBy(g => g.Key.maxSpeed).ThenBy(g => g.Key.perception).ToList();
        if (conditions.Count == 0)
            throw new InvalidDataException($"No {motion} conditions match {parameter} in {source}. " +
                AllRecordingCatalog.Available(source, motion.ToString()));

        foreach (var condition in conditions)
        {
            var key = condition.Key;
            string description = $"{motion}, random {key.random}, speed {key.maxSpeed}, perception {key.perception}";
            if (condition.Count() != runLayouts || condition.Any(c => c.layoutIndex < 0) ||
                condition.Select(c => c.layoutIndex).Distinct().Count() != runLayouts)
                throw new InvalidDataException($"Expected exactly {runLayouts} unique numbered layouts for {description}; " +
                    $"found {condition.Count()} clips. Check for missing layouts or duplicate batches in {source}.");

            // Validate with the same matching rules the player uses before recording any job.
            AllRecordingCatalog.FindRecordings(source, motion.ToString(), key.maxSpeed, key.perception, key.random, runLayouts);
            jobs.Add(new Job
            {
                folder = source, outputFolder = AllGridManager.GetRecordingFolder(source),
                motion = motion, random = (float)key.random,
                speed = (float)key.maxSpeed, perception = (float)key.perception
            });
        }
    }

    private bool MatchesSelection(AllRecordingCatalog.Clip clip)
    {
        switch (parameter)
        {
            case ParameterSelection.AllCombinations: return true;
            case ParameterSelection.Random: return Near(clip.maxSpeed, fixedSpeed) && Near(clip.perception, fixedPerception);
            case ParameterSelection.Speed: return Near(clip.random, fixedRandom) && Near(clip.perception, fixedPerception);
            case ParameterSelection.Perception: return Near(clip.random, fixedRandom) && Near(clip.maxSpeed, fixedSpeed);
            default: throw new ArgumentOutOfRangeException(nameof(parameter));
        }
    }

    private void BeginJob()
    {
        Job job = jobs[jobIndex];
        if (runRecordGrids && !createdOutputFolders.Contains(job.outputFolder))
        {
            if (Directory.Exists(job.outputFolder) || File.Exists(job.outputFolder))
            {
                Fail($"Output folder appeared before recording could start: {job.outputFolder}. No recording will be added to it.");
                return;
            }
            try
            {
                Directory.CreateDirectory(job.outputFolder);
                createdOutputFolders.Add(job.outputFolder);
            }
            catch (Exception exception)
            {
                Fail($"Could not create output folder {job.outputFolder}: {exception.Message}");
                return;
            }
        }
        gridManager.SetBatchOutputFolder(runRecordGrids ? job.outputFolder : null);
        gridManager.SetRecordingSelection(job.selection);
        gridManager.folderPath = job.folder;
        gridManager.motionType = job.motion;
        gridManager.randomMovement = job.random;
        gridManager.maxSpeed = job.speed;
        gridManager.perceptionRadius = job.perception;
        gridManager.anyRandomness = false;
        if (job.selection != null)
        {
            runLayouts = job.selection.clips.Length;
            runColumns = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(runLayouts * 16f / 9f)), 1, Mathf.Min(40, runLayouts));
        }
        gridManager.expectedLayouts = runLayouts;
        gridManager.columns = runColumns;
        gridManager.loop = false;
        gridManager.recordOneIteration = runRecordGrids;
        string description = job.selection != null ? job.selection.title
            : $"{job.motion}, random {job.random}, speed {job.speed}, perception {job.perception}";
        status = $"{(runRecordGrids ? "Recording" : "Playing")} {jobIndex + 1}/{totalRecordings}: {description}";
        Debug.Log($"[AllGridBatchManager] {status}", this);
        gridManager.BuildGrid();
    }

    private void Fail(string message)
    {
        IsRunning = false;
        if (gridManager != null)
        {
            gridManager.StopRecording();
            gridManager.SetBatchOutputFolder(null);
        }
        status = $"Failed after {completedRecordings}/{totalRecordings}: {message}";
        Debug.LogError($"[AllGridBatchManager] {status}", this);
    }

    private void OnDisable() => StopBatch();

    private static int MotionOrder(SwarmType motion)
    {
        switch (motion)
        {
            case SwarmType.Random: return 0;
            case SwarmType.Dispersion: return 1;
            case SwarmType.Flocking: return 2;
            case SwarmType.Densification: return 3;
            default: return 4 + (int)motion;
        }
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static bool Near(double value, double wanted) => Math.Abs(value - wanted) < 0.0001;

    private static string ResolveFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Recording folder is empty.");
        string path = folder.Trim().Replace('\\', '/');
        if (Path.IsPathRooted(path)) return path;
        if (path.Equals("Assets", StringComparison.OrdinalIgnoreCase)) return Application.dataPath;
        if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) path = path.Substring(7);
        return Path.GetFullPath(Path.Combine(Application.dataPath, path));
    }
}
