using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public enum SwarmType
{
    Flocking,
    Densification,
    Random,
    Dispersion
}

public class UI : MonoBehaviour
{
    [Header("Swarm Setup")]
    public GameObject agentPrefab;
    public SwarmManager swarmManager;

    [Tooltip("List of possible spawn areas in the scene")]
    public Transform[] spawnAreas;

    [Tooltip("List of possible obstacles in the scene")]
    public Transform[] obstacles;

    [Tooltip("List of possible common fates in the scene")]
    public Transform[] commonFates;

    [Header("Dispersion Setup")]
    [Tooltip("The circular spawn area for Dispersion type")]
    public Transform circleSpawnArea;
    [Tooltip("The obstacle specifically for Dispersion type")]
    public Transform dispersionObstacle;

    [Header("Densification Setup")]
    [Tooltip("The spawn area specifically for Densification type")]
    public Transform densificationSpawnArea;
    [Tooltip("The obstacle specifically for Densification type")]
    public Transform densificationObstacle;
    [Tooltip("The common fate specifically for Densification type")]
    public Transform densificationCommonFate;

    [Header("Flocking Setup")]
    [Tooltip("The spawn area specifically for Flocking type")]
    public Transform flockingSpawnArea;
    [Tooltip("The obstacle specifically for Flocking type")]
    public Transform flockingObstacle;
    [Tooltip("The common fate specifically for Flocking type")]
    public Transform flockingCommonFate;

    public bool showUI = true;
    private bool isRunning = false;
    private int uiNumberOfAgents = 40;
    private List<GameObject> activeAgents = new List<GameObject>();

    // UI Configuration values
    private SwarmType selectedSwarmType = SwarmType.Densification;
    private int selectedSpawnAreaIndex = 0;
    private int selectedObstacleIndex = 0;
    private int selectedCommonFateIndex = 0;

    private float uiCohesion = 5.0f;
    private float uiSeparation = 1.0f;
    private float uiAlignment = 2.0f;
    private float uiFriction = 0.1f;
    private float uiRandomMvmt = 0.0f;

    private float uiOverlapAvoid = 20.0f;
    private float uiSafetyDist = 0.2f;
    private float uiEnvAvoid = 40.0f;

    private float uiPerceptionRad = 0.2f;
    private float uiObstacleRad = 1.8f;
    private float uiMaxSpeed = 1.5f;
    private bool uiShowPerceptionRadius = true;
    private Vector2 scrollPosition;

    void Start()
    {
        if (swarmManager != null)
            swarmManager.enabled = false;

        ApplyPreset(selectedSwarmType);
        ResetScene();
    }

    void Update()
    {
        if (Keyboard.current == null) return;

        if (Keyboard.current.xKey.wasPressedThisFrame)
        {
            showUI = !showUI;
        }

        if (Keyboard.current.spaceKey.wasPressedThisFrame)
        {
            ToggleMotion();
        }
    }

    void ApplyPreset(SwarmType type)
    {
        switch (type)
        {
            case SwarmType.Flocking:
                uiCohesion = 2.0f;
                uiSeparation = 1f;
                uiAlignment = 4.0f;
                uiRandomMvmt = 0.0f;
                uiObstacleRad = 0.8f;
                uiSafetyDist = 0.5f;
                uiPerceptionRad = 1.7f;
                break;
            case SwarmType.Densification:
                uiCohesion = 5.0f;
                uiSeparation = 1.0f;
                uiAlignment = 2.0f;
                uiRandomMvmt = 0.0f;
                uiObstacleRad = 0.8f;
                uiSafetyDist = 0.5f;
                uiPerceptionRad = 1.7f;
                break;
            case SwarmType.Random:
                uiCohesion = 0.0f;
                uiSeparation = 0.0f;
                uiAlignment = 0.0f;
                uiObstacleRad = 0.8f;
                uiRandomMvmt = 20.0f;
                uiPerceptionRad = 3.2f;
                break;
            case SwarmType.Dispersion:
                uiCohesion = 0.0f;
                uiSeparation = 5.0f;
                uiAlignment = 0.0f;
                uiRandomMvmt = 0.0f;
                uiObstacleRad = 0.8f;
                uiSafetyDist = 0.5f;
                uiPerceptionRad = 2.0f;
                break;
        }
    }

