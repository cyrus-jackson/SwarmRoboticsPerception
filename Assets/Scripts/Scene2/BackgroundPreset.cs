using UnityEngine;

/// <summary>
/// Tints background sprites with one of a few fixed colours. Add it to the background sprite and pick a preset.
/// List more sprites under Extra Sprites to give them the same colour (e.g. spawn and goal areas).
/// </summary>
[ExecuteAlways]
public class BackgroundPreset : MonoBehaviour
{
    public enum Preset { Cream, MidGrey, LightGrey, OffWhite, Dark }

    public Preset preset = Preset.LightGrey;
    [Tooltip("Other sprites that should get the same colour as this object's own Sprite Renderer.")]
    public SpriteRenderer[] extraSprites;

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    public void Apply()
    {
        Color color = ColorFor(preset);
        SpriteRenderer own = GetComponent<SpriteRenderer>();
        if (own != null) own.color = color;
        if (extraSprites == null) return;
        foreach (SpriteRenderer sprite in extraSprites)
            if (sprite != null) sprite.color = color;
    }

    public static Color ColorFor(Preset preset)
    {
        switch (preset)
        {
            case Preset.Cream: return new Color(1f, 1f, 0.941f);
            case Preset.MidGrey: return new Color(0.50f, 0.50f, 0.50f);
            case Preset.OffWhite: return new Color(0.93f, 0.93f, 0.93f);
            case Preset.Dark: return new Color(0.25f, 0.25f, 0.25f);
            default: return new Color(0.75f, 0.75f, 0.75f);
        }
    }
}
