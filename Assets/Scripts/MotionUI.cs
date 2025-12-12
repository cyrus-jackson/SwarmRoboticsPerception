using UnityEngine;
using UnityEngine.InputSystem;

public enum MotionType
{
    Shape,
    Speed,
    Jitter,
    Pattern
}

public class MotionUI : MonoBehaviour
{
    [Header("Assets")]
    public GameObject agentPrefab;

    private BaseShape currentShape;
    private GameObject motionControllerGO;

    private bool showUI = true;
    private bool isRunning = false;

    private MotionType selectedMotionType = MotionType.Shape;

    private int uiNumberOfAgents;
    private float uiRadius;
    private float uiMoveSpeed;
    private SimilarityLevel uiSimilarityLevel = SimilarityLevel.High;
    private JitterType uiJitterType = JitterType.Sync;
    private bool uiSequential = false;

    void Start()
    {
        // Create the controller object dynamically
        motionControllerGO = new GameObject("MotionController");
        motionControllerGO.transform.SetParent(transform); // Keep scene clean

        SetMotionType(MotionType.Shape);

        // Initialize UI values from the script if assigned
        if (currentShape != null)
        {
            uiNumberOfAgents = currentShape.numberOfAgents;
            uiRadius = currentShape.radius;
            uiMoveSpeed = currentShape.moveSpeed;
        }
    }

    void SetMotionType(MotionType type)
    {
        if (currentShape != null)
        {
            currentShape.Clear();
            MonoBehaviour currentMb = currentShape as MonoBehaviour;
            if (currentMb != null) Destroy(currentMb);
        }

        switch (type)
        {
            case MotionType.Shape:
                currentShape = motionControllerGO.AddComponent<Shape>();
                break;
            case MotionType.Speed:
                currentShape = motionControllerGO.AddComponent<Speed>();
                break;
            case MotionType.Jitter:
                currentShape = motionControllerGO.AddComponent<Jitter>();
                break;
            case MotionType.Pattern:
                currentShape = motionControllerGO.AddComponent<Pattern>();
                break;
        }

        if (currentShape != null)
        {
            currentShape.agentPrefab = agentPrefab;
            currentShape.manualControl = true;
        }

        selectedMotionType = type;
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
            if (currentShape != null)
            {
                currentShape.ManualUpdate();
            }
        }
    }

    void OnGUI()
    {
        if (!showUI) return;

        // Create a background box
        GUILayout.BeginArea(new Rect(20, 20, 300, 450), GUI.skin.box);
        GUILayout.Label("Motion Control UI (Press 'x' to hide)", GUI.skin.label);
        GUILayout.Space(10);

        // Dropdown for Motion Type
        GUILayout.Label("Select Motion Type:");
        string[] names = System.Enum.GetNames(typeof(MotionType));
        int selected = GUILayout.Toolbar((int)selectedMotionType, names);
        if (selected != (int)selectedMotionType)
        {
            SetMotionType((MotionType)selected);
        }

        GUILayout.Space(10);

        if (currentShape != null)
        {
            DrawControls();
        }
        else
        {
            GUILayout.Label("No Motion script assigned.");
        }

        GUILayout.EndArea();
    }

    void DrawControls()
    {
        GUILayout.Label("<b>Settings</b>");

        // Number of Agents
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Agents: {uiNumberOfAgents}", GUILayout.Width(80));
        uiNumberOfAgents = (int)GUILayout.HorizontalSlider(uiNumberOfAgents, 3, 20);
        GUILayout.EndHorizontal();

        // Radius
        if (selectedMotionType == MotionType.Shape || selectedMotionType == MotionType.Pattern)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"Radius: {uiRadius:F1}", GUILayout.Width(80));
            uiRadius = GUILayout.HorizontalSlider(uiRadius, 1f, 20f);
            GUILayout.EndHorizontal();
        }

        // Move Speed
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Speed: {uiMoveSpeed:F1}", GUILayout.Width(80));
        uiMoveSpeed = GUILayout.HorizontalSlider(uiMoveSpeed, 0f, 20f);
        GUILayout.EndHorizontal();

        // Extra options for Speed
        if (selectedMotionType == MotionType.Speed)
        {
            GUILayout.Space(5);
            GUILayout.Label("Similarity Level:");
            string[] simNames = System.Enum.GetNames(typeof(SimilarityLevel));
            uiSimilarityLevel = (SimilarityLevel)GUILayout.Toolbar((int)uiSimilarityLevel, simNames);
        }

        // Extra options for Jitter
        if (selectedMotionType == MotionType.Jitter)
        {
            GUILayout.Space(5);
            GUILayout.Label("Jitter Type:");
            string[] jitterNames = System.Enum.GetNames(typeof(JitterType));
            uiJitterType = (JitterType)GUILayout.Toolbar((int)uiJitterType, jitterNames);
        }

        // Extra options for Pattern
        if (selectedMotionType == MotionType.Pattern)
        {
            GUILayout.Space(5);
            uiSequential = GUILayout.Toggle(uiSequential, "Sequential");
        }

        GUILayout.Space(10);

        // Control Buttons
        if (GUILayout.Button("Initialize / Reset"))
        {
            ApplySettings();
            currentShape.Initialize();
        }

        if (GUILayout.Button(isRunning ? "Stop Motion" : "Start Motion"))
        {
            isRunning = !isRunning;
        }
    }

    void ApplySettings()
    {
        if (currentShape == null) return;
        currentShape.numberOfAgents = uiNumberOfAgents;
        currentShape.radius = uiRadius;
        currentShape.moveSpeed = uiMoveSpeed;

        if (currentShape is Speed speedScript)
        {
            speedScript.similarityLevel = uiSimilarityLevel;
        }

        if (currentShape is Jitter jitterScript)
        {
            jitterScript.jitterType = uiJitterType;
        }

        if (currentShape is Pattern patternScript)
        {
            patternScript.sequential = uiSequential;
        }
    }
}