    void OnGUI()
    {
        if (!showUI) return;

        GUILayout.BeginArea(new Rect(20, 20, 350, Screen.height - 40), GUI.skin.box);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        GUILayout.Label("Swarm Control UI (Press 'X' to hide)", GUI.skin.label);
        GUILayout.Space(10);

        DrawControls();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawControls()
    {
        GUILayout.Label("<b>General Settings</b>");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Type:", GUILayout.Width(80));
        SwarmType newType = (SwarmType)GUILayout.Toolbar((int)selectedSwarmType, System.Enum.GetNames(typeof(SwarmType)));
        if (newType != selectedSwarmType)
        {
            selectedSwarmType = newType;
            ApplyPreset(selectedSwarmType);
        }
        GUILayout.EndHorizontal();

        // if (selectedSwarmType == SwarmType.Dispersion || selectedSwarmType == SwarmType.Densification || selectedSwarmType == SwarmType.Flocking)
        // {
        //     GUILayout.Space(5);
        //     string typeName = selectedSwarmType.ToString();
        //     GUILayout.Label($"<i>Using designated elements for {typeName}</i>");
        // }
        // else
        // {
        //     if (spawnAreas != null && spawnAreas.Length > 0)
        //     {
        //         GUILayout.BeginHorizontal();
        //         GUILayout.Label("Spawn Area:", GUILayout.Width(80));
        //         string[] areaNames = new string[spawnAreas.Length];
        //         for (int i = 0; i < spawnAreas.Length; i++) areaNames[i] = spawnAreas[i] != null ? spawnAreas[i].name : "None";
        //         selectedSpawnAreaIndex = GUILayout.SelectionGrid(selectedSpawnAreaIndex, areaNames, 2);
        //         GUILayout.EndHorizontal();
        //     }

        //     if (obstacles != null && obstacles.Length > 0)
        //     {
        //         GUILayout.BeginHorizontal();
        //         GUILayout.Label("Obstacle:", GUILayout.Width(80));
        //         string[] obsNames = new string[obstacles.Length];
        //         for (int i = 0; i < obstacles.Length; i++) obsNames[i] = obstacles[i] != null ? obstacles[i].name : "None";
        //         selectedObstacleIndex = GUILayout.SelectionGrid(selectedObstacleIndex, obsNames, 2);
        //         GUILayout.EndHorizontal();
        //     }

        //     if (commonFates != null && commonFates.Length > 0)
        //     {
        //         GUILayout.BeginHorizontal();
        //         GUILayout.Label("Cmmn Fate:", GUILayout.Width(80));
        //         string[] fateNames = new string[commonFates.Length];
        //         for (int i = 0; i < commonFates.Length; i++) fateNames[i] = commonFates[i] != null ? commonFates[i].name : "None";
        //         selectedCommonFateIndex = GUILayout.SelectionGrid(selectedCommonFateIndex, fateNames, 2);
        //         GUILayout.EndHorizontal();
        //     }
        // }

        GUILayout.Space(10);
        GUILayout.Label("<b>Swarm Parameters</b>");

        uiNumberOfAgents = DrawSlider("Agents", uiNumberOfAgents, 4, 200, true);
        uiCohesion = DrawSlider("Cohesion", uiCohesion, 0, 10);
        uiSeparation = DrawSlider("Separation", uiSeparation, 0, 10);
        uiAlignment = DrawSlider("Alignment", uiAlignment, 0, 10);
        uiFriction = DrawSlider("Friction", uiFriction, 0, 1);
        uiRandomMvmt = DrawSlider("Random Mvmt", uiRandomMvmt, 0, 20);

        GUILayout.Label("<b>Avoidance</b>");
        uiOverlapAvoid = DrawSlider("Overlap Avoid", uiOverlapAvoid, 0, 50);
        uiSafetyDist = DrawSlider("Safety Dist", uiSafetyDist, 0.1f, 5f);
        uiEnvAvoid = DrawSlider("Env Avoid", uiEnvAvoid, 0, 50);

        GUILayout.Label("<b>Perception</b>");
        uiPerceptionRad = DrawSlider("View Radius", uiPerceptionRad, 0, 10);
        uiObstacleRad = DrawSlider("Obs View Rad", uiObstacleRad, 0, 10);
        uiMaxSpeed = DrawSlider("Max Speed", uiMaxSpeed, 1, 20);

        GUILayout.Space(10); GUILayout.Label("<b>Visualization</b>");
        uiShowPerceptionRadius = GUILayout.Toggle(uiShowPerceptionRadius, " Show Perception Radius");

        GUILayout.Space(10);
        if (GUILayout.Button("Apply Settings to Active Swarm"))
        {
            UpdateSwarmManager();
        }

        if (GUILayout.Button("Reset Scene"))
        {
            ResetScene();
        }

        if (GUILayout.Button(isRunning ? "Pause Motion" : "Play Motion"))
        {
            ToggleMotion();
        }

        GUILayout.Space(10);
        if (GUILayout.Button("Record"))
        {
            SimRecorder recorder = GetComponent<SimRecorder>();
            if (recorder == null) recorder = gameObject.AddComponent<SimRecorder>();

            if (recorder != null)
            {
                recorder.uiController = this;
                if (swarmManager != null) recorder.swarmManager = swarmManager;
                recorder.StartBatchRecording();
            }
        }
    }

    float DrawSlider(string label, float val, float min, float max, bool isInt = false)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label($"{label}: {(isInt ? (int)val : val.ToString("F1"))}", GUILayout.Width(100));
        float result = GUILayout.HorizontalSlider(val, min, max);
        GUILayout.EndHorizontal();
        return isInt ? Mathf.Round(result) : result;
    }

