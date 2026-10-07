using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

/// <summary>
/// Selects recording conditions and plans comparison grids from batch metadata or video filenames.
/// </summary>
public static class AllRecordingCatalog
{
    /// <summary>Tolerance for matching a recorded parameter against a requested one.</summary>
    private const double Tolerance = 0.0001;

    /// <summary>Video parameters from a filename or manifest, with missing filename values marked NaN.</summary>
    public sealed class Clip
    {
        public string path;
        public string swarmType;
        public string layoutId;
        public int layoutIndex = -1;
        public double random = double.NaN;
        public double perception = double.NaN;
        public double maxSpeed = double.NaN;
        public string parameter1;
        public string parameter2;
        public string parameter3;
        public double parameter1Value;
        public double parameter2Value;
        public double parameter3Value;
        public string obstacleName;
        public string obstacleSpawnLocationName;
        public readonly Dictionary<string, double> parameters = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Reads the recording index saved by SimRecorder without requiring trajectory files.</summary>
    [Serializable]
    public sealed class Batch
    {
        public string batchType;
        public Recording[] simulations;
    }

    /// <summary>Maps one manifest entry to its video, starting layout, and simulation parameters.</summary>
    [Serializable]
    public sealed class Recording
    {
        public string fileName;
        public string swarmType;
        public string spawnLayoutId;
        public string parameter1;
        public string parameter2;
        public string parameter3;
        public double parameter1Value;
        public double parameter2Value;
        public double parameter3Value;
        public string obstacleName;
        public string obstacleSpawnLocationName;
        public double randomMovement;
        public double perceptionRadius;
        public double maxSpeed;
        public double cohesion;
        public double separation;
        public double alignment;
        public double friction;
        public double overlapAvoidance;
        public double safetyDistance;
        public double envAvoidance;
        public double obstacleRadius;
    }

    /// <summary>Describes the exact videos and labels to display and capture together.</summary>
    public sealed class Grid
    {
        public Clip[] clips;
        public string title;
        public string outputLabel;
        public string[] labels;
    }

