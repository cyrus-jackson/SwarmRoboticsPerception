using UnityEngine;
using System.Collections.Generic;
using System.Linq;

public enum SimilarityLevel
{
    Low,
    Medium,
    High
}

public class Speed : BaseShape
{
    public SimilarityLevel similarityLevel = SimilarityLevel.High;
    private List<GameObject> agents = new List<GameObject>();
    private List<float> speedOffsets = new List<float>();

    Vector3 topPadding = new Vector2(0, -1);

    public override void Initialize()
    {
        Clear();
        CalculateCameraBounds();
        SpawnAgents();

        speedOffsets.Clear();
        for (int i = 0; i < numberOfAgents; i++)
        {
            float offset = 0;
            switch (similarityLevel)
            {
                case SimilarityLevel.High:
                    offset = 0;
                    break;
                case SimilarityLevel.Medium:
                    offset = i * 0.5f;
                    break;
                case SimilarityLevel.Low:
                    offset = Random.Range(0f, numberOfAgents * 0.1f);
                    break;
            }
            speedOffsets.Add(offset);
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

            // Check if out of bounds
            if (pos.y < bottomLeft.y)
            {
                // Respawn at top
                Vector3 newPos = pos;
                newPos.y = topRight.y;
                agent.transform.position = newPos;

                // Reset velocity to prevent infinite acceleration
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

        Vector3 targetPos = new Vector2(0, Mathf.Sin(270));

        Vector2 direction = targetPos.normalized;
        float distance = Vector2.Distance(agent.transform.position, targetPos);

        Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
        rb.AddForce(direction * (moveSpeed + speedOffsets[index]));
        // rb.linearVelocity = direction * moveSpeed;
    }
}