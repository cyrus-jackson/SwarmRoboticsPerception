using UnityEngine;
using System.Collections.Generic;

public class Pattern : BaseShape
{
    private List<GameObject> agents = new List<GameObject>();
    private bool expanding = true;
    public bool sequential = false;

    // Sequential mode state
    private List<bool> agentExpandingStates = new List<bool>();
    private float timer = 0f;
    private int activeCount = 0;
    private float delay = 0.2f;

    public override void Initialize()
    {
        Clear();
        CalculateCameraBounds();
        SpawnAgents();

        // Reset sequential state
        agentExpandingStates.Clear();
        for (int i = 0; i < agents.Count; i++) agentExpandingStates.Add(true);
        activeCount = 0;
        timer = 0f;

        expanding = true;
    }

    private void SpawnAgents()
    {
        float angleStep = 2 * Mathf.PI / numberOfAgents;

        for (int i = 0; i < numberOfAgents; i++)
        {
            float angle = i * angleStep;

            // Calculate position on XY plane
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;

            Vector3 spawnPos = new Vector2(x, y);

            GameObject agent = Instantiate(agentPrefab, spawnPos, Quaternion.identity);
            agents.Add(agent);
        }
    }

    public override void ManualUpdate()
    {
        if (sequential)
        {
            UpdateSequential();
        }
        else
        {
            UpdateConcurrent();
        }
    }

    private void UpdateSequential()
    {
        // Activate agents one by one
        if (activeCount < agents.Count)
        {
            timer += Time.deltaTime;
            if (timer >= delay)
            {
                activeCount++;
                timer = 0f;
            }
        }

        // Update all active agents independently
        for (int i = 0; i < activeCount; i++)
        {
            if (i >= agents.Count) break;
            GameObject agent = agents[i];
            if (agent == null) continue;

            bool isExpanding = agentExpandingStates[i];
            Vector3 pos = agent.transform.position;

            bool hitEdge = (Mathf.Abs(pos.x) >= topRight.x * 0.9f || Mathf.Abs(pos.y) >= topRight.y * 0.9f);
            bool hitCenter = (pos.magnitude <= radius);

            if (isExpanding && hitEdge) isExpanding = false;
            if (!isExpanding && hitCenter) isExpanding = true;

            agentExpandingStates[i] = isExpanding;

            float currentForce = isExpanding ? moveSpeed : -moveSpeed;
            Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                Vector2 direction = agent.transform.position.normalized;
                rb.AddForce(direction * currentForce);
            }
        }
    }

    private void UpdateConcurrent()
    {
        bool hitEdge = false;
        bool hitCenter = false;

        // Check status of all agents to determine state
        foreach (var agent in agents)
        {
            if (agent == null) continue;

            Vector3 pos = agent.transform.position;

            // Check if near camera edges (corners/bounds)
            if (Mathf.Abs(pos.x) >= topRight.x * 0.9f || Mathf.Abs(pos.y) >= topRight.y * 0.9f)
            {
                hitEdge = true;
            }

            // Check if back to original radius
            if (pos.magnitude <= radius)
            {
                hitCenter = true;
            }
        }

        if (expanding && hitEdge) expanding = false;
        if (!expanding && hitCenter) expanding = true;

        float currentForce = expanding ? moveSpeed : -moveSpeed;

        foreach (var agent in agents)
        {
            if (agent == null) continue;

            Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                Vector2 direction = agent.transform.position.normalized;
                rb.AddForce(direction * currentForce);
            }
        }
    }

    public override void Clear()
    {
        foreach (var agent in agents)
        {
            if (agent != null) Destroy(agent);
        }
        agents.Clear();
    }
}
