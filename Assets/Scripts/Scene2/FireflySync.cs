using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Firefly synchronisation for the agents' blinker lights, after Nicky Case's "Fireflies"
/// (ncase.me/fireflies; the rule is ported from js/index.js in github.com/ncase/fireflies, CC0).
///
/// Every robot has a clock that runs from 0 to 1, and its light is on while the clock is below the
/// duty cycle. When the clock passes 1 the robot flashes and the clock starts again. With the nudge
/// rule on, every other robot within the radius then moves its clock forward by nudge x its own
/// clock: a robot about to flash is pushed to the end and flashes on the next step, one that has just
/// flashed barely moves. That chain, one hop per step, is what makes the waves.
///
/// Purely visual. It never moves an agent and never draws from UnityEngine.Random, so trajectories
/// are unchanged. It drives only SwarmManager's agents, so replay clones keep blinking on their own.
///
/// The first AgentBlinker in a scene with a SwarmManager creates it, so no scene needs editing. Add
/// it to a scene yourself to set its defaults in the Inspector. The panel shows and hides with L.
/// </summary>
public class FireflySync : MonoBehaviour
{
    public enum RadiusMode
    {
        /// <summary>Perception radius plus the agent radius: the perception graph's edge test.</summary>
        Perception,

        /// <summary>A fixed centre-to-centre distance in world units.</summary>
        Custom,
    }

    [Header("Panel")]
    [Tooltip("Key that shows and hides this panel.")]
    public Key toggleKey = Key.L;

    public bool visible = true;

    [Tooltip("Width of the panel. It sits top right, moving left of the cluster panel while that is open.")]
    public float panelWidth = 330f;

    [Tooltip("Hide this panel while SimRecorder is capturing with its overlay switched off, so it is never burnt into a recording. The lights keep syncing either way.")]
    public bool hideWhileRecording = true;

    [Header("Clock")]
    [Tooltip("Seconds per cycle. Replaces each AgentBlinker's own period while this drives it.")]
    [Range(0.2f, 5f)] public float period = 1f;

    [Tooltip("Fraction of each cycle the light is on, starting at the flash. Replaces each AgentBlinker's own duty cycle.")]
    [Range(0.05f, 0.95f)] public float dutyCycle = 0.5f;

    [Tooltip("Each robot's period differs from the one above by up to this fraction, fixed per robot. 0: identical clocks, as on the site. More lets groups that split apart drift out of step, but loosens the sync.")]
    [Range(0f, 0.3f)] public float speedSpread = 0f;

    [Header("Nudge thy neighbour")]
    [Tooltip("Off: every robot runs on its own clock and nothing synchronises.")]
    public bool nudgeNeighbours = true;

    [Tooltip("When a neighbour flashes, a robot's clock grows by this fraction of itself. The site's default is 0.035.")]
    [Range(0.01f, 0.1f)] public float nudge = 0.035f;

    public RadiusMode radiusMode = RadiusMode.Perception;

    [Tooltip("Custom radius: centre-to-centre distance in world units.")]
    [Range(0.2f, 10f)] public float customRadius = 2f;

    [Header("Start")]
    [Tooltip("On: each newly spawned swarm starts with scattered clocks, so the sync has to emerge. Off: all start in unison.")]
    public bool scatterAtSpawn = true;

    [Tooltip("Seed for the starting clocks and the per-robot speeds. Every spawn restarts it, so each run gets the same values by agent index.")]
    public int seed = 1;

    private class Robot
    {
        public GameObject agent;
        public AgentBlinker blinker;
        public float clock;
        public float speedDraw; // -1 to 1, scaled by speedSpread
        public Vector2 position;
        public bool active;
        public bool flashed;
    }

    private static FireflySync instance;

    private readonly List<Robot> robots = new List<Robot>();
    private GameObject[] trackedAgents;
    private System.Random random;

    private SwarmManager swarmManager;
    private SimRecorder recorder;
    private ClusterPanel clusterPanel;
    private bool clusterPanelSearched;

    private int shownRobots;
    private float shownRadius;
    private float shownNeighbours;
    private float shownSync;

    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private GUIStyle panelStyle;
    private Texture2D background;

    /// <summary>Called by every AgentBlinker as it wakes. Creates one sync in a scene that has a swarm.</summary>
    public static void EnsureExists()
    {
        if (instance != null) return;
        instance = FindAnyObjectByType<FireflySync>();
        if (instance != null || FindAnyObjectByType<SwarmManager>() == null) return;
        instance = new GameObject(nameof(FireflySync)).AddComponent<FireflySync>();
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
        if (swarmManager == null) swarmManager = FindAnyObjectByType<SwarmManager>();
        if (swarmManager == null) return;

        TrackAgents(swarmManager.agents);
        Step(Time.fixedDeltaTime);
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
        if (freshSwarm || random == null) random = new System.Random(seed);

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
        // Both values are drawn for every slot, so an agent index gets the same pair whatever the settings.
        float clock = (float)random.NextDouble();
        float speedDraw = (float)(random.NextDouble() * 2.0 - 1.0);

        return new Robot
        {
            agent = agent,
            blinker = agent != null ? agent.GetComponent<AgentBlinker>() : null,
            clock = scatterAtSpawn ? clock : 0f,
            speedDraw = speedDraw,
        };
    }