    public static Grid[] PlanGrids(string folder, Batch batch = null)
    {
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Recording folder not found: {folder}");
        folder = Path.GetFullPath(folder);
        string[] videos = Directory.EnumerateFiles(folder)
            .Where(path => string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (videos.Length == 0) throw new InvalidDataException($"No MP4 videos were found in {folder}.");
        var metadata = new Dictionary<string, Recording>(StringComparer.OrdinalIgnoreCase);
        if (batch != null)
        {
            if (batch.simulations == null || batch.simulations.Length == 0)
                throw new InvalidDataException("batch_config.json contains no simulation entries.");
            var names = new HashSet<string>(videos.Select(Path.GetFileNameWithoutExtension), StringComparer.OrdinalIgnoreCase);
            foreach (Recording recording in batch.simulations)
            {
                if (recording == null || string.IsNullOrWhiteSpace(recording.fileName))
                    throw new InvalidDataException("batch_config.json contains an entry without a video filename.");
                string name = recording.fileName.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase)
                    ? recording.fileName.Substring(0, recording.fileName.Length - 4) : recording.fileName;
                if (!names.Contains(name)) throw new InvalidDataException($"Manifest video is missing: {name}.mp4");
                if (metadata.ContainsKey(name)) throw new InvalidDataException($"Duplicate manifest video: {name}");
                metadata.Add(name, recording);
            }
        }

        List<Clip> clips = videos.Select(path => metadata.TryGetValue(Path.GetFileNameWithoutExtension(path), out Recording recording)
            ? FromRecording(path, recording) : Parse(path) ?? new Clip { path = path }).ToList();
        bool combinations = batch != null && (batch.batchType ?? "").StartsWith("combinations", StringComparison.OrdinalIgnoreCase);
        bool matchedStarts = string.Equals(batch?.batchType, "matched-start", StringComparison.OrdinalIgnoreCase);
        bool filenameConditions = batch == null && clips.All(clip => !string.IsNullOrEmpty(clip.swarmType) &&
            IsUsable(clip.maxSpeed) && IsUsable(clip.perception) && !double.IsNaN(clip.random));

        return clips.GroupBy(clip => batch != null && metadata.ContainsKey(Path.GetFileNameWithoutExtension(clip.path))
                ? ConditionKey(clip, !combinations) + (matchedStarts ? "|layout=" + clip.layoutId : "")
                : filenameConditions ? $"{clip.swarmType}|{ValueKey(clip.random)}|{ValueKey(clip.maxSpeed)}|{ValueKey(clip.perception)}"
                : "unindexed")
            .Select((group, index) =>
            {
                Clip[] ordered = group.OrderBy(clip => clip.parameter1Value).ThenBy(clip => clip.parameter2Value)
                    .ThenBy(clip => clip.parameter3Value).ThenBy(clip => clip.layoutIndex)
                    .ThenBy(clip => clip.path, StringComparer.Ordinal).ToArray();
                Clip first = ordered[0];
                string motion = !string.IsNullOrEmpty(first.swarmType) && ordered.All(clip =>
                    string.Equals(clip.swarmType, first.swarmType, StringComparison.OrdinalIgnoreCase)) ? first.swarmType : "Videos";
                string sweep = !combinations && batch != null
                    ? string.Join(" / ", new[] { first.parameter1, first.parameter2, first.parameter3 }.Where(IsSweepParameter)) : "";
                string details = string.IsNullOrEmpty(sweep) ? "" : $" · {sweep} comparison";
                if (combinations)
                {
                    var axes = new[] { (first.parameter1, first.parameter1Value), (first.parameter2, first.parameter2Value), (first.parameter3, first.parameter3Value) };
                    foreach (var axis in axes.Where(axis => IsSweepParameter(axis.Item1) && axis.Item1 != "RandomMovement" &&
                        axis.Item1 != "MaxSpeed" && axis.Item1 != "PerceptionRad"))
                        details += $" · {axis.Item1} {Number(axis.Item2)}";
                }
                if (matchedStarts) details += $" · {first.layoutId}";
                if (ordered.All(clip => clip.maxSpeed == first.maxSpeed) && IsUsable(first.maxSpeed)) details += $" · Speed {Number(first.maxSpeed)}";
                if (ordered.All(clip => clip.perception == first.perception) && IsUsable(first.perception)) details += $" · Perception {Number(first.perception)}";
                if (ordered.All(clip => clip.random == first.random) && !double.IsNaN(first.random)) details += $" · Noise {Number(first.random)}";
                return new Grid
                {
                    clips = ordered,
                    title = motion + details,
                    outputLabel = $"{motion}_{(string.IsNullOrEmpty(sweep) ? "condition" : sweep.Replace(" / ", "_"))}_{index + 1:00}".ToLowerInvariant(),
                    labels = ordered.Select(clip => CellLabel(clip, combinations || filenameConditions)).ToArray()
                };
            }).OrderBy(grid => grid.clips[0].swarmType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(grid => grid.clips[0].random).ThenBy(grid => grid.clips[0].maxSpeed)
            .ThenBy(grid => grid.clips[0].perception).ThenBy(grid => grid.clips[0].layoutId, StringComparer.Ordinal).ToArray();
    }

    private static Clip FromRecording(string path, Recording recording)
    {
        Clip clip = Parse(path) ?? new Clip { path = path };
        if (!string.IsNullOrWhiteSpace(recording.swarmType)) clip.swarmType = recording.swarmType;
        if (!string.IsNullOrWhiteSpace(recording.spawnLayoutId))
        {
            clip.layoutId = recording.spawnLayoutId;
            clip.layoutIndex = TrailingIndex(recording.spawnLayoutId);
        }
        clip.random = recording.randomMovement;
        clip.perception = recording.perceptionRadius;
        clip.maxSpeed = recording.maxSpeed;
        clip.parameter1 = recording.parameter1;
        clip.parameter2 = recording.parameter2;
        clip.parameter3 = recording.parameter3;
        clip.parameter1Value = recording.parameter1Value;
        clip.parameter2Value = recording.parameter2Value;
        clip.parameter3Value = recording.parameter3Value;
        clip.obstacleName = recording.obstacleName;
        clip.obstacleSpawnLocationName = recording.obstacleSpawnLocationName;
        clip.parameters["Cohesion"] = recording.cohesion;
        clip.parameters["Separation"] = recording.separation;
        clip.parameters["Alignment"] = recording.alignment;
        clip.parameters["Friction"] = recording.friction;
        clip.parameters["RandomMovement"] = recording.randomMovement;
        clip.parameters["OverlapAvoidance"] = recording.overlapAvoidance;
        clip.parameters["SafetyDistance"] = recording.safetyDistance;
        clip.parameters["EnvAvoidance"] = recording.envAvoidance;
        clip.parameters["ObstacleRadius"] = recording.obstacleRadius;
        clip.parameters["MaxSpeed"] = recording.maxSpeed;
        clip.parameters["PerceptionRad"] = recording.perceptionRadius;
        return clip;
    }

    private static string ConditionKey(Clip clip, bool excludeSweep)
    {
        return (clip.swarmType ?? "").ToLowerInvariant() + "|" + clip.obstacleName + "|" + clip.obstacleSpawnLocationName + "|" +
            string.Join("|", clip.parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Where(pair => !excludeSweep || !(string.Equals(pair.Key, clip.parameter1, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, clip.parameter2, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(pair.Key, clip.parameter3, StringComparison.OrdinalIgnoreCase)))
                .Select(pair => pair.Key + "=" + ValueKey(pair.Value)));
    }

    private static string CellLabel(Clip clip, bool layoutsOnly)
    {
        if (layoutsOnly && clip.layoutIndex >= 0) return $"Layout {clip.layoutIndex:00}";
        var parameters = new[] { (clip.parameter1, clip.parameter1Value), (clip.parameter2, clip.parameter2Value), (clip.parameter3, clip.parameter3Value) };
        string label = string.Join(" · ", parameters.Where(pair => IsSweepParameter(pair.Item1))
            .Select(pair => $"{(pair.Item1 == "RandomMovement" ? "Noise" : pair.Item1)} {Number(pair.Item2)}"));
        return string.IsNullOrEmpty(label) ? Path.GetFileNameWithoutExtension(clip.path) : label;
    }

    private static bool IsSweepParameter(string parameter) => !string.IsNullOrWhiteSpace(parameter) &&
        !string.Equals(parameter, "SpawnLayout", StringComparison.OrdinalIgnoreCase);
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string ValueKey(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>
    /// Every clip in <paramref name="folder"/> matching one condition, ordered by layout index.
    /// </summary>
    /// <param name="motionType">Motion type as it appears in the name, e.g. "Flocking".</param>
    /// <param name="random">Randomness to match. Pass NaN to accept any.</param>
    /// <param name="expectedLayouts">
    /// Layouts the caller insists on. 0 accepts however many are there, which is what lets this
    /// work for batches that were not recorded with forty layouts.
    /// </param>
    public static string[] FindRecordings(string folder, string motionType, double maxSpeed,
                                          double perception, double random = 0,
                                          int expectedLayouts = 0)
    {
        if (string.IsNullOrWhiteSpace(motionType))
            throw new ArgumentException("A motion type is required.", nameof(motionType));
        if (!IsUsable(maxSpeed) || !IsUsable(perception))
            throw new ArgumentException("Speed and perception radius must be finite positive values.");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Recording folder not found: {folder}");

        List<Clip> matches = new List<Clip>();
        int sawType = 0;

        foreach (string path in Directory.EnumerateFiles(folder, "*.mp4"))
        {
            Clip clip = Parse(path);
            if (clip == null) continue;

            if (!string.Equals(clip.swarmType, motionType, StringComparison.OrdinalIgnoreCase)) continue;
            sawType++;

            if (!Near(clip.maxSpeed, maxSpeed)) continue;
            if (!Near(clip.perception, perception)) continue;
            if (!double.IsNaN(random) && !Near(clip.random, random)) continue;

            matches.Add(clip);
        }

        if (matches.Count == 0)
        {
            string detail = sawType == 0
                ? $"No {motionType} clips at all in that folder."
                : $"{sawType} {motionType} clips are there, but none at speed {maxSpeed:0.##}, " +
                  $"perception {perception:0.##}" +
                  (double.IsNaN(random) ? "." : $", randomness {random:0.##}.");

            throw new InvalidDataException($"No recordings match. {detail} {Available(folder, motionType)}");
        }

        // Layout indices order the grid. Clips without one keep their relative file order, which is
        // what happens for a batch recorded without spawn layouts.
        matches.Sort((a, b) =>
        {
            if (a.layoutIndex != b.layoutIndex) return a.layoutIndex.CompareTo(b.layoutIndex);
            return string.CompareOrdinal(a.path, b.path);
        });

        List<Clip> distinct = Deduplicate(matches);

        if (expectedLayouts > 0 && distinct.Count != expectedLayouts)
        {
            throw new InvalidDataException(
                $"Expected {expectedLayouts} layouts for {motionType} at speed {maxSpeed:0.##}, " +
                $"perception {perception:0.##}, but found {distinct.Count}.");
        }

        return distinct.Select(c => Path.GetFullPath(c.path)).ToArray();
    }

    /// <summary>
    /// Conditions present in a folder for one motion type, as a readable line for an error message.
    ///
    /// A grid that will not build is nearly always pointed at the wrong condition rather than
    /// broken, so the message says what is actually there instead of only what was asked for.
    /// </summary>
    public static string Available(string folder, string motionType)
    {
        if (!Directory.Exists(folder)) return string.Empty;

        SortedSet<string> speeds = new SortedSet<string>();
        SortedSet<string> radii = new SortedSet<string>();
        SortedSet<string> randoms = new SortedSet<string>();
        SortedSet<string> types = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.EnumerateFiles(folder, "*.mp4"))
        {
            Clip clip = Parse(path);
            if (clip == null) continue;

            if (!string.IsNullOrEmpty(clip.swarmType)) types.Add(clip.swarmType);
            if (!string.Equals(clip.swarmType, motionType, StringComparison.OrdinalIgnoreCase)) continue;

            if (!double.IsNaN(clip.maxSpeed)) speeds.Add(clip.maxSpeed.ToString("0.##", CultureInfo.InvariantCulture));
            if (!double.IsNaN(clip.perception)) radii.Add(clip.perception.ToString("0.##", CultureInfo.InvariantCulture));
            if (!double.IsNaN(clip.random)) randoms.Add(clip.random.ToString("0.##", CultureInfo.InvariantCulture));
        }

        if (types.Count == 0) return "The folder holds no clips this can read.";
        if (speeds.Count == 0) return $"Motion types present: {string.Join(", ", types)}.";

        return $"Available for {motionType} — speeds: {string.Join(", ", speeds)}; " +
               $"perception: {string.Join(", ", radii)}; randomness: {string.Join(", ", randoms)}.";
    }

    /// <summary>Motion types that appear in a folder, for reporting what a folder holds.</summary>
    public static string[] MotionTypesIn(string folder)
    {
        SortedSet<string> types = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(folder)) return Array.Empty<string>();

        foreach (string path in Directory.EnumerateFiles(folder, "*.mp4"))
        {
            Clip clip = Parse(path);
            if (clip != null && !string.IsNullOrEmpty(clip.swarmType)) types.Add(clip.swarmType);
        }

        return types.ToArray();
    }

    /// <summary>
    /// Reads the parameters out of a file name.
    ///
    /// Everything after the last recognised value is the layout id, which keeps this working when
    /// the layout name contains underscores of its own — as it does, since it carries a timestamp.
    /// </summary>
    public static Clip Parse(string filePath)
    {
        string name = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrEmpty(name)) return null;

        string[] parts = name.Split('_');
        Clip clip = new Clip { path = filePath };
        int lastValue = -1;

        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "type":
                    clip.swarmType = parts[i + 1];
                    break;
                case "randommovement":
                    if (TryValue(parts[i + 1], out clip.random)) lastValue = i + 1;
                    break;
                case "perceptionrad":
                case "perceptionradius":
                    if (TryValue(parts[i + 1], out clip.perception)) lastValue = i + 1;
                    break;
                case "maxspeed":
                    if (TryValue(parts[i + 1], out clip.maxSpeed)) lastValue = i + 1;
                    break;
            }
        }

