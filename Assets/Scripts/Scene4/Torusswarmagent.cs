// TorusSwarmAgent.cs
// Implements per-agent dynamics from:
//   Brown, Kerman & Goodrich (2014). "Human-Swarm Interactions Based on Managing Attractors."
//   HRI '14, Bielefeld, Germany. https://doi.org/10.1145/2559636.2559661
//
// ── Per-tick pipeline ──────────────────────────────────────────────────────
//
//  1. For each other agent j:
//       Draw Bernoulli(p_ij),  p_ij = min(1, 1/d_ij)           [Section 2]
//       If visible, classify into repulsion / orientation / attraction zones.
//
//  2. Compute zone vectors:
//       uᵢʳ = −Σ_{nᵢʳ} (cⱼ−cᵢ) / ‖cⱼ−cᵢ‖²                  [Eq. 5]
//       uᵢᵒ = normalize( vᵢ + Σ_{nᵢᵒ} vⱼ )                   [Eq. 6]
//       uᵢᵃ = normalize( Σ_{nᵢᵃ} (cⱼ−cᵢ) )                   [Eq. 7]
//
//  3. Desired direction:   uᵢ = uᵢʳ + uᵢᵒ + uᵢᵃ
//
//  4. Angular velocity:    ωᵢ = k · wrap(atan2(uᵢʸ, uᵢˣ) − θᵢ) [Eq. 8]
//       where wrap(·) constrains to [−π, π].
//
//  5. Integrate (discrete Euler, Eq. 1):
//       θᵢ ← θᵢ + ωᵢ · Δt
//       pos ← pos + s · [cos θᵢ, sin θᵢ] · Δt
//
// ──────────────────────────────────────────────────────────────────────────

using UnityEngine;

