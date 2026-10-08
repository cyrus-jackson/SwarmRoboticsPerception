using System.Collections.Generic;
using UnityEngine;

/// <summary>Integrates swarm steering and tracks each agent's contacts with arena walls.</summary>
public class SwarmAgent : MonoBehaviour
{
    private Vector2 currentVelocity;
    private readonly HashSet<Collider2D> touchingWalls = new HashSet<Collider2D>();

    public bool HasHitWall { get; private set; }

    public void ResetWallContactTracking()
    {
        HasHitWall = false;
        // A sleeping body may receive no stay callback, so retain contacts present at capture start.
        foreach (Collider2D wall in touchingWalls)
        {
            if (wall != null && wall.enabled && wall.gameObject.activeInHierarchy)
            {
                HasHitWall = true;
                break;
            }
        }
    }

    private Vector2 randomMovementDirection;

    /// <summary>Current velocity, exposed so trajectory capture can record it.</summary>
    public Vector2 Velocity => currentVelocity;

    /// <summary>
    /// Draws the random movement vector in [-0.5, 0.5] for this frame's movement update.
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

        GameObject[] others = manager.agents;
        Collider2D[] otherColliders = manager.AgentColliders;
        SwarmAgent[] otherAgents = manager.AgentScripts;

        for (int i = 0; i < others.Length; i++)
        {
            GameObject otherObj = others[i];
            if (otherObj != gameObject && otherObj != null)
            {
                Vector2 otherPos = otherObj.transform.position;
                float centerDistance = Vector2.Distance(currentPosition, otherPos);

                // Calculate distance to the edge of the other agent (using its collider if available)
                float perceptionDistance = centerDistance;
                Collider2D otherCollider = i < otherColliders.Length ? otherColliders[i] : null;
                if (otherCollider != null)
                {
                    perceptionDistance = Vector2.Distance(currentPosition, otherCollider.ClosestPoint(currentPosition));
                }

                if (perceptionDistance < manager.perceptionRadius)
                {
                    cohesionSum += otherPos;
                    separationSum += (currentPosition - otherPos).normalized;

                    SwarmAgent otherAgent = i < otherAgents.Length ? otherAgents[i] : null;
                    if (otherAgent != null)
                    {
                        alignmentSum += otherAgent.currentVelocity;
                    }
                    neighborCount++;

                    // Rule 6 uses only perceived neighbours, as defined by A being a subset of N.
                    if (centerDistance < manager.safetyDistance && centerDistance > 0.0001f)
                    {
                        Vector2 avoidDirection = currentPosition - otherPos;
                        overlappingAvoidanceSum += avoidDirection.normalized;
                    }
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

        Collider2D obstacleCollider = manager.ObstacleCollider;
        if (obstacleCollider != null)
        {
            Vector2 away = currentPosition - obstacleCollider.ClosestPoint(currentPosition);
            float distance = away.magnitude;
            if (distance > 0.0001f && distance < manager.obstacleAvoidanceRadius)
            {
                float squaredDistance = Mathf.Max(distance * distance, 0.01f);
                acceleration += away.normalized * (manager.envObstacleAvoidanceIntensity / squaredDistance);
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
        RegisterWallContact(collision.collider, true);
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        RegisterWallContact(collision.collider, false);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        touchingWalls.Remove(collision.collider);
    }

    private void OnTriggerEnter2D(Collider2D collider)
    {
        RegisterWallContact(collider, true);
    }

    private void OnTriggerStay2D(Collider2D collider)
    {
        RegisterWallContact(collider, false);
    }

    private void OnTriggerExit2D(Collider2D collider)
    {
        touchingWalls.Remove(collider);
    }

    private void RegisterWallContact(Collider2D collider, bool logHit)
    {
        if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return;

        for (Transform wall = collider.transform; wall != null; wall = wall.parent)
        {
            if (wall.gameObject.CompareTag("Wall") ||
                wall.name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                touchingWalls.Add(collider);
                HasHitWall = true;
                if (logHit) Debug.Log($"{gameObject.name} hit the wall: {collider.gameObject.name}");
                return;
            }
        }
    }
}
