using UnityEngine;

public class SwarmManager : MonoBehaviour
{
    [Header("Swarm Elements")]
    public GameObject[] agents;
    public Transform commonFateTarget;
    public Collider2D commonFateCollider;
    public Transform centralObstacle;

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
        foreach (GameObject agentObj in agents)
        {
            if (agentObj != null)
            {
                SwarmAgent agent = agentObj.GetComponent<SwarmAgent>();
                if (agent != null)
                {
                    agent.UpdateAgent(this);
                }
            }
        }
    }
}