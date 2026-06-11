using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class TorusUI : MonoBehaviour
{
    [Header("Swarm Setup")]
    public GameObject agentPrefab;
    public TorusSwarmManager swarmManager;

    [Tooltip("The spawn area for the agents")]
    public Transform spawnArea;

    public bool showUI = true;
    private bool isRunning = false;

    private int uiNumberOfAgents = 100;
    private List<GameObject> activeAgents = new List<GameObject>();

    // UI Configuration values
    private TorusSwarmManager.InitMode selectedInitMode = TorusSwarmManager.InitMode.Torus;
    private AgentSpawnType selectedSpawnType = AgentSpawnType.Random;

    private float uiAgentSpeed = 5.0f;
    private float uiAngularGain = 0.5f;
    private float uiRepulsionRadius = 1.0f;
    private float uiOrientationRadius = 8.0f;

    private bool uiShowRepulsion = false;
    private bool uiShowOrientation = false;
    private Vector2 scrollPosition;

    void Start()
    {
        if (swarmManager != null)
            swarmManager.enabled = false;

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
            ToggleMotion();
    }

    void OnGUI()
    {
        if (!showUI) return;

        GUILayout.BeginArea(new Rect(20, 20, 550, Screen.height - 40), GUI.skin.box);
        scrollPosition = GUILayout.BeginScrollView(scrollPosition);

        GUILayout.Label("Torus Swarm Control UI (Press 'X' to hide)", GUI.skin.label);
        GUILayout.Space(10);

        DrawControls();

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    void DrawControls()
    {
        GUILayout.Label("<b>General Settings</b>");

        GUILayout.BeginHorizontal();
        GUILayout.Label("Init Mode:", GUILayout.Width(80));
        TorusSwarmManager.InitMode newMode = (TorusSwarmManager.InitMode)GUILayout.Toolbar((int)selectedInitMode, System.Enum.GetNames(typeof(TorusSwarmManager.InitMode)));
        if (newMode != selectedInitMode)
        {
            selectedInitMode = newMode;
            ResetScene();
        }
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Spawn:", GUILayout.Width(80));
        AgentSpawnType newSpawnType = (AgentSpawnType)GUILayout.Toolbar((int)selectedSpawnType, System.Enum.GetNames(typeof(AgentSpawnType)));
        if (newSpawnType != selectedSpawnType)
        {
            selectedSpawnType = newSpawnType;
            ResetScene();
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(10);
        GUILayout.Label("<b>Torus Parameters</b>");

        uiNumberOfAgents = DrawSlider("Agents", uiNumberOfAgents, 4, 300, true);
        uiAgentSpeed = DrawSlider("Agent Speed", uiAgentSpeed, 0.1f, 20f);
        uiAngularGain = DrawSlider("Angular Gain", uiAngularGain, 0.0f, 5.0f);
        uiRepulsionRadius = DrawSlider("Repulsion Rad", uiRepulsionRadius, 0.1f, 15.0f);
        uiOrientationRadius = DrawSlider("Orientation Rad", uiOrientationRadius, 0.1f, 30.0f);

        // Ensure R_repulsion <= R_orientation as described in paper
        if (uiRepulsionRadius > uiOrientationRadius)
        {
            uiOrientationRadius = uiRepulsionRadius + 0.1f;
        }

        GUILayout.Space(10); GUILayout.Label("<b>Visualization</b>");
        uiShowRepulsion = GUILayout.Toggle(uiShowRepulsion, " Show Repulsion Radius");
        uiShowOrientation = GUILayout.Toggle(uiShowOrientation, " Show Orientation Radius");

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
        GUILayout.Label($"{label}: {(isInt ? (int)val : val.ToString("F1"))}", GUILayout.Width(110));
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

    void ToggleMotion()
    {
        SetMotion(!isRunning);
    }

    void UpdateSwarmManager()
    {
        if (swarmManager == null) return;

        swarmManager.initMode = selectedInitMode;
        swarmManager.agentSpeed = uiAgentSpeed;
        swarmManager.angularGain = uiAngularGain;
        swarmManager.repulsionRadius = uiRepulsionRadius;
        swarmManager.orientationRadius = uiOrientationRadius;

        swarmManager.showRepulsionRadius = uiShowRepulsion;
        swarmManager.showOrientationRadius = uiShowOrientation;
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

        if (agentPrefab == null || spawnArea == null)
        {
            Debug.LogWarning("TorusUI: Missing agent prefab or spawn area!");
            return;
        }

        Vector3 center = spawnArea.position;
        Vector3 size = spawnArea.lossyScale;
        Vector3 min = center - size / 2f;
        Vector3 max = center + size / 2f;

        if (selectedSpawnType == AgentSpawnType.Spiral)
        {
            float radius = Mathf.Max(size.x, size.y) / 2f;
            float goldenAngle = 137.5f * Mathf.Deg2Rad;

            for (int i = 0; i < uiNumberOfAgents; i++)
            {
                float r = radius * Mathf.Sqrt((float)i / Mathf.Max(1, uiNumberOfAgents - 1));
                float theta = i * goldenAngle;

                float posX = center.x + r * Mathf.Cos(theta);
                float posY = center.y + r * Mathf.Sin(theta);
                Vector3 spawnPos = new Vector3(posX, posY, center.z);

                GameObject newAgent = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
                activeAgents.Add(newAgent);
            }
        }
        else if (selectedSpawnType == AgentSpawnType.Random)
        {
            for (int i = 0; i < uiNumberOfAgents; i++)
            {
                float posX = Random.Range(min.x, max.x);
                float posY = Random.Range(min.y, max.y);
                Vector3 spawnPos = new Vector3(posX, posY, center.z);

                GameObject newAgent = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
                activeAgents.Add(newAgent);
            }
        }
        else if (selectedSpawnType == AgentSpawnType.Chain)
        {
            float safeDist = 0.5f;
            for (int i = 0; i < uiNumberOfAgents; i++)
            {
                Vector3 spawnPos;
                if (i == 0)
                {
                    spawnPos = new Vector3(Random.Range(min.x, max.x), Random.Range(min.y, max.y), center.z);
                }
                else
                {
                    GameObject targetAgent = activeAgents[i - 1];
                    float angle = Random.Range(0f, Mathf.PI * 2f);
                    spawnPos = targetAgent.transform.position + new Vector3(Mathf.Cos(angle) * safeDist, Mathf.Sin(angle) * safeDist, 0);
                }

                GameObject newAgent = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
                activeAgents.Add(newAgent);
            }
        }
        else // Grid
        {
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
            UpdateSwarmManager();
            InitializeAgentHeadings();
        }
    }

    void InitializeAgentHeadings()
    {
        if (swarmManager == null) return;

        foreach (GameObject agentObj in activeAgents)
        {
            if (agentObj == null) continue;
            TorusSwarmAgent agent = agentObj.GetComponent<TorusSwarmAgent>();
            if (agent == null) continue;

            if (selectedInitMode == TorusSwarmManager.InitMode.Torus)
            {
                Vector2 pos = agentObj.transform.position;
                agent.heading = Mathf.Atan2(pos.y, pos.x) + Mathf.PI / 2f;
            }
            else // Flock
            {
                agent.heading = 0f;
            }

            // Set initial visual rotation
            agentObj.transform.rotation = Quaternion.AngleAxis(agent.heading * Mathf.Rad2Deg, Vector3.forward);
        }
    }
}
