using UnityEngine;
using System.Collections.Generic;

public class Polygon : MonoBehaviour
{
    public GameObject agentPrefab;
    public int numberOfAgents = 4;
    public float radius = 5f;
    public float moveForce = 5f; 
    public float maxSpeed = 5f;

    private List<GameObject> agents = new List<GameObject>();
    private int[] targetIndices;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (agentPrefab == null)
        {
            Debug.LogError("Agent Prefab is not assigned!");
            return;
        }

        SpawnAgents();
    }

    void SpawnAgents()
    {
        targetIndices = new int[numberOfAgents];

        for (int i = 0; i < numberOfAgents; i++)
        {
            Vector3 pos = GetVertexPosition(i);
            GameObject agent = Instantiate(agentPrefab, pos, Quaternion.identity);
            agents.Add(agent);
            
            targetIndices[i] = (i + 1) % numberOfAgents;
        }
    }

    Vector3 GetVertexPosition(int index)
    {
        float angleStep = 360f / numberOfAgents;
        float angle = index * angleStep * Mathf.Deg2Rad;
        return transform.position + new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0);
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        for (int i = 0; i < agents.Count; i++)
        {
            MoveAgent(i);
        }
    }

    void MoveAgent(int index)
    {
        GameObject agent = agents[index];
        if (agent == null) return;

        Vector3 targetPos = GetVertexPosition(targetIndices[index]);
        Vector3 direction = (targetPos - agent.transform.position).normalized;
        float distance = Vector3.Distance(agent.transform.position, targetPos);

        Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            Vector2 direction2D = new Vector2(direction.x, direction.y);
            Vector2 desiredVelocity = direction2D * maxSpeed;
            Vector2 steeringForce = (desiredVelocity - rb.linearVelocity) * moveForce;
            
            rb.AddForce(steeringForce);
        }
        else
        {
            agent.transform.position += direction * maxSpeed * Time.fixedDeltaTime;
        }

        if (distance < 0.1f)
        {
            targetIndices[index] = (targetIndices[index] + 1) % numberOfAgents;
        }
    }
}
