using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;
using System.Linq;
using System.Collections.Generic;
using TMPro;

public class VideoGridManager : MonoBehaviour
{
    [Header("Configuration")]
    public string folderPath = "Assets/SimulationRecordings/";

    [Header("UI Layout")]
    public RectTransform gridContainer;

    [Header("Grid Labels (Drag & Drop)")]
    public TextMeshProUGUI topMainLabel;
    public TextMeshProUGUI leftMainLabel;
    public TextMeshProUGUI[] topLabels;
    public TextMeshProUGUI[] leftLabels;

    [Header("Grid Info")]
    public int rows = 4;
    public int cols = 4;

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

        string[] videoFiles = Directory.GetFiles(path, "*.mp4")
            .OrderBy(f => System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(f), @"\d+(\.\d+)?", m =>
            {
                string[] p = m.Value.Split('.');
                string intPart = p[0].PadLeft(10, '0');
                string decPart = p.Length > 1 ? p[1].PadRight(10, '0') : "0000000000";
                return intPart + "." + decPart;
            }))
            .ToArray();
        if (videoFiles.Length == 0)
        {
            Debug.LogWarning($"[VideoGridManager] No MP4 files found in directory: {path}");
            return;
        }

        Debug.Log($"[VideoGridManager] Found {videoFiles.Length} videos. Generating {rows}x{cols} grid...");

        // Make sure container has a GridLayoutGroup or we calculate manually.
        GridLayoutGroup gridLayout = gridContainer.GetComponent<GridLayoutGroup>();
        if (gridLayout == null)
        {
            gridLayout = gridContainer.gameObject.AddComponent<GridLayoutGroup>();
            gridLayout.cellSize = new Vector2(renderWidth, renderHeight);
            gridLayout.spacing = new Vector2(10, 10);
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = cols;
        }

        for (int i = 0; i < videoFiles.Length; i++)
        {
            if (i >= rows * cols) break;
            CreateVideoElement(videoFiles[i], i);
        }
    }

    private void CreateVideoElement(string filePath, int index)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath);
        GameObject videoElement = new GameObject("VideoView_" + fileName);
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

        RenderTexture rt = new RenderTexture(renderWidth, renderHeight, 0, RenderTextureFormat.ARGB32);
        rt.Create();

        videoPlayer.targetTexture = rt;
        rawImage.texture = rt;

        // Parse filename to get param values, format expected: param1_val_param2_val
        string[] parts = fileName.Split('_');
        if (parts.Length >= 4)
        {
            int rowIndex = index / cols;
            int colIndex = index % cols;

            // Update Left labels (param 1) based on the first item in each row
            if (colIndex == 0 && leftLabels != null && rowIndex < leftLabels.Length && leftLabels[rowIndex] != null)
            {
                // leftLabels[rowIndex].text = $"{parts[0]}: {parts[1]}";
                leftLabels[rowIndex].text = $"{parts[1]}";
                if (leftMainLabel != null) leftMainLabel.text = parts[0];
            }

            // Update Top labels (param 2) based on the first item in each column
            if (rowIndex == 0 && topLabels != null && colIndex < topLabels.Length && topLabels[colIndex] != null)
            {
                // topLabels[colIndex].text = $"{parts[2]}: {parts[3]}";
                topLabels[colIndex].text = $"{parts[3]}";
                if (topMainLabel != null) topMainLabel.text = parts[2];
            }
        }
    }
}
