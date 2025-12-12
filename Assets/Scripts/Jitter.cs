using UnityEngine;
using System.Collections.Generic;

public enum JitterType
{
    Sync,
    Async
}

public class Jitter : BaseShape
{
    public JitterType jitterType = JitterType.Sync;
    private List<GameObject> agents = new List<GameObject>();
    private List<float> jitterOffsets = new List<float>();

    Vector3 topPadding = new Vector2(0, -1);

    public override void Initialize()
    {
        Clear();
        CalculateCameraBounds();
        SpawnAgents();

        jitterOffsets.Clear();
        for (int i = 0; i < numberOfAgents; i++)
        {
            float offset = 0;
            if (jitterType == JitterType.Async)
            {
                offset = Random.Range(0f, 100f);
            }
            jitterOffsets.Add(offset);
        }
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

    public override void ManualUpdate()
    {
        CheckBounds();
        DrawBounds();
        for (int i = 0; i < agents.Count; i++)
        {
            MoveAgent(i);
        }
    }

    void Update()
    {
        if (!manualControl)
        {
            ManualUpdate();
        }
    }

    void CheckBounds()
    {
        for (int i = agents.Count - 1; i >= 0; i--)
        {
            GameObject agent = agents[i];
            if (agent == null)
            {
                agents.RemoveAt(i);
                continue;
            }

            Vector3 pos = agent.transform.position;

            if (pos.y < bottomLeft.y)
            {
                Vector3 newPos = pos;
                newPos.y = topRight.y;
                agent.transform.position = newPos;

                Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector2.down * moveSpeed;
                }
            }
        }
    }

    void SpawnAgents()
    {
        float width = topRight.x - bottomLeft.x;
        float step = width / (numberOfAgents + 1);

        for (int i = 0; i < numberOfAgents; i++)
        {
            float xPos = bottomLeft.x + step * (i + 1);
            Vector3 startPos = new Vector3(xPos, topRight.y, 0) + topPadding;

            GameObject agent = Instantiate(agentPrefab, startPos, Quaternion.identity);
            agents.Add(agent);
        }
    }

    void MoveAgent(int index)
    {
        GameObject agent = agents[index];
        if (agent == null) return;

        // Move downwards and then slow down and move again.
        float frequency = 2f;
        float jitter = Mathf.Sin(Time.time * frequency + jitterOffsets[index]);

        Vector2 force = Vector2.down * moveSpeed;

        // When jitter is positive, it adds upward force (braking).
        // When jitter is negative, it adds downward force (accelerating).
        force.y += jitter * moveSpeed * 2f;

        Debug.Log($"Force: {Time.time}");

        Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
        rb.AddForce(force);
    }
}