using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws an outline round each spatial cluster, one colour per cluster.
///
/// Built the same way as <see cref="DensityAreaVisualizer"/> — LineRenderers rather than gizmos — so
/// the outlines appear in the Game view and end up in recordings, not only in the Scene view.
///
/// Each cluster gets the convex hull of its members, which is the same shape the density metric
/// draws for the whole swarm, so the two read consistently: the big hull is the swarm's extent, the
/// small hulls are the clumps inside it.
/// </summary>
public class ClusterVisualizer : MonoBehaviour
{
    [Tooltip("Width of the cluster outlines.")]
    public float lineWidth = 0.05f;

    [Tooltip("Colours cycled through for successive clusters. Runs out and repeats if there are more clusters than colours.")]
    public Color[] palette =
    {
        new Color(0.30f, 0.76f, 1.00f),
        new Color(1.00f, 0.55f, 0.25f),
        new Color(0.45f, 0.90f, 0.45f),
        new Color(1.00f, 0.45f, 0.75f),
        new Color(0.95f, 0.85f, 0.30f),
        new Color(0.65f, 0.55f, 1.00f),
        new Color(0.35f, 0.90f, 0.85f),
        new Color(1.00f, 0.40f, 0.40f),
    };

    [Tooltip("Ring drawn round agents the clusterer called noise. DBSCAN can leave agents in no cluster at all, which the perception graph never does, so they are worth showing.")]
    public bool showNoise = true;

    public Color noiseColor = new Color(0.65f, 0.65f, 0.70f, 0.9f);

    [Tooltip("Radius of the ring drawn round a noise agent.")]
    public float noiseMarkerRadius = 0.35f;

    private readonly List<LineRenderer> clusterLines = new List<LineRenderer>();
    private readonly List<LineRenderer> noiseLines = new List<LineRenderer>();
    private readonly List<Vector2> hullBuffer = new List<Vector2>();
    private readonly List<Vector2> memberBuffer = new List<Vector2>();

    /// <summary>
    /// Redraws the outlines for a labelling.
    ///
    /// Line renderers are pooled and hidden rather than destroyed, because this is called every
    /// frame and the cluster count changes constantly.
    /// </summary>
    public void Draw(IList<Vector2> points, SwarmSpatialClustering.Result result)
    {
        if (result == null || points == null)
        {
            HideFrom(clusterLines, 0);
            HideFrom(noiseLines, 0);
            return;
        }

        int used = 0;
        for (int cluster = 0; cluster < result.ClusterCount; cluster++)
        {
            memberBuffer.Clear();
            for (int i = 0; i < result.labels.Count && i < points.Count; i++)
            {
                if (result.labels[i] == cluster) memberBuffer.Add(points[i]);
            }

            // Two agents make a line, one makes nothing; both are legitimate small clusters, so
            // they are drawn as a marker rather than skipped.
            if (memberBuffer.Count < 3)
            {
                if (memberBuffer.Count > 0)
                {
                    LineRenderer marker = LineAt(clusterLines, used++);
                    DrawCircle(marker, Centroid(memberBuffer), noiseMarkerRadius,
                               palette.Length > 0 ? palette[cluster % palette.Length] : Color.white);
                }
                continue;
            }

            SwarmDensityMetrics.TrimmedHull(memberBuffer, 0f, hullBuffer, null);

            LineRenderer line = LineAt(clusterLines, used++);
            DrawPolygon(line, hullBuffer,
                        palette.Length > 0 ? palette[cluster % palette.Length] : Color.white);
        }

        HideFrom(clusterLines, used);

        int noiseUsed = 0;
        if (showNoise)
        {
            for (int i = 0; i < result.labels.Count && i < points.Count; i++)
            {
                if (result.labels[i] != SwarmSpatialClustering.Noise) continue;

                LineRenderer ring = LineAt(noiseLines, noiseUsed++);
                DrawCircle(ring, points[i], noiseMarkerRadius, noiseColor);
            }
        }

        HideFrom(noiseLines, noiseUsed);
    }

    /// <summary>Clears every outline, for when the display is switched off.</summary>
    public void Clear()
    {
        HideFrom(clusterLines, 0);
        HideFrom(noiseLines, 0);
    }

    // ------------------------------------------------------------------ drawing

    private LineRenderer LineAt(List<LineRenderer> pool, int index)
    {
        while (pool.Count <= index)
        {
            GameObject child = new GameObject($"ClusterOutline_{pool.Count:D2}");
            child.transform.SetParent(transform, false);

            LineRenderer line = child.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.loop = true;
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.positionCount = 0;

            pool.Add(line);
        }

        pool[index].enabled = true;
        return pool[index];
    }

    private static void HideFrom(List<LineRenderer> pool, int index)
    {
        for (int i = index; i < pool.Count; i++)
        {
            if (pool[i] != null) pool[i].positionCount = 0;
        }
    }

    private void DrawPolygon(LineRenderer line, IList<Vector2> polygon, Color color)
    {
        if (polygon == null || polygon.Count < 3)
        {
            line.positionCount = 0;
            return;
        }

        line.startColor = color;
        line.endColor = color;
        line.widthMultiplier = lineWidth;
        line.loop = true;
        line.positionCount = polygon.Count;

        for (int i = 0; i < polygon.Count; i++)
        {
            line.SetPosition(i, new Vector3(polygon[i].x, polygon[i].y, 0.03f));
        }
    }

    private void DrawCircle(LineRenderer line, Vector2 centre, float radius, Color color)
    {
        const int segments = 20;

        line.startColor = color;
        line.endColor = color;
        line.widthMultiplier = lineWidth;
        line.loop = true;
        line.positionCount = segments;

        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            line.SetPosition(i, new Vector3(centre.x + Mathf.Cos(a) * radius,
                                            centre.y + Mathf.Sin(a) * radius, 0.03f));
        }
    }

    private static Vector2 Centroid(List<Vector2> points)
    {
        Vector2 sum = Vector2.zero;
        foreach (Vector2 p in points) sum += p;
        return points.Count > 0 ? sum / points.Count : Vector2.zero;
    }
}
