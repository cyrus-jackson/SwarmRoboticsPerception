using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Robot = SwarmLightSync.Robot;

/// <summary>Owns the shared robot clocks and the standalone L panel.</summary>
public sealed class SwarmLightSyncController : MonoBehaviour
{
    private enum LightModel
    {
        Firefly,
        Swarmalator
    }

    private static readonly string[] LightModels = { "Swarmalator", "Firefly" };

    [Header("Panel")]
    [Tooltip("Key that shows and hides this panel.")]
    public Key toggleKey = Key.L;

    public bool visible = true;

    [Tooltip("Width of the panel. It sits top right, moving left of the cluster panel while that is open.")]
    public float panelWidth = 330f;

    [Tooltip("Hide this panel while SimRecorder is capturing with its overlay switched off, so it is never burnt into a recording. The lights keep syncing either way.")]
    public bool hideWhileRecording = true;

    [Header("Clock")]
    [Tooltip("Seconds per blink cycle. In Swarmalator mode this controls the display offset.")]
    [Range(0.2f, 5f)] public float period = 1f;

    [Tooltip("Fraction of each cycle the light is on, starting at the flash. Replaces each AgentBlinker's own duty cycle.")]
    [Range(0.05f, 0.95f)] public float dutyCycle = 0.5f;

    [Header("Light model")]
    [Tooltip("Run the selected light model and show its lights. Off hides the controlled lights and halos.")]
    [SerializeField] private bool lightSyncEnabled = true;
    [SerializeField] private LightModel lightModel = LightModel.Swarmalator;
    [SerializeField] private FireflyLightSync firefly = new FireflyLightSync();
    [SerializeField] private SwarmalatorLightSync swarmalator = new SwarmalatorLightSync();

    [Header("Start")]
    [Tooltip("On: each newly spawned swarm starts with scattered clocks, so the sync has to emerge. Off: all start in unison.")]
    public bool scatterAtSpawn = true;

    [Tooltip("Seed for starting clocks, per-robot frequencies, and phase noise. A fresh swarm restarts the random sequences.")]
    public int seed = 1;

    private static SwarmLightSyncController instance;

    private readonly List<Robot> robots = new List<Robot>();
    private SwarmLightSync CurrentModel => lightModel == LightModel.Firefly ? firefly : swarmalator;
    private GameObject[] trackedAgents;
    private System.Random random;

    private SwarmManager swarmManager;
    private SimRecorder recorder;
    private ClusterPanel clusterPanel;
    private bool clusterPanelSearched;

    private int shownRobots;
    private float shownNeighbours;
    private float shownSync;

    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private GUIStyle panelStyle;
    private GUIStyle toggleStyle;
    private Texture2D background;

    /// <summary>Called by every AgentBlinker as it wakes. Creates one sync in a scene that has a swarm.</summary>
    public static void EnsureExists()
    {
        if (instance != null) return;
        instance = FindAnyObjectByType<SwarmLightSyncController>();
        if (instance != null || FindAnyObjectByType<SwarmManager>() == null) return;
        instance = new GameObject(nameof(SwarmLightSyncController)).AddComponent<SwarmLightSyncController>();
    }

