using UnityEngine;

/// <summary>Net steering acceleration and its local interaction terms from the same physics step.</summary>
internal readonly struct SwarmForceSample
{
    public readonly Vector2 Acceleration;
    private readonly Vector2 cohesion, separation, alignment, overlapAvoidance;

    public SwarmForceSample(Vector2 acceleration, Vector2 cohesion, Vector2 separation,
        Vector2 alignment, Vector2 overlapAvoidance)
    {
        Acceleration = acceleration;
        this.cohesion = cohesion;
        this.separation = separation;
        this.alignment = alignment;
        this.overlapAvoidance = overlapAvoidance;
    }

    public Color ArrowColor()
    {
        Vector2 strongest = cohesion;
        Color hue = Color.red;
        Consider(separation, Color.blue, ref strongest, ref hue);
        Consider(overlapAvoidance, Color.blue, ref strongest, ref hue);
        Consider(alignment, Color.green, ref strongest, ref hue);

        if (Vector2.Dot(strongest, Acceleration) <= 0f) return Color.white;
        float angle = Vector2.Angle(strongest, Acceleration);
        return Color.Lerp(hue, Color.white, angle / 90f);
    }

    private static void Consider(Vector2 force, Color color, ref Vector2 strongest, ref Color hue)
    {
        if (force.sqrMagnitude > strongest.sqrMagnitude)
        {
            strongest = force;
            hue = color;
        }
        else if (force.sqrMagnitude == strongest.sqrMagnitude && color != hue)
        {
            // Equally strong, differently coloured interactions have no unique dominant type.
            hue = Color.white;
        }
    }
}
