using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

/// <summary>
/// Plays a grid of matched clips: one column per perception radius, one row per level of random
/// movement, all from the same spawn layout and max speed.
///
/// Each column is a controlled comparison. Because every clip in it began from the identical
/// arrangement of agents, the differences down a column are the randomness and nothing else — no
/// spawn luck, no difference in perception, no difference in speed.
///
/// Rows are listed top to bottom, and the default puts the zero-randomness reference at the bottom
/// so the eye reads upward from it.
///
/// Unlike VideoGridManager, files are not taken in sorted order and dropped into cells. They are
/// parsed for their parameters and looked up, because a grid that silently shows the wrong pair is
/// worse than one that reports a gap.
/// </summary>
public class DispersionGridManager : MonoBehaviour
{
    [Header("Configuration")]
    [Tooltip("Folder holding the clips. Both the randomised and the zero-randomness runs must be in here, which is how a Combinations or matched-start batch writes them.")]
    public string folderPath = "Assets/SimulationRecordings/Combinations/20260825_014231";

    [Header("What to compare")]
    [Tooltip("One row per value, listed TOP to BOTTOM as they appear on screen. The default puts no randomness at the bottom with 40 and 80 stacked above it, so the eye reads upward from the reference. Any number of rows works.")]
    public float[] randomnessRows = new float[] { 80f, 40f, 0f };

    [Tooltip("Perception radius per column, left to right. One clip pair per value.")]
    public float[] perceptionValues = new float[] { 0.90f, 1.50f, 2.00f, 2.50f, 3.00f };

    [Tooltip("Max speed for every cell. Both rows use this, so it is not a variable here.")]
    public float maxSpeed = 1.5f;

    [Tooltip("Which spawn layout to show, e.g. 20260819_150007_layout_00. Leave empty to use whichever layout covers the most of the requested grid.")]
    public string spawnLayoutId = "";

    [Tooltip("Motion type to match in the file names.")]
    public string swarmType = "dispersion";

    [Header("UI Layout")]
    public RectTransform gridContainer;

    [Header("Grid Labels (Drag & Drop)")]
    public TextMeshProUGUI topMainLabel;
    public TextMeshProUGUI leftMainLabel;
    public TextMeshProUGUI[] topLabels;
    public TextMeshProUGUI[] leftLabels;

    [Header("Optimization Settings")]
    public int renderWidth = 640;
    public int renderHeight = 900;

    /// <summary>Values are matched within this, because they come off sliders and drift.</summary>
    private const float Tolerance = 0.02f;

    /// <summary>Speed drifts further than radius does: "4" has been recorded as 3.9654903.</summary>
    private const float SpeedTolerance = 0.1f;

    [Header("Playback")]
    [Tooltip("Hold each clip on its last frame until the others have finished, then restart them all together. Off plays every clip on its own loop, which drifts out of step because the clips differ in length.")]
    public bool holdUntilAllFinished = true;

    [Tooltip("WholeGrid: every cell waits for every other. PerColumn: each column restarts on its own, which keeps a column in step but lets columns drift apart from each other.")]
    public SyncScope syncScope = SyncScope.WholeGrid;

    [Tooltip("Seconds to hold on the last frame after the group finishes, before restarting.")]
    public float restartPause = 0.5f;

    /// <summary>How much of the grid has to finish before anything restarts.</summary>
    public enum SyncScope
    {
        WholeGrid,
        PerColumn
    }

    /// <summary>One parsed clip.</summary>
    private class Clip
    {
        public string path;
        public string swarmType;
        public float random;
        public float perception;
        public float maxSpeed;
        public string layoutId;
    }

    /// <summary>A live cell in the grid, tracked so the group can be started and restarted as one.</summary>
    private class Cell
    {
        public VideoPlayer player;
        public int column;
        public string path;
        public bool prepared;
        public bool finished;
    }

    private readonly List<Cell> cells = new List<Cell>();
    private bool started;
    private float groupFinishedAt = -1f;
    private readonly Dictionary<int, float> columnFinishedAt = new Dictionary<int, float>();