        if (string.IsNullOrEmpty(clip.swarmType)) return null;

        if (lastValue >= 0 && lastValue + 1 < parts.Length)
        {
            clip.layoutId = string.Join("_", parts.Skip(lastValue + 1));
            clip.layoutIndex = TrailingIndex(clip.layoutId);
        }

        return clip;
    }

    /// <summary>
    /// The number a layout id ends with, or -1. `..._layout_07` gives 7, which is what orders the
    /// grid; anything else keeps -1 and falls back to file order.
    /// </summary>
    private static int TrailingIndex(string layoutId)
    {
        if (string.IsNullOrEmpty(layoutId)) return -1;

        int end = layoutId.Length;
        int start = end;
        while (start > 0 && char.IsDigit(layoutId[start - 1])) start--;
        if (start == end) return -1;

        return int.TryParse(layoutId.Substring(start, end - start), NumberStyles.None,
                            CultureInfo.InvariantCulture, out int value) ? value : -1;
    }

    /// <summary>
    /// Drops repeats of the same layout, keeping the first.
    ///
    /// Two clips of one layout mean two batches are sitting in the folder. The flocking catalog
    /// treats that as fatal; a grid can still be shown from the first batch, so it is kept and the
    /// count in the heading makes the situation visible.
    /// </summary>
    private static List<Clip> Deduplicate(List<Clip> ordered)
    {
        List<Clip> distinct = new List<Clip>();
        HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Clip clip in ordered)
        {
            string key = clip.layoutId ?? clip.path;
            if (seen.Add(key)) distinct.Add(clip);
        }

        return distinct;
    }

    private static bool Near(double value, double wanted)
    {
        return !double.IsNaN(value) && Math.Abs(value - wanted) < Tolerance;
    }

    private static bool IsUsable(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }

    private static bool TryValue(string token, out double value)
    {
        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
