using UnityEngine;

/// <summary>Advances the swarm simulation and updates its goal, density, and perception displays.</summary>
public class SwarmManager : MonoBehaviour
{
    [Header("Swarm Elements")]
    public GameObject[] agents;
    public Transform commonFateTarget;
    public Collider2D commonFateCollider;
    public Transform centralObstacle;

    [Header("Goal Area")]
    [Tooltip("Target region for the current swarm type. Assigned by UI.cs from the per-type goal areas. Counts agents inside and can end a recording early.")]
    public GoalArea goalArea;

    [Header("Density")]
    [Tooltip("Tracks how spread out the swarm is against a baseline. Provisioned by UI.cs if left empty.")]
    public SwarmDensityMonitor densityMonitor;

    [Header("Behavior Version")]
    [Tooltip("Selected by UI.cs. Identifies Densification V2 for preset and recording selection; all modes use the shared SwarmAgent rules.")]
    public bool useDensificationV2 = false;

    [Header("Swarm Parameters")]
    public float cohesionIntensity = 5.0f;    // cI
    public float separationIntensity = 1.0f;  // sI
    public float alignmentIntensity = 2.0f;   // aI
    public float frictionIntensity = 0.9f;    // fI
    public float randomMovementIntensity = 1.0f; // rI

    [Header("Overlapping Avoidance (Agent to Agent)")]
    public float overlappingAvoidanceIntensity = 20.0f; // oI
    public float safetyDistance = 1f; // sd

    // Retained for existing recording schemas; blind agents exert no obstacle avoidance force.
    [HideInInspector] public float envObstacleAvoidanceIntensity;
    [HideInInspector] public float obstacleAvoidanceRadius;

    [Header("Perception")]
    public float perceptionRadius = 1f;
    public float maxSpeed = 5.0f;

    [Header("Visualization")]
    public bool showPerceptionRadius = false;
    [Tooltip("Draw the measured density region as a runtime outline, visible in the Game view.")]
    public bool showDensityArea = false;

    private Collider2D[] agentColliders = new Collider2D[0];
    private SwarmAgent[] agentScripts = new SwarmAgent[0];

    private Collider2D cachedObstacleCollider;
    private Transform cachedObstacleTransform;

    /// <summary>
    /// Collider of the active central obstacle, resolved on demand and re-resolved when the
    /// obstacle changes.
    /// </summary>
    public Collider2D ObstacleCollider
    {
        get
        {
            if (centralObstacle != cachedObstacleTransform)
            {
                cachedObstacleTransform = centralObstacle;
                cachedObstacleCollider = centralObstacle != null
                    ? centralObstacle.GetComponent<Collider2D>()
                    : null;
            }
            return cachedObstacleCollider;
        }
    }

    /// <summary>Colliders of the agents array, index aligned. May contain nulls.</summary>
    public Collider2D[] AgentColliders => agentColliders;

    /// <summary>SwarmAgent components of the agents array, index aligned. May contain nulls.</summary>
    public SwarmAgent[] AgentScripts => agentScripts;

    /// <summary>
    /// Re-resolves the per-agent component cache. Call after assigning a new agents array.
    /// </summary>
    public void RebuildAgentCache()
    {
        int count = agents != null ? agents.Length : 0;
        agentColliders = new Collider2D[count];
        agentScripts = new SwarmAgent[count];

        for (int i = 0; i < count; i++)
        {
            if (agents[i] == null) continue;
            agentColliders[i] = agents[i].GetComponent<Collider2D>();
            agentScripts[i] = agents[i].GetComponent<SwarmAgent>();
            if (agentScripts[i] != null) agentScripts[i].SetSimulationActive(isActiveAndEnabled);
        }
    }

    private void OnEnable()
    {
        envObstacleAvoidanceIntensity = 0f;
        obstacleAvoidanceRadius = 0f;
        SetAgentSimulationActive(true);
    }

    private void OnDisable()
    {
        SetAgentSimulationActive(false);
    }

    private void SetAgentSimulationActive(bool active)
    {
        foreach (SwarmAgent agent in agentScripts)
        {
            if (agent != null) agent.SetSimulationActive(active);
        }
    }

    void Start()
    {
        if (commonFateTarget != null)
        {
            commonFateCollider = commonFateTarget.GetComponent<Collider2D>();
        }
    }

    void FixedUpdate()
    {
        if (agents == null) return;

        if (agentScripts.Length != agents.Length) RebuildAgentCache();

        for (int i = 0; i < agentScripts.Length; i++)
        {
            if (agentScripts[i] != null) agentScripts[i].SampleRandomMovement();
        }

        float deltaTime = Time.deltaTime;
        for (int i = 0; i < agentScripts.Length; i++)
        {
            if (agentScripts[i] != null) agentScripts[i].UpdateAgent(this, deltaTime);
        }

        foreach (GameObject agentObj in agents)
        {
            if (agentObj == null) continue;

            // Runtime visualization: ensure each agent has a PerceptionVisualizer
            PerceptionVisualizer pv = agentObj.GetComponent<PerceptionVisualizer>();
            if (pv == null && showPerceptionRadius)
            {
                pv = agentObj.AddComponent<PerceptionVisualizer>();
                pv.segments = 48;
                pv.lineWidth = 0.02f;
            }

            if (pv != null)
            {
                pv.radius = perceptionRadius;
                pv.enabled = showPerceptionRadius;
            }
        }

        // Recount agents inside the goal area after they have all moved this frame.
        if (goalArea != null)
        {
            goalArea.Evaluate(agents);
        }

        // Resample the swarm density metric (throttled internally). While paused the monitor
        // samples itself instead, and it also owns the runtime outline's enabled state.
        if (densityMonitor != null)
        {
            densityMonitor.Evaluate(agents);
        }
    }

    /// <summary>Percentage of agents currently inside the active goal area (0 when no area is set).</summary>
    public float GoalAreaPercentInside => goalArea != null ? goalArea.PercentInside : 0f;

    /// <summary>Number of agents currently inside the active goal area (0 when no area is set).</summary>
    public int GoalAreaAgentsInside => goalArea != null ? goalArea.AgentsInside : 0;

    private void OnDrawGizmos()
    {
        if (showPerceptionRadius && agents != null)
        {
            Gizmos.color = Color.red;
            foreach (GameObject agentObj in agents)
            {
                if (agentObj != null)
                {
                    Gizmos.DrawWireSphere(agentObj.transform.position, perceptionRadius);
                }
            }
        }
    }
}
