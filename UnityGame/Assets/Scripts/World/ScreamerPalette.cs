using UnityEngine;

/// <summary>
/// The single source of truth for every color in SCREAMER (GDD section 2).
/// All code-built materials, lights and UI reference these constants.
///
/// Rules the palette enforces by existing:
/// - Pure white (#FFFFFF) and pure black (#000000) are forbidden everywhere.
/// - MonsterRed belongs EXCLUSIVELY to the Monster and danger. Red in
///   peripheral vision always means death; nothing else may borrow it.
/// - Survivor identity colors are assigned in join order and stay
///   colorblind-safe at distance (with a deuteranopia-safe swap set).
///
/// The shader chain and the runtime material helper live here too, so the
/// project survives a future render-pipeline migration without touching any
/// other file.
/// </summary>
public static class ScreamerPalette
{
    // ------------------------- Core palette (GDD section 2) -------------------------

    /// <summary>#FFB84D - hero light color: den practicals, task-available glow, task-complete pulse.</summary>
    public static readonly Color LamplightAmber = new Color32(0xFF, 0xB8, 0x4D, 0xFF);

    /// <summary>#C4502E - floors, furniture accents, secondary UI accents.</summary>
    public static readonly Color ShagRust = new Color32(0xC4, 0x50, 0x2E, 0xFF);

    /// <summary>#1E6E6E - ambient tint, ALL shadows (never gray), yard moonlight, dread desaturation target.</summary>
    public static readonly Color HauntedTeal = new Color32(0x1E, 0x6E, 0x6E, 0xFF);

    /// <summary>#2B1B3D - deepest darks: skybox horizon, hallway, corridor ends, letterbox bars.</summary>
    public static readonly Color MidnightPlum = new Color32(0x2B, 0x1B, 0x3D, 0xFF);

    /// <summary>#FFE234 - survivor identity: progress fills, gag popups, READY pulses, title wordmark.</summary>
    public static readonly Color ScreamYellow = new Color32(0xFF, 0xE2, 0x34, 0xFF);

    /// <summary>#FF2E4C - EXCLUSIVELY the Monster: vignette, noise pings, kill feed, morph burst. Nothing else.</summary>
    public static readonly Color MonsterRed = new Color32(0xFF, 0x2E, 0x4C, 0xFF);

    /// <summary>#7DFFD4 - spectator ghosts, escape-door-unlocked state, ghost HUD tint.</summary>
    public static readonly Color GhostMint = new Color32(0x7D, 0xFF, 0xD4, 0xFF);

    /// <summary>#FFF3DC - all UI panels and text plates; default body text color.</summary>
    public static readonly Color NoodleCream = new Color32(0xFF, 0xF3, 0xDC, 0xFF);

    /// <summary>#141019 - UI text on cream plates, drop shadows (never #000000).</summary>
    public static readonly Color InkBlack = new Color32(0x14, 0x10, 0x19, 0xFF);

    // ------------------------- Lighting accents (GDD 6.2) -------------------------
    // These hexes are specified by the lighting spec; they live here so the
    // palette stays the single source of truth for every shipped color.

    /// <summary>#7E8FC4 - the directional "moon" over the whole map.</summary>
    public static readonly Color MoonlightBlue = new Color32(0x7E, 0x8F, 0xC4, 0xFF);

    /// <summary>#AFC4D8 - the den TV's emissive static (area glow only, never a real light).</summary>
    public static readonly Color TvStaticBlue = new Color32(0xAF, 0xC4, 0xD8, 0xFF);

    /// <summary>#C44FD0 - the karaoke practical. The only palette exception: diegetic toy-neon.</summary>
    public static readonly Color ToyNeonMagenta = new Color32(0xC4, 0x4F, 0xD0, 0xFF);

    /// <summary>#9FD8B8 - the bathroom's sickly green-teal fluorescent.</summary>
    public static readonly Color FluorescentGreen = new Color32(0x9F, 0xD8, 0xB8, 0xFF);

    // ------------------------- Survivor identity colors -------------------------

