using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

/// <summary>
/// Finds the recorded clips for one condition — one motion type at one randomness, perception
/// radius and max speed — and returns them ordered by spawn layout.
///
/// <see cref="FlockRecordingCatalog"/> answers the same question for flocking only, with a fixed
/// regex and a hard requirement of forty layouts. This is the general form, used by
/// <see cref="AllGridManager"/>; the flocking pair is left alone so existing scenes keep behaving
/// exactly as they did.
///
/// Names are read key by key rather than by a fixed pattern:
///
///   type_flocking_randommovement_0.00_perceptionrad_4.00_maxspeed_3.00_20260908_013309_layout_07
///
/// SimRecorder builds that name from whichever parameters a batch happened to sweep, so their
/// order and which of them appear both vary between batches. A positional regex only fits the
/// batch it was written for; reading `key_value` pairs fits any of them.
/// </summary>
public static class AllRecordingCatalog
{
    /// <summary>Tolerance for matching a recorded parameter against a requested one.</summary>
    private const double Tolerance = 0.0001;

    /// <summary>Parameters read out of one file name. NaN means the key was absent.</summary>
    public sealed class Clip
    {
        public string path;
        public string swarmType;
        public string layoutId;
        public int layoutIndex = -1;
        public double random = double.NaN;
        public double perception = double.NaN;
        public double maxSpeed = double.NaN;
    }

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
