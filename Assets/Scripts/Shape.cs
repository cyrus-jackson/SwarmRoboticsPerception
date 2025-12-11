using UnityEngine;
using System.Collections.Generic;

public class Shape : MonoBehaviour
{

    public GameObject agentPrefab;
    public int numberOfAgents = 4;
    public float radius = 5f;
    public float moveSpeed = 5f;
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

        InitializeShape();
    }

    void InitializeShape()
    {
        if (numberOfAgents < 3) numberOfAgents = 3;

        CalculateCorners();
        SpawnAgents();
    }

    void CalculateCorners()
    {
        if (corners == null) corners = new List<Vector3>();
        corners.Clear();
        float angleStep = 360f / numberOfAgents;

        for (int i = 0; i < numberOfAgents; i++)
        {
            // Calculate angle in radians
            float angle = ((numberOfAgents - i - 1) * angleStep + rotationOffset) * Mathf.Deg2Rad;

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

        for (int i = 0; i < numberOfAgents; i++)
        {
            Vector3 startPos = corners[i];
            GameObject agent = Instantiate(agentPrefab, startPos, Quaternion.identity);
            
            Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
            agents.Add(agent);
            targetIndices[i] = (i + 1) % numberOfAgents;
        }
    }

    void FixedUpdate()
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
            targetIndices[index] = (targetIndex + 1) % numberOfAgents;
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
