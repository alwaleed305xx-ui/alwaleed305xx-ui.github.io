using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The package-free post stack (GDD 6.5): one full-screen overlay canvas at
/// sort order 5000 carrying, back to front - desaturation tint, procedural
/// radial vignette, scrolling film grain, gamma calibration layers, event
/// flash, and killcam letterbox bars.
///
/// HouseFactory creates the GameObject + Canvas; this component builds its own
/// children and procedural textures at runtime in Awake, so nothing
/// non-serializable ever needs to survive a scene save. Every layer ignores
/// raycasts - the stack can never eat a button click.
///
/// Settings: the "Screen Effects" slider scales the whole stack, LOW quality
/// disables grain, and the gamma slider drives the calibration layers. All
/// applied live through ScreamerSettings.OnChanged.
/// </summary>
public class ScreenFxOverlay : MonoBehaviour
{
    public static ScreenFxOverlay Instance { get; private set; }

    [Header("Defaults (GDD 6.5)")]
    [Tooltip("Vignette alpha outside monster rounds; GameUI raises it to 0.45 on monster screens.")]
    public float defaultVignetteAlpha = 0.25f;
    [Tooltip("Film grain alpha at full Screen Effects.")]
    public float grainAlpha = 0.08f;
    [Tooltip("Letterbox bar height as a fraction of screen height, per bar.")]
    public float letterboxFraction = 0.12f;
    [Tooltip("Letterbox open/close speed, fractions per second.")]
    public float letterboxSpeed = 0.6f;

    // Layer images, built in Awake.
    Image desaturationImage;
    Image vignetteImage;
    RawImage grainImage;
    Image gammaDarkImage;
    Image gammaBrightImage;
    Image flashImage;
    Image letterboxTop;
    Image letterboxBottom;

    // Requested state.
    float vignetteAlpha;
    Color vignetteTint;
    float desaturationAmount;
    bool grainRequested = true;
    bool letterboxOn;
    float letterboxCurrent;

    // Flash envelope.
    Color flashColor;
    float flashDuration;
    float flashTimer;

    // Live settings snapshot (safe defaults when the settings singleton is absent).
    float effectsScale = 1f;
    int qualityTier = 1;
    float gamma;

    Texture2D vignetteTexture;
    Texture2D grainTexture;

    void Awake()
    {
        Instance = this;
        vignetteAlpha = defaultVignetteAlpha;
        vignetteTint = ScreamerPalette.InkBlack;
        BuildStack();
        ApplySettings();
    }

    void OnEnable()
    {
        ScreamerSettings.OnChanged += ApplySettings;
    }

    void OnDisable()
    {
        ScreamerSettings.OnChanged -= ApplySettings;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (vignetteTexture != null) Destroy(vignetteTexture);
        if (grainTexture != null) Destroy(grainTexture);
    }

    // ------------------------- Public API (inter-module contract) -------------------------

    /// <summary>Vignette base alpha and tint. 0.25 + InkBlack normal; 0.45 + MonsterRed on monster screens.</summary>
    public void SetVignette(float alpha, Color tint)
    {
        vignetteAlpha = Mathf.Clamp01(alpha);
        vignetteTint = tint;
        RefreshStaticLayers();
    }

    /// <summary>One event flash: alpha spikes then decays to zero over <paramref name="duration"/> seconds.</summary>
    public void Flash(Color color, float duration)
    {
        flashColor = color;
        flashDuration = Mathf.Max(0.02f, duration);
        flashTimer = flashDuration;
    }

    /// <summary>Slides the killcam letterbox bars in or out.</summary>
    public void SetLetterbox(bool on)
    {
        letterboxOn = on;
    }

    /// <summary>Request film grain. Actual visibility also requires MEDIUM+ quality and Screen Effects &gt; 0.</summary>
    public void SetGrain(bool on)
    {
        grainRequested = on;
        RefreshStaticLayers();
    }

    /// <summary>Ghost-view desaturation tint, 0..1 (ghosts use 0.3).</summary>
    public void SetDesaturation(float amount)
    {
        desaturationAmount = Mathf.Clamp01(amount);
        RefreshStaticLayers();
    }

    /// <summary>Returns the per-round layers to baseline (desaturation, letterbox, flash). Vignette is GameUI's call.</summary>
    public void ResetRoundFx()
    {
        desaturationAmount = 0f;
        letterboxOn = false;
        flashTimer = 0f;
        if (flashImage != null) flashImage.color = Color.clear;
        RefreshStaticLayers();
    }

    // ------------------------- Per-frame animation -------------------------

    void Update()
    {
        // Film grain: re-roll the scroll offset every frame so the noise never
        // reads as a fixed pattern; tile it so a grain cell lands around 3 px.
        if (grainImage != null && grainImage.enabled)
        {
            float tilesX = Mathf.Max(1f, Screen.width / 192f);
            float tilesY = Mathf.Max(1f, Screen.height / 192f);
            grainImage.uvRect = new Rect(Random.value, Random.value, tilesX, tilesY);
        }

        // Flash decay.
        if (flashTimer > 0f && flashImage != null)
        {
            flashTimer -= Time.unscaledDeltaTime;
            float normalized = Mathf.Clamp01(flashTimer / flashDuration);
            float alpha = normalized * 0.85f * effectsScale;
            flashImage.color = new Color(flashColor.r, flashColor.g, flashColor.b, alpha);
        }

        // Letterbox slide.
        float target = letterboxOn ? letterboxFraction : 0f;
        if (!Mathf.Approximately(letterboxCurrent, target))
        {
            letterboxCurrent = Mathf.MoveTowards(letterboxCurrent, target, letterboxSpeed * Time.unscaledDeltaTime);
            ApplyLetterbox();
        }
    }

