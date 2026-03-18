using System.Collections.Generic;
using UnityEngine;

public class SwarmAgent : MonoBehaviour
{
    private Vector2 currentVelocity;

    // Cached values for visualization
    private float cachedPerceptionRadius;
    private bool cachedShowPerceptionRadius;

    public void UpdateAgent(SwarmManager manager)
    {
        // Cache variables for gizmos
        cachedPerceptionRadius = manager.perceptionRadius;
        cachedShowPerceptionRadius = manager.showPerceptionRadius;

        Vector2 currentPosition = transform.position;

        Vector2 cohesionSum = Vector2.zero;
        Vector2 separationSum = Vector2.zero;
        Vector2 alignmentSum = Vector2.zero;
        Vector2 overlappingAvoidanceSum = Vector2.zero;
        int neighborCount = 0;

        foreach (GameObject otherObj in manager.agents)
        {
            if (otherObj != gameObject && otherObj != null)
            {
                Vector2 otherPos = otherObj.transform.position;
                float distance = Vector2.Distance(currentPosition, otherPos);

                if (distance < manager.perceptionRadius)
                {
                    cohesionSum += otherPos;
                    separationSum += (currentPosition - otherPos).normalized;

                    SwarmAgent otherAgent = otherObj.GetComponent<SwarmAgent>();
                    if (otherAgent != null)
                    {
                        alignmentSum += otherAgent.currentVelocity;
                    }
                    neighborCount++;
                }

                // Rule 6: Overlapping Avoidance
                if (distance < manager.safetyDistance && distance > 0.0001f)
                {
                    Vector2 avoidDirection = currentPosition - otherPos;
                    overlappingAvoidanceSum += avoidDirection.normalized;
                }
            }
        }

        Vector2 acceleration = Vector2.zero;

        if (neighborCount > 0)
        {
            // Rule 1: Cohesion
            Vector2 cohesionForce = ((cohesionSum / neighborCount) - currentPosition) * manager.cohesionIntensity;

            // Rule 2: Separation
            Vector2 separationForce = (separationSum / neighborCount) * manager.separationIntensity;

            // Rule 3: Alignment
            Vector2 alignmentForce = (alignmentSum / neighborCount) * manager.alignmentIntensity;

            acceleration += cohesionForce + separationForce + alignmentForce;
        }

        // Rule 6 Application: Overlapping Avoidance
        Vector2 overlappingAvoidanceForce = overlappingAvoidanceSum * manager.overlappingAvoidanceIntensity;
        acceleration += overlappingAvoidanceForce;

        // Rule 4 Random movement rule
        float randX = Random.Range(-0.5f, 0.5f);
        float randY = Random.Range(-0.5f, 0.5f);
        Vector2 randomMovementForce = new Vector2(randX, randY) * manager.randomMovementIntensity;
        acceleration += randomMovementForce;

        // Common Fate (Attraction to global target)
        if (manager.commonFateTarget != null)
        {
            Vector2 targetPoint = (Vector2)manager.commonFateTarget.position;

            // If the target has a collider, attract to the closest point on the collider
            if (manager.commonFateCollider != null)
            {
                targetPoint = manager.commonFateCollider.ClosestPoint(currentPosition);
            }

            Vector2 directionToFate = (targetPoint - currentPosition).normalized;
            acceleration += directionToFate * manager.cohesionIntensity;
        }

        // Environmental Obstacle Avoidance (Repulsive potential field)
        if (manager.centralObstacle != null)
        {
            float distToObstacle = Vector2.Distance(currentPosition, manager.centralObstacle.position);
            if (distToObstacle < manager.obstacleAvoidanceRadius)
            {
                Vector2 avoidDirection = (currentPosition - (Vector2)manager.centralObstacle.position);
                // Inverse square law
                Vector2 avoidForce = (avoidDirection / Mathf.Max(distToObstacle * distToObstacle, 0.01f)) * manager.envObstacleAvoidanceIntensity;
                acceleration += avoidForce;
            }
        }

        // Rule 5: Friction
        Vector2 frictionForce = -currentVelocity * manager.frictionIntensity;
        acceleration += frictionForce;

        // Apply Dynamics Updates
        currentVelocity += acceleration * Time.deltaTime;

        // Limit to max speed
        if (currentVelocity.magnitude > manager.maxSpeed)
        {
            currentVelocity = currentVelocity.normalized * manager.maxSpeed;
        }

        transform.position += (Vector3)(currentVelocity * Time.deltaTime);

        // Face the direction of movement
        if (currentVelocity != Vector2.zero)
        {
            float angle = Mathf.Atan2(currentVelocity.y, currentVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    private void OnDrawGizmos()
    {
        if (cachedShowPerceptionRadius)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, cachedPerceptionRadius);
        }
    }
}