    private void Awake()
    {
        if (instance == null) instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void OnDisable()
    {
        // Hand the lights back to their own clocks rather than freezing them where they are.
        foreach (Robot robot in robots)
        {
            if (robot.blinker != null) robot.blinker.ReleaseClock();
        }
    }

    // ------------------------------------------------------------------ sync

    private void FixedUpdate()
    {
        if (!RefreshRobots()) return;

        if (lightSyncEnabled) CurrentModel.Step(robots, swarmManager, period, Time.fixedDeltaTime);
        UpdateLights();
    }

    private void UpdateLights(bool refreshImmediately = false)
    {
        SwarmLightSync model = CurrentModel;
        foreach (Robot robot in robots)
        {
            if (!robot.active) continue;

            if (lightSyncEnabled) model.DriveLight(robot, dutyCycle);
            else robot.blinker.DriveClock(0f, 0f);

            if (refreshImmediately) robot.blinker.RefreshLight();
        }
    }

    private void SetLightSyncEnabled(bool enable)
    {
        if (lightSyncEnabled == enable) return;

        lightSyncEnabled = enable;
        if (!RefreshRobots()) return;

        UpdateLights(refreshImmediately: true);
        if (lightSyncEnabled) Measure();
    }

    private bool RefreshRobots()
    {
        if (swarmManager == null) swarmManager = FindAnyObjectByType<SwarmManager>();
        if (swarmManager == null) return false;

        TrackAgents(swarmManager.agents);
        foreach (Robot robot in robots)
        {
            robot.active = robot.blinker != null && robot.agent.activeInHierarchy;
            if (robot.active) robot.position = robot.agent.transform.position;
        }
        return true;
    }

    /// <summary>
    /// Follows SwarmManager's agents array, which a respawn replaces. Robots that stay keep their
    /// clocks; a swarm with none of the previous robots in it starts from the seed again.
    /// </summary>
    private void TrackAgents(GameObject[] agents)
    {
        if (agents == trackedAgents && SameAgents(agents)) return;

        Dictionary<GameObject, Robot> previous = new Dictionary<GameObject, Robot>();
        foreach (Robot robot in robots)
        {
            if (robot.agent != null) previous[robot.agent] = robot;
        }

        bool freshSwarm = true;
        if (agents != null)
        {
            foreach (GameObject agent in agents)
            {
                if (agent != null && previous.ContainsKey(agent))
                {
                    freshSwarm = false;
                    break;
                }
            }
        }
        if (freshSwarm || random == null)
        {
            random = new System.Random(seed);
            swarmalator.ResetForSwarm(seed);
        }

        List<Robot> next = new List<Robot>(agents != null ? agents.Length : 0);
        if (agents != null)
        {
            foreach (GameObject agent in agents)
            {
                if (agent != null && previous.TryGetValue(agent, out Robot kept))
                {
                    previous.Remove(agent);
                    next.Add(kept);
                }
                else
                {
                    next.Add(NewRobot(agent));
                }
            }
        }

        foreach (Robot gone in previous.Values)
        {
            if (gone.blinker != null) gone.blinker.ReleaseClock();
        }

        robots.Clear();
        robots.AddRange(next);
        trackedAgents = agents;
    }

    private bool SameAgents(GameObject[] agents)
    {
        int count = agents != null ? agents.Length : 0;
        if (count != robots.Count) return false;

        for (int i = 0; i < count; i++)
        {
            if (!ReferenceEquals(agents[i], robots[i].agent)) return false;
        }
        return true;
    }

    private Robot NewRobot(GameObject agent)
    {
        // Draw for every slot so switching models or changing a setting preserves each agent's samples.
        float clock = (float)random.NextDouble();
        float randomSample = (float)(random.NextDouble() * 2.0 - 1.0);

        return new Robot
        {
            agent = agent,
            blinker = agent != null ? agent.GetComponent<AgentBlinker>() : null,
            clock = scatterAtSpawn ? clock : 0f,
            randomSample = randomSample,
            naturalFrequencySample = swarmalator.SampleFrequencyFactor(),
        };
    }

    private void Scatter()
    {
        if (random == null) random = new System.Random(seed);
        foreach (Robot robot in robots) robot.clock = (float)random.NextDouble();
    }

    private void Unison()
    {
        if (!RefreshRobots()) return;

        swarmalator.ResetDisplayClock();
        foreach (Robot robot in robots) robot.clock = 0f;

        UpdateLights(refreshImmediately: true);
        Measure();
    }

    // ------------------------------------------------------------------ panel

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame) visible = !visible;

