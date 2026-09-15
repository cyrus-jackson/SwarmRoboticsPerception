using System;
using System.Globalization;
using System.IO;
using System.Linq;

/// <summary>Checks recording selection against all real conditions and malformed or incomplete batches.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        string source = args.Length > 0 ? args[0] : "Assets/SimulationRecordings/Combinations/20260908_013309_flocking_40";
        foreach (float speed in new[] { 1.5f, 3f })
        foreach (float radius in new[] { .5f, 1f, 1.5f, 1.8f, 2f, 2.5f, 3f, 3.5f, 4f })
        {
            string[] files = FlockRecordingCatalog.FindRecordings(source, speed, radius);
            Require(files.Length == 40 && files.Distinct().Count() == 40, "Condition must contain forty unique clips.");
            for (int i = 0; i < files.Length; i++)
            {
                Require(files[i].EndsWith($"_layout_{i:00}.mp4"), "Layouts must be in numeric order.");
                Require(File.Exists(Path.ChangeExtension(files[i], ".json")), "Video must have a matching trajectory.");
            }
        }

        string temporary = Path.Combine(Path.GetTempPath(), "flock-catalog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            for (int i = 39; i >= 0; i--) File.WriteAllText(Path.Combine(temporary, Name(i)), "");
            File.WriteAllText(Path.Combine(temporary, Name(0).Replace("flocking", "dispersion")), "");
            File.WriteAllText(Path.Combine(temporary, Name(0).Replace("randommovement_0.00", "randommovement_1.00")), "");
            File.WriteAllText(Path.Combine(temporary, Name(0).Replace("perceptionrad_4.00", "perceptionrad_3.50")), "");
            Require(FlockRecordingCatalog.FindRecordings(temporary, 3, 4).Length == 40, "Ignore other conditions and parse independently of locale.");
            Reject<ArgumentException>(() => FlockRecordingCatalog.FindRecordings(temporary, float.NaN, 4));
            Reject<ArgumentException>(() => FlockRecordingCatalog.FindRecordings(temporary, 3, float.PositiveInfinity));
            Reject<InvalidDataException>(() => FlockRecordingCatalog.FindRecordings(temporary, 3, 3.99f));
            string duplicate = Path.Combine(temporary, Name(0).Replace("layout_00", "layout_000"));
            File.WriteAllText(duplicate, "");
            Reject<InvalidDataException>(() => FlockRecordingCatalog.FindRecordings(temporary, 3, 4));
            File.Delete(duplicate);
            string anotherBatch = Path.Combine(temporary, Name(0).Replace("20260908_013309", "20260909_013309"));
            File.WriteAllText(anotherBatch, "");
            Reject<InvalidDataException>(() => FlockRecordingCatalog.FindRecordings(temporary, 3, 4));
            File.Delete(anotherBatch);
            File.Delete(Path.Combine(temporary, Name(17)));
            Reject<InvalidDataException>(() => FlockRecordingCatalog.FindRecordings(temporary, 3, 4));
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            Directory.Delete(temporary, true);
        }
        Console.WriteLine("PASS: all 18 conditions / 720 video-trajectory pairs; ordering, locale, filtering, invalid parameters, duplicates, mixed batches and missing layouts.");
        return 0;
    }

    private static string Name(int layout) => $"type_flocking_randommovement_0.00_perceptionrad_4.00_maxspeed_3.00_20260908_013309_layout_{layout:00}.mp4";

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Reject<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