    int DrawSlider(string label, int val, int min, int max, bool isInt = true)
    {
        return (int)DrawSlider(label, (float)val, (float)min, (float)max, true);
    }

    public void SetMotion(bool play)
    {
        isRunning = play;
        if (swarmManager != null)
        {
            UpdateSwarmManager();
            swarmManager.enabled = isRunning;
        }
    }

    public void SetParameter(SwarmParameterToRecord param, float value)
    {
        switch (param)
        {
            case SwarmParameterToRecord.Cohesion: uiCohesion = value; break;
            case SwarmParameterToRecord.Separation: uiSeparation = value; break;
            case SwarmParameterToRecord.Alignment: uiAlignment = value; break;
            case SwarmParameterToRecord.Friction: uiFriction = value; break;
            case SwarmParameterToRecord.RandomMovement: uiRandomMvmt = value; break;
            case SwarmParameterToRecord.OverlapAvoidance: uiOverlapAvoid = value; break;
            case SwarmParameterToRecord.SafetyDistance: uiSafetyDist = value; break;
            case SwarmParameterToRecord.EnvAvoidance: uiEnvAvoid = value; break;
            case SwarmParameterToRecord.PerceptionRadius: uiPerceptionRad = value; break;
            case SwarmParameterToRecord.ObstacleRadius: uiObstacleRad = value; break;
            case SwarmParameterToRecord.MaxSpeed: uiMaxSpeed = value; break;
        }
    }

    void ToggleMotion()
    {
        SetMotion(!isRunning);
    }

