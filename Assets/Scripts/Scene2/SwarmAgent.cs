using System.Collections.Generic;
using UnityEngine;

public class SwarmAgent : MonoBehaviour
{
    private Vector2 currentVelocity;

    // Random movement rule (Hénard et al. 2024, rule 4): X and Y are drawn in [-0.5, 0.5].
    // Sampled once per rendered frame and reused across the frame's substeps, so subdividing
    // the integration step does not change the noise magnitude per unit time.
    private Vector2 randomMovementDirection;

    /// <summary>
    /// Draws a new random movement vector. Called once per rendered frame by SwarmManager,
    /// before that frame's integration substeps.
    /// </summary>
    public void SampleRandomMovement()
    {
        randomMovementDirection = new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f));
    }

    /// <summary>
    /// One explicit Euler step of the paper's model:
    ///   v(t) = v(t-1) + a(t) * dt
    ///   p(t) = p(t-1) + v(t) * dt
    /// </summary>
    public void UpdateAgent(SwarmManager manager, float deltaTime)
    {
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
                float centerDistance = Vector2.Distance(currentPosition, otherPos);

                // Calculate distance to the edge of the other agent (using its collider if available)
                float perceptionDistance = centerDistance;
                Collider2D otherCollider = otherObj.GetComponent<Collider2D>();
                if (otherCollider != null)
                {
                    perceptionDistance = Vector2.Distance(currentPosition, otherCollider.ClosestPoint(currentPosition));
                }

                if (perceptionDistance < manager.perceptionRadius)
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
                if (centerDistance < manager.safetyDistance && centerDistance > 0.0001f)
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

        // Rule 4 Random movement rule (vector sampled once per frame, see SampleRandomMovement)
        Vector2 randomMovementForce = randomMovementDirection * manager.randomMovementIntensity;
        acceleration += randomMovementForce;

        // Common Fate (Attraction to global target)
        if (manager.commonFateTarget != null)
        {
            Vector2 targetPoint = (Vector2)manager.commonFateTarget.position;

            // If the target has a collider, attract to the closest point on the collider
            if (manager.commonFateCollider != null)
            {
                // Debug.LogWarning("Attracting to common fate collider: " + manager.commonFateCollider.name);
                targetPoint = manager.commonFateCollider.ClosestPoint(currentPosition);
            }

            Vector2 directionToFate = (targetPoint - currentPosition).normalized;
            acceleration += directionToFate * manager.cohesionIntensity;
        }

        // Environmental Obstacle Avoidance (Repulsive potential field)
        if (manager.centralObstacle != null)
        {
            Collider2D obstacleCollider = manager.centralObstacle.GetComponent<Collider2D>();
            if (obstacleCollider != null)
            {
                Vector2 closest = obstacleCollider.ClosestPoint(currentPosition);
                Vector2 away = currentPosition - closest;
                float distFromSurface = away.magnitude;

                // If we're inside the collider, ClosestPoint returns the point itself.
                if (distFromSurface <= 0.0001f)
                {
                    away = currentPosition - (Vector2)obstacleCollider.bounds.center;
                    distFromSurface = away.magnitude;

                    if (distFromSurface <= 0.0001f)
                    {
                        away = Random.insideUnitCircle;
                        distFromSurface = away.magnitude;
                    }
                }

                if (distFromSurface < manager.obstacleAvoidanceRadius)
                {
                    // Inverse square law based on distance from the collider surface.
                    Vector2 avoidDirection = away.normalized;
                    float denom = Mathf.Max(distFromSurface * distFromSurface, 0.01f);
                    Vector2 avoidForce = (avoidDirection / denom) * manager.envObstacleAvoidanceIntensity;
                    acceleration += avoidForce;
                }
            }
            else
            {
                // Fallback: approximate by distance to transform position.
                float distToObstacle = Vector2.Distance(currentPosition, manager.centralObstacle.position);
                if (distToObstacle < manager.obstacleAvoidanceRadius)
                {
                    Vector2 avoidDirection = (currentPosition - (Vector2)manager.centralObstacle.position);
                    Vector2 avoidForce = (avoidDirection / Mathf.Max(distToObstacle * distToObstacle, 0.01f)) * manager.envObstacleAvoidanceIntensity;
                    acceleration += avoidForce;
                }
            }
        }

        // Rule 5: Friction
        Vector2 frictionForce = -currentVelocity * manager.frictionIntensity;
        acceleration += frictionForce;

        // Apply Dynamics Updates
        currentVelocity += acceleration * deltaTime;

        // Limit to max speed
        if (currentVelocity.magnitude > manager.maxSpeed)
        {
            currentVelocity = currentVelocity.normalized * manager.maxSpeed;
        }

        transform.position += (Vector3)(currentVelocity * deltaTime);

        // Face the direction of movement
        if (currentVelocity != Vector2.zero)
        {
            float angle = Mathf.Atan2(currentVelocity.y, currentVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.name.ToLower().Contains("wall") || collision.gameObject.CompareTag("Wall"))
        {
            Debug.Log($"{gameObject.name} hit the wall: {collision.gameObject.name}");
        }
    }

    private void OnTriggerEnter2D(Collider2D collider)
    {
        if (collider.gameObject.name.ToLower().Contains("wall") || collider.gameObject.CompareTag("Wall"))
        {
            Debug.Log($"{gameObject.name} hit the wall: {collider.gameObject.name}");
        }
    }
}