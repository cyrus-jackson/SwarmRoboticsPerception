using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Records one complete grid per existing condition, across all motions or a selected parameter sweep.</summary>
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
    public string folderPath = "";
    [Tooltip("Optional folder overrides when motion types were recorded in separate batches. Child folders are not searched.")]
    public MotionSource[] motionSources = Array.Empty<MotionSource>();

    [Header("Grid")]
    [Min(1)] public int expectedLayouts = 10;
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
        public SwarmType motion;
        public float random;
        public float speed;
        public float perception;
    }

    private static readonly SwarmType[] Motions =
    {
        SwarmType.Random, SwarmType.Dispersion, SwarmType.Flocking, SwarmType.Densification
    };

    private readonly List<Job> jobs = new List<Job>();
    private AllGridManager gridManager;
    private int jobIndex;
    private int runLayouts;
    private int runColumns;

    private void Start()
    {
        if (recordOnStart) StartBatch();
    }

    [ContextMenu("Start Batch Recording")]
    public void StartBatch()
    {
        if (!Application.isPlaying || IsRunning) return;
        gridManager = GetComponent<AllGridManager>();
        if (string.IsNullOrWhiteSpace(folderPath)) folderPath = gridManager.folderPath;
        completedRecordings = totalRecordings = 0;
        jobs.Clear();
        runLayouts = expectedLayouts;
        runColumns = Mathf.Clamp(columns, 1, 40);
#if !UNITY_EDITOR
        Fail("Grid recording requires Play mode in the Unity Editor.");
        return;
#else
        try
        {
            if (!gridManager.isActiveAndEnabled)
                throw new InvalidOperationException("Enable the All Grid Manager before starting the batch.");
            if (expectedLayouts < 1)
                throw new ArgumentException("Expected Layouts must be at least one.");
            foreach (SwarmType motion in allMotionTypes ? Motions : new[] { selectedMotionType })
                AddJobs(motion);
            totalRecordings = jobs.Count;
            jobIndex = 0;
            IsRunning = true;
            BeginJob();
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
        }
#endif
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
        if (string.IsNullOrEmpty(gridManager.LastRecordingPath))
        {
            if (!gridManager.recordOneIteration)
                Fail("The current grid recording was interrupted.");
            return;
        }

        completedRecordings++;
        jobIndex++;
        if (jobIndex < jobs.Count)
        {
            BeginJob();
            return;
        }
        IsRunning = false;
        status = $"Complete: saved {completedRecordings} grid recordings.";
        Debug.Log($"[AllGridBatchManager] {status}", this);
    }

    [ContextMenu("Stop Batch Recording")]
    public void StopBatch()
    {
        if (!IsRunning) return;
        IsRunning = false;
        if (gridManager != null) gridManager.StopRecording();
        status = $"Stopped after {completedRecordings}/{totalRecordings} recordings. Current capture is partial.";
        Debug.Log($"[AllGridBatchManager] {status}", this);
    }

    private void AddJobs(SwarmType motion)
    {
        string source = string.IsNullOrWhiteSpace(folderPath) ? gridManager.folderPath : folderPath;
        MotionSource[] overrides = (motionSources ?? Array.Empty<MotionSource>())
            .Where(s => s != null && s.motionType == motion && !string.IsNullOrWhiteSpace(s.folderPath)).ToArray();
        if (overrides.Length > 1)
            throw new ArgumentException($"More than one source folder was supplied for {motion}.");
        if (overrides.Length == 1) source = overrides[0].folderPath;
        source = ResolveFolder(source);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Recording folder not found: {source}");

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
                folder = source, motion = motion, random = (float)key.random,
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
        gridManager.folderPath = job.folder;
        gridManager.motionType = job.motion;
        gridManager.randomMovement = job.random;
        gridManager.maxSpeed = job.speed;
        gridManager.perceptionRadius = job.perception;
        gridManager.anyRandomness = false;
        gridManager.expectedLayouts = runLayouts;
        gridManager.columns = runColumns;
        gridManager.loop = false;
        gridManager.recordOneIteration = true;
        status = $"Recording {jobIndex + 1}/{totalRecordings}: {job.motion}, random {job.random}, speed {job.speed}, perception {job.perception}";
        Debug.Log($"[AllGridBatchManager] {status}", this);
        gridManager.BuildGrid();
    }

    private void Fail(string message)
    {
        IsRunning = false;
        if (gridManager != null) gridManager.StopRecording();
        status = $"Failed after {completedRecordings}/{totalRecordings}: {message}";
        Debug.LogError($"[AllGridBatchManager] {status}", this);
    }

    private void OnDisable() => StopBatch();

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