    // ------------------------- Settings -------------------------

    void ApplySettings()
    {
        ScreamerSettings settings = ScreamerSettings.Instance;
        if (settings != null)
        {
            effectsScale = Mathf.Clamp01(settings.ScreenEffects);
            qualityTier = settings.QualityTier;
            gamma = Mathf.Clamp(settings.Gamma, -0.5f, 0.5f);
        }
        RefreshStaticLayers();
    }

    void RefreshStaticLayers()
    {
        if (vignetteImage == null) return; // not built yet (Awake order)

        vignetteImage.color = new Color(vignetteTint.r, vignetteTint.g, vignetteTint.b,
            vignetteAlpha * effectsScale);

        bool grainVisible = grainRequested && qualityTier >= 1 && effectsScale > 0.001f;
        grainImage.enabled = grainVisible;
        var grainColor = grainImage.color;
        grainImage.color = new Color(grainColor.r, grainColor.g, grainColor.b, grainAlpha * effectsScale);

        Color desatTint = Color.Lerp(ScreamerPalette.GhostMint, ScreamerPalette.InkBlack, 0.55f);
        desaturationImage.color = new Color(desatTint.r, desatTint.g, desatTint.b,
            desaturationAmount * 0.45f * effectsScale);

        // Gamma calibration layers are deliberately NOT scaled by Screen
        // Effects - they are display calibration, not an effect.
        Color dark = ScreamerPalette.InkBlack;
        gammaDarkImage.color = new Color(dark.r, dark.g, dark.b, Mathf.Max(0f, -gamma) * 0.8f);
        Color bright = ScreamerPalette.NoodleCream;
        gammaBrightImage.color = new Color(bright.r, bright.g, bright.b, Mathf.Max(0f, gamma) * 0.5f);
    }

    void ApplyLetterbox()
    {
        if (letterboxTop == null) return;

        letterboxTop.rectTransform.anchorMin = new Vector2(0f, 1f - letterboxCurrent);
        letterboxTop.rectTransform.anchorMax = Vector2.one;
        letterboxBottom.rectTransform.anchorMin = Vector2.zero;
        letterboxBottom.rectTransform.anchorMax = new Vector2(1f, letterboxCurrent);

        bool visible = letterboxCurrent > 0.001f;
        letterboxTop.enabled = visible;
        letterboxBottom.enabled = visible;
    }

    // ------------------------- Construction -------------------------

    void BuildStack()
    {
        Canvas canvas = GetComponent<Canvas>();
        if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        // Back-to-front stacking follows creation order.
        desaturationImage = FullScreenImage("Fx_Desaturation");
        vignetteImage = FullScreenImage("Fx_Vignette");
        vignetteImage.sprite = BuildVignetteSprite();
        vignetteImage.type = Image.Type.Simple;
        grainImage = BuildGrainLayer();
        gammaDarkImage = FullScreenImage("Fx_GammaDark");
        gammaBrightImage = FullScreenImage("Fx_GammaBright");
        flashImage = FullScreenImage("Fx_Flash");
        letterboxTop = FullScreenImage("Fx_LetterboxTop");
        letterboxBottom = FullScreenImage("Fx_LetterboxBottom");

        Color plum = ScreamerPalette.MidnightPlum;
        letterboxTop.color = new Color(plum.r, plum.g, plum.b, 1f);
        letterboxBottom.color = new Color(plum.r, plum.g, plum.b, 1f);
        letterboxCurrent = 0f;
        ApplyLetterbox();
    }

    Image FullScreenImage(string name)
    {
        var child = new GameObject(name, typeof(RectTransform));
        child.transform.SetParent(transform, false);

        var rect = (RectTransform)child.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        var image = child.AddComponent<Image>();
        image.raycastTarget = false;
        image.color = Color.clear;
        return image;
    }

    RawImage BuildGrainLayer()
    {
        var child = new GameObject("Fx_Grain", typeof(RectTransform));
        child.transform.SetParent(transform, false);

        var rect = (RectTransform)child.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        grainTexture = BuildGrainTexture();

        var raw = child.AddComponent<RawImage>();
        raw.raycastTarget = false;
        raw.texture = grainTexture;
        Color cream = ScreamerPalette.NoodleCream;
        raw.color = new Color(cream.r, cream.g, cream.b, grainAlpha);
        return raw;
    }

    Sprite BuildVignetteSprite()
    {
        const int size = 256;
        vignetteTexture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Tex_Vignette",
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        var pixels = new Color32[size * size];
        Vector2 center = new Vector2((size - 1) * 0.5f, (size - 1) * 0.5f);
        float maxDistance = center.magnitude;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float normalized = Vector2.Distance(new Vector2(x, y), center) / maxDistance;
                // Transparent center, darkening shoulder toward the corners.
                float alpha = Mathf.Clamp01((normalized - 0.45f) / 0.55f);
                alpha *= alpha; // ease-in so the center stays clean
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        vignetteTexture.SetPixels32(pixels);
        vignetteTexture.Apply(false, false); // stays readable: FullRect sprite meshing reads it

        return Sprite.Create(vignetteTexture, new Rect(0f, 0f, size, size),
            new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
    }

    Texture2D BuildGrainTexture()
    {
        const int size = 64;
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Tex_Grain",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Point
        };

        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte value = (byte)Random.Range(0, 256);
            pixels[i] = new Color32(value, value, value, value);
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }
}
