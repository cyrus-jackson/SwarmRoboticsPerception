using System.Collections.Generic;
using UnityEngine;

public enum SwarmDensityMetric
{
    /// <summary>Convex hull area with the most extreme agents discarded first. Shrinks as the swarm contracts.</summary>
    TrimmedHullArea,
    /// <summary>Mean distance to the K nearest neighbours (Hénard et al. intra-group distance). Shrinks as the swarm contracts.</summary>
    MeanKnnDistance
}

/// <summary>
/// Tracks how spread out the swarm is over time and compares it against a baseline captured at
/// the start of a run. SimRecorder can end a recording once the ratio crosses a target, and the
/// same numbers are shown live in play mode.
///
/// Both metrics get smaller as the swarm contracts, so a target ratio below 1 means "contract to
/// this fraction of the starting spread" and above 1 means "expand to this multiple".
/// </summary>
public class SwarmDensityMonitor : MonoBehaviour
{
    [Header("Metric")]
    public SwarmDensityMetric metric = SwarmDensityMetric.TrimmedHullArea;

    [Tooltip("Fraction of agents furthest from the centroid to discard before hulling. 0.1 drops the most extreme 10%, which stops a single straggler dominating the area.")]
    [Range(0f, 0.5f)]
    public float hullTrimFraction = 0.1f;

    [Tooltip("K for the mean nearest neighbour distance. Hénard et al. use 3.")]
    public int knnK = 3;

    [Header("Sampling")]
    [Tooltip("Seconds between measurements. The metrics are cheap but there is no need to run them every frame.")]
    public float sampleInterval = 0.1f;

    [Header("Connectivity Readout")]
    [Tooltip("Used to read the perception radius and agent count. Assigned by UI.cs.")]
    public SwarmManager swarmManager;

    [Tooltip("Critical mean degree for connectivity in 2D continuum percolation. Below this the perception graph tends to break into components, which is the paper's definition of fragmentation. Assumes roughly uniform placement, so treat it as an anchor rather than an exact figure.")]
    public float percolationCriticalDegree = 4.51f;

    [Header("Gizmos")]
    [Tooltip("Draw the measured region in the scene view. Gizmos are not captured in recordings unless Game view gizmos are switched on.")]
    public bool showGizmo = true;

    [Tooltip("Outline of the area currently being measured.")]
    public Color hullGizmoColor = new Color(0.15f, 0.75f, 1f, 1f);

    [Tooltip("Outline of the area at the moment the baseline was captured, so the change is visible.")]
    public Color baselineGizmoColor = new Color(0.15f, 0.75f, 1f, 0.28f);

    [Tooltip("Marks agents discarded by the hull trim.")]
    public Color trimmedAgentGizmoColor = new Color(1f, 0.45f, 0.1f, 1f);

    [Tooltip("Draw a link from each agent to its K nearest neighbours when the KNN metric is selected.")]
    public bool showKnnLinks = true;

    public Color knnLinkGizmoColor = new Color(0.4f, 1f, 0.5f, 0.5f);

    [Tooltip("Z depth the gizmos are drawn at. The arena is on the XY plane.")]
    public float gizmoZ = 0f;

    [Header("Logging")]
    [Tooltip("Log the metric as it changes while the swarm is running.")]
    public bool logChanges = true;

    [Tooltip("Minimum seconds between logs.")]
    public float logMinInterval = 1f;

    [Tooltip("Only log once the ratio has moved by at least this much since the last log.")]
    public float logMinRatioChange = 0.05f;

    [Tooltip("Prefix for log messages, e.g. the motion type. Set by UI.cs.")]
    public string contextLabel = "";

    /// <summary>Metric value at the moment the baseline was captured.</summary>
    public float Baseline { get; private set; }

    /// <summary>Most recent metric value.</summary>
    public float CurrentValue { get; private set; }

    /// <summary>CurrentValue / Baseline. 1 until the swarm has moved, 0 when no baseline exists.</summary>
    public float Ratio => Baseline > 0.0001f ? CurrentValue / Baseline : 0f;

