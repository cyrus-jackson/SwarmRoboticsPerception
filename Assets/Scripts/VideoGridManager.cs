using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class VideoGridManager : MonoBehaviour
{
    [Header("Configuration")]
    public string folderPath = "Assets/SimulationRecordings/";

    [Header("UI Layout")]
    public RectTransform gridContainer;

    [Header("Optimization Settings")]
    public int renderWidth = 640;
    public int renderHeight = 360;

    void Start()
    {
        string path = folderPath;
        if (path.StartsWith("Assets/"))
        {
            path = Path.Combine(Application.dataPath, path.Substring(7));
        }

        LoadVideosFromFolder(path);
    }

    public void LoadVideosFromFolder(string path)
    {
        if (gridContainer == null)
        {
            Debug.LogError("[VideoGridManager] Grid Container is missing. Please assign a parent RectTransform.");
            return;
        }

        if (!Directory.Exists(path))
        {
            Debug.LogError($"[VideoGridManager] Directory not found: {path}");
            return;
        }

        string[] videoFiles = Directory.GetFiles(path, "*.mp4");
        if (videoFiles.Length == 0)
        {
            Debug.LogWarning($"[VideoGridManager] No MP4 files found in directory: {path}");
            return;
        }

        Debug.Log($"[VideoGridManager] Found {videoFiles.Length} videos. Generating grid...");

        foreach (string file in videoFiles)
        {
            CreateVideoElement(file);
        }
    }

    private void CreateVideoElement(string filePath)
    {
        // Create container for the video Element
        GameObject videoElement = new GameObject("VideoView_" + Path.GetFileNameWithoutExtension(filePath));
        videoElement.transform.SetParent(gridContainer, false);

        // Add UI RawImage to display the video texture
        RawImage rawImage = videoElement.AddComponent<RawImage>();
        rawImage.color = Color.white;

        // Add VideoPlayer component
        VideoPlayer videoPlayer = videoElement.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = true;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = filePath;
        videoPlayer.isLooping = true;
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.audioOutputMode = VideoAudioOutputMode.None; // Mute audio because multiple videos will overlap

        // Create a unique Render Texture dynamically for each video
        // Smaller resolutions are ideal here to prevent GPU memory crashes when loading 10+ videos at once
        RenderTexture rt = new RenderTexture(renderWidth, renderHeight, 0, RenderTextureFormat.ARGB32);
        rt.Create();

        videoPlayer.targetTexture = rt;
        rawImage.texture = rt;
    }
}