    /// <summary>
    /// Seven survivor colors, assigned in join order (bots take the tail of
    /// the list). Used for name tags, rim lights, dance notes, karaoke
    /// particles. Index with <see cref="Survivor(int)"/>, which also honors
    /// the colorblind setting.
    /// </summary>
    public static readonly Color[] SurvivorColors =
    {
        new Color32(0xFF, 0x8A, 0x3D, 0xFF), // orange
        new Color32(0x4D, 0xA6, 0xFF, 0xFF), // blue
        new Color32(0xFF, 0xD2, 0x3F, 0xFF), // gold
        new Color32(0xB8, 0x6B, 0xFF, 0xFF), // violet
        new Color32(0x3D, 0xE1, 0xAD, 0xFF), // jade
        new Color32(0xFF, 0x6B, 0xB5, 0xFF), // pink
        new Color32(0x9B, 0xCB, 0x3C, 0xFF)  // lime
    };

    /// <summary>Deuteranopia-safe replacement set (settings: "colorblind palette").</summary>
    public static readonly Color[] SurvivorColorsColorblind =
    {
        new Color32(0xE6, 0x9F, 0x00, 0xFF),
        new Color32(0x56, 0xB4, 0xE9, 0xFF),
        new Color32(0xF0, 0xE4, 0x42, 0xFF),
        new Color32(0xCC, 0x79, 0xA7, 0xFF),
        new Color32(0x00, 0x9E, 0x73, 0xFF),
        new Color32(0xD5, 0x5E, 0x00, 0xFF),
        new Color32(0x99, 0x99, 0x99, 0xFF)
    };

    /// <summary>
    /// The identity color for a join-order index. Honors
    /// ScreamerSettings.ColorblindPalette when the settings singleton exists;
    /// out-of-range indices wrap so an overfull lobby never throws.
    /// </summary>
    public static Color Survivor(int colorIndex)
    {
        bool colorblind = ScreamerSettings.Instance != null && ScreamerSettings.Instance.ColorblindPalette;
        Color[] set = colorblind ? SurvivorColorsColorblind : SurvivorColors;

        int count = set.Length;
        int index = ((colorIndex % count) + count) % count; // positive wrap, negatives included
        return set[index];
    }

    // ------------------------- Shader + material helpers -------------------------

    static Shader cachedLitShader;

    /// <summary>
    /// The lit shader for every code-built material, chosen by the pipeline
    /// that is actually active: Standard on Built-in, URP/Lit or HDRP/Lit
    /// when the project runs one of those. Choosing by which shaders merely
    /// exist is wrong - an imported pack can install the HDRP package while
    /// the project still renders Built-in, and HDRP/Lit is then pink.
    /// </summary>
    public static Shader LitShader()
    {
        if (cachedLitShader != null) return cachedLitShader;

        var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline;
        string pipelineType = pipeline == null ? "" : pipeline.GetType().FullName;

        if (pipelineType.Contains("Universal"))
            cachedLitShader = Shader.Find("Universal Render Pipeline/Lit");
        else if (pipelineType.Contains("HighDefinition") || pipelineType.Contains("HDRenderPipeline"))
            cachedLitShader = Shader.Find("HDRP/Lit");

        if (cachedLitShader == null) cachedLitShader = Shader.Find("Standard");
        return cachedLitShader;
    }

    /// <summary>Forget the cached shader, e.g. after the pipeline changes.</summary>
    public static void ResetShaderCache()
    {
        cachedLitShader = null;
    }

    /// <summary>
    /// Play-mode material fallback. The editor wizard passes an asset-backed
    /// factory instead; module tests and runtime rebuilds use this one.
    /// Setting <c>Material.color</c> targets the shader's main color property
    /// on every shader in the fallback chain.
    /// </summary>
    public static Material MakeRuntimeMaterial(string name, Color c)
    {
        var material = new Material(LitShader())
        {
            name = string.IsNullOrEmpty(name) ? "Mat_Runtime" : name,
            color = c
        };
        return material;
    }
}
