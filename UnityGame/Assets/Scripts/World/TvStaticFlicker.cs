using UnityEngine;

/// <summary>
/// The den TV: an emissive quad whose static jumps to a new random brightness
/// every couple of frames (GDD 6.2: "2-frame random static flicker") and
/// silhouettes anyone who crosses it. Emission is written through a
/// MaterialPropertyBlock, so the shared material asset is never mutated.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class TvStaticFlicker : MonoBehaviour
{
    [Tooltip("Base static color; defaults to the palette's TV blue-gray.")]
    public Color staticColor = Color.clear;

    [Tooltip("Frames each static level holds before re-rolling.")]
    public int holdFrames = 2;

    [Tooltip("Random emission multiplier range per roll.")]
    public float minBrightness = 0.45f;
    public float maxBrightness = 1.35f;

    [Tooltip("Chance per roll of a near-black dropout frame (broadcast hiccup).")]
    [Range(0f, 1f)] public float dropoutChance = 0.06f;

    static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    Renderer cachedRenderer;
    MaterialPropertyBlock block;
    int framesHeld;
    float currentBrightness = 1f;

    void Awake()
    {
        cachedRenderer = GetComponent<Renderer>();
        block = new MaterialPropertyBlock();
        if (staticColor == Color.clear) staticColor = ScreamerPalette.TvStaticBlue;
    }

    void Update()
    {
        framesHeld++;
        if (framesHeld < Mathf.Max(1, holdFrames)) return;
        framesHeld = 0;

        currentBrightness = Random.value < dropoutChance
            ? 0.05f
            : Random.Range(minBrightness, maxBrightness);

        cachedRenderer.GetPropertyBlock(block);
        block.SetColor(EmissionColorId, staticColor * currentBrightness);
        cachedRenderer.SetPropertyBlock(block);
    }
}
