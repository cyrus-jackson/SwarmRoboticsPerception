using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Splits the swarm into the groups its agents can actually communicate within.
///
/// Follows the definition in Hénard et al. 2024, "Human perception of swarm fragmentation":
/// agents are nodes, an agent's neighbours are adjacent nodes, and the swarm is fragmented when
/// that graph has two or more connected components — "there is no coordination or communication
/// possible between the two groups".
///
/// The edge test is deliberately the same one <see cref="SwarmAgent"/> uses to pick neighbours:
/// the distance from one agent's centre to the *edge* of the other's collider, compared against
/// the perception radius. Measuring clusters with a different rule than the agents use to steer
/// would report a fragmentation the swarm itself does not experience, or miss one it does.
///
/// A lone agent counts as a group of one. The paper treats losing a single robot as fragmentation
/// ("the swarm loses one or more robots or splits into several groups"), and reporting sizes rather
/// than a bare count leaves that judgement to the analysis.
/// </summary>
public static class SwarmClusterMetrics
{
    // Reused across frames so a per-frame sample allocates nothing.
    private static int[] parent = new int[0];
    private static int[] rank = new int[0];
    private static int[] size = new int[0];
    private static Vector2[] positions = new Vector2[0];
    private static float[] radii = new float[0];

    /// <summary>
    /// Connected component sizes, largest first.
    ///
    /// <paramref name="sizesOut"/> is filled rather than returned so a caller sampling every frame
    /// can keep one list. Returns the number of components, which is also sizesOut.Count.
    /// </summary>
    public static int ClusterSizes(GameObject[] agents, float perceptionRadius,
                                   List<int> sizesOut, Collider2D[] colliders = null)
    {
        if (sizesOut == null) sizesOut = new List<int>();
        sizesOut.Clear();

        int n = CollectAgents(agents, colliders);
        if (n == 0) return 0;

        EnsureCapacity(n);
        for (int i = 0; i < n; i++) { parent[i] = i; rank[i] = 0; size[i] = 1; }

        // O(n^2) pair test. n is 40 here, so 780 pairs per sample — cheaper than the neighbour
        // loop the simulation already runs every substep, and this runs once per captured frame.
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (AreNeighbours(i, j, perceptionRadius)) Union(i, j);
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (Find(i) == i) sizesOut.Add(size[i]);
        }

        sizesOut.Sort((a, b) => b.CompareTo(a));
        return sizesOut.Count;
    }

    /// <summary>Number of connected components, when the sizes themselves are not needed.</summary>
    public static int ClusterCount(GameObject[] agents, float perceptionRadius,
                                   Collider2D[] colliders = null)
    {
        List<int> scratch = new List<int>();
        return ClusterSizes(agents, perceptionRadius, scratch, colliders);
    }

    /// <summary>
    /// True when an agent perceives another, matching SwarmAgent's own test.
    ///
    /// SwarmAgent compares its centre against <c>otherCollider.ClosestPoint(...)</c>, which for a
    /// circle of radius r sitting outside is the centre distance minus r. Using the stored radius
    /// reproduces that without a physics query per pair, and stays symmetric: agents share a
    /// radius, so i seeing j implies j seeing i.
    /// </summary>
    private static bool AreNeighbours(int i, int j, float perceptionRadius)
    {
        float centreDistance = Vector2.Distance(positions[i], positions[j]);
        float edgeDistance = centreDistance - Mathf.Max(radii[i], radii[j]);
        return edgeDistance < perceptionRadius;
    }

    private static int CollectAgents(GameObject[] agents, Collider2D[] colliders)
    {
        if (agents == null) return 0;

        if (positions.Length < agents.Length)
        {
            positions = new Vector2[agents.Length];
            radii = new float[agents.Length];
        }

        int n = 0;
        for (int i = 0; i < agents.Length; i++)
        {
            GameObject agent = agents[i];
            if (agent == null) continue;

            positions[n] = agent.transform.position;

            // Prefer the cached collider array; fall back to a lookup only when it is absent.
            Collider2D collider = (colliders != null && i < colliders.Length) ? colliders[i] : null;
            if (collider == null) collider = agent.GetComponent<Collider2D>();

            radii[n] = collider != null
                ? Mathf.Max(collider.bounds.extents.x, collider.bounds.extents.y)
                : 0f;

            n++;
        }

        return n;
    }

    private static void EnsureCapacity(int n)
    {
        if (parent.Length >= n) return;
        parent = new int[n];
        rank = new int[n];
        size = new int[n];
    }

    private static int Find(int x)
    {
        while (parent[x] != x)
        {
            parent[x] = parent[parent[x]];   // path halving
            x = parent[x];
        }
        return x;
    }

    private static void Union(int a, int b)
    {
        int ra = Find(a);
        int rb = Find(b);
        if (ra == rb) return;

        if (rank[ra] < rank[rb]) { int t = ra; ra = rb; rb = t; }

        parent[rb] = ra;
        size[ra] += size[rb];
        if (rank[ra] == rank[rb]) rank[ra]++;
    }

    // ------------------------------------------------------------------ reporting

    /// <summary>Compact description for a debug line or a UI readout, e.g. "3 groups: 21, 17, 2".</summary>
    public static string Describe(List<int> sizes)
    {
        if (sizes == null || sizes.Count == 0) return "no agents";
        if (sizes.Count == 1) return $"1 group of {sizes[0]}";

        return $"{sizes.Count} groups: {string.Join(", ", sizes)}";
    }

    /// <summary>Agents in the largest group as a fraction of all of them. 1 means intact.</summary>
    public static float LargestFraction(List<int> sizes)
    {
        if (sizes == null || sizes.Count == 0) return 0f;

        int total = 0;
        for (int i = 0; i < sizes.Count; i++) total += sizes[i];
        return total > 0 ? sizes[0] / (float)total : 0f;
    }

    /// <summary>Groups holding at least <paramref name="minSize"/> agents, ignoring stragglers.</summary>
    public static int CountAtLeast(List<int> sizes, int minSize)
    {
        if (sizes == null) return 0;

        int count = 0;
        for (int i = 0; i < sizes.Count; i++)
        {
            if (sizes[i] >= minSize) count++;
        }
        return count;
    }
}
