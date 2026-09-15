using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>Finds the forty zero-randomness flocking videos for one speed and perception radius.</summary>
public static class FlockRecordingCatalog
{
    public const int LayoutCount = 40;
    private static readonly Regex FilePattern = new Regex(
        @"^type_flocking_randommovement_(?<random>\d+(?:\.\d+)?)_perceptionrad_(?<radius>\d+(?:\.\d+)?)_maxspeed_(?<speed>\d+(?:\.\d+)?)_(?<batch>\d{8}_\d{6})_layout_(?<layout>\d+)\.mp4$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string[] FindRecordings(string folder, float speed, float perception)
    {
        if (float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0 ||
            float.IsNaN(perception) || float.IsInfinity(perception) || perception <= 0)
            throw new ArgumentException("Speed and perception radius must be finite positive values.");
        if (!Directory.Exists(folder))
            throw new DirectoryNotFoundException($"Recording folder not found: {folder}");

        string[] recordings = new string[LayoutCount];
        string batch = null;
        foreach (string path in Directory.EnumerateFiles(folder))
        {
            Match match = FilePattern.Match(Path.GetFileName(path));
            if (!match.Success || !Matches(match, "random", 0) ||
                !Matches(match, "speed", speed) || !Matches(match, "radius", perception)) continue;

            string clipBatch = match.Groups["batch"].Value;
            if (batch != null && batch != clipBatch)
                throw new InvalidDataException("Multiple recording batches match. Select a folder containing only one batch.");
            batch = clipBatch;
            if (!int.TryParse(match.Groups["layout"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int layout) ||
                layout < 0 || layout >= LayoutCount)
                throw new InvalidDataException($"Expected layouts 00–39, found {Path.GetFileName(path)}.");
            if (recordings[layout] != null)
                throw new InvalidDataException($"Duplicate layout {layout:00}: {recordings[layout]} and {path}");
            recordings[layout] = Path.GetFullPath(path);
        }

        for (int layout = 0; layout < recordings.Length; layout++)
            if (recordings[layout] == null)
                throw new InvalidDataException($"Missing layout {layout:00} for speed {speed:0.##}, perception {perception:0.##}. " +
                                               "A complete grid requires forty matching MP4 recordings.");
        return recordings;
    }

    private static bool Matches(Match match, string group, float wanted)
    {
        return double.TryParse(match.Groups[group].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) &&
               Math.Abs(value - wanted) < 0.0001;
    }
}