    private void Step(float deltaTime)
    {
        int flashes = 0;

        // Every clock moves before any nudge, so who flashes does not depend on the order robots are visited in.
        foreach (Robot robot in robots)
        {
            robot.flashed = false;
            robot.active = robot.blinker != null && robot.agent.activeInHierarchy;
            if (!robot.active) continue;

            robot.position = robot.agent.transform.position;
            robot.clock += deltaTime / RobotPeriod(robot);

            if (robot.clock > 1f)
            {
                // The site resets to 0. Keeping the overshoot keeps a free-running period exact.
                robot.clock = Mathf.Repeat(robot.clock, 1f);
                robot.flashed = true;
                flashes++;
            }
        }

        if (nudgeNeighbours && flashes > 0) NudgeNeighbours();

        foreach (Robot robot in robots)
        {
            if (robot.active) robot.blinker.DriveClock(robot.clock, dutyCycle);
        }
    }

    private float RobotPeriod(Robot robot)
    {
        return Mathf.Max(period, 0.05f) * (1f + Mathf.Clamp(speedSpread, 0f, 0.9f) * robot.speedDraw);
    }

    /// <summary>
    /// The site's rule: each flash a robot sees moves its clock forward by nudge x the clock, capped
    /// at 1, so a robot pushed to the end flashes on the next step. Seeing k flashes in one step
    /// compounds k times, which is what applying them one after another does.
    /// </summary>
    private void NudgeNeighbours()
    {
        float radius = CurrentRadius();
        float radiusSq = radius * radius;
        float gain = 1f + nudge;

        foreach (Robot robot in robots)
        {
            // Robots that have just flashed are left alone, as on the site, where their clock is 0.
            if (!robot.active || robot.flashed) continue;

            int seen = 0;
            foreach (Robot other in robots)
            {
                if (other.flashed && (other.position - robot.position).sqrMagnitude < radiusSq) seen++;
            }

            if (seen > 0) robot.clock = Mathf.Min(1f, robot.clock * Mathf.Pow(gain, seen));
        }
    }

    private float CurrentRadius()
    {
        if (radiusMode == RadiusMode.Custom) return customRadius;
        return swarmManager.perceptionRadius + AgentRadius();
    }

    /// <summary>
    /// SwarmAgent measures perception to the other agent's collider edge, so centre to centre the
    /// reach is the perception radius plus the agent radius. Same cutoff as ClusterPanel's perception graph.
    /// </summary>
    private float AgentRadius()
    {
        Collider2D[] colliders = swarmManager.AgentColliders;
        if (colliders == null) return 0f;

        foreach (Collider2D collider in colliders)
        {
            if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
            {
                return Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.y);
            }
        }
        return 0f;
    }

    private void Scatter()
    {
        if (random == null) random = new System.Random(seed);
        foreach (Robot robot in robots) robot.clock = (float)random.NextDouble();
    }

    private void Unison()
    {
        foreach (Robot robot in robots) robot.clock = 0f;
    }

    // ------------------------------------------------------------------ panel

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame) visible = !visible;

        // Only measured while the panel is open. The sync itself runs either way.
        if (visible && swarmManager != null) Measure();
    }

    private void Measure()
    {
        shownRadius = CurrentRadius();
        float radiusSq = shownRadius * shownRadius;
        float x = 0f;
        float y = 0f;
        int count = 0;
        int pairs = 0;

        foreach (Robot robot in robots)
        {
            if (!robot.active) continue;

            count++;
            float angle = robot.clock * 2f * Mathf.PI;
            x += Mathf.Cos(angle);
            y += Mathf.Sin(angle);

            foreach (Robot other in robots)
            {
                if (other != robot && other.active && (other.position - robot.position).sqrMagnitude < radiusSq) pairs++;
            }
        }

        shownRobots = count;
        shownNeighbours = count > 0 ? (float)pairs / count : 0f;

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

        GUILayout.Label("Firefly sync", headerStyle);
        GUILayout.Label("lights that synchronise like ncase.me/fireflies", labelStyle);
        GUILayout.Space(6);

        nudgeNeighbours = GUILayout.Toggle(nudgeNeighbours, " Nudge thy neighbour");
        Row("Nudge", $"{nudge:F3}");
        nudge = GUILayout.HorizontalSlider(nudge, 0.01f, 0.1f);

        GUILayout.Space(4);
        radiusMode = (RadiusMode)GUILayout.SelectionGrid((int)radiusMode,
            new[] { "Perception radius", "Custom" }, 2);
        if (radiusMode == RadiusMode.Custom)
        {
            Row("Radius", $"{customRadius:F2}");
            customRadius = GUILayout.HorizontalSlider(customRadius, 0.2f, 10f);
        }
        else
        {
            Row("Radius", $"{shownRadius:F2}");
        }

        GUILayout.Space(6);
        Row("Period", $"{period:F2} s");
        period = GUILayout.HorizontalSlider(period, 0.2f, 5f);
        Row("Light on", $"{dutyCycle * 100f:F0}% of the cycle");
        dutyCycle = GUILayout.HorizontalSlider(dutyCycle, 0.05f, 0.95f);
        Row("Speed spread", $"±{speedSpread * 100f:F0}%");
        speedSpread = GUILayout.HorizontalSlider(speedSpread, 0f, 0.3f);

        GUILayout.Space(6);
        scatterAtSpawn = GUILayout.Toggle(scatterAtSpawn, " Scatter clocks at spawn");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Scatter now")) Scatter();
        if (GUILayout.Button("Unison")) Unison();
        GUILayout.EndHorizontal();

        GUILayout.Space(8);
        Row("Robots", $"{shownRobots}");
        Row("Mean neighbours", $"{shownNeighbours:F1}");
        Row("Sync level", $"{shownSync:F2}", true);

        GUILayout.EndVertical();
        GUILayout.EndArea();
    }

    private void Row(string label, string value, bool strong = false)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(panelWidth * 0.45f));
        GUILayout.Label(value, strong ? headerStyle : valueStyle);
        GUILayout.EndHorizontal();
    }

    /// <summary>Same look as ClusterPanel.</summary>
    private void EnsureStyles()
    {
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
