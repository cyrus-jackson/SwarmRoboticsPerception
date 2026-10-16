using System;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Displays all 32 motion combinations and optionally records one iteration to a chosen folder.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AllGridManager))]
[DefaultExecutionOrder(-200)]
public sealed class MotionCombinationGridManager : MonoBehaviour
{
    [Header("Source")]
    [Tooltip("Folder containing the MP4s directly. Accepts an absolute path or a path relative to Assets.")]
    public string folderPath = "Assets/SimulationRecordings/Combinations/20261009_113950";

    [Tooltip("Recording repeat number in the filename, for example 3 selects _repeat_03.mp4.")]
    [Min(1)] public int repeat = 1;

    [Header("Rows (top to bottom)")]
    [Tooltip("Reorder these four motion types to choose the row order. Each row shows all eight combinations.")]
    public SwarmType[] motionOrder =
    {
        SwarmType.Flocking, SwarmType.DensificationV2, SwarmType.Dispersion, SwarmType.Random
    };

    [Header("Recording")]
    [Tooltip("Record one complete iteration of all 32 videos. Checking during playback restarts the grid first. Resets when finished; Unity Editor only.")]
    public bool recordOneIteration;
    [Tooltip("Destination for the recorded MP4. Accepts an absolute path or a path relative to the project folder. Created if needed; existing folders can be reused. Changes apply to the next recording.")]
    public string outputFolderPath = "Grid Recordings/Motion Combinations";

    [Header("Progress")]
    [SerializeField] private string status = "Ready";

    public string Status => status;

    private AllGridManager gridManager;
    private string loadedFolder;
    private int loadedRepeat;
    private SwarmType[] loadedOrder;
    private bool built;
    private bool recordToggleState;

    private void Start() => BuildGrid();

    private void Update()
    {
        if (!built || loadedFolder != folderPath || loadedRepeat != repeat ||
            !(loadedOrder ?? Array.Empty<SwarmType>()).SequenceEqual(motionOrder ?? Array.Empty<SwarmType>()))
        {
            BuildGrid();
            return;
        }
        if (recordOneIteration != recordToggleState)
        {
            if (recordOneIteration && gridManager.HasFailed)
            {
                BuildGrid();
                return;
            }
            try
            {
                ApplyRecordingRequest();
                status = recordOneIteration ? "Recording one iteration…" : "Recording canceled";
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
        }
        if (recordToggleState && !gridManager.recordOneIteration)
        {
            recordOneIteration = recordToggleState = false;
            status = string.IsNullOrEmpty(gridManager.LastRecordingPath) ? "Recording interrupted"
                : $"Saved: {gridManager.LastRecordingPath}";
        }
        if (gridManager.HasFailed)
        {
            recordOneIteration = recordToggleState = false;
            status = gridManager.LastError;
        }
    }

    [ContextMenu("Build / Reload 32-Video Grid")]
    public void BuildGrid()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        gridManager = GetComponent<AllGridManager>();
        AllGridBatchManager batch = GetComponent<AllGridBatchManager>();
        if (batch != null)
        {
            batch.StopBatch();
            batch.enabled = false;
        }
        loadedFolder = folderPath;
        loadedRepeat = repeat;
        loadedOrder = motionOrder == null ? null : (SwarmType[])motionOrder.Clone();
        built = true;
        try
        {
            string[] order = motionOrder == null ? null : Array.ConvertAll(motionOrder, motion => motion.ToString());
            AllRecordingCatalog.Grid selection = MotionCombinationCatalog.Select(ResolveFolder(folderPath), repeat, order);
            gridManager.folderPath = folderPath;
            gridManager.columns = MotionCombinationCatalog.CombinationsPerMotion;
            gridManager.expectedLayouts = MotionCombinationCatalog.MotionCount * MotionCombinationCatalog.CombinationsPerMotion;
            gridManager.SetRecordingSelection(selection);
            ApplyRecordingRequest();
            gridManager.enabled = true;
            gridManager.BuildGrid();
            status = $"Repeat {repeat:00} · 4 rows × 8 combinations";
        }
        catch (Exception exception)
        {
            ShowError(exception.Message);
        }
    }

    [ContextMenu("Record One Iteration")]
    public void RecordOneIteration()
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        recordOneIteration = true;
        BuildGrid();
    }

    private void ApplyRecordingRequest()
    {
        string destination = null;
        if (recordOneIteration)
        {
            if (string.IsNullOrWhiteSpace(outputFolderPath))
                throw new ArgumentException("Output Folder Path is empty. Choose a folder for the recorded MP4.");
            string path = outputFolderPath.Trim().Replace('\\', '/');
            destination = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Application.dataPath, "..", path));
        }
        gridManager.SetBatchOutputFolder(destination);
        gridManager.recordOneIteration = recordToggleState = recordOneIteration;
    }

    private void ShowError(string message)
    {
        recordOneIteration = recordToggleState = false;
        gridManager.StopRecording();
        status = message;
        gridManager.ShowSelectionError(message);
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
        if (gridManager != null)
        {
            gridManager.StopRecording();
            gridManager.SetBatchOutputFolder(null);
            recordOneIteration = recordToggleState = false;
        }
        built = false;
    }
}