    private void Start()
    {
        BuildGrid();
    }

    public void BuildGrid()
    {
        if (gridContainer == null)
        {
            Debug.LogError("[DispersionGrid] Grid Container is missing. Assign a parent RectTransform.");
            return;
        }

        string path = ResolveFolder(folderPath);
        if (!Directory.Exists(path))
        {
            Debug.LogError($"[DispersionGrid] Directory not found: {path}");
            return;
        }

        List<Clip> clips = Directory.GetFiles(path, "*.mp4").Select(Parse).Where(c => c != null).ToList();
        if (clips.Count == 0)
        {
            Debug.LogError($"[DispersionGrid] No parsable mp4 files in {path}. Names must look like " +
                           $"type_<motion>_randommovement_<v>_perceptionrad_<v>_maxspeed_<v>_<layout>.mp4");
            return;
        }

        string layout = string.IsNullOrWhiteSpace(spawnLayoutId) ? ChooseLayout(clips) : spawnLayoutId.Trim();
        if (string.IsNullOrEmpty(layout))
        {
            Debug.LogError("[DispersionGrid] No spawn layout found in these file names. The clips were " +
                           "recorded without layouts, so there are no matched pairs to show.");
            return;
        }

        int cols = perceptionValues != null ? perceptionValues.Length : 0;
        if (cols == 0)
        {
            Debug.LogError("[DispersionGrid] Perception Values is empty; nothing to lay out.");
            return;
        }

        int rows = randomnessRows != null ? randomnessRows.Length : 0;
        if (rows == 0)
        {
            Debug.LogError("[DispersionGrid] Randomness Rows is empty; nothing to lay out.");
            return;
        }

        ConfigureLayout(cols);

        // Rebuilding starts a fresh set of players, so the old bookkeeping must go with the old
        // cells or a stale entry would keep the group waiting on a player that no longer exists.
        cells.Clear();
        columnFinishedAt.Clear();
        started = false;
        groupFinishedAt = -1f;

        // Filled row by row because GridLayoutGroup places children in that order, so the array is
        // read top to bottom exactly as it appears in the Inspector.
        int found = 0;
        for (int row = 0; row < rows; row++)
        {
            float wantedRandom = randomnessRows[row];

            for (int col = 0; col < cols; col++)
            {
                float R = perceptionValues[col];
                Clip clip = Find(clips, layout, R, wantedRandom);

                if (clip != null)
                {
                    CreateVideoElement(clip, col);
                    found++;
                }
                else
                {
                    CreateMissingElement(R, wantedRandom);
                    Debug.LogWarning($"[DispersionGrid] No clip for layout '{layout}', " +
                                     $"perception {R:F2}, maxSpeed {maxSpeed:F2}, random {wantedRandom:F0}.");
                }

                UpdateLabels(row, col, R, wantedRandom);
            }
        }

        Debug.Log($"[DispersionGrid] {found}/{rows * cols} cells filled from layout '{layout}' " +
                  $"at maxSpeed {maxSpeed:F2}. Rows top to bottom: " +
                  $"{string.Join(", ", randomnessRows.Select(v => $"random {v:0.#}"))}.");
    }

    // ------------------------------------------------------------------ playback

    private void Update()
    {
        if (cells.Count == 0) return;

        // Nothing plays until every clip is decoded and ready, so they all start on the same frame.
        if (!started)
        {
            foreach (Cell cell in cells)
            {
                if (!cell.prepared) return;
            }

            StartGroup(cells);
            started = true;
            return;
        }

        if (!holdUntilAllFinished) return;

        if (syncScope == SyncScope.WholeGrid)
        {
            CheckGroup(cells, ref groupFinishedAt);
            return;
        }

        foreach (int column in cells.Select(c => c.column).Distinct())
        {
            float finishedAt = columnFinishedAt.TryGetValue(column, out float value) ? value : -1f;
            List<Cell> group = cells.Where(c => c.column == column).ToList();

            CheckGroup(group, ref finishedAt);
            columnFinishedAt[column] = finishedAt;
        }
    }

    /// <summary>
    /// Restarts a group once every clip in it has ended and the pause has elapsed.
    ///
    /// A finished VideoPlayer leaves its last decoded frame in the render texture, so a cell that
    /// ends early simply holds that image — no extra work needed to freeze it.
    /// </summary>
    private void CheckGroup(List<Cell> group, ref float finishedAt)
    {
        if (group.Count == 0) return;

        if (finishedAt < 0f)
        {
            foreach (Cell cell in group)
            {
                if (!cell.finished) return;
            }

            finishedAt = Time.time;
            return;
        }

        if (Time.time - finishedAt < restartPause) return;

        StartGroup(group);
        finishedAt = -1f;
    }

    private void StartGroup(List<Cell> group)
    {
        foreach (Cell cell in group)
        {
            cell.finished = false;
            cell.player.frame = 0;
            cell.player.Play();
        }
    }

    // ------------------------------------------------------------------ selection

    /// <summary>
    /// The layout that covers most of the requested grid.
    ///
    /// Picking the first layout alphabetically would be arbitrary and could land on one with a
    /// missing clip, leaving a hole that looks like a bug in the grid rather than a gap in the data.
    /// </summary>
    private string ChooseLayout(List<Clip> clips)
    {
        string best = null;
        int bestScore = -1;

        foreach (string layout in clips.Select(c => c.layoutId).Where(l => !string.IsNullOrEmpty(l)).Distinct())
        {
            int score = 0;
            foreach (float R in perceptionValues)
            {
                foreach (float random in randomnessRows)
                {
                    if (Find(clips, layout, R, random) != null) score++;
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                best = layout;
            }
        }

        if (best != null)
        {
            Debug.Log($"[DispersionGrid] Using layout '{best}', which covers {bestScore}/" +
                      $"{randomnessRows.Length * perceptionValues.Length} of the requested cells.");
        }

        return best;
    }

    private Clip Find(List<Clip> clips, string layout, float perception, float random)
    {
        return clips.FirstOrDefault(c =>
            c.layoutId == layout &&
            string.Equals(c.swarmType, swarmType, System.StringComparison.OrdinalIgnoreCase) &&
            Mathf.Abs(c.perception - perception) <= Tolerance &&
            Mathf.Abs(c.maxSpeed - maxSpeed) <= SpeedTolerance &&
            Mathf.Abs(c.random - random) <= Tolerance);
    }

    /// <summary>
    /// Reads the parameters out of a file name rather than relying on field positions.
    ///
    /// Names look like:
    ///   type_dispersion_randommovement_40.00_perceptionrad_2.50_maxspeed_1.50_20260819_150007_layout_00
    ///
    /// The layout id is whatever follows the max speed value, which keeps this working when the
    /// layout name itself contains underscores.
    /// </summary>
    private static Clip Parse(string filePath)
    {
        string name = Path.GetFileNameWithoutExtension(filePath);
        string[] parts = name.Split('_');

        Clip clip = new Clip { path = filePath, perception = float.NaN, maxSpeed = float.NaN, random = float.NaN };
        int lastValueIndex = -1;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "type":
                    clip.swarmType = parts[i + 1];
                    break;
                case "randommovement":
                    if (TryValue(parts[i + 1], out clip.random)) lastValueIndex = i + 1;
                    break;
                case "perceptionrad":
                    if (TryValue(parts[i + 1], out clip.perception)) lastValueIndex = i + 1;
                    break;
                case "maxspeed":
                    if (TryValue(parts[i + 1], out clip.maxSpeed)) lastValueIndex = i + 1;
                    break;
            }
        }

        if (float.IsNaN(clip.random) || float.IsNaN(clip.perception)) return null;

        if (lastValueIndex >= 0 && lastValueIndex + 1 < parts.Length)
        {
            clip.layoutId = string.Join("_", parts.Skip(lastValueIndex + 1));
        }

        return clip;
    }

    private static bool TryValue(string token, out float value)
    {
        return float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static string ResolveFolder(string folder)
    {
        if (string.IsNullOrEmpty(folder)) return folder;

        string trimmed = folder.Trim().Replace('\\', '/').TrimEnd('/');
        if (Path.IsPathRooted(trimmed)) return trimmed;
        if (trimmed.Equals("Assets", System.StringComparison.OrdinalIgnoreCase)) return Application.dataPath;
        if (trimmed.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase)) trimmed = trimmed.Substring(7);

        return Path.Combine(Application.dataPath, trimmed);
    }

    // ------------------------------------------------------------------ building

    private void ConfigureLayout(int cols)
    {
        GridLayoutGroup grid = gridContainer.GetComponent<GridLayoutGroup>();
        if (grid == null) grid = gridContainer.gameObject.AddComponent<GridLayoutGroup>();

        grid.cellSize = new Vector2(renderWidth, renderHeight);
        // grid.spacing = new Vector2(10, 10);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = cols;
    }

    private void CreateVideoElement(Clip clip, int column)
    {
        GameObject element = new GameObject($"Cell_r{clip.random:F0}_R{clip.perception:F2}");
        element.transform.SetParent(gridContainer, false);

        RawImage image = element.AddComponent<RawImage>();
        image.color = Color.white;

        VideoPlayer player = element.AddComponent<VideoPlayer>();

        // Not playOnAwake and not looping: every clip is prepared first and the whole group is
        // started together, then each one holds its final frame until the rest have finished.
        // Left to themselves the clips start at different moments and, because they differ in
        // length, drift further apart on every loop — which would quietly destroy the comparison
        // the grid exists to make.
        player.playOnAwake = false;
        player.isLooping = false;
        player.source = VideoSource.Url;
        player.url = clip.path;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.skipOnDrop = true;

        RenderTexture texture = new RenderTexture(renderWidth, renderHeight, 0, RenderTextureFormat.ARGB32);
        texture.Create();

        player.targetTexture = texture;
        image.texture = texture;

        Cell cell = new Cell { player = player, column = column, path = clip.path };
        cells.Add(cell);

        // A clip that fails to open must not stall the group waiting for it to finish.
        player.errorReceived += (source, message) =>
        {
            Debug.LogWarning($"[DispersionGrid] {Path.GetFileName(cell.path)}: {message}");
            cell.prepared = true;
            cell.finished = true;
        };

        player.prepareCompleted += source => cell.prepared = true;
        player.loopPointReached += source => cell.finished = true;

        player.Prepare();
    }

    /// <summary>
    /// A visible placeholder for a pair that could not be completed.
    ///
    /// Leaving the cell out entirely would shift every later clip one place left and quietly break
    /// the column pairing, which is the one property this grid exists to guarantee.
    /// </summary>
    private void CreateMissingElement(float perception, float random)
    {
        GameObject element = new GameObject($"Missing_r{random:F0}_R{perception:F2}");
        element.transform.SetParent(gridContainer, false);

        Image background = element.AddComponent<Image>();
        background.color = new Color(0.16f, 0.16f, 0.18f, 1f);

        GameObject textObject = new GameObject("Label");
        textObject.transform.SetParent(element.transform, false);

        TextMeshProUGUI label = textObject.AddComponent<TextMeshProUGUI>();
        label.text = $"no clip\nR {perception:0.##}   random {random:0.#}";
        label.alignment = TextAlignmentOptions.Center;
        label.fontSize = 22;
        label.color = new Color(1f, 0.5f, 0.45f);

        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void UpdateLabels(int row, int col, float perception, float random)
    {
        if (row == 0 && topLabels != null && col < topLabels.Length && topLabels[col] != null)
        {
            topLabels[col].text = $"{perception:0.##}";
            if (topMainLabel != null) topMainLabel.text = "perception radius";
        }

        if (col == 0 && leftLabels != null && row < leftLabels.Length && leftLabels[row] != null)
        {
            // Keyed off the value, not the row index: the reference row is wherever 0 is placed.
            leftLabels[row].text = Mathf.Abs(random) <= Tolerance
                ? "no randomness"
                : $"random {random:0.#}";
            if (leftMainLabel != null) leftMainLabel.text = $"maxSpeed {maxSpeed:0.##}";
        }
    }
}
