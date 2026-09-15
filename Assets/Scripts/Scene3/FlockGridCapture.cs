#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using UnityEditor.Recorder;
using UnityEditor.Recorder.Encoder;
using UnityEditor.Recorder.Input;
using UnityEngine;

/// <summary>Records the grid's Game view to one MP4 and marks interrupted captures as partial.</summary>
internal sealed class FlockGridCapture
{
    public string FinalPath { get; private set; }
    private string workingPath;
    private RecorderController controller;
    private RecorderControllerSettings controllerSettings;
    private MovieRecorderSettings movieSettings;

    public void Start(float speed, float perception)
    {
        string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Grid Recordings"));
        Directory.CreateDirectory(folder);
        string name = string.Format(CultureInfo.InvariantCulture,
            "flocking_grid_speed_{0:0.00}_perception_{1:0.00}_{2:yyyyMMdd_HHmmss_fff}_{3}",
            speed, perception, DateTime.Now, Guid.NewGuid().ToString("N").Substring(0, 8));
        FinalPath = Path.Combine(folder, name + ".mp4");
        workingPath = Path.Combine(folder, name + "_partial.mp4");

        controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        movieSettings = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        movieSettings.name = "Flock Grid Recorder";
        movieSettings.Enabled = true;
        movieSettings.EncoderSettings = new CoreEncoderSettings
        {
            Codec = CoreEncoderSettings.OutputCodec.MP4,
            EncodingQuality = CoreEncoderSettings.VideoEncodingQuality.High
        };
        movieSettings.OutputFile = Path.Combine(folder, name + "_partial");
        movieSettings.ImageInputSettings = new GameViewInputSettings { OutputWidth = 1920, OutputHeight = 1080 };
        movieSettings.AudioInputSettings.PreserveAudio = false;
        controllerSettings.AddRecorderSettings(movieSettings);
        controllerSettings.SetRecordModeToManual();
        controllerSettings.FrameRate = 30;
        controllerSettings.FrameRatePlayback = FrameRatePlayback.Constant;
        controllerSettings.CapFrameRate = true;
        controller = new RecorderController(controllerSettings);
        controller.PrepareRecording();
        if (!controller.StartRecording())
            throw new InvalidOperationException("Unity Recorder did not start. See the Console for the encoder error.");
    }

    public string Finish(bool complete)
    {
        try
        {
            controller?.StopRecording();
            if (workingPath == null || !File.Exists(workingPath))
            {
                if (complete) throw new IOException("Unity Recorder did not produce the expected MP4.");
                return null;
            }
            if (complete)
            {
                File.Move(workingPath, FinalPath);
                return FinalPath;
            }
            return workingPath;
        }
        finally
        {
            controller = null;
            if (movieSettings != null) UnityEngine.Object.Destroy(movieSettings);
            if (controllerSettings != null) UnityEngine.Object.Destroy(controllerSettings);
            movieSettings = null;
            controllerSettings = null;
        }
    }
}
#endif
