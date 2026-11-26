using UnityEngine;

/// <summary>Draws a fixed-length arrow for the selected live velocity or force cue.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public sealed class DirectionArrowVisualizer : MonoBehaviour
{
    private const float LengthInBodyDiameters = 1.25f;
    private const float MinimumSpeed = 0.001f;
    private const float MinimumAcceleration = 0.000001f;
    private static Sprite arrowSprite;
    private static readonly Color DirectionColor = new Color32(233, 63, 168, 255);
    private bool showForce;
    private SwarmAgent agent;
    private SpriteRenderer arrow;

    internal static void SetMode(SwarmAgent[] agents, SwarmCue mode)
    {
        bool visible = mode == SwarmCue.Directions || mode == SwarmCue.Forces;
        foreach (SwarmAgent agent in agents)
        {
            if (agent == null) continue;
            var overlay = agent.GetComponentInChildren<DirectionArrowVisualizer>(true);
            if (overlay == null && visible)
            {
                var child = new GameObject("Direction Arrow");
                child.layer = agent.gameObject.layer;
                child.transform.SetParent(agent.transform, false);
                overlay = child.AddComponent<DirectionArrowVisualizer>();
            }
            if (overlay == null) continue;
            overlay.showForce = mode == SwarmCue.Forces;
            overlay.gameObject.SetActive(visible);
        }
    }

    private void Awake()
    {
        agent = GetComponentInParent<SwarmAgent>();
        arrow = GetComponent<SpriteRenderer>();
        arrow.sprite = GetArrowSprite();
        arrow.color = DirectionColor;
        arrow.enabled = false;

        SpriteRenderer body = agent.GetComponent<SpriteRenderer>();
        if (body != null)
        {
            arrow.sortingLayerID = body.sortingLayerID;
            arrow.sortingOrder = body.sortingOrder + 4; // Above the body, light and halo.
        }
        float diameter = body != null && body.sprite != null ? body.sprite.bounds.size.x : 1f;
        transform.localScale = Vector3.one * (diameter * LengthInBodyDiameters);
    }

    private void LateUpdate()
    {
        Vector2 heading = showForce ? agent.LastForces.Acceleration : agent.Velocity;
        float threshold = showForce ? MinimumAcceleration : MinimumSpeed;
        arrow.enabled = heading.sqrMagnitude >= threshold * threshold;
        if (!arrow.enabled) return;
        arrow.color = showForce ? agent.LastForces.ArrowColor() : DirectionColor;
        transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg);
    }

    private static Sprite GetArrowSprite()
    {
        if (arrowSprite != null) return arrowSprite;

        // A one-unit sprite with a rectangular shaft and triangular head, shared by every agent.
        arrowSprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, 1, 1),
            new Vector2(0.5f, 0.5f), 1f, 0, SpriteMeshType.FullRect);
        arrowSprite.name = "Direction Arrow";
        arrowSprite.OverrideGeometry(new[]
        {
            new Vector2(0f, 0.4f), new Vector2(0f, 0.6f),
            new Vector2(0.56f, 0.6f), new Vector2(0.56f, 0.4f),
            new Vector2(0.56f, 0.79f), new Vector2(1f, 0.5f), new Vector2(0.56f, 0.21f)
        }, new ushort[] { 0, 1, 2, 0, 2, 3, 4, 5, 6 });
        return arrowSprite;
    }
}