    public bool HasBaseline { get; private set; }
    public bool HasSampled { get; private set; }

    /// <summary>Trimmed hull area from the last sample, kept even when the KNN metric is selected.</summary>
    public float LastHullArea { get; private set; }

    /// <summary>Number of agents in the last sample.</summary>
    public int LastAgentCount { get; private set; }

    /// <summary>Perception radius currently in force, the length scale the readout is relative to.</summary>
    public float PerceptionRadius => swarmManager != null ? swarmManager.perceptionRadius : 0f;

    /// <summary>
    /// Expected number of neighbours within the perception radius, k = (N / A) * pi * R^2.
    /// </summary>
    public float MeanDegree
    {
        get
        {
            float r = PerceptionRadius;
            if (LastHullArea <= 0.0001f || LastAgentCount <= 0 || r <= 0f) return 0f;
            return (LastAgentCount / LastHullArea) * Mathf.PI * r * r;
        }
    }

    /// <summary>
    /// Area at which the perception graph sits on the percolation threshold,
    /// A_c = N * pi * R^2 / criticalDegree. Larger than this tends to fragment.
    /// </summary>
    public float CriticalArea
    {
        get
        {
            float r = PerceptionRadius;
            if (LastAgentCount <= 0 || r <= 0f) return 0f;
            return LastAgentCount * Mathf.PI * r * r / Mathf.Max(0.01f, percolationCriticalDegree);
        }
    }

    /// <summary>Current area as a multiple of the critical area. Below 1 is connected.</summary>
    public float AreaOverCriticalArea => CriticalArea > 0.0001f ? LastHullArea / CriticalArea : 0f;

    /// <summary>Hull of the most recent measurement, for the gizmo and the runtime outline.</summary>
    public IReadOnlyList<Vector2> CurrentHull => currentHull;

    /// <summary>Hull as it was when the baseline was captured.</summary>
    public IReadOnlyList<Vector2> BaselineHull => baselineHull;

    private readonly List<Vector2> positionBuffer = new List<Vector2>();
    private float nextSampleTime = 0f;
    private float lastLoggedRatio = -1f;
    private float lastLogTime = -999f;

    // Cached geometry from the last measurement, drawn by OnDrawGizmos.
    private readonly List<Vector2> currentHull = new List<Vector2>();
    private readonly List<Vector2> baselineHull = new List<Vector2>();
    private readonly List<Vector2> trimmedOutPoints = new List<Vector2>();
    private readonly List<Vector2> gizmoPositions = new List<Vector2>();
    private readonly List<int> knnPairs = new List<int>();

    public string MetricName => metric == SwarmDensityMetric.TrimmedHullArea ? "hull area" : $"mean {knnK}NN dist";

    /// <summary>Clears the baseline and cached values. Call when a new run is set up.</summary>
    public void ResetTracking()
    {
        Baseline = 0f;
        CurrentValue = 0f;
        HasBaseline = false;
        HasSampled = false;
        nextSampleTime = 0f;
        lastLoggedRatio = -1f;
        lastLogTime = -999f;

        currentHull.Clear();
        baselineHull.Clear();
        trimmedOutPoints.Clear();
        gizmoPositions.Clear();
        knnPairs.Clear();
    }

    /// <summary>
    /// Measures the swarm now and stores the result as the reference the ratio is taken against.
    /// For recordings this should happen after the warm-up delay, so the settling contraction is
    /// not counted toward the threshold.
    /// </summary>
    public float CaptureBaseline(GameObject[] agents)
    {
        Baseline = Measure(agents);

        // Freeze the outline as it was at baseline so the change is visible in the scene view.
        baselineHull.Clear();
        baselineHull.AddRange(currentHull);

        CurrentValue = Baseline;
        HasBaseline = Baseline > 0.0001f;
        HasSampled = true;
        nextSampleTime = Time.time + sampleInterval;
        lastLoggedRatio = -1f;

        if (logChanges)
        {
            Debug.Log($"[Density{LogSuffix()}] baseline {MetricName} = {Baseline:F3}");
        }

        return Baseline;
    }