public class TorusSwarmAgent : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // State
    // -----------------------------------------------------------------------

    /// <summary>Current heading angle θ (radians).</summary>
    [HideInInspector] public float heading;

    // -----------------------------------------------------------------------
    // Cached values for OnDrawGizmos
    // -----------------------------------------------------------------------
    private float _repulsionRadius;
    private float _orientationRadius;
    private bool _showRepulsion;
    private bool _showOrientation;

    // -----------------------------------------------------------------------
    // Main update — called by TorusSwarmManager every frame
    // -----------------------------------------------------------------------

    public void UpdateAgent(TorusSwarmManager manager)
    {
        // Cache for gizmos
        _repulsionRadius = manager.repulsionRadius;
        _orientationRadius = manager.orientationRadius;
        _showRepulsion = manager.showRepulsionRadius;
        _showOrientation = manager.showOrientationRadius;

        Vector2 ci = transform.position;

        // ── Accumulators ────────────────────────────────────────────────────

        // Eq. 5 — repulsion (inverse-square, so it naturally dominates at short range)
        Vector2 uRepulsion = Vector2.zero;

        // Eq. 6 — orientation: numerator starts with own velocity vᵢ
        Vector2 orientationSum = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading));

        // Eq. 7 — attraction: summed displacement toward all visible agents
        Vector2 attractionSum = Vector2.zero;
        bool hasAttractionNeighbor = false;

        // ── Iterate over all other agents ───────────────────────────────────

        foreach (GameObject otherObj in manager.agents)
        {
            if (otherObj == gameObject || otherObj == null) continue;

            Vector2 cj = otherObj.transform.position;
            float dij = Vector2.Distance(ci, cj);
            if (dij < 1e-4f) continue; // skip coincident agents

            // ── Probabilistic sensing (Section 2) ───────────────────────────
            // aᵢⱼ ~ Bernoulli(p_ij),  p_ij = min(1, 1/d_ij)
            float pij = Mathf.Min(1f, 1f / dij);
            if (Random.value > pij) continue; // agent j not perceived this step

            // ── Zone 1: Repulsion (Eq. 5) ────────────────────────────────────
            // nᵢʳ = { j : ‖cᵢ−cⱼ‖ ≤ Rᵣ, aᵢⱼ = 1 }
            // uᵢʳ = −Σ (cⱼ−cᵢ) / ‖cⱼ−cᵢ‖²
            if (dij <= manager.repulsionRadius)
            {
                // Minus sign because repulsion pushes away from neighbour
                uRepulsion -= (cj - ci) / (dij * dij);
            }

            // ── Zone 2: Orientation (Eq. 6) ──────────────────────────────────
            // nᵢᵒ = { j : ‖cᵢ−cⱼ‖ ≤ Rₒ, aᵢⱼ = 1 }   (includes repulsion zone since Rₒ > Rᵣ)
            if (dij <= manager.orientationRadius)
            {
                TorusSwarmAgent other = otherObj.GetComponent<TorusSwarmAgent>();
                if (other != null)
                {
                    orientationSum += new Vector2(
                        Mathf.Cos(other.heading),
                        Mathf.Sin(other.heading));
                }
            }

            // ── Zone 3: Attraction (Eq. 7) ───────────────────────────────────
            // nᵢᵃ = { j : aᵢⱼ = 1 }  — no hard radius; all probabilistically visible agents
            attractionSum += (cj - ci);
            hasAttractionNeighbor = true;
        }

        // ── Build normalised zone vectors ────────────────────────────────────

        // Eq. 5: uᵢʳ already accumulated as inverse-square; pass through unnormalised
        // (the inverse-square magnitude is essential — it makes repulsion dominant at
        //  close range without requiring an explicit priority switch).
        Vector2 ur = uRepulsion;

        // Eq. 6: uᵢᵒ = normalize( vᵢ + Σ vⱼ )
        Vector2 uo = orientationSum.magnitude > 1e-4f
            ? orientationSum.normalized
            : Vector2.zero;

        // Eq. 7: uᵢᵃ = normalize( Σ (cⱼ−cᵢ) )
        Vector2 ua = (hasAttractionNeighbor && attractionSum.magnitude > 1e-4f)
            ? attractionSum.normalized
            : Vector2.zero;

        // ── Desired direction ────────────────────────────────────────────────
        // u = uᵢʳ + uᵢᵒ + uᵢᵃ  (unnormalised sum; only atan2 direction matters)
        Vector2 u = ur + uo + ua;

        // ── Angular velocity (Eq. 8) ─────────────────────────────────────────
        // ωᵢ = k · (atan2(uᵢʸ, uᵢˣ) − θᵢ),  limited to [−π, π]
        float omega = 0f;
        if (u.magnitude > 1e-4f)
        {
            float targetAngle = Mathf.Atan2(u.y, u.x);
            float diff = WrapToPi(targetAngle - heading);
            omega = manager.angularGain * diff;
        }

        // ── Discrete-time Euler integration (Eq. 1) ─────────────────────────
        // θᵢ(t) = θᵢ(t−1) + ωᵢ · Δt
        heading = WrapToPi(heading + omega * Time.deltaTime);

        // pos(t) = pos(t−1) + s · [cos θᵢ, sin θᵢ] · Δt
        Vector2 velocity = new Vector2(Mathf.Cos(heading), Mathf.Sin(heading))
                           * manager.agentSpeed;
        transform.position += (Vector3)(velocity * Time.deltaTime);

        // ── Visual rotation ──────────────────────────────────────────────────
        transform.rotation = Quaternion.AngleAxis(heading * Mathf.Rad2Deg, Vector3.forward);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Wraps an angle in radians to the interval (−π, π].</summary>
    private static float WrapToPi(float angle)
    {
        // Standard modular arithmetic approach — faster than a while loop
        angle = (angle + Mathf.PI) % (2f * Mathf.PI);
        if (angle < 0f) angle += 2f * Mathf.PI;
        return angle - Mathf.PI;
    }

    // -----------------------------------------------------------------------
    // Editor gizmos
    // -----------------------------------------------------------------------

    private void OnDrawGizmos()
    {
        if (_showRepulsion)
        {
            Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, _repulsionRadius);
        }
        if (_showOrientation)
        {
            Gizmos.color = new Color(0.2f, 0.5f, 1f, 0.3f);
            Gizmos.DrawWireSphere(transform.position, _orientationRadius);
        }
    }
}