using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A panel of its own for the spatial clustering, on the right of the screen, hidden until T.
///
/// Deliberately separate from <see cref="UI"/>: that panel is already long, and this is a different
/// question. The perception-graph count that UI shows answers "what can the swarm communicate
/// across"; this answers "how many clumps are on screen", which is what a viewer sees and which does
/// not depend on the perception radius at all.
///
/// Attaches anywhere. It finds the swarm manager itself and creates its own visualiser, so nothing
/// has to be wired up in the Inspector.
/// </summary>
public class ClusterPanel : MonoBehaviour
{
    public enum Method
    {
        /// <summary>Fixed neighbourhood radius, set below in world units.</summary>
        DbscanFixedEps,

        /// <summary>Neighbourhood radius derived from the agent spacing each frame.</summary>
        DbscanAutoEps,

        /// <summary>Connected components of the perception graph, for comparison.</summary>
        PerceptionGraph,
    }

    [Header("Panel")]
    [Tooltip("Key that shows and hides this panel.")]
    public Key toggleKey = Key.T;

    public bool visible = false;

    [Tooltip("Width of the panel. It is anchored to the right edge.")]
    public float panelWidth = 330f;

    [Header("Clustering")]
    public Method method = Method.DbscanAutoEps;

    [Tooltip("DbscanFixedEps: neighbourhood radius in world units. Around four times the agent spacing is a reasonable starting point.")]
    public float eps = 2f;

    [Tooltip("Neighbours a point needs before it can hold a cluster together. Above 1 stops a single agent between two clumps welding them into one, which is exactly what the perception graph does.")]
    public int minPoints = 3;

    [Tooltip("DbscanAutoEps: the radius is this multiple of the median distance to the kth nearest neighbour.")]
    public float spacingMultiple = 4f;

    public int autoEpsK = 3;

    [Header("Display")]
    public bool showOutlines = true;

    private SwarmManager swarmManager;
    private ClusterVisualizer visualizer;

    private readonly List<Vector2> positions = new List<Vector2>();
    private readonly List<int> perceptionSizes = new List<int>();
    private readonly SwarmSpatialClustering.Result result = new SwarmSpatialClustering.Result();

    private float lastEps;
    private GUIStyle headerStyle;
    private GUIStyle labelStyle;
    private GUIStyle valueStyle;
    private Texture2D background;

    private void Awake()
    {
        swarmManager = FindObjectOfType<SwarmManager>();
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[toggleKey].wasPressedThisFrame)
        {
            visible = !visible;
            if (!visible && visualizer != null) visualizer.Clear();
        }

        // Only measured while the panel is open: clustering 40 agents is cheap but not free, and
        // there is no reason to pay for it when nothing is reading the answer.
        if (!visible) return;

        Recompute();

