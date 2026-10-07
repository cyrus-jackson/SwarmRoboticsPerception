using System.Collections.Generic;
using UnityEngine;

/// <summary>Integrates swarm steering, constrains movement against the central obstacle, and tracks wall contacts.</summary>
public class SwarmAgent : MonoBehaviour
{
    private Vector2 currentVelocity;
    private Collider2D agentCollider;
    private readonly List<RaycastHit2D> obstacleHits = new List<RaycastHit2D>(16);
    private const float ObstacleSkin = 0.001f;
    private readonly HashSet<Collider2D> touchingWalls = new HashSet<Collider2D>();

    public bool HasHitWall { get; private set; }

    private void Awake()
    {
        agentCollider = GetComponent<Collider2D>();
    }

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

    // Random movement rule (Hénard et al. 2024, rule 4): X and Y are drawn in [-0.5, 0.5].
    // Sampled once per rendered frame and reused across the frame's substeps, so subdividing
    // the integration step does not change the noise magnitude per unit time.
    private Vector2 randomMovementDirection;

    /// <summary>Current velocity, exposed so trajectory capture can record it.</summary>
    public Vector2 Velocity => currentVelocity;

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
        Collider2D obstacle = manager.ObstacleCollider;
        bool usableObstacle = obstacle != null && obstacle.enabled && obstacle.gameObject.activeInHierarchy &&
            (obstacle.attachedRigidbody == null || obstacle.attachedRigidbody.simulated);
        if (usableObstacle) ResolveObstacleOverlap(obstacle, ref currentPosition);

        Vector2 cohesionSum = Vector2.zero;
        Vector2 separationSum = Vector2.zero;
        Vector2 alignmentSum = Vector2.zero;
        Vector2 overlappingAvoidanceSum = Vector2.zero;
        int neighborCount = 0;