    void Update()
    {
        // While the swarm is running, SwarmManager drives Evaluate straight after the agents move,
        // which keeps the sample in step with the simulation. When motion is paused the manager is
        // disabled and stops calling us, so sample here instead to keep the readout, the gizmo and
        // the runtime outline live in the paused state.
        if (swarmManager != null && !swarmManager.enabled)
        {
            Evaluate(swarmManager.agents);
        }

        SyncAreaVisualizer();
    }

    /// <summary>
    /// Creates the runtime outline on demand and matches its enabled state to the panel toggle.
    /// Lives here rather than in SwarmManager so it also responds while motion is paused.
    /// </summary>
    private void SyncAreaVisualizer()
    {
        bool wanted = swarmManager != null && swarmManager.showDensityArea;

        DensityAreaVisualizer visualizer = GetComponent<DensityAreaVisualizer>();
        if (visualizer == null)
        {
            if (!wanted) return;
            visualizer = gameObject.AddComponent<DensityAreaVisualizer>();
        }

        visualizer.enabled = wanted;
    }

    /// <summary>Recomputes the metric if the sample interval has elapsed. Called each frame by SwarmManager.</summary>
    public void Evaluate(GameObject[] agents)
    {
        if (Time.time < nextSampleTime) return;
        nextSampleTime = Time.time + Mathf.Max(0.01f, sampleInterval);

        CurrentValue = Measure(agents);
        HasSampled = true;

        if (logChanges && HasBaseline && ShouldLog())
        {
            Debug.Log($"[Density{LogSuffix()}] {MetricName} {CurrentValue:F3} / baseline {Baseline:F3} = ratio {Ratio:F2}");
            lastLoggedRatio = Ratio;
            lastLogTime = Time.time;
        }
    }

    private bool ShouldLog()
    {
        if (lastLoggedRatio < 0f) return true;
        if (Mathf.Abs(Ratio - lastLoggedRatio) < Mathf.Max(0.001f, logMinRatioChange)) return false;
        if (logMinInterval > 0f && Time.time - lastLogTime < logMinInterval) return false;
        return true;
    }

    private string LogSuffix()
    {
        return string.IsNullOrEmpty(contextLabel) ? "" : $":{contextLabel}";
    }

    /// <summary>Computes the selected metric for the current agent positions.</summary>
    public float Measure(GameObject[] agents)
    {
        SwarmDensityMetrics.CollectPositions(agents, positionBuffer);

        // The hull is always kept up to date so the scene gizmo and the runtime outline have
        // something to draw. For the KNN metric it is not the measured quantity, but it still
        // shows the region the swarm occupies.
        float area = SwarmDensityMetrics.TrimmedHull(positionBuffer, hullTrimFraction, currentHull, trimmedOutPoints);
        LastHullArea = area;
        LastAgentCount = positionBuffer.Count;

        gizmoPositions.Clear();
        gizmoPositions.AddRange(positionBuffer);

        if (showGizmo && showKnnLinks && metric == SwarmDensityMetric.MeanKnnDistance)
        {
            SwarmDensityMetrics.CollectKnnPairs(gizmoPositions, knnK, knnPairs);
        }
        else
        {
            knnPairs.Clear();
        }

        if (metric == SwarmDensityMetric.MeanKnnDistance)
        {
            return SwarmDensityMetrics.MeanKnnDistance(positionBuffer, knnK);
        }

        return area;
    }

    /// <summary>
    /// True once the swarm has reached the target ratio. A target below 1 is a contraction test
    /// (ratio has fallen to or past it), above 1 an expansion test.
    /// </summary>
    public bool IsRatioReached(float targetRatio)
    {
        if (!HasBaseline || !HasSampled) return false;
        if (targetRatio <= 0f) return false;

        return targetRatio < 1f ? Ratio <= targetRatio : Ratio >= targetRatio;
    }

