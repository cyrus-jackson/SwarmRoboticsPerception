using UnityEngine;
using UnityEngine.InputSystem;

public class MotionUI : MonoBehaviour
{
    [Header("Assets")]
    public GameObject agentPrefab;

    private Shape shapeScript;

    private bool showUI = true;
    private bool isRunning = false;

    // UI State for Shape
    private int uiNumberOfAgents;
    private float uiRadius;
    private float uiMoveSpeed;

    void Start()
    {
        // Create the Shape instance dynamically
        GameObject shapeGO = new GameObject("ShapeController");
        shapeGO.transform.SetParent(transform); // Keep scene clean
        
        shapeScript = shapeGO.AddComponent<Shape>();

        // Initialize UI values from the script if assigned
        if (shapeScript != null)
        {
            shapeScript.agentPrefab = agentPrefab;
            shapeScript.manualUpdate = true; // Take control
            
            uiNumberOfAgents = shapeScript.numberOfAgents;
            uiRadius = shapeScript.radius;
            uiMoveSpeed = shapeScript.moveSpeed;
        }
    }

    void Update()
    {
        // Toggle UI visibility with 'x'
        if (Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
        {
            showUI = !showUI;
        }
    }

    void FixedUpdate()
    {
        // Run the logic if playing
        if (isRunning)
        {
            if (shapeScript != null)
            {
                shapeScript.ManualUpdate();
            }
            // Add other motion updates here
        }
    }

    void OnGUI()
    {
        if (!showUI) return;

        // Create a background box
        GUILayout.BeginArea(new Rect(20, 20, 300, 400), GUI.skin.box);
        GUILayout.Label("Motion Control UI (Press 'x' to hide)", GUI.skin.label);
        GUILayout.Space(10);

        if (shapeScript != null)
        {
            DrawShapeControls();
        }
        else
        {
            GUILayout.Label("No Shape script assigned.");
        }

        // You can add more Draw calls here for other motions
        
        GUILayout.EndArea();
    }

    void DrawShapeControls()
    {
        GUILayout.Label("<b>Shape Settings</b>"); // Rich text might not work in default skin without style, but worth a try or just plain text
        
        // Number of Agents
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Agents: {uiNumberOfAgents}", GUILayout.Width(80));
        uiNumberOfAgents = (int)GUILayout.HorizontalSlider(uiNumberOfAgents, 3, 20);
        GUILayout.EndHorizontal();

        // Radius
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Radius: {uiRadius:F1}", GUILayout.Width(80));
        uiRadius = GUILayout.HorizontalSlider(uiRadius, 1f, 20f);
        GUILayout.EndHorizontal();

        // Move Speed
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Speed: {uiMoveSpeed:F1}", GUILayout.Width(80));
        uiMoveSpeed = GUILayout.HorizontalSlider(uiMoveSpeed, 0f, 20f);
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        // Control Buttons
        if (GUILayout.Button("Initialize / Reset"))
        {
            ApplySettingsToShape();
            shapeScript.InitializeShape();
        }

        if (GUILayout.Button(isRunning ? "Stop Motion" : "Start Motion"))
        {
            isRunning = !isRunning;
        }
    }

    void ApplySettingsToShape()
    {
        if (shapeScript == null) return;
        shapeScript.numberOfAgents = uiNumberOfAgents;
        shapeScript.radius = uiRadius;
        shapeScript.moveSpeed = uiMoveSpeed;
    }
}
