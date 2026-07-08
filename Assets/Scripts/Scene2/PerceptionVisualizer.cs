using UnityEngine;

[RequireComponent(typeof(Transform))]
public class PerceptionVisualizer : MonoBehaviour
{
    [Tooltip("Radius to draw (world units)")]
    public float radius = 1f;

    [Tooltip("Number of segments used to approximate the circle")]
    public int segments = 36;

    [Tooltip("Line width")]
    public float lineWidth = 0.02f;

    private LineRenderer lr;

    void Awake()
    {
        EnsureLineRenderer();
    }

    void OnEnable()
    {
        if (lr != null) lr.enabled = true;
        DrawCircle();
    }

    void OnDisable()
    {
        if (lr != null) lr.enabled = false;
    }

    void Update()
    {
        // Keep the circle in sync with radius and position each frame.
        if (lr == null) EnsureLineRenderer();
        if (lr != null && lr.enabled)
        {
            DrawCircle();
        }
    }

    private void EnsureLineRenderer()
    {
        if (lr != null) return;
        lr = gameObject.GetComponent<LineRenderer>();
        if (lr == null) lr = gameObject.AddComponent<LineRenderer>();

        lr.useWorldSpace = true;
        lr.loop = true;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.widthMultiplier = Mathf.Max(0.0001f, lineWidth);
        lr.positionCount = segments > 0 ? segments : 36;
        lr.numCornerVertices = 0;
        lr.numCapVertices = 0;
        lr.startColor = new Color(1f, 0.25f, 0.25f, 0.9f);
        lr.endColor = new Color(1f, 0.25f, 0.25f, 0.9f);
    }

    private void DrawCircle()
    {
        if (segments <= 0) segments = 36;
        if (lr == null) return;

        lr.positionCount = segments + 1;
        float angleStep = 2f * Mathf.PI / segments;
        for (int i = 0; i <= segments; i++)
        {
            float angle = i * angleStep;
            Vector3 offset = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f);
            lr.SetPosition(i, transform.position + offset);
        }
        lr.widthMultiplier = Mathf.Max(0.0001f, lineWidth);
    }

    void OnValidate()
    {
        if (lr != null) lr.widthMultiplier = Mathf.Max(0.0001f, lineWidth);
        DrawCircle();
    }
}