    void UpdateSwarmManager()
    {
        if (swarmManager == null) return;

        swarmManager.cohesionIntensity = uiCohesion;
        swarmManager.separationIntensity = uiSeparation;
        swarmManager.alignmentIntensity = uiAlignment;
        swarmManager.frictionIntensity = uiFriction;
        swarmManager.randomMovementIntensity = uiRandomMvmt;

        swarmManager.overlappingAvoidanceIntensity = uiOverlapAvoid;
        swarmManager.safetyDistance = uiSafetyDist;
        swarmManager.envObstacleAvoidanceIntensity = uiEnvAvoid;

        swarmManager.perceptionRadius = uiPerceptionRad;
        swarmManager.obstacleAvoidanceRadius = uiObstacleRad;
        swarmManager.maxSpeed = uiMaxSpeed;

        swarmManager.showPerceptionRadius = uiShowPerceptionRadius;

        if (selectedSwarmType == SwarmType.Dispersion || selectedSwarmType == SwarmType.Densification || selectedSwarmType == SwarmType.Flocking)
        {
            if (dispersionObstacle != null)
            {
                dispersionObstacle.gameObject.SetActive(selectedSwarmType == SwarmType.Dispersion);
                if (selectedSwarmType == SwarmType.Dispersion) swarmManager.centralObstacle = dispersionObstacle;
            }
            if (densificationObstacle != null)
            {
                densificationObstacle.gameObject.SetActive(selectedSwarmType == SwarmType.Densification);
                if (selectedSwarmType == SwarmType.Densification) swarmManager.centralObstacle = densificationObstacle;
            }
            if (flockingObstacle != null)
            {
                flockingObstacle.gameObject.SetActive(selectedSwarmType == SwarmType.Flocking);
                if (selectedSwarmType == SwarmType.Flocking) swarmManager.centralObstacle = flockingObstacle;
            }

            if (densificationCommonFate != null)
            {
                densificationCommonFate.gameObject.SetActive(selectedSwarmType == SwarmType.Densification);
                if (selectedSwarmType == SwarmType.Densification)
                {
                    swarmManager.commonFateTarget = densificationCommonFate;
                    swarmManager.commonFateCollider = densificationCommonFate.GetComponent<Collider2D>();
                }
            }
            if (flockingCommonFate != null)
            {
                flockingCommonFate.gameObject.SetActive(selectedSwarmType == SwarmType.Flocking);
                if (selectedSwarmType == SwarmType.Flocking)
                {
                    swarmManager.commonFateTarget = flockingCommonFate;
                    swarmManager.commonFateCollider = flockingCommonFate.GetComponent<Collider2D>();
                }
            }
            if (selectedSwarmType == SwarmType.Dispersion)
            {
                swarmManager.commonFateTarget = null;
                swarmManager.commonFateCollider = null;
            }

            // Hide other obstacles
            if (obstacles != null)
            {
                for (int i = 0; i < obstacles.Length; i++)
                {
                    if (obstacles[i] != null) obstacles[i].gameObject.SetActive(false);
                }
            }

            // Hide other common fates
            if (commonFates != null)
            {
                for (int i = 0; i < commonFates.Length; i++)
                {
                    if (commonFates[i] != null) commonFates[i].gameObject.SetActive(false);
                }
            }
        }
        else
        {
            if (dispersionObstacle != null) dispersionObstacle.gameObject.SetActive(false);
            if (densificationObstacle != null) densificationObstacle.gameObject.SetActive(false);
            if (densificationCommonFate != null) densificationCommonFate.gameObject.SetActive(false);

            if (flockingObstacle != null) flockingObstacle.gameObject.SetActive(false);
            if (flockingCommonFate != null) flockingCommonFate.gameObject.SetActive(false);

            if (obstacles != null && obstacles.Length > 0 && selectedObstacleIndex < obstacles.Length)
            {
                swarmManager.centralObstacle = obstacles[selectedObstacleIndex];

                // toggle active state of obstacles so only selected is visible
                for (int i = 0; i < obstacles.Length; i++)
                {
                    if (obstacles[i] != null) obstacles[i].gameObject.SetActive(i == selectedObstacleIndex);
                }
            }

            if (commonFates != null && commonFates.Length > 0 && selectedCommonFateIndex < commonFates.Length)
            {
                swarmManager.commonFateTarget = commonFates[selectedCommonFateIndex];
                if (swarmManager.commonFateTarget != null)
                {
                    swarmManager.commonFateCollider = swarmManager.commonFateTarget.GetComponent<Collider2D>();
                }

                // toggle active state of common fates so only selected is visible
                for (int i = 0; i < commonFates.Length; i++)
                {
                    if (commonFates[i] != null) commonFates[i].gameObject.SetActive(i == selectedCommonFateIndex);
                }
            }
            else
            {
                swarmManager.commonFateTarget = null;
                swarmManager.commonFateCollider = null;
            }
        }
    }

