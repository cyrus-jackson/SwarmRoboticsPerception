using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Geometry helpers for measuring how spread out a swarm is.
///
/// Two metrics are provided:
///
///  - Trimmed convex hull area. The convex hull is the smallest convex polygon containing the
///    agents; its area is the footprint the swarm occupies, so N / area is a density. A raw hull
///    is decided entirely by its extreme points, which makes it very sensitive to a single
///    straggler, so a fraction of the agents furthest from the centroid is discarded first. 
///
///  - Mean K nearest neighbour distance. For each agent, the mean distance to its K closest
///    neighbours, averaged over the swarm. This is the intra-group distance measure used by
///    Hénard et al. (2024), section 3.2.1, with K = 3.
/// </summary>
public static class SwarmDensityMetrics
{
    /// <summary>Collects the XY positions of the non-null agents into the supplied list.</summary>
    public static void CollectPositions(GameObject[] agents, List<Vector2> into)
    {
        into.Clear();
        if (agents == null) return;

        foreach (GameObject agentObj in agents)
        {
            if (agentObj == null) continue;
            Vector3 p = agentObj.transform.position;
            into.Add(new Vector2(p.x, p.y));
        }
    }

    public static Vector2 Centroid(IList<Vector2> points)
    {
        if (points == null || points.Count == 0) return Vector2.zero;

        Vector2 sum = Vector2.zero;
        for (int i = 0; i < points.Count; i++) sum += points[i];
        return sum / points.Count;
    }

    /// <summary>
    /// Area of the convex hull of the points, after discarding the given fraction of agents
    /// furthest from the centroid. trimFraction 0 gives the plain convex hull area.
    /// </summary>
    public static float TrimmedHullArea(IList<Vector2> points, float trimFraction)
    {
        return TrimmedHull(points, trimFraction, null, null);
    }

    /// <summary>
    /// As TrimmedHullArea, but also reports the hull polygon and the discarded points so they can
    /// be drawn. Either output list may be null.
    /// </summary>
    public static float TrimmedHull(IList<Vector2> points, float trimFraction, List<Vector2> hullOut, List<Vector2> droppedOut)
    {
        hullOut?.Clear();
        droppedOut?.Clear();

        if (points == null || points.Count < 3) return 0f;

        List<Vector2> working = new List<Vector2>(points);

        int dropCount = Mathf.FloorToInt(working.Count * Mathf.Clamp01(trimFraction));
        // Never trim below the three points needed for an area.
        dropCount = Mathf.Min(dropCount, Mathf.Max(0, working.Count - 3));

        if (dropCount > 0)
        {
            Vector2 centre = Centroid(working);
            working.Sort((a, b) => (a - centre).sqrMagnitude.CompareTo((b - centre).sqrMagnitude));

            if (droppedOut != null)
            {
                for (int i = working.Count - dropCount; i < working.Count; i++) droppedOut.Add(working[i]);
            }

            working.RemoveRange(working.Count - dropCount, dropCount);
        }

        List<Vector2> hull = ConvexHull(working);
        if (hullOut != null) hullOut.AddRange(hull);

        return PolygonArea(hull);
    }

    /// <summary>Area of the convex hull of the points, via monotone chain plus the shoelace formula.</summary>
    public static float HullArea(IList<Vector2> points)
    {
        return PolygonArea(ConvexHull(points));
    }

    /// <summary>Shoelace area of an already-ordered polygon.</summary>
    public static float PolygonArea(IList<Vector2> polygon)
    {
        if (polygon == null || polygon.Count < 3) return 0f;

        float doubleArea = 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            Vector2 current = polygon[i];
            Vector2 next = polygon[(i + 1) % polygon.Count];
            doubleArea += (current.x * next.y) - (next.x * current.y);
        }

        return Mathf.Abs(doubleArea) * 0.5f;
    }

    /// <summary>
    /// Appends the K nearest neighbour pairs (as index pairs into points) for every point.
    /// Used for drawing what the mean KNN metric is measuring.
    /// </summary>
    public static void CollectKnnPairs(IList<Vector2> points, int k, List<int> pairsOut)
    {
        pairsOut?.Clear();
        if (points == null || points.Count < 2 || pairsOut == null) return;

        int neighbourCount = Mathf.Clamp(k, 1, points.Count - 1);
        List<KeyValuePair<float, int>> ranked = new List<KeyValuePair<float, int>>(points.Count);

        for (int i = 0; i < points.Count; i++)
        {
            ranked.Clear();
            for (int j = 0; j < points.Count; j++)
            {
                if (i == j) continue;
                ranked.Add(new KeyValuePair<float, int>((points[i] - points[j]).sqrMagnitude, j));
            }

            ranked.Sort((a, b) => a.Key.CompareTo(b.Key));

            for (int n = 0; n < neighbourCount; n++)
            {
                pairsOut.Add(i);
                pairsOut.Add(ranked[n].Value);
            }
        }
    }

    /// <summary>
    /// Convex hull in counter-clockwise order (Andrew's monotone chain, O(n log n)).
    /// </summary>
    public static List<Vector2> ConvexHull(IList<Vector2> points)
    {
        List<Vector2> hull = new List<Vector2>();
        if (points == null || points.Count < 3) return hull;

        List<Vector2> sorted = new List<Vector2>(points);
        sorted.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        // Lower chain, then upper chain.
        List<Vector2> lower = BuildChain(sorted);
        sorted.Reverse();
        List<Vector2> upper = BuildChain(sorted);

        // Drop each chain's last point: it is the first point of the other chain.
        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);

        hull.AddRange(lower);
        hull.AddRange(upper);
        return hull;
    }

    private static List<Vector2> BuildChain(List<Vector2> sorted)
    {
        List<Vector2> chain = new List<Vector2>();

        foreach (Vector2 point in sorted)
        {
            while (chain.Count >= 2 && Cross(chain[chain.Count - 2], chain[chain.Count - 1], point) <= 0f)
            {
                chain.RemoveAt(chain.Count - 1);
            }
            chain.Add(point);
        }

        return chain;
    }

    /// <summary>Z component of (b - a) x (c - a). Positive means a left turn.</summary>
    private static float Cross(Vector2 a, Vector2 b, Vector2 c)
    {
        return ((b.x - a.x) * (c.y - a.y)) - ((b.y - a.y) * (c.x - a.x));
    }

    /// <summary>
    /// Mean distance to the K nearest neighbours, averaged over all agents. O(n^2) in the agent
    /// count, which is nothing at swarm sizes of a few dozen.
    /// </summary>
    public static float MeanKnnDistance(IList<Vector2> points, int k)
    {
        if (points == null || points.Count < 2) return 0f;

        int neighbourCount = Mathf.Clamp(k, 1, points.Count - 1);
        List<float> distances = new List<float>(points.Count);
        float total = 0f;

        for (int i = 0; i < points.Count; i++)
        {
            distances.Clear();
            for (int j = 0; j < points.Count; j++)
            {
                if (i == j) continue;
                distances.Add(Vector2.Distance(points[i], points[j]));
            }

            distances.Sort();

            float sum = 0f;
            for (int n = 0; n < neighbourCount; n++) sum += distances[n];
            total += sum / neighbourCount;
        }

        return total / points.Count;
    }
}
