using UnityEngine;

/// <summary>
/// A target region in the arena. Counts how many swarm agents are currently inside it,
/// logs whenever that count changes, and reports whether a configurable percentage of the
/// swarm has arrived (used by SimRecorder as an early recording end condition).
///
/// Attach to any Transform used as a goal area. If the object has a Collider2D the exact
/// collider shape is used; otherwise the object's scale is treated as an axis-aligned
/// rectangle (consistent with how UI.cs treats spawn areas).
/// </summary>
public class GoalArea : MonoBehaviour
{
    [Header("Logging")]
    [Tooltip("Log the number of agents inside this area as it changes.")]
    public bool logCountChanges = true;

    [Tooltip("Minimum seconds between logs. Raise to quieten the console, 0 for no throttle.")]
    public float logMinInterval = 1f;

    [Tooltip("Only log once the count has moved by at least this many agents since the last log. 1 logs every change.")]
    public int logMinAgentChange = 5;

    [Tooltip("Always log the moments the area becomes empty or holds every agent, regardless of the limits above.")]
    public bool alwaysLogEmptyAndFull = true;

    [Tooltip("Prefix for log messages, e.g. the motion type being recorded. Set by UI.cs.")]
    public string contextLabel = "";

    [Header("Visualization")]
    public bool showGizmo = true;
    public Color gizmoColor = new Color(0f, 0.85f, 0.35f, 1f);

    /// <summary>Number of agents inside the area as of the last evaluation.</summary>
    public int AgentsInside { get; private set; }

    /// <summary>
    /// Number of distinct agents that have been inside at any point since the last ResetTracking().
    ///
    /// Only ever rises. An agent that enters and drifts back out still counts, which is what makes
    /// this a measure of how many reached the area rather than how many happen to be standing in it
    /// at one instant.
    /// </summary>
    public int AgentsEverInside { get; private set; }

    /// <summary>Number of non-null agents considered in the last evaluation.</summary>
    public int TrackedAgents { get; private set; }

    /// <summary>True once this area has been evaluated at least since the last ResetTracking().</summary>
    public bool HasEvaluated { get; private set; }

    public float FractionInside => TrackedAgents > 0 ? (float)AgentsInside / TrackedAgents : 0f;
    public float PercentInside => FractionInside * 100f;

    public float FractionEverInside => TrackedAgents > 0 ? (float)AgentsEverInside / TrackedAgents : 0f;
    public float PercentEverInside => FractionEverInside * 100f;

    private Collider2D areaCollider;
    private int lastLoggedCount = -1;
    private float lastLogTime = -999f;

    // One flag per agent slot. Indexed rather than keyed on the object, because the agent array is
    // stable for the length of a run and the recorder already identifies agents by that index.
    private bool[] everInside = new bool[0];

    void Awake()
    {
        CacheCollider();
    }

    void OnValidate()
    {
        CacheCollider();
    }

    private void CacheCollider()
    {
        if (areaCollider == null)
        {
            areaCollider = GetComponent<Collider2D>();
        }
    }

    /// <summary>
    /// Clears the counter. Call when a new simulation run starts so a stale count from the
    /// previous run cannot satisfy an end condition on the first frame.
    /// </summary>
    public void ResetTracking()
    {
        AgentsInside = 0;
        AgentsEverInside = 0;
        TrackedAgents = 0;
        HasEvaluated = false;
        lastLoggedCount = -1;
        lastLogTime = -999f;

        System.Array.Clear(everInside, 0, everInside.Length);
    }

    /// <summary>
    /// Recounts the agents inside the area. Returns the number inside.
    /// </summary>
    public int Evaluate(GameObject[] agents)
    {
        CacheCollider();

        int inside = 0;
        int tracked = 0;

        if (agents != null)
        {
            // A different array length means the swarm was respawned, so the previous run's arrivals
            // must not carry over into this one.
            if (everInside.Length != agents.Length)
            {
                everInside = new bool[agents.Length];
                AgentsEverInside = 0;
            }

            for (int i = 0; i < agents.Length; i++)
            {
                GameObject agentObj = agents[i];
                if (agentObj == null) continue;
                tracked++;

                if (Contains(agentObj.transform.position))
                {
                    inside++;

                    if (!everInside[i])
                    {
                        everInside[i] = true;
                        AgentsEverInside++;
                    }
                }
            }
        }

        AgentsInside = inside;
        TrackedAgents = tracked;
        HasEvaluated = true;

        if (logCountChanges && ShouldLog(inside, tracked))
        {
            string prefix = string.IsNullOrEmpty(contextLabel) ? name : $"{contextLabel}/{name}";
            Debug.Log($"[GoalArea:{prefix}] {inside}/{tracked} agents inside ({PercentInside:F1}%)");
            lastLoggedCount = inside;
            lastLogTime = Time.time;
        }

        return inside;
    }

    /// <summary>
    /// Rate limits the count logging: a log needs a large enough change and enough elapsed time,
    /// except for the empty and all-inside moments which are always worth seeing.
    /// </summary>
    private bool ShouldLog(int inside, int tracked)
    {
        if (inside == lastLoggedCount) return false;

        if (alwaysLogEmptyAndFull && (inside == 0 || (tracked > 0 && inside == tracked))) return true;

        // First log of a run always goes through.
        if (lastLoggedCount < 0) return true;

        if (Mathf.Abs(inside - lastLoggedCount) < Mathf.Max(1, logMinAgentChange)) return false;
        if (logMinInterval > 0f && Time.time - lastLogTime < logMinInterval) return false;

        return true;
    }

    /// <summary>
    /// True when at least <paramref name="percent"/> percent of the tracked agents are inside.
    /// Returns false until the area has been evaluated at least once.
    /// </summary>
    public bool IsPercentReached(float percent)
    {
        if (!HasEvaluated || TrackedAgents == 0) return false;
        return PercentInside >= percent;
    }

    /// <summary>
    /// Point-in-area test. Uses the Collider2D shape when present, otherwise the object's
    /// scale as an axis-aligned rectangle.
    /// </summary>
    public bool Contains(Vector3 worldPoint)
    {
        Vector2 point = new Vector2(worldPoint.x, worldPoint.y);

        CacheCollider();
        if (areaCollider != null)
        {
            return areaCollider.OverlapPoint(point);
        }

        Vector3 size = transform.lossyScale;
        Vector3 center = transform.position;
        return Mathf.Abs(point.x - center.x) <= Mathf.Abs(size.x) / 2f
            && Mathf.Abs(point.y - center.y) <= Mathf.Abs(size.y) / 2f;
    }

    void OnDrawGizmos()
    {
        if (!showGizmo) return;

        Gizmos.color = gizmoColor;

        Collider2D col = areaCollider != null ? areaCollider : GetComponent<Collider2D>();
        if (col is CircleCollider2D circle)
        {
            float scale = Mathf.Max(Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.y));
            Gizmos.DrawWireSphere(circle.bounds.center, circle.radius * scale);
        }
        else if (col != null)
        {
            Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
        }
        else
        {
            Gizmos.DrawWireCube(transform.position, transform.lossyScale);
        }
    }
}