        // Readouts are only needed while the panel and the light model are active.
        if (visible && lightSyncEnabled && swarmManager != null) Measure();
    }

    private void Measure()
    {
        float x = 0f;
        float y = 0f;
        int count = 0;

        foreach (Robot robot in robots)
        {
            if (!robot.active) continue;

            count++;
            float angle = robot.clock * 2f * Mathf.PI;
            x += Mathf.Cos(angle);
            y += Mathf.Sin(angle);
        }

        shownRobots = count;
        shownNeighbours = CurrentModel.MeanNeighbours(robots, swarmManager, count);

        // Kuramoto order parameter of the clocks: 0 when scattered, 1 when every robot is at the same point of its cycle.
        shownSync = count > 0 ? Mathf.Sqrt(x * x + y * y) / count : 0f;
    }

    /// <summary>Same rule as ClusterPanel: nothing of the panel is drawn while SimRecorder hides its overlay.</summary>
    private bool GuiSuppressed()
    {
        if (!hideWhileRecording) return false;

        if (recorder == null) recorder = FindAnyObjectByType<SimRecorder>();
        return recorder != null && recorder.OperatorOverlayHidden;
    }

    /// <summary>Top right, or left of the cluster panel while that is open, so the two never overlap.</summary>
    private float PanelX()
    {
        if (!clusterPanelSearched)
        {
            clusterPanel = FindAnyObjectByType<ClusterPanel>();
            clusterPanelSearched = true;
        }

        float right = Screen.width - 10f;
        if (clusterPanel != null && clusterPanel.isActiveAndEnabled && clusterPanel.visible)
        {
            right -= clusterPanel.panelWidth + 10f;
        }
        return right - panelWidth;
    }

    private void OnGUI()
    {
        if (!visible || GuiSuppressed()) return;

        EnsureStyles();

        // The background belongs to the vertical group, so the panel is exactly as tall as its contents.
        GUILayout.BeginArea(new Rect(PanelX(), 10f, panelWidth, Screen.height - 20f));
        GUILayout.BeginVertical(panelStyle);

        GUILayout.Label("Light sync (L)", headerStyle);
        SetLightSyncEnabled(GUILayout.Toggle(lightSyncEnabled, "Enable synchronized lights", toggleStyle));

        bool previousGuiEnabled = GUI.enabled;
        GUI.enabled = previousGuiEnabled && lightSyncEnabled;
        int selectedTab = lightModel == LightModel.Swarmalator ? 0 : 1;
        lightModel = GUILayout.Toolbar(selectedTab, LightModels) == 0 ? LightModel.Swarmalator : LightModel.Firefly;
        GUILayout.Space(6);

        SwarmLightSync model = CurrentModel;
        model.DrawControls(this, swarmManager);

        GUILayout.Space(6);
        scatterAtSpawn = GUILayout.Toggle(scatterAtSpawn, " Scatter clocks at spawn");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Scatter now")) Scatter();
        if (GUILayout.Button("Unison")) Unison();
        GUILayout.EndHorizontal();
        GUI.enabled = previousGuiEnabled;

        GUILayout.Space(8);
        if (lightSyncEnabled)
        {
            Row("Robots", $"{shownRobots}");
            Row(model.NeighboursLabel, $"{shownNeighbours:F1}");
        }
        Row("Sync level", lightSyncEnabled ? $"{shownSync:F2}" : "Off", true);

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    internal void DrawBlinkControls()
    {
        GUILayout.Space(6);
        Row("Period", $"{period:F2} s");
        period = GUILayout.HorizontalSlider(period, 0.2f, 5f);
        Row("Light on", $"{dutyCycle * 100f:F0}% of the cycle");
        dutyCycle = GUILayout.HorizontalSlider(dutyCycle, 0.05f, 0.95f);
    }

    internal void Caption(string text) => GUILayout.Label(text, labelStyle);

    internal void Row(string label, string value, bool strong = false)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(panelWidth * 0.45f));
        GUILayout.Label(value, strong ? headerStyle : valueStyle);
        GUILayout.EndHorizontal();
    }

    /// <summary>Same look as ClusterPanel.</summary>
    private void EnsureStyles()
    {
        if (toggleStyle == null)
        {
            toggleStyle = new GUIStyle(GUI.skin.toggle);
            toggleStyle.normal.textColor = toggleStyle.onNormal.textColor = Color.white;
        }

        if (background == null)
        {
            background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0.09f, 0.10f, 0.12f, 0.94f));
            background.Apply();
        }

        if (panelStyle == null)
        {
            panelStyle = new GUIStyle
            {
                normal = { background = background },
                padding = new RectOffset(12, 12, 10, 10),
            };
        }

        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.42f, 0.78f, 1f) },
            };
        }

        if (labelStyle == null)
        {
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true,
                normal = { textColor = new Color(0.62f, 0.66f, 0.72f) },
            };
        }

        if (valueStyle == null)
        {
            valueStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = Color.white },
            };
        }
    }
}
