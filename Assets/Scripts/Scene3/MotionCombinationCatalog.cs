using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>Selects one recorded repeat as four motion rows with eight parameter combinations each.</summary>
public static class MotionCombinationCatalog
{
    public const int MotionCount = 4;
    public const int CombinationsPerMotion = 8;
    private static readonly Regex RepeatSuffix = new Regex(@"(?:^|_)repeat_(\d+)$", RegexOptions.IgnoreCase);

    public static AllRecordingCatalog.Grid Select(string folder, int repeat, string[] motionOrder)
    {
        if (repeat < 1) throw new ArgumentException("Repeat must be at least 1.", nameof(repeat));
        if (motionOrder == null || motionOrder.Length != MotionCount || motionOrder.Any(string.IsNullOrWhiteSpace) ||
            motionOrder.Distinct(StringComparer.OrdinalIgnoreCase).Count() != MotionCount)
            throw new ArgumentException("Motion Order must contain four different motion types, one per row.", nameof(motionOrder));
        if (!Directory.Exists(folder)) throw new DirectoryNotFoundException($"Recording folder not found: {folder}");

        var clips = new List<AllRecordingCatalog.Clip>();
        var availableRepeats = new SortedSet<int>();
        foreach (string path in Directory.EnumerateFiles(Path.GetFullPath(folder)))
        {
            if (!string.Equals(Path.GetExtension(path), ".mp4", StringComparison.OrdinalIgnoreCase)) continue;
            Match match = RepeatSuffix.Match(Path.GetFileNameWithoutExtension(path));
            if (!match.Success || !int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int index)) continue;
            availableRepeats.Add(index);
            if (index != repeat) continue;
            AllRecordingCatalog.Clip clip = AllRecordingCatalog.Parse(path);
            if (clip == null || !Finite(clip.random) || clip.random < 0 ||
                !Finite(clip.perception) || clip.perception <= 0 || !Finite(clip.maxSpeed) || clip.maxSpeed <= 0)
                throw new InvalidDataException($"Cannot read motion, randomness, perception radius and speed from {Path.GetFileName(path)}.");
            clips.Add(clip);
        }
        if (clips.Count == 0)
            throw new InvalidDataException($"No videos for repeat {repeat:00}. Available repeats: " +
                (availableRepeats.Count == 0 ? "none (expected filenames ending in _repeat_01.mp4)." : string.Join(", ", availableRepeats) + "."));

        string[] unexpected = clips.Select(clip => clip.swarmType).Distinct(StringComparer.OrdinalIgnoreCase)
            .Except(motionOrder, StringComparer.OrdinalIgnoreCase).ToArray();
        if (unexpected.Length > 0)
            throw new InvalidDataException($"Motion Order is missing recorded motion types: {string.Join(", ", unexpected)}.");

        var ordered = new List<AllRecordingCatalog.Clip>();
        var labels = new List<string>();
        double[] sharedRadii = null;
        double[] sharedSpeeds = null;
        foreach (string motion in motionOrder)
        {
            AllRecordingCatalog.Clip[] row = clips.Where(clip => string.Equals(clip.swarmType, motion, StringComparison.OrdinalIgnoreCase))
                .OrderBy(clip => clip.random).ThenBy(clip => clip.perception).ThenBy(clip => clip.maxSpeed).ToArray();
            double[] noise = row.Select(clip => clip.random).Distinct().OrderBy(value => value).ToArray();
            double[] radii = row.Select(clip => clip.perception).Distinct().OrderBy(value => value).ToArray();
            double[] speeds = row.Select(clip => clip.maxSpeed).Distinct().OrderBy(value => value).ToArray();
            if (row.Length != CombinationsPerMotion || noise.Length != 2 || radii.Length != 2 || speeds.Length != 2 ||
                row.Select(clip => (clip.random, clip.perception, clip.maxSpeed)).Distinct().Count() != CombinationsPerMotion)
                throw new InvalidDataException($"{motion}, repeat {repeat:00}: expected eight unique combinations " +
                    $"(2 randomness × 2 radius × 2 speed); found {row.Length} videos. Check for missing clips or duplicate batches.");
            if (sharedRadii != null && (!sharedRadii.SequenceEqual(radii) || !sharedSpeeds.SequenceEqual(speeds)))
                throw new InvalidDataException($"{motion} has different radius or speed values from the other rows.");
            sharedRadii = radii;
            sharedSpeeds = speeds;
            ordered.AddRange(row);
            string label = MotionLabel(motion);
            labels.AddRange(row.Select(clip => $"{label}\nNoise {NoiseLabel(clip)} · Perception {Number(clip.perception)} · Speed {Number(clip.maxSpeed)}"));
        }

        return new AllRecordingCatalog.Grid
        {
            clips = ordered.ToArray(),
            labels = labels.ToArray(),
            title = $"Layout {repeat:00}",
            titleOnly = true,
            showEndLabels = false,
            outputLabel = $"all_motion_combinations_repeat_{repeat:00}",
            labelHeight = 40f
        };
    }

    internal static string MotionLabel(string motion)
    {
        if (string.Equals(motion, "DensificationV2", StringComparison.OrdinalIgnoreCase)) return "Densification";
        if (string.Equals(motion, "Dispersion", StringComparison.OrdinalIgnoreCase)) return "Expansion";
        return motion;
    }

    internal static string NoiseLabel(AllRecordingCatalog.Clip clip)
    {
        bool remap = string.Equals(clip.swarmType, "Random", StringComparison.OrdinalIgnoreCase) &&
            (clip.random == 20 || clip.random == 60);
        return Number(remap ? clip.random - 20 : clip.random);
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    private static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
