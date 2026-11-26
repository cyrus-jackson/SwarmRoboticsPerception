using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Draws the undirected live perception graph as a single mesh of grey strips.</summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class InteractionLinksVisualizer : MonoBehaviour
{
    private const float MaxWidth = 0.04f;
    private readonly List<Node> nodes = new List<Node>();
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color32> colors = new List<Color32>();
    private readonly List<int> triangles = new List<int>();
    private SwarmManager manager;
    private Mesh mesh;
    private Material material;

    private readonly struct Node
    {
        public readonly Vector2 Position;
        private readonly Vector2 circleCentre;
        private readonly float circleRadius;
        private readonly Collider2D collider;

        public Node(Transform robot, Collider2D collider)
        {
            Position = robot.position;
            this.collider = collider;
            circleCentre = Position;
            circleRadius = 0f;
            if (collider is CircleCollider2D circle)
            {
                // The live prefabs use circles. Their geometry also works before Play Motion,
                // when the rigidbodies are paused and unavailable for physics queries.
                circleCentre = circle.transform.TransformPoint(circle.offset);
                Vector3 scale = circle.transform.lossyScale;
                circleRadius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            }
        }

        public float DistanceFrom(Vector2 observer)
        {
            if (collider is CircleCollider2D)
                return Mathf.Max(0f, Vector2.Distance(observer, circleCentre) - circleRadius);
            Vector2 closest = collider != null ? collider.ClosestPoint(observer) : Position;
            return Vector2.Distance(observer, closest);
        }
    }

    private void Awake()
    {
        manager = GetComponentInParent<SwarmManager>();
        mesh = new Mesh { name = "Live interaction links", indexFormat = IndexFormat.UInt32 };
        mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = mesh;
        material = new Material(Shader.Find("Sprites/Default"));
        var renderer = GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.sortingOrder = 1; // Above the arena and below the live robot bodies.
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    internal static void SetVisible(SwarmManager manager, bool visible)
    {
        var links = manager.GetComponentInChildren<InteractionLinksVisualizer>(true);
        if (links == null && visible)
        {
            var child = new GameObject("Interaction Links");
            child.layer = manager.gameObject.layer;
            child.transform.SetParent(manager.transform, false);
            links = child.AddComponent<InteractionLinksVisualizer>();
        }
        if (links != null) links.gameObject.SetActive(visible);
    }

    private void LateUpdate()
    {
        nodes.Clear();
        var agents = manager.AgentScripts;
        var colliders = manager.AgentColliders;
        for (int i = 0; i < agents.Length; i++)
        {
            if (agents[i] != null && agents[i].gameObject.activeInHierarchy)
                nodes.Add(new Node(agents[i].transform, colliders[i]));
        }

        vertices.Clear();
        colors.Clear();
        triangles.Clear();
        float range = manager.perceptionRadius;
        Matrix4x4 toLocal = transform.worldToLocalMatrix;
        for (int i = 0; i < nodes.Count; i++)
        {
            Node a = nodes[i];
            for (int j = i + 1; j < nodes.Count; j++)
            {
                Node b = nodes[j];
                float width = LinkWidth(b.DistanceFrom(a.Position), a.DistanceFrom(b.Position), range);
                if (width > 0f) AddLink(a.Position, b.Position, width, toLocal);
            }
        }

        mesh.Clear();
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
    }

    internal static float LinkWidth(float distanceAB, float distanceBA, float range)
    {
        // Either robot can perceive the other. The link breaks when both are out of range.
        float distance = Mathf.Min(distanceAB, distanceBA);
        return distance < range ? MaxWidth * (1f - distance / range) : 0f;
    }

    private void AddLink(Vector2 a, Vector2 b, float width, Matrix4x4 toLocal)
    {
        Vector2 delta = b - a;
        if (delta.sqrMagnitude < 1e-12f) return;
        Vector2 side = Vector2.Perpendicular(delta.normalized) * (width * 0.5f);
        int start = vertices.Count;
        vertices.Add(toLocal.MultiplyPoint3x4(a - side));
        vertices.Add(toLocal.MultiplyPoint3x4(a + side));
        vertices.Add(toLocal.MultiplyPoint3x4(b + side));
        vertices.Add(toLocal.MultiplyPoint3x4(b - side));
        for (int i = 0; i < 4; i++) colors.Add(new Color32(107, 107, 107, 255));
        triangles.Add(start);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    private void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }
}
