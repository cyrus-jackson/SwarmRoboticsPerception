using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Clusters the swarm by where the agents actually are, without reference to what they can see.
///
/// <see cref="SwarmClusterMetrics"/> answers a different question: it joins agents that can perceive
/// one another, which is the paper's definition of a group and the right measure for communication.
/// But it ties the measurement to the perception radius, so sweeping that radius changes both the
/// swarm's behaviour and the definition of a cluster. At a small radius the same arrangement of
/// agents reads as twenty-odd groups by perception and about four by eye.
///
/// These two measure the second thing — how many clumps are on screen — so a viewer's impression can
/// be compared against what the agents can communicate across.
///
///   DBSCAN    fixed neighbourhood radius, with a minimum number of neighbours so a thin chain of
///             stragglers cannot bridge two separate clumps. One parameter to choose, in world
///             units, independent of perception.
///
///   AutoEps   picks that radius from the agent spacing in the frame, so nothing has to be set by
///             hand and the measure adapts as the swarm spreads.
///
/// Agents are labelled individually and -1 means noise: an agent DBSCAN puts in no cluster at all,
/// which the perception graph can never produce.
/// </summary>
public static class SwarmSpatialClustering
{
    /// <summary>Label used for points that belong to no cluster.</summary>
    public const int Noise = -1;

    /// <summary>Result of a clustering pass, reused between frames.</summary>
    public class Result
    {
        /// <summary>Cluster index per agent, or <see cref="Noise"/>. Indexed like the agent array.</summary>
        public readonly List<int> labels = new List<int>();

        /// <summary>Agent count in each cluster, largest first.</summary>
        public readonly List<int> sizes = new List<int>();

        /// <summary>Number of clusters found, excluding noise.</summary>
        public int ClusterCount => sizes.Count;

        /// <summary>Agents assigned to no cluster.</summary>
        public int NoiseCount { get; internal set; }

        internal void Clear()
        {
            labels.Clear();
            sizes.Clear();
            NoiseCount = 0;
        }

        /// <summary>Recomputes sizes and the noise count from the labels.</summary>
        internal void Summarise(int clusterCount)
        {
            sizes.Clear();
            NoiseCount = 0;

            int[] counts = new int[Mathf.Max(clusterCount, 0)];
            foreach (int label in labels)
            {
                if (label < 0) NoiseCount++;
                else if (label < counts.Length) counts[label]++;
            }

            foreach (int count in counts)
            {
                if (count > 0) sizes.Add(count);
            }

            sizes.Sort((a, b) => b.CompareTo(a));
        }
    }

    // ------------------------------------------------------------------ DBSCAN

    /// <summary>
    /// Density clustering at a fixed radius.
    ///
    /// A point with at least <paramref name="minPoints"/> neighbours within <paramref name="eps"/>
    /// (counting itself) is a core point; core points within eps of each other join, and non-core
    /// points on the edge of a cluster are pulled in but do not extend it further. Everything else
    /// is noise.
    ///
    /// minPoints above 1 is what separates this from the perception graph: a single agent midway
    /// between two clumps no longer welds them together.
    /// </summary>
    public static void DBSCAN(IList<Vector2> points, float eps, int minPoints, Result result)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        result.Clear();

        int n = points != null ? points.Count : 0;
        for (int i = 0; i < n; i++) result.labels.Add(Noise);
        if (n == 0) return;

        float epsSqr = eps * eps;
        bool[] visited = new bool[n];
        List<int> neighbours = new List<int>();
        List<int> queue = new List<int>();
        int clusterCount = 0;

        for (int i = 0; i < n; i++)
        {
            if (visited[i]) continue;
            visited[i] = true;

            RegionQuery(points, i, epsSqr, neighbours);
            if (neighbours.Count < minPoints) continue;   // stays noise unless a cluster claims it

            int cluster = clusterCount++;
            result.labels[i] = cluster;

            queue.Clear();
            queue.AddRange(neighbours);

            for (int q = 0; q < queue.Count; q++)
            {
                int j = queue[q];

                // A point reached from a core point joins the cluster even if it is not core itself.
                if (result.labels[j] == Noise) result.labels[j] = cluster;

                if (visited[j]) continue;
                visited[j] = true;

                RegionQuery(points, j, epsSqr, neighbours);
                if (neighbours.Count < minPoints) continue;   // border point: joins, but does not expand

                foreach (int k in neighbours)
                {
                    if (!queue.Contains(k)) queue.Add(k);
                }
            }
        }

        result.Summarise(clusterCount);
    }

    private static void RegionQuery(IList<Vector2> points, int index, float epsSqr, List<int> into)
    {
        into.Clear();
        Vector2 p = points[index];

        for (int i = 0; i < points.Count; i++)
        {
            if ((points[i] - p).sqrMagnitude <= epsSqr) into.Add(i);
        }
    }

    // ------------------------------------------------------------------ choosing eps

    /// <summary>
    /// Picks a neighbourhood radius from the arrangement itself, so nothing has to be set by hand.
    ///
    /// Takes each agent's distance to its <paramref name="k"/>th nearest neighbour, sorts them, and
    /// returns a multiple of the median. Within a clump those distances are all roughly the agent
    /// spacing; the multiple is the factor by which a gap has to exceed normal spacing before it
    /// counts as a gap between clumps.
    ///
    /// This is the honest replacement for HDBSCAN here. HDBSCAN chooses the scale by finding the
    /// branches of the density hierarchy that survive the widest range of thresholds, which is more
    /// principled but a much larger piece of machinery; this reads the one scale that matters for a
    /// swarm of uniformly sized agents, and can be checked by eye against the gizmo.
    /// </summary>
    public static float AutoEps(IList<Vector2> points, int k = 3, float spacingMultiple = 4f)
    {
        int n = points != null ? points.Count : 0;
        if (n < 2) return 0f;

        k = Mathf.Clamp(k, 1, n - 1);

        float[] kth = new float[n];
        float[] row = new float[n];

        for (int i = 0; i < n; i++)
        {
            int count = 0;
            for (int j = 0; j < n; j++)
            {
                if (j != i) row[count++] = Vector2.Distance(points[i], points[j]);
            }
            Array.Sort(row, 0, count);
            kth[i] = row[Mathf.Min(k - 1, count - 1)];
        }

        Array.Sort(kth);
        float median = n % 2 == 1 ? kth[n / 2] : 0.5f * (kth[n / 2 - 1] + kth[n / 2]);

        return median * Mathf.Max(1f, spacingMultiple);
    }

    // ------------------------------------------------------------------ helpers

    /// <summary>Agent positions as a flat list, so the clusterers do not need the GameObjects.</summary>
    public static void CollectPositions(GameObject[] agents, List<Vector2> into)
    {
        into.Clear();
        if (agents == null) return;

        foreach (GameObject agent in agents)
        {
            if (agent == null) continue;
            Vector3 p = agent.transform.position;
            into.Add(new Vector2(p.x, p.y));
        }
    }

    /// <summary>Short readout, e.g. "4 clusters: 18, 11, 7, 3 (1 noise)".</summary>
    public static string Describe(Result result)
    {
        if (result == null || result.labels.Count == 0) return "no agents";
        if (result.ClusterCount == 0) return $"no clusters ({result.NoiseCount} noise)";

        string noise = result.NoiseCount > 0 ? $"  ({result.NoiseCount} noise)" : "";
        return $"{result.ClusterCount} clusters: {string.Join(", ", result.sizes)}{noise}";
    }
}
