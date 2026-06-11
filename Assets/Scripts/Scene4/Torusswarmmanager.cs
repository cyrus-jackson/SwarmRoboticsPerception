// TorusSwarmManager.cs
// Implements the swarm model from:
//   Brown, Kerman & Goodrich (2014). "Human-Swarm Interactions Based on Managing Attractors."
//   HRI '14, Bielefeld, Germany. https://doi.org/10.1145/2559636.2559661
//
// Agent dynamics (Eq. 1):
//   ẋᵢ = s·cos(θᵢ)
//   ẏᵢ = s·sin(θᵢ)
//   θ̇ᵢ = ωᵢ
//
// Three interaction zones (Eqs. 2–4):
//   Repulsion  nᵢʳ = { j : ‖cᵢ−cⱼ‖ ≤ Rᵣ, aᵢⱼ = 1 }
//   Orientation nᵢᵒ = { j : ‖cᵢ−cⱼ‖ ≤ Rₒ, aᵢⱼ = 1 }
//   Attraction  nᵢᵃ = { j : aᵢⱼ = 1 }          (all probabilistically visible agents)
//
// Probabilistic sensing (Section 2):
//   p_ij = min(1, 1/d_ij)   — Bernoulli draw each tick
//
// Default paper parameters for torus/flock tipping point:
//   N=100, s=5, k=0.5, Rₒ=8, Rᵣ=1  (Table footnote 1, p. 91)

using UnityEngine;

public class TorusSwarmManager : MonoBehaviour
{
    // -----------------------------------------------------------------------
    // Inspector fields
    // -----------------------------------------------------------------------

    [Header("Agents")]
    [Tooltip("All agent GameObjects that carry a TorusSwarmAgent component.")]
    public GameObject[] agents;

    [Header("Model Parameters — Brown et al. 2014")]
    [Tooltip("Constant agent speed  s  (paper default: 5).")]
    public float agentSpeed = 5f;

    [Tooltip("Angular velocity gain  k  (paper default: 0.5).")]
    public float angularGain = 0.5f;

    [Tooltip("Radius of repulsion  Rᵣ  (paper default: 1). Must satisfy Rᵣ < Rₒ.")]
    public float repulsionRadius = 1f;

    [Tooltip("Radius of orientation  Rₒ  (paper default: 8). Must satisfy Rₒ > Rᵣ.")]
    public float orientationRadius = 8f;

    [Header("Initialization")]
    [Tooltip(
        "Torus  : each agent's heading is set tangent to its position vector — " +
        "θᵢ = atan2(yᵢ, xᵢ) + π/2  (counter-clockwise, Section 4.2).\n" +
        "Flock  : all agents start with θᵢ = 0 (Section 4.2).")]
    public InitMode initMode = InitMode.Torus;

    public enum InitMode { Torus, Flock }

    [Header("Visualization")]
    public bool showRepulsionRadius = false;
    public bool showOrientationRadius = false;

    // -----------------------------------------------------------------------
    // Unity lifecycle
    // -----------------------------------------------------------------------

    void Start()
    {
        InitializeAgentHeadings();
    }

    void Update()
    {
        foreach (GameObject agentObj in agents)
        {
            if (agentObj == null) continue;
            TorusSwarmAgent agent = agentObj.GetComponent<TorusSwarmAgent>();
            if (agent != null)
                agent.UpdateAgent(this);
        }
    }

    // -----------------------------------------------------------------------
    // Heading initialisation
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets initial headings according to the chosen mode.
    ///
    /// Torus:  θᵢ = atan2(yᵢ, xᵢ) + π/2
    ///   Each agent's velocity is tangent to the circle centred on the origin,
    ///   giving a counter-clockwise rotation — the initial condition used in
    ///   Section 4.2 ("each agent was given a random initial position cᵢ with
    ///   initial heading θᵢ = atan2(cᵢˣ, cᵢʸ) + π/2 to form a counter-clockwise
    ///   torus").  Note: the paper notation atan2(x, y) is equivalent to
    ///   atan2(y, x) + π/2 only under a 90° convention; here we use the
    ///   geometrically correct tangent: atan2(y, x) + π/2.
    ///
    /// Flock:  θᵢ = 0  ∀i  (all agents face the +x direction, Section 4.2).
    /// </summary>
    void InitializeAgentHeadings()
    {
        foreach (GameObject agentObj in agents)
        {
            if (agentObj == null) continue;
            TorusSwarmAgent agent = agentObj.GetComponent<TorusSwarmAgent>();
            if (agent == null) continue;

            if (initMode == InitMode.Torus)
            {
                Vector2 pos = agentObj.transform.position;
                // Tangent to the position vector → counter-clockwise orbit
                agent.heading = Mathf.Atan2(pos.y, pos.x) + Mathf.PI / 2f;
            }
            else // Flock
            {
                agent.heading = 0f;
            }
        }
    }
}