using UnityEngine;

[DisallowMultipleComponent]
public class AgentBlinker : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Seconds for one on/off cycle.")]
    [Min(0.05f)] public float period = 1f;
    [Tooltip("Fraction of each cycle the lights are on.")]
    [Range(0f, 1f)] public float dutyCycle = 0.5f;
    [Tooltip("Off: every agent blinks in unison. On: each agent gets its own fixed phase offset.")]
    public bool randomPhase = false;

    [Header("Appearance")]
    public Color onColor = new Color(1f, 0.72f, 0f, 1f);
    [Tooltip("Light diameter in the agent's local units (the robot body is 1 unit wide).")]
    [Min(0f)] public float lightSize = 0.4f;
    [Tooltip("Position of the left light in local units: x = forward, y = left. The right light is mirrored.")]
    public Vector2 lightOffset = new Vector2(0.4f, 0.0f);
    [Tooltip("Off: the light is drawn on top of the robot. On: it sits underneath, so the body covers the part of the light and halo it overlaps.")]
    public bool lightUnderBody = true;

    [Header("Robot body")]
    [Tooltip("Off: dark robot, for light backgrounds. On: light robot, for dark backgrounds.")]
    public bool lightBody = false;
    public Color darkBodyColor = new Color(0.22f, 0.22f, 0.22f, 1f);
    [Tooltip("White shows the sprite's own light grey.")]
    public Color lightBodyColor = Color.white;

    [Header("Halo")]
    [Tooltip("Soft glow drawn around the light while it is on.")]
    public bool showHalo = true;
    [Tooltip("Halo diameter as a multiple of the light diameter.")]
    [Min(1f)] public float haloSize = 3.5f;
    [Tooltip("Opacity of the halo where it meets the light.")]
    [Range(0f, 1f)] public float haloIntensity = 0.7f;

    private const string LeftName = "BlinkerLeft";
    private const string RightName = "BlinkerRight";
    private const string HaloName = "BlinkerHalo";
    private const int TextureSize = 32;
    private const int HaloTextureSize = 64;

    private static Sprite lightSprite;
    private static Sprite haloSprite;
    private static int instanceCount;

    private SpriteRenderer left;
    // private SpriteRenderer right;
    private SpriteRenderer haloRenderer;
    private SpriteRenderer bodyRenderer;
    private int bodySortingOrder;
    private float phase;
    private bool isOn;

    private void Awake()
    {
        SpriteRenderer body = GetComponent<SpriteRenderer>();
        bodyRenderer = body;
        bodySortingOrder = body != null ? body.sortingOrder : 0;
        ApplyBody();
        haloRenderer = GetOrCreateLight(HaloName, body, GetHaloSprite());
        left = GetOrCreateLight(LeftName, body, GetLightSprite());
        // right = GetOrCreateLight(RightName, body);

        // A golden-ratio sequence instead of UnityEngine.Random, so the simulation's random stream is untouched.
        phase = randomPhase ? Mathf.Repeat(++instanceCount * 0.6180339887f, 1f) : 0f;

        ApplySorting();
        Layout();
        Apply(ShouldBeOn());
    }

    private void Update()
    {
        bool on = ShouldBeOn();
        if (on != isOn) Apply(on);
    }

    private void OnValidate()
    {
        if (!Application.isPlaying || left == null) return;
        ApplyBody();
        ApplySorting();
        Layout();
        Apply(isOn);
    }

    private bool ShouldBeOn()
    {
        float cycle = Mathf.Repeat(Time.time / Mathf.Max(period, 0.05f) + phase, 1f);
        return cycle < dutyCycle;
    }

    private void Apply(bool on)
    {
        isOn = on;
        // The light is hidden entirely while off, so it does not read as a part of the robot.
        if (left != null)
        {
            left.color = onColor;
            left.enabled = on;
        }
        if (haloRenderer != null)
        {
            Color glow = onColor;
            glow.a *= haloIntensity;
            haloRenderer.color = glow;
            haloRenderer.enabled = showHalo && on;
        }
        // if (right != null) right.color = color;
    }

    private void ApplyBody()
    {
        if (bodyRenderer != null) bodyRenderer.color = lightBody ? lightBodyColor : darkBodyColor;
    }

    /// <summary>
    /// Stacks halo, light and body. Sprites sort by sorting layer and order before z, so a z offset alone cannot put the
    /// light under the body. On top, the glow spills onto the robot and its neighbours; underneath, every body covers
    /// every light, so the glow only shows on the floor around the robots.
    /// </summary>
    private void ApplySorting()
    {
        if (haloRenderer != null) haloRenderer.sortingOrder = bodySortingOrder + 1;
        left.sortingOrder = bodySortingOrder + 2;
        if (bodyRenderer != null) bodyRenderer.sortingOrder = lightUnderBody ? bodySortingOrder + 3 : bodySortingOrder;
    }

    private void Layout()
    {
        Vector3 scale = Vector3.one * lightSize;
        left.transform.localPosition = new Vector3(lightOffset.x, lightOffset.y, 0f);
        // right.transform.localPosition = new Vector3(lightOffset.x, -lightOffset.y, 0f);
        left.transform.localScale = scale;
        if (haloRenderer != null)
        {
            haloRenderer.transform.localPosition = left.transform.localPosition;
            haloRenderer.transform.localScale = scale * haloSize;
        }
        // right.transform.localScale = scale;
    }

    private SpriteRenderer GetOrCreateLight(string lightName, SpriteRenderer body, Sprite sprite)
    {
        Transform lightTransform = transform.Find(lightName);
        if (lightTransform == null)
        {
            GameObject created = new GameObject(lightName);
            created.layer = gameObject.layer;
            lightTransform = created.transform;
            lightTransform.SetParent(transform, false);
        }

        SpriteRenderer lightRenderer = lightTransform.GetComponent<SpriteRenderer>();
        if (lightRenderer == null) lightRenderer = lightTransform.gameObject.AddComponent<SpriteRenderer>();
        lightRenderer.sprite = sprite;
        if (body != null)
        {
            lightRenderer.sharedMaterial = body.sharedMaterial;
            lightRenderer.sortingLayerID = body.sortingLayerID;
        }
        return lightRenderer;
    }

    /// <summary>A white disc with a soft edge, built once and shared by every light.</summary>
    private static Sprite GetLightSprite()
    {
        if (lightSprite != null) return lightSprite;

        Texture2D texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        texture.name = "BlinkerLight";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.HideAndDontSave;

        Color32[] pixels = new Color32[TextureSize * TextureSize];
        float radius = TextureSize * 0.5f;
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float alpha = Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        lightSprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f),
            TextureSize, 0, SpriteMeshType.FullRect);
        lightSprite.name = "BlinkerLight";
        lightSprite.hideFlags = HideFlags.HideAndDontSave;
        return lightSprite;
    }

    /// <summary>A white radial glow that fades smoothly to nothing at its edge, built once and shared.</summary>
    private static Sprite GetHaloSprite()
    {
        if (haloSprite != null) return haloSprite;

        Texture2D texture = new Texture2D(HaloTextureSize, HaloTextureSize, TextureFormat.RGBA32, false);
        texture.name = "BlinkerHalo";
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.hideFlags = HideFlags.HideAndDontSave;

        Color32[] pixels = new Color32[HaloTextureSize * HaloTextureSize];
        float radius = HaloTextureSize * 0.5f;
        for (int y = 0; y < HaloTextureSize; y++)
        {
            for (int x = 0; x < HaloTextureSize; x++)
            {
                float dx = x + 0.5f - radius;
                float dy = y + 0.5f - radius;
                float t = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy) / radius);
                float alpha = t * t * (3f - 2f * t);
                pixels[y * HaloTextureSize + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
            }
        }
        texture.SetPixels32(pixels);
        texture.Apply(false, true);

        haloSprite = Sprite.Create(texture, new Rect(0f, 0f, HaloTextureSize, HaloTextureSize), new Vector2(0.5f, 0.5f),
            HaloTextureSize, 0, SpriteMeshType.FullRect);
        haloSprite.name = "BlinkerHalo";
        haloSprite.hideFlags = HideFlags.HideAndDontSave;
        return haloSprite;
    }
}
