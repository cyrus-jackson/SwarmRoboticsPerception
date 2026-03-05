using UnityEngine;
using System.Collections.Generic;

public class ObstacleAvoidance : BaseShape
{
    public float rotationSpeed = 50f;

    private GameObject obstacleInstance;
    private List<GameObject> agents = new List<GameObject>();
    private Dictionary<GameObject, Vector3> agentTargets = new Dictionary<GameObject, Vector3>();
    private GameObject scoutAgent;
    private bool scoutHasPassed = false;
    private Vector3 targetPosition;

    // Threshold to consider the scout has successfully passed/interacted with the obstacle
    private float passedThresholdX = 2.0f;

    public override void Initialize()
    {
        // Note: Clear() is called by MotionUI before Initialize
        CalculateCameraBounds();

        // Target is right side horizontally, vertically centered
        targetPosition = new Vector3(topRight.x - 2f, (topRight.y + bottomLeft.y) / 2f, 0);

        // CreateObstacle();
        SpawnAgents();
    }

    public void SetObstacle(GameObject obstacle)
    {
        this.obstacleInstance = obstacle;
    }

    private void SpawnAgents()
    {
        agentTargets.Clear();
        // Start on the left
        float startX = bottomLeft.x + 3f;
        float startY = (topRight.y + bottomLeft.y) / 2f;

        for (int i = 0; i < numberOfAgents; i++)
        {
            // Vertical line arrangement
            Vector3 pos = new Vector3(startX, startY + (i - (numberOfAgents - 1) / 2f) * 1.5f, 0);
            GameObject agent = Instantiate(agentPrefab, pos, Quaternion.identity);
            agents.Add(agent);

            // Assign specific target to maintain order at the destination
            agentTargets[agent] = new Vector3(topRight.x - 2f, pos.y, 0);
        }

        // Pick the middle agent as the scout
        if (agents.Count > 0)
        {
            scoutAgent = agents[agents.Count / 2];

            // Highlight scout
            Renderer r = scoutAgent.GetComponent<Renderer>();
            if (r != null) r.material.color = Color.yellow;
        }
    }

    public override void Clear()
    {
        agentTargets.Clear();
        if (agents != null)
        {
            foreach (var agent in agents)
            {
                if (agent != null) Destroy(agent);
            }
            agents.Clear();
        }
        if (obstacleInstance != null)
        {
            Destroy(obstacleInstance);
        }
        scoutHasPassed = false;
        scoutAgent = null;
    }

    public override void ManualUpdate()
    {
        DrawBounds();
        UpdateObstacle();

        if (agents.Count == 0) return;

        // Ensure scout is valid
        if (scoutAgent == null && agents.Count > 0) scoutAgent = agents[0];

        // Scout logic
        if (!scoutHasPassed)
        {
            if (scoutAgent != null)
            {
                MoveAgent(scoutAgent, false);

                // check if scout has passed the obstacle
                if (scoutAgent.transform.position.x > passedThresholdX)
                {
                    scoutHasPassed = true;
                    Debug.Log("Scout passed! Others will now follow with avoidance.");
                }
            }
        }
        else
        {
            // All agents move
            foreach (var agent in agents)
            {
                if (agent != null)
                {
                    // Avoidance is ON for everyone now (including scout if it wants, though it's already past)
                    // The "others" avoid. The scout just keeps going to target.
                    bool useAvoidance = (agent != scoutAgent);
                    MoveAgent(agent, useAvoidance);
                }
            }
        }
    }

    void UpdateObstacle()
    {
        if (obstacleInstance != null)
        {
            // Rotate around Z axis
            obstacleInstance.transform.Rotate(0, 0, rotationSpeed * Time.deltaTime);
        }
    }

    void MoveAgent(GameObject agent, bool avoidObstacle)
    {
        Rigidbody2D rb = agent.GetComponent<Rigidbody2D>();
        if (rb == null) return;

        Vector3 currentPos = agent.transform.position;
        Vector3 target = agentTargets.ContainsKey(agent) ? agentTargets[agent] : targetPosition;

        // Avoidance Logic
        if (avoidObstacle)
        {
            // If we are to the left of the obstacle and it's in the way
            if (currentPos.x < 2f && currentPos.x > -5f)
            {
                // Detour: Go up or down to avoid the center (0,0)
                // We pick a waypoint above the obstacle
                // The obstacle is at 0,0 with scale 10 in Y? So radius ~5.
                // Let's go to (0, 6)

                // Simple state check: am I near center?
                Vector3 avoidancePoint = new Vector3(0, 6f, 0);

                // Add vertical spacing to maintain order during avoidance
                if (agentTargets.ContainsKey(agent))
                {
                    float verticalOffset = (target.y - (topRight.y + bottomLeft.y) / 2f);
                    avoidancePoint.y += verticalOffset;
                }

                // If the agent started lower, maybe go lower? 
                // For simplicity, let's just make them all go UP around it.
                if (target.y < 0) avoidancePoint.y = -6f + (agentTargets.ContainsKey(agent) ? (target.y - (topRight.y + bottomLeft.y) / 2f) : 0);

                // Move towards avoidance point until we pass x=0
                if (currentPos.x < 0)
                {
                    target = avoidancePoint;
                }
            }
        }

        // Correct path to target (simple P-controller style force or Velocity)
        Vector2 direction = (target - currentPos).normalized;

        // Apply velocity directly for stable movement, or force. 
        // BaseShape has 'moveSpeed'. 
        // We want constant speed towards target.

        // However, if we hit the obstacle, we want physics to bounce us off.
        // If we set velocity directly every frame, it might override collision physics too hard.
        // Better to add force to correct velocity.

        Vector2 desiredVel = direction * moveSpeed;
        Vector2 currentVel = rb.linearVelocity;

        // Check if arrived
        if (Vector3.Distance(currentPos, target) < 0.2f)
        {
            rb.linearVelocity = Vector2.zero;
            return;
        }

        // Force based steering
        Vector2 steer = desiredVel - currentVel;
        rb.AddForce(steer * 5f); // 5f is a steering strength factor
    }

    void Update()
    {
        if (!manualControl)
        {
            ManualUpdate();
        }
    }
}
