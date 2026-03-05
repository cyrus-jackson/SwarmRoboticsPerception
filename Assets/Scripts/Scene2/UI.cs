using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public enum SwarmType
{
    Flocking,
    Densification,
    Random
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

    private bool showUI = true;
    private bool isRunning = false;
    private int uiNumberOfAgents = 40;
    private List<GameObject> activeAgents = new List<GameObject>();

    // UI Configuration values
    private SwarmType selectedSwarmType = SwarmType.Densification;
    private int selectedSpawnAreaIndex = 0;
    private int selectedObstacleIndex = 0;

    private float uiCohesion = 5.0f;
    private float uiSeparation = 1.0f;
    private float uiAlignment = 2.0f;
    private float uiFriction = 0.1f;
    private float uiRandomMvmt = 0.0f;
    
    private float uiOverlapAvoid = 20.0f;
    private float uiSafetyDist = 1f;
    private float uiEnvAvoid = 40.0f;
    
    private float uiPerceptionRad = 10.0f;
    private float uiObstacleRad = 2.0f;
    private float uiMaxSpeed = 1.5f;

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
                break;
            case SwarmType.Densification:
                uiCohesion = 5.0f;
                uiSeparation = 1.0f;
                uiAlignment = 2.0f;
                uiRandomMvmt = 0.0f;
                break;
            case SwarmType.Random:
                uiCohesion = 0.0f;
                uiSeparation = 0.0f;
                uiAlignment = 0.0f;
                uiRandomMvmt = 10.0f;
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

        if (spawnAreas != null && spawnAreas.Length > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Spawn Area:", GUILayout.Width(80));
            string[] areaNames = new string[spawnAreas.Length];
            for (int i=0; i<spawnAreas.Length; i++) areaNames[i] = spawnAreas[i] != null ? spawnAreas[i].name : "None";
            selectedSpawnAreaIndex = GUILayout.SelectionGrid(selectedSpawnAreaIndex, areaNames, 2);
            GUILayout.EndHorizontal();
        }

        if (obstacles != null && obstacles.Length > 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Obstacle:", GUILayout.Width(80));
            string[] obsNames = new string[obstacles.Length];
            for (int i=0; i<obstacles.Length; i++) obsNames[i] = obstacles[i] != null ? obstacles[i].name : "None";
            selectedObstacleIndex = GUILayout.SelectionGrid(selectedObstacleIndex, obsNames, 2);
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(10);
        GUILayout.Label("<b>Swarm Parameters</b>");
        
        uiNumberOfAgents = DrawSlider("Agents", uiNumberOfAgents, 4, 200, true);
        uiCohesion = DrawSlider("Cohesion", uiCohesion, 0, 10);
        uiSeparation = DrawSlider("Separation", uiSeparation, 0, 10);
        uiAlignment = DrawSlider("Alignment", uiAlignment, 0, 10);
        uiFriction = DrawSlider("Friction", uiFriction, 0, 1);
        uiRandomMvmt = DrawSlider("Random Mvmt", uiRandomMvmt, 0, 10);
        
        GUILayout.Label("<b>Avoidance</b>");
        uiOverlapAvoid = DrawSlider("Overlap Avoid", uiOverlapAvoid, 0, 50);
        uiSafetyDist = DrawSlider("Safety Dist", uiSafetyDist, 0.1f, 5f);
        uiEnvAvoid = DrawSlider("Env Avoid", uiEnvAvoid, 0, 50);

        GUILayout.Label("<b>Perception</b>");
        uiPerceptionRad = DrawSlider("View Radius", uiPerceptionRad, 1, 10);
        uiObstacleRad = DrawSlider("Obs View Rad", uiObstacleRad, 1, 10);
        uiMaxSpeed = DrawSlider("Max Speed", uiMaxSpeed, 1, 20);

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

    void ToggleMotion()
    {
        isRunning = !isRunning;
        if (swarmManager != null)
        {
            UpdateSwarmManager();
            swarmManager.enabled = isRunning;
        }
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

        if (obstacles != null && obstacles.Length > 0 && selectedObstacleIndex < obstacles.Length)
        {
            swarmManager.centralObstacle = obstacles[selectedObstacleIndex];
            
            // Optionally toggle active state of obstacles so only selected is visible
            for(int i = 0; i < obstacles.Length; i++)
            {
                 if (obstacles[i] != null) obstacles[i].gameObject.SetActive(i == selectedObstacleIndex);
            }
        }
    }

    void ResetScene()
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
        if (spawnAreas != null && spawnAreas.Length > 0 && selectedSpawnAreaIndex < spawnAreas.Length)
        {
            activeSpawnArea = spawnAreas[selectedSpawnAreaIndex];
        }

        if (agentPrefab == null || activeSpawnArea == null) 
        {
            Debug.LogWarning("UI: Missing agent prefab or spawn area!");
            return;
        }

        Vector3 center = activeSpawnArea.position;
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

        if (swarmManager != null)
        {
            swarmManager.agents = activeAgents.ToArray();
            UpdateSwarmManager(); // Apply parameters after reset
        }
    }
}
