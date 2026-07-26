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
        public string parameter3;
        public float parameter3Value;
        public string swarmType;
        public string obstacleName;
        public string obstacleSpawnLocationName;
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
    public float recordingTimePerSim = 20f;
    public string saveFolder = "SimulationRecordings";

    [Header("Parameter 1 Modification")]
    public SwarmParameterToRecord parameterToRecord1 = SwarmParameterToRecord.PerceptionRad;
    public float param1Start = 0.15f;
    public float param1Step = 5.0f;
    public int param1Iterations = 4;

    [Header("Parameter 2 Modification")]
    public SwarmParameterToRecord parameterToRecord2 = SwarmParameterToRecord.RandomMovement;
    public float param2Start = 0.0f;
    public float param2Step = 20.0f;
    public int param2Iterations = 4;

    [Header("SwarmType + Parameter Batch")]
    public List<SwarmType> swarmTypesToRecord = new List<SwarmType> { SwarmType.Flocking, SwarmType.Densification, SwarmType.Random, SwarmType.Dispersion };
    public SwarmParameterToRecord swarmTypeBatchParameter = SwarmParameterToRecord.MaxSpeed;
    public float swarmTypeParamStart = 1.0f;
    public float swarmTypeParamStep = 1.0f;
    public int swarmTypeParamIterations = 4;

    [Header("Combinations Batch")]
    public List<SwarmType> combinationSwarmTypes = new List<SwarmType> { SwarmType.Flocking, SwarmType.Densification, SwarmType.Random, SwarmType.Dispersion };
    public SwarmParameterToRecord combinationParameter1 = SwarmParameterToRecord.RandomMovement;
    public float[] combinationParam1Values = new float[] { 0.0f, 30.0f, 64.0f };
    public SwarmParameterToRecord combinationParameter2 = SwarmParameterToRecord.PerceptionRad;
    public float[] combinationParam2Values = new float[] { 0.15f, 3.15f, 40.15f };
    public SwarmParameterToRecord combinationParameter3 = SwarmParameterToRecord.MaxSpeed;
    public float[] combinationParam3Values = new float[] { 1.5f, 4.0f };

    [Header("Single Parameter Batch")]
    public SwarmParameterToRecord singleBatchParameter = SwarmParameterToRecord.MaxSpeed;
    public float singleParamStart = 0.15f;
    public float singleParamStep = 2.8f;
    public int singleParamIterations = 16;

    [Header("Obstacle Batch (Obstacle List + 1 Parameter)")]
    public SwarmParameterToRecord obstacleBatchParameter = SwarmParameterToRecord.RandomMovement;
    public float obstacleParamStart = 1.3f;
    public float obstacleParamStep = 0.4f;
    public int obstacleParamIterations = 4;

    private bool isRecording = false;
    private float currentParam1DisplayValue = 0f;
    private float currentParam2DisplayValue = 0f;
    private string currentObstacleDisplayName = "";
    private string currentSpawnLocationDisplayName = "";
    private bool isObstacleBatchMode = false;
    private bool isObstacleSpawnBatchMode = false;
    private bool isSingleParameterBatchMode = false;
    private bool isSwarmTypeBatchMode = false;
    private bool hideOverlayText = false;
    private SwarmType currentSwarmTypeDisplay;

    void OnGUI()
    {
        if (isRecording && !hideOverlayText)
        {
            GUIStyle style = new GUIStyle();
            style.fontSize = 36;
            style.fontStyle = FontStyle.Bold;
            style.normal.textColor = Color.black;

            string displayText;
            if (isObstacleSpawnBatchMode)
            {
                displayText = $"Obstacle: {currentObstacleDisplayName} | Spawn: {currentSpawnLocationDisplayName}";
            }
            else if (isObstacleBatchMode)
            {
                displayText = $"Obstacle: {currentObstacleDisplayName} | {obstacleBatchParameter}: {currentParam1DisplayValue:F2}";
            }
            else if (isSingleParameterBatchMode)
            {
                displayText = $"{singleBatchParameter}: {currentParam1DisplayValue:F2}";
            }
            else if (isSwarmTypeBatchMode)
            {
                displayText = $"Type: {currentSwarmTypeDisplay} | {swarmTypeBatchParameter}: {currentParam1DisplayValue:F2}";
            }
            else
            {
                displayText = $"{parameterToRecord1}: {currentParam1DisplayValue:F2} | {parameterToRecord2}: {currentParam2DisplayValue:F2}";
            }

            GUI.Label(new Rect(22, 22, 1000, 50), displayText, new GUIStyle(style) { normal = { textColor = Color.white } });
            GUI.Label(new Rect(20, 20, 1000, 50), displayText, style);
        }
    }
    public void StartSingleParameterBatchRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(SingleParameterBatchRecordCoroutine());
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

    public void StartObstacleSpawnLocationBatchRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(ObstacleSpawnLocationBatchRecordCoroutine());
        }
    }

    public void StartSwarmTypeParameterBatchRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(SwarmTypeParameterBatchRecordCoroutine());
        }
    }

    public void StartCombinationsRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(CombinationsRecordCoroutine());
        }
    }

    public void StartCurrentSettingsRecording()
    {
        if (!isRecording)
        {
            StartCoroutine(CurrentSettingsRecordCoroutine());
        }
    }

    private IEnumerator CurrentSettingsRecordCoroutine()
    {
        isRecording = true;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = false;
        hideOverlayText = true;

        if (swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing swarmManager; cannot start current settings recording.");
            isRecording = false;
            hideOverlayText = false;
            yield break;
        }

        string currentFolderPath = Path.Combine(Application.dataPath, saveFolder, "Current");
        if (!Directory.Exists(currentFolderPath))
        {
            Directory.CreateDirectory(currentFolderPath);
        }

        string fileName = $"current_{System.DateTime.Now:yyyyMMdd_HHmmss}";

        if (uiController != null)
        {
            uiController.showUI = false;
            uiController.SetMotion(true);
        }

#if UNITY_EDITOR
        var controllerSettings = ScriptableObject.CreateInstance<RecorderControllerSettings>();
        var recorderController = new RecorderController(controllerSettings);

        var videoRecorder = ScriptableObject.CreateInstance<MovieRecorderSettings>();
        videoRecorder.name = "Current Settings Recorder";
        videoRecorder.Enabled = true;
        videoRecorder.OutputFormat = MovieRecorderSettings.VideoRecorderOutputFormat.MP4;
        videoRecorder.OutputFile = Path.Combine(currentFolderPath, fileName);

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

        SimulationConfig config = new SimulationConfig
        {
            fileName = fileName,
            variedParameter = "CurrentSettings",
            variedParameterValue = 0f,
            parameter1 = null,
            parameter1Value = 0f,
            parameter2 = null,
            parameter2Value = 0f,
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

        string configJson = JsonUtility.ToJson(config, true);
        File.WriteAllText(Path.Combine(currentFolderPath, $"{fileName}_config.json"), configJson);

        if (uiController != null)
        {
            uiController.SetMotion(false);
            uiController.showUI = true;
        }

        hideOverlayText = false;
        isRecording = false;
        Debug.Log($"[SimRecorder] Saved current settings recording to {currentFolderPath}/{fileName}.mp4");
    }

    private IEnumerator CombinationsRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = true;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = false;

        if (uiController == null || swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing uiController or swarmManager; cannot start combinations recording.");
            isRecording = false;
            hideOverlayText = false;
            yield break;
        }

        if (combinationSwarmTypes == null || combinationSwarmTypes.Count == 0)
        {
            Debug.LogWarning("[SimRecorder] Combination swarm type list is empty; nothing to record.");
            isRecording = false;
            hideOverlayText = false;
            yield break;
        }

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = "Combinations";
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath))
        {
            Directory.CreateDirectory(targetFolderPath);
        }

        uiController.showUI = false;

        List<SimulationConfig> simulations = new List<SimulationConfig>();
        HashSet<string> seenCombinationKeys = new HashSet<string>();

        if (combinationParam1Values == null || combinationParam1Values.Length == 0 || combinationParam2Values == null || combinationParam2Values.Length == 0 || combinationParam3Values == null || combinationParam3Values.Length == 0)
        {
            Debug.LogWarning("[SimRecorder] One or more combination value arrays are empty; nothing to record.");
            isRecording = false;
            hideOverlayText = false;
            yield break;
        }

        int combinationIndex = 0;

        for (int typeIndex = 0; typeIndex < combinationSwarmTypes.Count; typeIndex++)
        {
            SwarmType swarmType = combinationSwarmTypes[typeIndex];
            float[] param1ValuesForType = GetCombinationParam1ValuesForType(swarmType);

            for (int param1Index = 0; param1Index < param1ValuesForType.Length; param1Index++)
            {
                float currentParam1 = param1ValuesForType[param1Index];

                for (int param2Index = 0; param2Index < combinationParam2Values.Length; param2Index++)
                {
                    float currentParam2 = combinationParam2Values[param2Index];

                    for (int param3Index = 0; param3Index < combinationParam3Values.Length; param3Index++)
                    {

                        float currentParam3 = combinationParam3Values[param3Index];

                        uiController.SetSwarmType(swarmType);
                        uiController.SetParameter(combinationParameter1, currentParam1);
                        uiController.SetParameter(combinationParameter2, currentParam2);
                        uiController.SetParameter(combinationParameter3, currentParam3);

                        string combinationKey = $"{swarmType}|{combinationParameter1}|{currentParam1:F4}|{combinationParameter2}|{currentParam2:F4}|{combinationParameter3}|{currentParam3:F4}";
                        Debug.Log($"[SimRecorder] Current Combination: {combinationKey}");
                        if (!seenCombinationKeys.Add(combinationKey))
                        {
                            Debug.Log($"[SimRecorder] Skipping duplicate combination: {combinationKey}");
                            continue;
                        }

                        uiController.ResetScene();
                        uiController.SetMotion(true);

                        string fileName = $"type_{swarmType.ToString().ToLower()}_{combinationParameter1.ToString().ToLower()}_{currentParam1:F2}_{combinationParameter2.ToString().ToLower()}_{currentParam2:F2}_{combinationParameter3.ToString().ToLower()}_{currentParam3:F2}";
                        fileName = SanitizeFileName(fileName);

                        SimulationConfig config = new SimulationConfig
                        {
                            fileName = fileName,
                            variedParameter = paramFolderName,
                            variedParameterValue = combinationIndex,
                            parameter1 = "SwarmType",
                            parameter1Value = typeIndex,
                            parameter2 = combinationParameter1.ToString(),
                            parameter2Value = currentParam1,
                            parameter3 = combinationParameter2.ToString(),
                            parameter3Value = currentParam2,
                            swarmType = swarmType.ToString(),
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
                        videoRecorder.name = "Combinations Recorder";
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
                        Debug.Log($"[SimRecorder] Saved combinations recording to {targetFolderPath}/{fileName}.mp4");
                        combinationIndex++;
                    }
                }
            }
        }

        BatchConfig batchConfig = new BatchConfig
        {
            batchType = "combinations",
            folderName = paramFolderName,
            timestamp = timestampFolder,
            recordingTimePerSim = recordingTimePerSim,
            saveFolder = saveFolder,
            simulations = simulations.ToArray()
        };

        string configJson = JsonUtility.ToJson(batchConfig, true);
        File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);

        uiController.showUI = true;
        uiController.SetMotion(false);

        hideOverlayText = false;
        isRecording = false;
        Debug.Log("[SimRecorder] Combinations recording finished.");
    }

    private IEnumerator SingleParameterBatchRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = false;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = true;
        isSwarmTypeBatchMode = false;

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = $"{singleBatchParameter}";
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

        for (int i = 0; i < singleParamIterations; i++)
        {
            // float currentParam = singleParamStart + Mathf.Pow(singleParamStep, i);
            // if (currentParam == 1f)
            //     currentParam = 0;
            float currentParam = singleParamStart + (i * singleParamStep);
            currentParam1DisplayValue = currentParam;

            // Set parameter via code
            if (uiController != null)
            {
                uiController.SetParameter(singleBatchParameter, currentParam);
            }

            uiController.ResetScene();

            // Start simulation
            uiController.SetMotion(true);

            string fileName = $"{singleBatchParameter.ToString().ToLower()}_{currentParam:F2}";
            SimulationConfig config = new SimulationConfig
            {
                fileName = fileName,
                variedParameter = paramFolderName,
                variedParameterValue = currentParam,
                parameter1 = singleBatchParameter.ToString(),
                parameter1Value = currentParam,
                parameter2 = null,
                parameter2Value = 0f,
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

        // Write a single config for the whole folder.
        {
            BatchConfig batchConfig = new BatchConfig
            {
                batchType = "single-parameter",
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
        isSingleParameterBatchMode = false;
        Debug.Log("[SimRecorder] Single parameter batch recording finished.");
    }

    private IEnumerator BatchRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = false;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = false;

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
                float currentParam = swarmTypeParamStart + Mathf.Pow(swarmTypeParamStep, i);
                if (currentParam == 1f)
                    currentParam = 0;
                // float currentParam2 = param2Start + Mathf.Pow(param2Step, j);
                // if (currentParam2 == 1f)
                //     currentParam2 = 0;
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
        hideOverlayText = false;
        isObstacleBatchMode = true;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = false;

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

    private IEnumerator ObstacleSpawnLocationBatchRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = false;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = true;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = false;

        if (uiController == null || swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing uiController or swarmManager; cannot start obstacle/spawn-location batch recording.");
            isRecording = false;
            isObstacleSpawnBatchMode = false;
            yield break;
        }

        if (uiController.obstacles == null || uiController.obstacles.Count == 0)
        {
            Debug.LogWarning("[SimRecorder] UI obstacle list is empty; nothing to record.");
            isRecording = false;
            isObstacleSpawnBatchMode = false;
            yield break;
        }

        if (uiController.obstacleSpawnLocations == null || uiController.obstacleSpawnLocations.Count == 0)
        {
            Debug.LogWarning("[SimRecorder] UI obstacle spawn locations list is empty; nothing to record.");
            isRecording = false;
            isObstacleSpawnBatchMode = false;
            yield break;
        }

        // Snapshot lists so we can restore them afterward.
        List<Transform> originalObstacles = new List<Transform>(uiController.obstacles);
        List<Transform> originalSpawnLocations = new List<Transform>(uiController.obstacleSpawnLocations);

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = "Obstacle_x_SpawnLocation";
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

            for (int spawnIndex = 0; spawnIndex < originalSpawnLocations.Count; spawnIndex++)
            {
                Transform spawn = originalSpawnLocations[spawnIndex];
                if (spawn == null) continue;

                currentSpawnLocationDisplayName = spawn.name;
                uiController.SetDefaultObstacleSpawnLocation(spawn);

                uiController.ResetScene();
                uiController.SetMotion(true);

                string obstacleSafe = SanitizeFileName(obstacle.name);
                string spawnSafe = SanitizeFileName(spawn.name);
                string fileName = $"obstacle_{obstacleSafe}_spawn_{spawnSafe}";
                fileName = SanitizeFileName(fileName);

                SimulationConfig config = new SimulationConfig
                {
                    fileName = fileName,
                    variedParameter = paramFolderName,
                    variedParameterValue = (obstacleIndex * 100f) + spawnIndex,
                    parameter1 = "Obstacle",
                    parameter1Value = obstacleIndex,
                    parameter2 = "SpawnLocation",
                    parameter2Value = spawnIndex,
                    obstacleName = obstacle.name,
                    obstacleSpawnLocationName = spawn.name,
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
                Debug.Log($"[SimRecorder] Saved obstacle/spawn batch video to {targetFolderPath}/{fileName}.mp4");
            }
        }

        // Write a single config for the whole folder.
        {
            BatchConfig batchConfig = new BatchConfig
            {
                batchType = "obstacles+spawn-locations",
                folderName = paramFolderName,
                timestamp = timestampFolder,
                recordingTimePerSim = recordingTimePerSim,
                saveFolder = saveFolder,
                simulations = simulations.ToArray()
            };

            string configJson = JsonUtility.ToJson(batchConfig, true);
            File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);
        }

        // Restore lists and UI
        uiController.obstacles.Clear();
        uiController.obstacles.AddRange(originalObstacles);
        uiController.obstacleSpawnLocations.Clear();
        uiController.obstacleSpawnLocations.AddRange(originalSpawnLocations);

        uiController.showUI = true;
        uiController.SetMotion(false);

        isRecording = false;
        isObstacleSpawnBatchMode = false;
        Debug.Log("[SimRecorder] Obstacle/spawn-location batch recording finished.");
    }

    private IEnumerator SwarmTypeParameterBatchRecordCoroutine()
    {
        isRecording = true;
        hideOverlayText = false;
        isObstacleBatchMode = false;
        isObstacleSpawnBatchMode = false;
        isSingleParameterBatchMode = false;
        isSwarmTypeBatchMode = true;

        if (uiController == null || swarmManager == null)
        {
            Debug.LogError("[SimRecorder] Missing uiController or swarmManager; cannot start swarm type batch recording.");
            isRecording = false;
            isSwarmTypeBatchMode = false;
            yield break;
        }

        string baseFolderPath = Path.Combine(Application.dataPath, saveFolder);
        string paramFolderName = $"SwarmType_vs_{swarmTypeBatchParameter}";
        string timestampFolder = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string targetFolderPath = Path.Combine(baseFolderPath, paramFolderName, timestampFolder);

        if (!Directory.Exists(targetFolderPath))
        {
            Directory.CreateDirectory(targetFolderPath);
        }

        // Hide UI
        uiController.showUI = false;

        List<SimulationConfig> simulations = new List<SimulationConfig>();
        for (int typeIndex = 0; typeIndex < swarmTypesToRecord.Count; typeIndex++)
        {
            SwarmType sType = swarmTypesToRecord[typeIndex];
            currentSwarmTypeDisplay = sType;

            for (int i = 0; i < swarmTypeParamIterations; i++)
            {

                float currentParam = swarmTypeParamStart + (i * swarmTypeParamStep);

                // float currentParam = swarmTypeParamStart + Mathf.Pow(swarmTypeParamStep, i);
                // if (currentParam == 1f)
                //     currentParam = 0;
                currentParam1DisplayValue = currentParam;

                uiController.SetSwarmType(sType);
                uiController.SetParameter(swarmTypeBatchParameter, currentParam);

                if (swarmTypeBatchParameter != SwarmParameterToRecord.PerceptionRad)
                {
                    uiController.SetParameter(SwarmParameterToRecord.PerceptionRad, 46f);
                }

                uiController.ResetScene();
                uiController.SetMotion(true);

                string typeSafe = SanitizeFileName(sType.ToString());
                string fileName = $"type_{typeSafe}_{swarmTypeBatchParameter.ToString().ToLower()}_{currentParam:F2}";
                fileName = SanitizeFileName(fileName);

                SimulationConfig config = new SimulationConfig
                {
                    fileName = fileName,
                    variedParameter = $"{paramFolderName}",
                    variedParameterValue = currentParam,
                    parameter1 = "SwarmType",
                    parameter1Value = typeIndex,
                    parameter2 = swarmTypeBatchParameter.ToString(),
                    parameter2Value = currentParam,
                    swarmType = sType.ToString(),
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
                Debug.Log($"[SimRecorder] Saved swarm type batch video to {targetFolderPath}/{fileName}.mp4");
            }
        }

        // Write a single config for the whole folder.
        {
            BatchConfig batchConfig = new BatchConfig
            {
                batchType = "swarmtype+one-parameter",
                folderName = paramFolderName,
                timestamp = timestampFolder,
                recordingTimePerSim = recordingTimePerSim,
                saveFolder = saveFolder,
                simulations = simulations.ToArray()
            };

            string configJson = JsonUtility.ToJson(batchConfig, true);
            File.WriteAllText(Path.Combine(targetFolderPath, "batch_config.json"), configJson);
        }

        uiController.showUI = true;
        uiController.SetMotion(false);

        isRecording = false;
        isSwarmTypeBatchMode = false;
        Debug.Log("[SimRecorder] Swarm type batch recording finished.");
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

    private float[] GetCombinationParam1ValuesForType(SwarmType swarmType)
    {
        if (swarmType == SwarmType.Random)
        {
            return new float[] { 60.0f };
        }

        return combinationParam1Values;
    }
}