        // Indexed rather than foreach, so the cached component arrays on the manager can be used.
        // Looking these up per pair per substep was the single most expensive thing in the sim.
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
                if (otherCollider != null && !manager.useDensificationV2)
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
                }

                // Rule 6: Overlapping Avoidance
                // The paper defines overlap avoidance over perceived neighbours only.
                if ((!manager.useDensificationV2 || perceptionDistance < manager.perceptionRadius) &&
                    centerDistance < manager.safetyDistance && centerDistance > 0.0001f)
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
        if (!manager.useDensificationV2 && manager.commonFateTarget != null)
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
            Collider2D obstacleCollider = obstacle;
            if (usableObstacle)
            {
                Vector2 closest = obstacleCollider.ClosestPoint(currentPosition);
                Vector2 away = currentPosition - closest;
                float distFromSurface = away.magnitude;

                if (distFromSurface > 0.0001f && distFromSurface < manager.obstacleAvoidanceRadius)
                {
                    // Inverse square law based on distance from the collider surface.
                    Vector2 avoidDirection = away.normalized;
                    float denom = Mathf.Max(distFromSurface * distFromSurface, 0.01f);
                    Vector2 avoidForce = (avoidDirection / denom) * manager.envObstacleAvoidanceIntensity;
                    acceleration += avoidForce;
                }
            }
            else if (obstacleCollider == null)
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

        Vector2 nextPosition = usableObstacle
            ? MoveAgainstObstacle(obstacle, currentPosition, currentVelocity * deltaTime)
            : currentPosition + currentVelocity * deltaTime;
        Vector3 position = transform.position;
        transform.position = new Vector3(nextPosition.x, nextPosition.y, position.z);

        // Face the direction of movement
        if (currentVelocity != Vector2.zero)
        {
            float angle = Mathf.Atan2(currentVelocity.y, currentVelocity.x) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.AngleAxis(angle, Vector3.forward);
        }
    }

    private void ResolveObstacleOverlap(Collider2D obstacle, ref Vector2 position)
    {
        if (agentCollider == null || !agentCollider.enabled || agentCollider.attachedRigidbody == null ||
            !agentCollider.attachedRigidbody.simulated ||
            obstacle.attachedRigidbody == null) return;

        for (int attempt = 0; attempt < 4; attempt++)
        {
            ColliderDistance2D distance = ObstacleDistance(obstacle, position);
            if (!distance.isValid || distance.distance >= ObstacleSkin) return;

            Vector2 direction = distance.isOverlapped
                ? distance.pointB - distance.pointA : distance.pointA - distance.pointB;
            if (direction.sqrMagnitude < 0.00000001f) direction = distance.normal;
            if (direction.sqrMagnitude < 0.00000001f) return;
            direction.Normalize();
            float correction = Mathf.Abs(distance.distance) + ObstacleSkin;
            Vector2 candidate = position + direction * correction;
            ColliderDistance2D candidateDistance = ObstacleDistance(obstacle, candidate);
            if (!candidateDistance.isValid) return;
            if (candidateDistance.distance < distance.distance)
            {
                direction = -direction;
                candidate = position + direction * correction;
            }
            position = candidate;
            RemoveInwardVelocity(direction);
            RegisterWallContact(obstacle, false, false);
        }
    }

    private ColliderDistance2D ObstacleDistance(Collider2D obstacle, Vector2 position)
    {
        Rigidbody2D body = agentCollider.attachedRigidbody;
        Vector2 bodyPosition = position + (Vector2)(body.transform.position - transform.position);
        Rigidbody2D obstacleBody = obstacle.attachedRigidbody;
        // Explicit poses keep queries correct during Euler substeps without syncing every agent transform.
        return agentCollider.Distance(bodyPosition, body.transform.eulerAngles.z, obstacle,
            obstacleBody.transform.position, obstacleBody.transform.eulerAngles.z);
    }

    private Vector2 MoveAgainstObstacle(Collider2D obstacle, Vector2 position, Vector2 movement)
    {
        if (agentCollider == null || !agentCollider.enabled || agentCollider.attachedRigidbody == null ||
            !agentCollider.attachedRigidbody.simulated) return position + movement;

        ContactFilter2D filter = new ContactFilter2D { useTriggers = true };
        filter.SetLayerMask(1 << obstacle.gameObject.layer);
        // Steering forces are soft; sweep the full collider so a substep cannot cross a thin obstacle.
        for (int attempt = 0; attempt < 3 && movement.sqrMagnitude > 0.00000001f; attempt++)
        {
            float length = movement.magnitude;
            Vector2 direction = movement / length;
            Rigidbody2D body = agentCollider.attachedRigidbody;
            Vector2 bodyPosition = position + (Vector2)(body.transform.position - transform.position);
            int count = agentCollider.Cast(bodyPosition, body.transform.eulerAngles.z, direction, filter, obstacleHits, length + ObstacleSkin);
            bool blocked = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit2D hit = obstacleHits[i];
                if (hit.collider != obstacle || Vector2.Dot(movement, hit.normal) >= 0f) continue;
                float travel = Mathf.Clamp(hit.distance - ObstacleSkin, 0f, length);
                position += direction * travel;
                movement -= direction * travel;
                movement -= hit.normal * Mathf.Min(0f, Vector2.Dot(movement, hit.normal));
                RemoveInwardVelocity(hit.normal);
                RegisterWallContact(obstacle, false, false);
                blocked = true;
                break;
            }
            if (!blocked)
            {
                position += movement;
                break;
            }
        }
        ResolveObstacleOverlap(obstacle, ref position);
        return position;
    }

    private void RemoveInwardVelocity(Vector2 outwardNormal)
    {
        currentVelocity -= outwardNormal * Mathf.Min(0f, Vector2.Dot(currentVelocity, outwardNormal));
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

    private void RegisterWallContact(Collider2D collider, bool logHit, bool rememberContact = true)
    {
        if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy) return;

        for (Transform wall = collider.transform; wall != null; wall = wall.parent)
        {
            if (wall.gameObject.CompareTag("Wall") ||
                wall.name.IndexOf("wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (rememberContact) touchingWalls.Add(collider);
                HasHitWall = true;
                if (logHit) Debug.Log($"{gameObject.name} hit the wall: {collider.gameObject.name}");
                return;
            }
        }
    }
}
