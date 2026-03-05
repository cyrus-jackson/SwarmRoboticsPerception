using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

public class UI : MonoBehaviour
{
    [Header("Swarm Setup")]
    public GameObject agentPrefab;
    public SwarmManager swarmManager;
    
    [Tooltip("The bounded area in the scene where agents will spawn, determined by its Transform scale")]
    public Transform spawnArea;

    private bool showUI = true;
    private bool isRunning = false;
    private int uiNumberOfAgents = 40;
    private List<GameObject> activeAgents = new List<GameObject>();

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
        {
            ToggleMotion();
        }
    }

    void OnGUI()
    {
        if (!showUI) return;

        GUILayout.BeginArea(new Rect(20, 20, 300, 200), GUI.skin.box);
        GUILayout.Label("Swarm Control UI (Press 'X' to hide)", GUI.skin.label);
        GUILayout.Space(10);

        DrawControls();

        GUILayout.EndArea();
    }

    void DrawControls()
    {
        GUILayout.Label("<b>Settings</b>");

        // Number of Agents
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Agents: {uiNumberOfAgents}", GUILayout.Width(80));
        uiNumberOfAgents = (int)GUILayout.HorizontalSlider(uiNumberOfAgents, 4, 100);
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        // Control Buttons
        if (GUILayout.Button("Reset Scene"))
        {
            ResetScene();
        }

        if (GUILayout.Button(isRunning ? "Pause Motion" : "Play Motion"))
        {
            ToggleMotion();
        }
    }

    void ToggleMotion()
    {
        isRunning = !isRunning;
        if (swarmManager != null)
        {
            swarmManager.enabled = isRunning;
        }
    }

    void ResetScene()
    {
        isRunning = false;
        if (swarmManager != null)
            swarmManager.enabled = false;

        // Clear existing agents
        foreach (var agent in activeAgents)
        {
            if (agent != null) Destroy(agent);
        }
        activeAgents.Clear();

        if (agentPrefab == null || spawnArea == null) 
        {
            Debug.LogWarning("UI: Missing agent prefab or spawn area!");
            return;
        }

        // Spawn evenly spaced based on the Transform scale and position
        Vector3 center = spawnArea.position;
        Vector3 size = spawnArea.lossyScale;
        Vector3 min = center - size / 2f;
        
        // Calculate grid dimensions for a 2D spread (X and Y)
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

        // Assign to swarm manager
        if (swarmManager != null)
        {
            swarmManager.agents = activeAgents.ToArray();
        }
    }
}
