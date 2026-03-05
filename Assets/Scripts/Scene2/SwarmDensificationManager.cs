using UnityEngine;

public class SwarmDensificationManager : MonoBehaviour
{
    public GameObject agentPrefab; // Assign your agent prefab in inspector
    public GameObject[] agents; // Public array for agents (populated at runtime)
    public GameObject obstacle; // Assign your obstacle GameObject in inspector
    public int numAgents = 50; // Number of agents, from large swarms in sources
    public float spawnAreaSize = 20f; // Spawn area size for random positions

    // Parameters from "Hénard et al. - 2024" (Page 4: Table 1 bounds, adjusted for densification)
    public float fieldOfViewDistance = 5f; // d: perception range
    public float blindSpotAngle = 120f; // alpha: blind spot
    public float cI = 1.5f; // Cohesion intensity (high for densification)
    public float sI = 0.5f; // Separation intensity (low for densification)
    public float aI = 0.2f; // Alignment intensity (low to avoid flocking)
    public float rmI = 0.1f; // Random movement intensity
    public float fI = 0.05f; // Friction intensity
    public float oI = 2f; // Overlap avoidance intensity
    public float sd = 0.5f; // Safe distance for overlap
    public float obstacleRepelStrength = 2f; // Repulsion from obstacle
    public float maxSpeed = 5f; // Cap speed

    void Start()
    {
        agents = new GameObject[numAgents];
        for (int i = 0; i < numAgents; i++)
        {
            Vector3 spawnPos = new Vector3(Random.Range(-spawnAreaSize, spawnAreaSize), 0, Random.Range(-spawnAreaSize, spawnAreaSize));
            agents[i] = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
            agents[i].AddComponent<AgentData>(); // Custom component to store velocity
        }
    }

    void Update()
    {
        foreach (var agent in agents)
        {
            UpdateAgent(agent);
        }
    }

    void UpdateAgent(GameObject agent)
    {
        AgentData data = agent.GetComponent<AgentData>();
        Vector3 acceleration = Vector3.zero;

        // Find neighbors (local control, per "Kolling et al. - 2016" Page 1)
        Vector3[] neighborsPos = GetNeighbors(agent.transform.position, agent.transform.forward);

        if (neighborsPos.Length == 0) return; // No neighbors, idle

        int neighborCount = neighborsPos.Length;

        // Cohesion (from "Hénard et al. - 2024" Page 4 equation)
        Vector3 avgPos = Vector3.zero;
        foreach (var pos in neighborsPos) avgPos += pos;
        avgPos /= neighborCount;
        Vector3 cohesion = (avgPos - agent.transform.position) * cI;

        // Separation
        Vector3 separation = Vector3.zero;
        foreach (var pos in neighborsPos)
        {
            Vector3 diff = agent.transform.position - pos;
            separation += diff.normalized / diff.magnitude;
        }
        separation = (separation / neighborCount) * sI;

        // Alignment (low for densification)
        Vector3 alignment = Vector3.zero;
        foreach (var otherAgent in agents)
        {
            if (otherAgent == agent) continue;
            if (IsNeighbor(agent.transform.position, agent.transform.forward, otherAgent.transform.position))
                alignment += otherAgent.GetComponent<AgentData>().velocity;
        }
        if (neighborCount > 0) alignment = (alignment / neighborCount) * aI;

        // Random Movement
        Vector3 randomMovement = new Vector3(Random.Range(-0.5f, 0.5f), 0, Random.Range(-0.5f, 0.5f)) * rmI;

        // Friction
        Vector3 friction = -data.velocity * fI;

        // Overlap Avoidance
        Vector3 avoid = Vector3.zero;
        foreach (var pos in neighborsPos)
        {
            Vector3 diff = agent.transform.position - pos;
            if (diff.magnitude < sd) avoid += diff.normalized / diff.magnitude;
        }
        avoid *= oI;

        // Obstacle Avoidance (repulsion, extended from "walker-perception-of-swarm-behavior2016.pdf" Page 2)
        Vector3 obsDiff = agent.transform.position - obstacle.transform.position;
        if (obsDiff.magnitude < fieldOfViewDistance)
        {
            avoid += (obsDiff.normalized / obsDiff.magnitude) * obstacleRepelStrength;
        }

        // Sum acceleration (per "Hénard et al. - 2024" Page 4)
        acceleration = cohesion + separation + alignment + randomMovement + friction + avoid;

        // Update velocity and position
        data.velocity += acceleration * Time.deltaTime;
        data.velocity = Vector3.ClampMagnitude(data.velocity, maxSpeed);
        agent.transform.position += data.velocity * Time.deltaTime;
        if (data.velocity != Vector3.zero) agent.transform.forward = data.velocity.normalized;
    }

    // Neighbor detection (field of view, no blind spot check for simplicity; full in source)
    Vector3[] GetNeighbors(Vector3 pos, Vector3 forward)
    {
        System.Collections.Generic.List<Vector3> neighbors = new();
        foreach (var other in agents)
        {
            if (other.transform.position == pos) continue;
            if (IsNeighbor(pos, forward, other.transform.position))
                neighbors.Add(other.transform.position);
        }
        return neighbors.ToArray();
    }

    bool IsNeighbor(Vector3 pos, Vector3 forward, Vector3 otherPos)
    {
        Vector3 diff = otherPos - pos;
        if (diff.magnitude > fieldOfViewDistance) return false;

        // Blind spot check (from "Hénard et al. - 2024" Page 4 Figure 2)
        float angle = Vector3.Angle(forward, diff);
        return angle < (180f - blindSpotAngle / 2f); // Approximate blind spot
    }
}

// Custom component for velocity storage
public class AgentData : MonoBehaviour
{
    public Vector3 velocity = Vector3.zero;
}