        if (visualizer != null)
        {
            if (showOutlines) visualizer.Draw(positions, result);
            else visualizer.Clear();
        }
    }

    private void Recompute()
    {
        if (swarmManager == null) swarmManager = FindObjectOfType<SwarmManager>();
        if (swarmManager == null) return;

        SwarmSpatialClustering.CollectPositions(swarmManager.agents, positions);
        if (positions.Count == 0) return;

        switch (method)
        {
            case Method.DbscanFixedEps:
                lastEps = eps;
                SwarmSpatialClustering.DBSCAN(positions, lastEps, minPoints, result);
                break;

            case Method.DbscanAutoEps:
                lastEps = SwarmSpatialClustering.AutoEps(positions, autoEpsK, spacingMultiple);
                SwarmSpatialClustering.DBSCAN(positions, lastEps, minPoints, result);
                break;

            case Method.PerceptionGraph:
                lastEps = swarmManager.perceptionRadius;
                PerceptionGraphLabels();
                break;
        }

        EnsureVisualizer();
    }

    /// <summary>
    /// Runs the perception-graph grouping through the same label structure, so the panel and the
    /// outlines can show it beside the spatial methods without a second drawing path.
    /// </summary>
    private void PerceptionGraphLabels()
    {
        SwarmClusterMetrics.ClusterSizes(swarmManager.agents, swarmManager.perceptionRadius,
                                         perceptionSizes, swarmManager.AgentColliders);

        // ClusterSizes reports sizes, not memberships, so the graph is walked again here to colour
        // the agents. Cheap at this swarm size and keeps one definition of the edge test.
        result.Clear();
        int n = positions.Count;
        for (int i = 0; i < n; i++) result.labels.Add(SwarmSpatialClustering.Noise);

        float radius = swarmManager.perceptionRadius;
        float agentRadius = 0f;
        Collider2D[] colliders = swarmManager.AgentColliders;
        if (colliders != null && colliders.Length > 0 && colliders[0] != null)
        {
            agentRadius = Mathf.Max(colliders[0].bounds.extents.x, colliders[0].bounds.extents.y);
        }

        float cutoff = radius + agentRadius;
        int next = 0;

        for (int i = 0; i < n; i++)
        {
            if (result.labels[i] != SwarmSpatialClustering.Noise) continue;

            int label = next++;
            Stack<int> stack = new Stack<int>();
            stack.Push(i);
            result.labels[i] = label;

            while (stack.Count > 0)
            {
                int a = stack.Pop();
                for (int b = 0; b < n; b++)
                {
                    if (result.labels[b] != SwarmSpatialClustering.Noise) continue;
                    if (Vector2.Distance(positions[a], positions[b]) >= cutoff) continue;

                    result.labels[b] = label;
                    stack.Push(b);
                }
            }
        }

        result.Summarise(next);
    }

    private void EnsureVisualizer()
    {
        if (visualizer != null) return;

        GameObject host = new GameObject("ClusterOutlines");
        host.transform.SetParent(transform, false);
        visualizer = host.AddComponent<ClusterVisualizer>();
    }

    // ------------------------------------------------------------------ panel

    private void OnGUI()
    {
        if (!visible)
        {
            // A one-line hint, so the panel is discoverable without cluttering anything.
            GUI.Label(new Rect(Screen.width - 150, 6, 150, 20), $"[{toggleKey}] clusters");
            return;
        }

        EnsureStyles();

        float x = Screen.width - panelWidth - 10f;
        Rect area = new Rect(x, 10f, panelWidth, 330f);

        GUI.DrawTexture(area, background);
        GUILayout.BeginArea(new Rect(area.x + 12f, area.y + 10f, area.width - 24f, area.height - 20f));

        GUILayout.Label("Spatial clusters", headerStyle);
        GUILayout.Label("what the eye sees, not what agents perceive", labelStyle);
        GUILayout.Space(6);

        method = (Method)GUILayout.SelectionGrid((int)method,
            new[] { "DBSCAN fixed", "DBSCAN auto", "Perception" }, 3);

        GUILayout.Space(6);

        if (method == Method.DbscanFixedEps)
        {
            Row("Radius (eps)", $"{eps:F2}");
            eps = GUILayout.HorizontalSlider(eps, 0.2f, 8f);
        }
        else if (method == Method.DbscanAutoEps)
        {
            Row("Spacing multiple", $"{spacingMultiple:F1}x");
            spacingMultiple = GUILayout.HorizontalSlider(spacingMultiple, 1.5f, 10f);
            Row("Radius chosen", $"{lastEps:F2}");
        }
        else
        {
            Row("Perception radius", $"{lastEps:F2}");
        }

        if (method != Method.PerceptionGraph)
        {
            Row("Min neighbours", $"{minPoints}");
            minPoints = Mathf.RoundToInt(GUILayout.HorizontalSlider(minPoints, 1f, 8f));
        }

        GUILayout.Space(8);
        Row("Clusters", $"{result.ClusterCount}", true);
        Row("Sizes", result.sizes.Count > 0 ? string.Join(", ", result.sizes) : "—");
        Row("Unassigned", $"{result.NoiseCount}");
        Row("Agents", $"{positions.Count}");

        GUILayout.Space(8);
        showOutlines = GUILayout.Toggle(showOutlines, " Show cluster outlines");

        GUILayout.Space(4);
        GUILayout.Label(method == Method.PerceptionGraph
                ? "Groups agents that can see each other. Tied to the perception radius, so sweeping "
                  + "that radius changes the measure as well as the swarm."
                : "Groups agents by distance alone. Independent of perception radius, so it can be "
                  + "compared across radii.",
            labelStyle);

        GUILayout.EndArea();
    }

    private void Row(string label, string value, bool strong = false)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, labelStyle, GUILayout.Width(panelWidth * 0.45f));
        GUILayout.Label(value, strong ? headerStyle : valueStyle);
        GUILayout.EndHorizontal();
    }

    private void EnsureStyles()
    {
        if (background == null)
        {
            background = new Texture2D(1, 1);
            background.SetPixel(0, 0, new Color(0.09f, 0.10f, 0.12f, 0.94f));
            background.Apply();
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