    public void ResetScene()
    {
        isRunning = false;
        if (swarmManager != null)
            swarmManager.enabled = false;

        foreach (var agent in activeAgents)
        {
            if (agent != null) Destroy(agent);
        }
        activeAgents.Clear();

        Transform activeSpawnArea = null;

        if (selectedSwarmType == SwarmType.Dispersion || selectedSwarmType == SwarmType.Densification || selectedSwarmType == SwarmType.Flocking)
        {
            if (selectedSwarmType == SwarmType.Dispersion)
            {
                activeSpawnArea = circleSpawnArea;
                if (circleSpawnArea != null) circleSpawnArea.gameObject.SetActive(true);
                if (densificationSpawnArea != null) densificationSpawnArea.gameObject.SetActive(false);
                if (flockingSpawnArea != null) flockingSpawnArea.gameObject.SetActive(false);
            }
            else if (selectedSwarmType == SwarmType.Densification)
            {
                activeSpawnArea = densificationSpawnArea;
                if (densificationSpawnArea != null) densificationSpawnArea.gameObject.SetActive(true);
                if (circleSpawnArea != null) circleSpawnArea.gameObject.SetActive(false);
                if (flockingSpawnArea != null) flockingSpawnArea.gameObject.SetActive(false);
            }
            else
            {
                activeSpawnArea = flockingSpawnArea;
                if (flockingSpawnArea != null) flockingSpawnArea.gameObject.SetActive(true);
                if (circleSpawnArea != null) circleSpawnArea.gameObject.SetActive(false);
                if (densificationSpawnArea != null) densificationSpawnArea.gameObject.SetActive(false);
            }

            // disable other spawn areas
            if (spawnAreas != null)
            {
                foreach (var area in spawnAreas)
                {
                    if (area != null) area.gameObject.SetActive(false);
                }
            }
        }
        else
        {
            if (circleSpawnArea != null) circleSpawnArea.gameObject.SetActive(false);
            if (densificationSpawnArea != null) densificationSpawnArea.gameObject.SetActive(false);
            if (flockingSpawnArea != null) flockingSpawnArea.gameObject.SetActive(false);

            if (spawnAreas != null && spawnAreas.Length > 0)
            {
                // enable only the selected standard spawn area
                for (int i = 0; i < spawnAreas.Length; i++)
                {
                    if (spawnAreas[i] != null) spawnAreas[i].gameObject.SetActive(i == selectedSpawnAreaIndex);
                }

                if (selectedSpawnAreaIndex < spawnAreas.Length)
                {
                    activeSpawnArea = spawnAreas[selectedSpawnAreaIndex];
                }
            }
        }

        if (agentPrefab == null || activeSpawnArea == null)
        {
            Debug.LogWarning("UI: Missing agent prefab or active spawn area!");
            return;
        }

        Vector3 center = activeSpawnArea.position;

        if (selectedSwarmType == SwarmType.Dispersion || selectedSwarmType == SwarmType.Densification)
        {
            // Spawn evenly spaced in a filled circle using Fermat's spiral
            float radius = Mathf.Max(activeSpawnArea.lossyScale.x, activeSpawnArea.lossyScale.y) / 2f;
            float goldenAngle = 137.5f * Mathf.Deg2Rad;

            for (int i = 0; i < uiNumberOfAgents; i++)
            {
                // Calculate distance from center to evenly distribute points area wise
                float r = radius * Mathf.Sqrt((float)i / Mathf.Max(1, uiNumberOfAgents - 1));
                float theta = i * goldenAngle;

                float posX = center.x + r * Mathf.Cos(theta);
                float posY = center.y + r * Mathf.Sin(theta);
                Vector3 spawnPos = new Vector3(posX, posY, center.z);

                GameObject newAgent = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
                activeAgents.Add(newAgent);
            }
        }
        else
        {
            // Grid spawn
            Vector3 size = activeSpawnArea.lossyScale;
            Vector3 min = center - size / 2f;

            int sideLength = Mathf.CeilToInt(Mathf.Sqrt(uiNumberOfAgents));
            float stepX = sideLength > 1 ? size.x / (sideLength - 1) : 0;
            float stepY = sideLength > 1 ? size.y / (sideLength - 1) : 0;

            int count = 0;
            for (int x = 0; x < sideLength; x++)
            {
                for (int y = 0; y < sideLength; y++)
                {
                    if (count >= uiNumberOfAgents) break;

                    float posX = sideLength == 1 ? center.x : min.x + (x * stepX);
                    float posY = sideLength == 1 ? center.y : min.y + (y * stepY);
                    Vector3 spawnPos = new Vector3(posX, posY, center.z);

                    GameObject newAgent = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
                    activeAgents.Add(newAgent);
                    count++;
                }
            }
        }

        if (swarmManager != null)
        {
            swarmManager.agents = activeAgents.ToArray();
            UpdateSwarmManager(); // Apply parameters after reset
        }
    }

    void OnDrawGizmos()
    {
        Gizmos.color = Color.blue;

        if (obstacles != null)
        {
            foreach (Transform obs in obstacles)
            {
                if (obs != null)
                {
                    Gizmos.DrawWireSphere(obs.position, uiObstacleRad);
                }
            }
        }

        if (dispersionObstacle != null)
        {
            Gizmos.DrawWireSphere(dispersionObstacle.position, uiObstacleRad);
        }

        if (densificationObstacle != null)
        {
            Gizmos.DrawWireSphere(densificationObstacle.position, uiObstacleRad);
        }

        if (flockingObstacle != null)
        {
            Gizmos.DrawWireSphere(flockingObstacle.position, uiObstacleRad);
        }
    }
}
