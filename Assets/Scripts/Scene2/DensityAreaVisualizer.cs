using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime outline of the region the density metric is measuring, drawn with LineRenderers so it
/// is visible in the Game view rather than only as a scene gizmo. Mirrors how PerceptionVisualizer
/// is toggled from the UI panel.
///
/// Added automatically alongside the SwarmDensityMonitor and enabled or disabled by
/// SwarmManager.showDensityArea.
/// </summary>
[RequireComponent(typeof(SwarmDensityMonitor))]
public class DensityAreaVisualizer : MonoBehaviour
{
    [Tooltip("Line width of the outlines (world units)")]
    public float lineWidth = 0.04f;

    [Tooltip("Also outline the swarm as it was when the baseline was captured.")]
    public bool showBaselineOutline = true;

    private SwarmDensityMonitor monitor;
    private LineRenderer currentLine;
    private LineRenderer baselineLine;

    void Awake()
    {
        monitor = GetComponent<SwarmDensityMonitor>();
        EnsureLineRenderers();
    }

    void OnEnable()
    {
        EnsureLineRenderers();
        if (currentLine != null) currentLine.enabled = true;
        if (baselineLine != null) baselineLine.enabled = showBaselineOutline;
        Redraw();
    }

    void OnDisable()
    {
        if (currentLine != null) currentLine.enabled = false;
        if (baselineLine != null) baselineLine.enabled = false;
    }

    void LateUpdate()
    {
        Redraw();
    }

    private void EnsureLineRenderers()
    {
        if (monitor == null) monitor = GetComponent<SwarmDensityMonitor>();

        if (currentLine == null)
        {
            currentLine = gameObject.GetComponent<LineRenderer>();
            if (currentLine == null) currentLine = gameObject.AddComponent<LineRenderer>();
            ConfigureLine(currentLine, monitor != null ? monitor.hullGizmoColor : Color.cyan);
        }

        if (baselineLine == null)
        {
            Transform existing = transform.Find("DensityBaselineOutline");
            GameObject child = existing != null
                ? existing.gameObject
                : new GameObject("DensityBaselineOutline");

            child.transform.SetParent(transform, false);
            baselineLine = child.GetComponent<LineRenderer>();
            if (baselineLine == null) baselineLine = child.AddComponent<LineRenderer>();
            ConfigureLine(baselineLine, monitor != null ? monitor.baselineGizmoColor : new Color(0f, 1f, 1f, 0.3f));
        }
    }

    private void ConfigureLine(LineRenderer line, Color color)
    {
        line.useWorldSpace = true;
        line.loop = true;
        line.material = new Material(Shader.Find("Sprites/Default"));
        line.widthMultiplier = Mathf.Max(0.0001f, lineWidth);
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.positionCount = 0;
        line.startColor = color;
        line.endColor = color;
    }

    private void Redraw()
    {
        if (monitor == null) return;
        if (currentLine == null || baselineLine == null) EnsureLineRenderers();

        ApplyPolygon(currentLine, monitor.CurrentHull, monitor.hullGizmoColor);

        baselineLine.enabled = enabled && showBaselineOutline;
        ApplyPolygon(baselineLine, showBaselineOutline ? monitor.BaselineHull : null, monitor.baselineGizmoColor);
    }

    private void ApplyPolygon(LineRenderer line, IReadOnlyList<Vector2> polygon, Color color)
    {
        if (line == null) return;

        if (polygon == null || polygon.Count < 3)
        {
            line.positionCount = 0;
            return;
        }

        line.widthMultiplier = Mathf.Max(0.0001f, lineWidth);
        line.startColor = color;
        line.endColor = color;
        line.positionCount = polygon.Count;

        float z = monitor != null ? monitor.gizmoZ : 0f;
        for (int i = 0; i < polygon.Count; i++)
        {
            line.SetPosition(i, new Vector3(polygon[i].x, polygon[i].y, z));
        }
    }
}
