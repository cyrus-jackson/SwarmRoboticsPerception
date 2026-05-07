using System.Collections;
using System.Collections.Generic;
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
    MaxSpeed,
    PerceptionRad
}

public class SimRecorder : MonoBehaviour
{
    [System.Serializable]
    public class SimulationConfig
    {
        public string fileName;
        public string variedParameter;
        public float variedParameterValue;
        public string parameter1;
        public float parameter1Value;
        public string parameter2;
        public float parameter2Value;
        public string obstacleName;
        public float perceptionRadius;
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

    [System.Serializable]
    private class BatchConfig
    {
        public string batchType;
        public string folderName;
        public string timestamp;
        public float recordingTimePerSim;
        public string saveFolder;
        public SimulationConfig[] simulations;
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

    [Header("Obstacle Batch (Obstacle List + 1 Parameter)")]
    public SwarmParameterToRecord obstacleBatchParameter = SwarmParameterToRecord.PerceptionRad;
    public float obstacleParamStart = 1.3f;
    public float obstacleParamStep = 0.4f;
    public int obstacleParamIterations = 4;

    private bool isRecording = false;
    private float currentParam1DisplayValue = 0f;
    private float currentParam2DisplayValue = 0f;
    private string currentObstacleDisplayName = "";
    private bool isObstacleBatchMode = false;

    void OnGUI()
    {
        if (isRecording)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 32;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.white;

            string displayText = isObstacleBatchMode
                ? $"Obstacle: {currentObstacleDisplayName} | {obstacleBatchParameter}: {currentParam1DisplayValue:F2}"
                : $"{parameterToRecord1}: {currentParam1DisplayValue:F2} | {parameterToRecord2}: {currentParam2DisplayValue:F2}";

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

    public void StartObstacleBatchRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(ObstacleBatchRecordCoroutine());
        }
    }

    private IEnumerator BatchRecordCoroutine()
    {
        isRecording = true;
        isObstacleBatchMode = false;

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

        List<SimulationConfig> simulations = new List<SimulationConfig>();

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
                    fileName = fileName,
                    variedParameter = paramFolderName,
                    variedParameterValue = currentParam1, // Storing one for backward compatibility or change if needed
                    parameter1 = parameterToRecord1.ToString(),
                    parameter1Value = currentParam1,
                    parameter2 = parameterToRecord2.ToString(),
                    parameter2Value = currentParam2,
                    obstacleName = swarmManager.centralObstacle != null ? swarmManager.centralObstacle.name : null,
                    perceptionRadius = swarmManager.perceptionRadius,
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

                simulations.Add(config);

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

        // Write a single config for the whole folder.
        {
            BatchConfig batchConfig = new BatchConfig
            {
                batchType = "two-parameter",
                folderName = paramFolderName,
                timestamp = timestampFolder,
                recordingTimePerSim = recordingTimePerSim,
                saveFolder = saveFolder,
                simulations = simulations.ToArray()
            };

            string configJson = JsonUtility.ToJson(batchConfig, true);
            File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);
        }

        // Restore UI
        if (uiController != null)
        {
            uiController.showUI = true;
        }

        isRecording = false;
        Debug.Log("[SimRecorder] Batch recording finished.");
    }

    private IEnumerator ObstacleBatchRecordCoroutine()
    {
        isRecording = true;
        isObstacleBatchMode = true;

        if (uiController == null || swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing uiController or swarmManager; cannot start obstacle batch recording.");
            isRecording = false;
            isObstacleBatchMode = false;
            yield break;
        }

        if (uiController.obstacles == null || uiController.obstacles.Count == 0)
        {
            Debug.LogWarning("[SimRecorder] UI obstacle list is empty; nothing to record.");
            isRecording = false;
            isObstacleBatchMode = false;
            yield break;
        }

        // Snapshot the obstacle list so we can restore it afterward.
        List<Transform> originalObstacles = new List<Transform>(uiController.obstacles);

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = $"Obstacle_vs_{obstacleBatchParameter}";
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath))
        {
            Directory.CreateDirectory(targetFolderPath);
        }

        // Hide UI
        uiController.showUI = false;

        List<SimulationConfig> simulations = new List<SimulationConfig>();

        for (int obstacleIndex = 0; obstacleIndex < originalObstacles.Count; obstacleIndex++)
        {
            Transform obstacle = originalObstacles[obstacleIndex];
            if (obstacle == null) continue;

            currentObstacleDisplayName = obstacle.name;
            uiController.SetDefaultObstacle(obstacle);

            for (int i = 0; i < obstacleParamIterations; i++)
            {
                float currentParam = obstacleParamStart + (i * obstacleParamStep);
                currentParam1DisplayValue = currentParam;

                uiController.SetParameter(obstacleBatchParameter, currentParam);
                uiController.ResetScene();
                uiController.SetMotion(true);

                string obstacleSafe = SanitizeFileName(obstacle.name);
                string fileName = $"obstacle_{obstacleSafe}_{obstacleBatchParameter.ToString().ToLower()}_{currentParam:F2}";
                fileName = SanitizeFileName(fileName);

                SimulationConfig config = new SimulationConfig
                {
                    fileName = fileName,
                    variedParameter = $"{paramFolderName}:{obstacleSafe}",
                    variedParameterValue = currentParam,
                    parameter1 = obstacleBatchParameter.ToString(),
                    parameter1Value = currentParam,
                    parameter2 = null,
                    parameter2Value = 0f,
                    obstacleName = obstacle.name,
                    perceptionRadius = swarmManager.perceptionRadius,
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

                simulations.Add(config);

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
                Debug.Log($"[SimRecorder] Saved obstacle batch video to {targetFolderPath}/{fileName}.mp4");
            }
        }

        // Write a single config for the whole folder.
        {
            BatchConfig batchConfig = new BatchConfig
            {
                batchType = "obstacles+one-parameter",
                folderName = paramFolderName,
                timestamp = timestampFolder,
                recordingTimePerSim = recordingTimePerSim,
                saveFolder = saveFolder,
                simulations = simulations.ToArray()
            };

            string configJson = JsonUtility.ToJson(batchConfig, true);
            File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);
        }

        // Restore obstacle list and UI
        uiController.obstacles.Clear();
        uiController.obstacles.AddRange(originalObstacles);
        uiController.showUI = true;
        uiController.SetMotion(false);

        isRecording = false;
        isObstacleBatchMode = false;
        Debug.Log("[SimRecorder] Obstacle batch recording finished.");
    }

    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "unnamed";
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name;
    }
}
