using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one component that is allowed to write to a practical light. Every
/// per-frame influence funnels through here so the systems never fight:
///
///   intensity = base x Perlin flicker x global envelope (HouseLightsDirector)
///               x noise flinch x (1 - house-tell dim)
///   color     = base lerped toward HauntedTeal by the house-tell amount,
///               overridden while an event strobe is running.
///
/// HouseTells feeds the tell targets (4 Hz poll, smoothed here), and
/// HouseLightsDirector drives flinches, strobes and the global envelope.
/// Instances self-register in <see cref="Active"/> so directors never need
/// scene searches.
/// </summary>
[RequireComponent(typeof(Light))]
public class LightFlicker : MonoBehaviour
{
    /// <summary>Every enabled practical, for the directors to iterate. Never null entries.</summary>
    public static readonly List<LightFlicker> Active = new List<LightFlicker>();

    [Header("Base look (captured from the Light if left at 0)")]
    public float baseIntensity;
    public Color baseColor = Color.clear;

    [Header("Perlin flicker (GDD 6.2)")]
    public bool usePerlin = true;
    [Tooltip("Intensity multiplier at Perlin 0.")]
    public float flickerMin = 0.8f;
    [Tooltip("Intensity multiplier at Perlin 1.")]
    public float flickerMax = 1.2f;
    [Tooltip("Perlin samples per second; boosted temporarily by BoostFlickerSpeed.")]
    public float flickerSpeed = 1.4f;

    [Header("House-tell identity (GDD 6.4)")]
    [Tooltip("Room center this practical belongs to; the tell triggers on monster distance to it.")]
    public Vector3 roomCenter;
    public string roomName = "";
    [Tooltip("The den hearth: additionally dims 35% when the monster is near the den.")]
    public bool isFireplace;
    [Tooltip("How fast the tell fades in/out (per second toward target).")]
    public float tellLerpSpeed = 2.5f;

    Light cachedLight;
    float perlinSeed;

    // House tell (targets set by HouseTells, smoothed here)
    float tellDesaturationTarget;
    float tellDimTarget;
    float tellDesaturation;
    float tellDim;

    // Noise flinch (HouseLightsDirector)
    float flinchFraction;
    float flinchTimer;

    // Event strobe (wrong note, noodle burn)
    Color strobeColor;
    float strobeHz;
    float strobeTimer;

    // Temporary flicker-speed boost (bathroom rhythm degradation)
    float speedBoost = 1f;
    float speedBoostTimer;

    void Awake()
    {
        cachedLight = GetComponent<Light>();
        perlinSeed = Random.Range(0f, 1000f);

        // Capture whatever the factory configured as the authored baseline.
        if (baseIntensity <= 0f) baseIntensity = cachedLight.intensity;
        if (baseColor == Color.clear) baseColor = cachedLight.color;
    }

    void OnEnable()
    {
        if (!Active.Contains(this)) Active.Add(this);
    }

    void OnDisable()
    {
        Active.Remove(this);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        // Smooth the house tell toward its polled target.
        tellDesaturation = Mathf.MoveTowards(tellDesaturation, tellDesaturationTarget, tellLerpSpeed * dt);
        tellDim = Mathf.MoveTowards(tellDim, tellDimTarget, tellLerpSpeed * dt);

        // Tick down timed effects.
        if (flinchTimer > 0f) flinchTimer -= dt;
        if (strobeTimer > 0f) strobeTimer -= dt;
        if (speedBoostTimer > 0f) speedBoostTimer -= dt;
        else speedBoost = 1f;

        // --- Intensity ---
        float flicker = 1f;
        if (usePerlin)
        {
            float noise = Mathf.PerlinNoise(perlinSeed, Time.time * flickerSpeed * speedBoost);
            flicker = Mathf.Lerp(flickerMin, flickerMax, noise);
        }

        float flinch = flinchTimer > 0f ? 1f - flinchFraction : 1f;
        float global = HouseLightsDirector.GlobalIntensityMultiplier;

        cachedLight.intensity = Mathf.Max(0f, baseIntensity * flicker * flinch * global * (1f - tellDim));

        // --- Color ---
        Color color = Color.Lerp(baseColor, ScreamerPalette.HauntedTeal, tellDesaturation);

        if (strobeTimer > 0f)
        {
            // Hz <= 0 means a solid flash for the whole duration; otherwise a
            // square wave alternating strobe color and the normal color.
            bool strobeOn = strobeHz <= 0f || Mathf.Repeat(strobeTimer * strobeHz, 1f) < 0.5f;
            if (strobeOn) color = strobeColor;
        }

        cachedLight.color = color;
    }

    // ------------------------- Director API -------------------------

    /// <summary>House tell targets (0..1 each); smoothed internally so the tell breathes in and out.</summary>
    public void SetTellTarget(float desaturation, float dim)
    {
        tellDesaturationTarget = Mathf.Clamp01(desaturation);
        tellDimTarget = Mathf.Clamp01(dim);
    }

    /// <summary>Momentary dip: the practical loses <paramref name="fraction"/> of its output for <paramref name="seconds"/>.</summary>
    public void Flinch(float fraction, float seconds)
    {
        flinchFraction = Mathf.Clamp01(fraction);
        flinchTimer = Mathf.Max(flinchTimer, seconds);
    }

    /// <summary>
    /// Event strobe. Hz &lt;= 0 holds <paramref name="color"/> solid for the
    /// duration (karaoke wrong note); a positive Hz alternates at that rate
    /// (noodle-burn klaxon light).
    /// </summary>
    public void Strobe(Color color, float hz, float seconds)
    {
        strobeColor = color;
        strobeHz = hz;
        strobeTimer = seconds;
    }

    /// <summary>Temporarily multiplies the Perlin sampling speed (bathroom rhythm feedback).</summary>
    public void BoostFlickerSpeed(float multiplier, float seconds)
    {
        speedBoost = Mathf.Max(1f, multiplier);
        speedBoostTimer = Mathf.Max(speedBoostTimer, seconds);
    }

    /// <summary>Clears every transient effect. Part of the rematch lighting reset.</summary>
    public void ResetDynamicState()
    {
        tellDesaturationTarget = 0f;
        tellDimTarget = 0f;
        tellDesaturation = 0f;
        tellDim = 0f;
        flinchTimer = 0f;
        strobeTimer = 0f;
        speedBoost = 1f;
        speedBoostTimer = 0f;
    }
}