    void OnDrawGizmos()
    {
        if (!showGizmo) return;

        // Baseline outline first, so the current one draws over it.
        DrawPolygonGizmo(baselineHull, baselineGizmoColor);
        DrawPolygonGizmo(currentHull, hullGizmoColor);

        // Agents excluded by the trim, so it is obvious which ones are not shaping the hull.
        if (trimmedOutPoints.Count > 0)
        {
            Gizmos.color = trimmedAgentGizmoColor;
            foreach (Vector2 p in trimmedOutPoints)
            {
                Vector3 c = new Vector3(p.x, p.y, gizmoZ);
                float r = 0.12f;
                Gizmos.DrawLine(c + new Vector3(-r, -r, 0f), c + new Vector3(r, r, 0f));
                Gizmos.DrawLine(c + new Vector3(-r, r, 0f), c + new Vector3(r, -r, 0f));
            }
        }

        // For the KNN metric, show the links that are actually being averaged.
        if (metric == SwarmDensityMetric.MeanKnnDistance && showKnnLinks && knnPairs.Count >= 2)
        {
            Gizmos.color = knnLinkGizmoColor;
            for (int i = 0; i + 1 < knnPairs.Count; i += 2)
            {
                int a = knnPairs[i];
                int b = knnPairs[i + 1];
                if (a >= gizmoPositions.Count || b >= gizmoPositions.Count) continue;

                Gizmos.DrawLine(
                    new Vector3(gizmoPositions[a].x, gizmoPositions[a].y, gizmoZ),
                    new Vector3(gizmoPositions[b].x, gizmoPositions[b].y, gizmoZ));
            }
        }

#if UNITY_EDITOR
        if (HasSampled && currentHull.Count > 0)
        {
            Vector2 centre = SwarmDensityMetrics.Centroid(currentHull);
            string label = HasBaseline
                ? $"{MetricName} {CurrentValue:F2}\nbaseline {Baseline:F2}  ratio {Ratio:F2}"
                : $"{MetricName} {CurrentValue:F2}";

            if (PerceptionRadius > 0f)
            {
                label += $"\ndegree {MeanDegree:F1}  area/A_c {AreaOverCriticalArea:F2}x";
            }

            UnityEditor.Handles.color = hullGizmoColor;
            UnityEditor.Handles.Label(new Vector3(centre.x, centre.y, gizmoZ), label);
        }
#endif
    }

    private void DrawPolygonGizmo(List<Vector2> polygon, Color color)
    {
        if (polygon == null || polygon.Count < 2) return;

        Gizmos.color = color;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Count];
            Gizmos.DrawLine(new Vector3(a.x, a.y, gizmoZ), new Vector3(b.x, b.y, gizmoZ));
        }
    }

    /// <summary>One line summary for on-screen display.</summary>
    public string Describe(float targetRatio)
    {
        if (!HasSampled) return $"{MetricName}: no agents to measure";

        // Without a baseline there is no ratio, but the measured value is still worth showing —
        // this is the paused state, before a run has started.
        if (!HasBaseline)
        {
            return $"{MetricName} {CurrentValue:F2}  (hull area {LastHullArea:F1})  no baseline yet";
        }

        string direction = targetRatio < 1f ? "<=" : ">=";
        string state = IsRatioReached(targetRatio) ? "REACHED" : "…";
        return $"{MetricName} {CurrentValue:F2} / {Baseline:F2} = {Ratio:F2} (target {direction} {targetRatio:F2}) {state}";
    }

    /// <summary>
    /// Connectivity view of the same sample: mean degree against the percolation threshold, and
    /// the hull area against the critical area for the current perception radius.
    /// </summary>
    public string DescribeConnectivity()
    {
        if (PerceptionRadius <= 0f) return "no perception radius available";
        if (!HasSampled || LastAgentCount == 0) return "no sample yet";

        float over = AreaOverCriticalArea;
        string verdict = MeanDegree >= percolationCriticalDegree ? "connected" : "fragmenting";

        return $"mean degree {MeanDegree:F1} vs crit {percolationCriticalDegree:F2} -> {verdict}\n"
             + $"area {LastHullArea:F1} / A_c {CriticalArea:F1} = {over:F2}x  (N {LastAgentCount}, R {PerceptionRadius:F2})";
    }
}
