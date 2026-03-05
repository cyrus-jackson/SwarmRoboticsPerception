using UnityEngine;
using System.Collections.Generic;

public class Shape : BaseShape
{
    public float stoppingDistance = 0.1f;
    public float rotationOffset = 0f;

    private List<GameObject> agents = new List<GameObject>();
    private List<Vector3> corners = new List<Vector3>();
    private int[] targetIndices;

    void Start()
    {
        if (agentPrefab == null)
        {
            return;
        }

        if (!manualControl)
        {
            Initialize();
        }
    }

    public override void Initialize()
    {
        Clear();
        if (numberOfAgents < 3) numberOfAgents = 3;

        CalculateCorners();
        SpawnAgents();
    }

    public override void Clear()
    {
        if (agents != null)
        {
            foreach (var agent in agents)
            {
                if (agent != null) Destroy(agent);
            }
            agents.Clear();
        }
    }

    void CalculateCorners()
    {
        if (corners == null) corners = new List<Vector3>();
        corners.Clear();
        
        int cornersCount = Mathf.Max(3, numberOfAgents / 2);
        float angleStep = 360f / cornersCount;

        for (int i = 0; i < cornersCount; i++)
        {
            // Calculate angle in radians
            float angle = ((cornersCount - i - 1) * angleStep + rotationOffset) * Mathf.Deg2Rad;

            // Calculate position on XY plane
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;

            corners.Add(new Vector3(x, y, 0));
        }
    }

    void OnValidate()
    {
        CalculateCorners();
    }

    void SpawnAgents()
    {
        targetIndices = new int[numberOfAgents];
        int cornersCount = corners.Count;

        for (int i = 0; i < numberOfAgents; i++)
        {
            // Distribute 2 agents per edge: one at corner, one at midpoint
            int edgeIndex = (i / 2) % cornersCount;
            float t = (i % 2) * 0.5f;

            Vector3 startCorner = corners[edgeIndex];
            Vector3 endCorner = corners[(edgeIndex + 1) % cornersCount];
            Vector3 startPos = Vector3.Lerp(startCorner, endCorner, t);

            GameObject agent = Instantiate(agentPrefab, startPos, Quaternion.identity);
            
            Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
            agents.Add(agent);
            
            // Target is the next corner
            targetIndices[i] = (edgeIndex + 1) % cornersCount;
        }
    }

    void FixedUpdate()
    {
        if (!manualControl)
        {
            if (agents.Count == 0) return;

            for (int i = 0; i < agents.Count; i++)
            {
                MoveAgent(i);
            }
        }
    }

    public override void ManualUpdate()
    {
        if (agents.Count == 0) return;

        for (int i = 0; i < agents.Count; i++)
        {
            MoveAgent(i);
        }
    }

    void MoveAgent(int index)
    {
        GameObject agent = agents[index];
        if (agent == null) return;

        int targetIndex = targetIndices[index];
        Vector3 targetPos = corners[targetIndex];

        Vector2 direction = (targetPos - agent.transform.position).normalized;
        float distance = Vector2.Distance(agent.transform.position, targetPos);

        Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
        rb.AddForce(direction * moveSpeed);
        // rb.linearVelocity = direction * moveSpeed;

        // Check if reached the target corner
        if (distance < stoppingDistance)
        {
            rb.linearVelocity = direction * 0;
            targetIndices[index] = (targetIndex + 1) % corners.Count;
        }
    }

    void OnDrawGizmos()
    {
        if (corners == null || corners.Count == 0) return;

        Gizmos.color = Color.red;
        for (int i = 0; i < corners.Count; i++)
        {
            Gizmos.DrawWireSphere(corners[i], 0.2f);
            Gizmos.DrawLine(corners[i], corners[(i + 1) % corners.Count]);
        }
    }
}
