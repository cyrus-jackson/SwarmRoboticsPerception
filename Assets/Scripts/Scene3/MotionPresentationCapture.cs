#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEngine;

/// <summary>Flushes each motion's recording separately and joins the sections into one movie without re-encoding.</summary>
internal sealed class MotionPresentationCapture
{
    private readonly List<string> segments = new List<string>();
    private readonly string folder;
    private readonly string finalPath;
    private readonly string ffmpeg;
    private readonly int width;
    private readonly int height;
    private FlockGridCapture active;

    public MotionPresentationCapture(string destination, int layout, int width, int height)
    {
        ffmpeg = FindFfmpeg();
        string name = $"motion_presentation_layout_{layout:00}_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{Guid.NewGuid():N}";
        finalPath = Path.Combine(destination, name + ".mov");
        folder = Path.Combine(destination, name + "_segments");
        Directory.CreateDirectory(folder);
        this.width = width;
        this.height = height;
    }

    public void StartSection()
    {
        if (active != null) throw new InvalidOperationException("The previous motion recording is still open.");
        active = new FlockGridCapture();
        active.Start(0, 0, $"section_{segments.Count:00}", folder, false, width, height);
    }

    public void FinishSection()
    {
        if (active == null) return;
        FlockGridCapture finished = active;
        active = null;
        segments.Add(finished.Finish(true));
    }

    public string Finish(bool complete)
    {
        if (active != null)
        {
            FlockGridCapture finished = active;
            active = null;
            string path = finished.Finish(complete);
            if (!string.IsNullOrEmpty(path)) segments.Add(path);
        }
        if (segments.Count == 0) { Directory.Delete(folder, true); return null; }
        string list = Path.Combine(folder, "sections.txt");
        File.WriteAllLines(list, segments.Select(path => "file '" + path.Replace("\\", "/").Replace("'", "'\\''") + "'"));
        string partial = Path.Combine(Path.GetDirectoryName(finalPath), Path.GetFileNameWithoutExtension(finalPath) + "_partial.mov");
        using (var process = new Process())
        {
            process.StartInfo = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = "-hide_banner -loglevel error -y -f concat -safe 0 -i " + Quote(list) +
                    " -map 0:v:0 -c copy -movflags +faststart -f mov " + Quote(partial),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true
            };
            process.Start();
            if (!process.WaitForExit(30000))
            {
                process.Kill();
                throw new IOException($"Joining the movie timed out. Recorded sections are preserved in {folder}");
            }
            string error = process.StandardError.ReadToEnd();
            if (process.ExitCode != 0)
                throw new IOException($"Could not join the movie: {error}. Recorded sections are preserved in {folder}");
        }
        if (complete) File.Move(partial, finalPath);
        Directory.Delete(folder, true);
        return complete ? finalPath : partial;
    }

    private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string FindFfmpeg()
    {
        string executable = Application.platform == RuntimePlatform.WindowsEditor ? "ffmpeg.exe" : "ffmpeg";
        var candidates = new List<string> { "/opt/homebrew/bin/ffmpeg", "/usr/local/bin/ffmpeg", "/usr/bin/ffmpeg" };
        candidates.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path)).Select(path => Path.Combine(path, executable)));
        string found = candidates.FirstOrDefault(File.Exists);
        if (found == null) throw new FileNotFoundException("Install FFmpeg to join the motion recordings into one QuickTime MOV, then restart Unity.");
        return found;
    }
}
#endif
