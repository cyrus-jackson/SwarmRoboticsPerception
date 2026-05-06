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
    ObstacleRadius,
    MaxSpeed
}

public class SimRecorder : MonoBehaviour
{
    [System.Serializable]
    public class SimulationConfig
    {
        public string variedParameter;
        public float variedParameterValue;
        public float cohesion;
        public float separation;
        public float alignment;
        public float friction;
        public float randomMovement;
        public float overlapAvoidance;
        public float safetyDistance;
        public float envAvoidance;
        public float obstacleRadius;
        public float maxSpeed;
        public int numAgents;
    }

    public UI uiController;
    public SwarmManager swarmManager;

    [Header("Recording Settings")]
    public float recordingTimePerSim = 15f;
    public string saveFolder = "SimulationRecordings";

    [Header("Parameter 1 Modification")]
    public SwarmParameterToRecord parameterToRecord1 = SwarmParameterToRecord.Alignment;
    public float param1Start = 0.0f;
    public float param1Step = 2.0f;
    public int param1Iterations = 4;

    [Header("Parameter 2 Modification")]
    public SwarmParameterToRecord parameterToRecord2 = SwarmParameterToRecord.RandomMovement;
    public float param2Start = 0.0f;
    public float param2Step = 2.0f;
    public int param2Iterations = 4;

    private bool isRecording = false;
    private float currentParam1DisplayValue = 0f;
    private float currentParam2DisplayValue = 0f;

    void OnGUI()
    {
        if (isRecording)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 32;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.white;

            string displayText = $"{parameterToRecord1}: {currentParam1DisplayValue:F2} | {parameterToRecord2}: {currentParam2DisplayValue:F2}";

            GUI.Label(new Rect(22, 22, 1000, 50), displayText, new GUIStyle(style) { normal = { textColor = Color.black } });
            GUI.Label(new Rect(20, 20, 1000, 50), displayText, style);
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
        string paramFolderName = $"{parameterToRecord1}_vs_{parameterToRecord2}";
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

        for (int i = 0; i < param1Iterations; i++)
        {
            float currentParam1 = param1Start + (i * param1Step);
            currentParam1DisplayValue = currentParam1;

            for (int j = 0; j < param2Iterations; j++)
            {
                float currentParam2 = param2Start + (j * param2Step);
                currentParam2DisplayValue = currentParam2;

                // Set parameter via code
                if (uiController != null)
                {
                    uiController.SetParameter(parameterToRecord1, currentParam1);
                    uiController.SetParameter(parameterToRecord2, currentParam2);
                }

                uiController.ResetScene();

                // Start simulation
                uiController.SetMotion(true);

                string fileName = $"{parameterToRecord1.ToString().ToLower()}_{currentParam1:F2}_{parameterToRecord2.ToString().ToLower()}_{currentParam2:F2}";

                SimulationConfig config = new SimulationConfig
                {
                    variedParameter = paramFolderName,
                    variedParameterValue = currentParam1, // Storing one for backward compatibility or change if needed
                    cohesion = swarmManager.cohesionIntensity,
                    separation = swarmManager.separationIntensity,
                    alignment = swarmManager.alignmentIntensity,
                    friction = swarmManager.frictionIntensity,
                    randomMovement = swarmManager.randomMovementIntensity,
                    overlapAvoidance = swarmManager.overlappingAvoidanceIntensity,
                    safetyDistance = swarmManager.safetyDistance,
                    envAvoidance = swarmManager.envObstacleAvoidanceIntensity,
                    obstacleRadius = swarmManager.obstacleAvoidanceRadius,
                    maxSpeed = swarmManager.maxSpeed,
                    numAgents = swarmManager.agents != null ? swarmManager.agents.Length : 0
                };
                string configJson = JsonUtility.ToJson(config, true);
                File.WriteAllText(Path.Combine(targetFolderPath, fileName + "_config.json"), configJson);

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
