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

    [Header("Swarm Parameters")]
    public float cohesionIntensity = 5.0f;    // cI
    public float separationIntensity = 1.0f;  // sI
    public float alignmentIntensity = 2.0f;   // aI
    public float frictionIntensity = 0.1f;    // fI
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

        foreach (GameObject agentObj in agents)
        {
            if (agentObj == null) continue;

            SwarmAgent agent = agentObj.GetComponent<SwarmAgent>();
            if (agent != null)
            {
                agent.UpdateAgent(this);
            }

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