using UnityEngine;

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

    [Header("Swarm Parameters")]
    public float cohesionIntensity = 5.0f;    // cI
    public float separationIntensity = 1.0f;  // sI
    public float alignmentIntensity = 2.0f;   // aI
    public float frictionIntensity = 0.9f;    // fI
    public float randomMovementIntensity = 1.0f; // rI

    [Header("Overlapping Avoidance (Agent to Agent)")]
    public float overlappingAvoidanceIntensity = 20.0f; // oI
    public float safetyDistance = 1f; // sd

    [Header("Environmental Obstacle Avoidance")]
    public float envObstacleAvoidanceIntensity = 20.0f;

    [Header("Perception")]
    public float perceptionRadius = 1f;
    public float obstacleAvoidanceRadius = 2.5f;
    public float maxSpeed = 5.0f;

    [Header("Visualization")]
    public bool showPerceptionRadius = false;
    [Tooltip("Draw the measured density region as a runtime outline, visible in the Game view.")]
    public bool showDensityArea = false;

    [Header("Integration")]
    [Tooltip("Fixed simulation step in seconds. Each frame's elapsed time is consumed in steps of this size using the paper's Euler update, so behaviour no longer depends on frame rate and fast agents cannot step over an obstacle in one go.")]
    public float simulationStep = 1f / 120f;
    [Tooltip("Maximum substeps per frame. Prevents a stall if the editor hitches; any leftover time is dropped.")]
    public int maxSubstepsPerFrame = 16;

    private float stepAccumulator = 0f;

    void Start()
    {
        if (commonFateTarget != null)
        {
            commonFateCollider = commonFateTarget.GetComponent<Collider2D>();
        }
    }

    void Update()
    {
        if (agents == null) return;

        StepSimulation();

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

    /// <summary>
    /// Advances the swarm by this frame's elapsed time, in fixed steps of simulationStep. The rule
    /// set and the Euler update are unchanged from Hénard et al. (2024); only the step size is
    /// subdivided, which keeps a fast agent from crossing an obstacle between two samples. The
    /// random movement vector is drawn once per frame so its magnitude per unit time is unaffected
    /// by the number of substeps.
    /// </summary>
    private void StepSimulation()
    {
        float step = Mathf.Max(0.0001f, simulationStep);
        int stepCap = Mathf.Max(1, maxSubstepsPerFrame);

        foreach (GameObject agentObj in agents)
        {
            if (agentObj == null) continue;

            SwarmAgent agent = agentObj.GetComponent<SwarmAgent>();
            if (agent != null) agent.SampleRandomMovement();
        }

        stepAccumulator += Time.deltaTime;

        int stepsTaken = 0;
        while (stepAccumulator >= step && stepsTaken < stepCap)
        {
            foreach (GameObject agentObj in agents)
            {
                if (agentObj == null) continue;

                SwarmAgent agent = agentObj.GetComponent<SwarmAgent>();
                if (agent != null) agent.UpdateAgent(this, step);
            }

            stepAccumulator -= step;
            stepsTaken++;
        }

        // Drop any backlog rather than trying to catch up, which would produce a speed spike.
        if (stepsTaken >= stepCap) stepAccumulator = 0f;
    }

    /// <summary>Clears the leftover time so a new run starts on a clean step boundary.</summary>
    public void ResetIntegration()
    {
        stepAccumulator = 0f;
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