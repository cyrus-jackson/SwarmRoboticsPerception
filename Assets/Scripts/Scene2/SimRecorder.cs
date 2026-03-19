using System.Collections;
using System.IO;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor.Recorder;
using UnityEditor.Recorder.Input;
#endif

public enum SwarmParameterToRecord
{
    Cohesion,
    Separation,
    Alignment,
    Friction,
    RandomMovement,
    OverlapAvoidance,
    SafetyDistance,
    EnvAvoidance,
    PerceptionRadius,
    ObstacleRadius,
    MaxSpeed
}

public class SimRecorder : MonoBehaviour
{
    public UI uiController;
    public SwarmManager swarmManager;

    [Header("Recording Settings")]
    public float recordingTimePerSim = 10f;
    public string saveFolder = "SimulationRecordings";

    [Header("Parameter Modification")]
    public SwarmParameterToRecord parameterToRecord = SwarmParameterToRecord.PerceptionRadius;
    public float paramStart = 1.0f;
    public float paramStep = 0.2f;
    public int paramIterations = 5;

    private bool isRecording = false; private float currentParamDisplayValue = 0f;

    void OnGUI()
    {
        if (isRecording)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 32;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.white;

            string displayText = $"{parameterToRecord}: {currentParamDisplayValue:F2}";

            GUI.Label(new Rect(22, 22, 500, 50), displayText, new GUIStyle(style) { normal = { textColor = Color.black } });
            GUI.Label(new Rect(20, 20, 500, 50), displayText, style);
        }
    }
    public void StartBatchRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(BatchRecordCoroutine());
        }
    }

    private IEnumerator BatchRecordCoroutine()
    {
        isRecording = true;

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = parameterToRecord.ToString();
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");

        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath))
        {
            Directory.CreateDirectory(targetFolderPath);
        }

        // Hide UI
        if (uiController != null)
        {
            uiController.showUI = false;
        }

        for (int i = 0; i < paramIterations; i++)
        {
            float currentParam = paramStart + (i * paramStep);
            currentParamDisplayValue = currentParam;

            // Set parameter via code
            if (uiController != null)
            {
                uiController.SetParameter(parameterToRecord, currentParam);
            }

            uiController.ResetScene();

            // Start simulation
            uiController.SetMotion(true);

            string fileName = $"{paramFolderName.ToLower()}_{currentParam:F2}";

#if UNITY_EDITOR
            var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
            var recorderController = new RecorderController(controllerSettings);

            var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
            videoRecorder.name = "My Video Recorder";
            videoRecorder.Enabled = true;
            videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
            videoRecorder.OutputFile = Path.Combine(targetFolderPath, fileName);

            videoRecorder.ImageInputSettings = new GameViewInputSettings
            {
                OutputWidth = 1920,
                OutputHeight = 1080
            };
            
            videoRecorder.AudioInputSettings.PreserveAudio = false;

            controllerSettings.AddRecorderSettings(videoRecorder);
            controllerSettings.SetRecordModeToManual();
            controllerSettings.FrameRate = 30;

            recorderController.PrepareRecording();
            recorderController.StartRecording();
#else
            Debug.LogWarning("Unity Recorder is only available in the Editor interface.");
#endif

            float timer = 0f;

            while (timer < recordingTimePerSim)
            {
                yield return new WaitForEndOfFrame();
                timer += Time.deltaTime;
            }

#if UNITY_EDITOR
            recorderController.StopRecording();
#endif

            uiController.SetMotion(false);
            Debug.Log($"[SimRecorder] Saved video sequence to {targetFolderPath}/{fileName}.mp4");
        }

        // Restore UI
        if (uiController != null)
        {
            uiController.showUI = true;
        }

        isRecording = false;
        Debug.Log("[SimRecorder] Batch recording finished.");
    }
}
