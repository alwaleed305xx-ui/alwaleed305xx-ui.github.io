using UnityEngine;

/// <summary>
/// Event lighting for the whole house (GDD 6.3): the task-complete amber
/// pulse, the finale surge, the morph brown-out, the loud-noise flinch, the
/// escape-door state light, and per-event practical strobes (karaoke wrong
/// note, noodle-burn klaxon, bathroom rhythm feedback).
///
/// It is both called directly (the contract methods below) and self-wired:
/// it subscribes to GameManager state changes, TaskBase completions and
/// NoiseSystem.OnNoiseVisible, so the lights react even if nobody remembers
/// to call it. Per-light writing stays inside LightFlicker - this director
/// only publishes the global envelope and per-light requests.
/// </summary>
public class HouseLightsDirector : MonoBehaviour
{
    public static HouseLightsDirector Instance { get; private set; }

    /// <summary>
    /// The house-wide intensity envelope every LightFlicker multiplies in.
    /// 1 at rest; raised by pulses/surges, crushed by the morph brown-out.
    /// </summary>
    public static float GlobalIntensityMultiplier { get; private set; } = 1f;

    [Header("Task-complete pulse (GDD 6.3)")]
    public float taskPulseBoost = 0.25f;
    public float taskPulseSeconds = 0.6f;

    [Header("Finale surge")]
    public float finaleSurgeBoost = 0.2f;
    public float finaleSurgeSeconds = 1f;

    [Header("Morph brown-out")]
    [Tooltip("Fraction of normal output during the brown-out.")]
    public float brownoutLevel = 0.2f;
    public float brownoutSeconds = 0.4f;

    [Header("Loud-noise flinch")]
    public float flinchMinLoudness = 0.8f;
    public float flinchRadius = 12f;
    public float flinchFraction = 0.3f;
    public float flinchSeconds = 0.2f;

    [Header("Escape-door light (wired by HouseFactory)")]
    public Light doorLight;
    public float doorLockedIntensity = 0.8f;
    public float doorFinaleIntensity = 1.3f;
    [Tooltip("Length of the power-surge double-flicker when the door unlocks.")]
    public float doorSurgeSeconds = 0.7f;

    [Header("Practicals with event strobes (wired by HouseFactory)")]
    public LightFlicker karaokeLight;
    public LightFlicker stoveLight;
    public LightFlicker bathroomLight;

    // Envelope timers.
    float taskPulseTimer;
    float surgeTimer;
    float brownoutTimer;

    // Door state.
    bool doorFinale;
    float doorSurgeTimer;

    Camera backgroundCamera;
    float cameraRecheckTimer;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        GameManager.OnClientStateChanged += HandleStateChanged;
        TaskBase.OnAnyTaskCompleted += HandleTaskCompleted;
        NoiseSystem.OnNoiseVisible += HandleNoiseVisible;
    }

    void OnDisable()
    {
        GameManager.OnClientStateChanged -= HandleStateChanged;
        TaskBase.OnAnyTaskCompleted -= HandleTaskCompleted;
        NoiseSystem.OnNoiseVisible -= HandleNoiseVisible;
        GlobalIntensityMultiplier = 1f;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        ApplyDoorState(instant: true);
    }

    void Update()
    {
        float dt = Time.deltaTime;

        if (taskPulseTimer > 0f) taskPulseTimer -= dt;
        if (surgeTimer > 0f) surgeTimer -= dt;
        if (brownoutTimer > 0f) brownoutTimer -= dt;

        // Boosts add, the brown-out multiplies them all down.
        float boost = 0f;
        if (taskPulseTimer > 0f)
            boost += taskPulseBoost * Mathf.SmoothStep(0f, 1f, taskPulseTimer / taskPulseSeconds);
        if (surgeTimer > 0f)
            boost += finaleSurgeBoost * Mathf.Clamp01(surgeTimer / 0.2f); // hold, then a quick fade
        float brownout = brownoutTimer > 0f ? brownoutLevel : 1f;

        GlobalIntensityMultiplier = (1f + boost) * brownout;

        UpdateDoorLight(dt);
        EnforceCameraBackground(dt);
    }

    // ------------------------- Contract API (GDD 6.3) -------------------------

    /// <summary>House-wide amber pulse: +25% intensity easing out over 0.6 s (hope made visible).</summary>
    public void TaskCompletePulse()
    {
        taskPulseTimer = taskPulseSeconds;
    }

    /// <summary>Sixth task: every light surges 20% for 1 s.</summary>
    public void FinaleSurge()
    {
        surgeTimer = finaleSurgeSeconds;
    }

    /// <summary>Countdown zero: all practicals brown out to 20% for 0.4 s while the monster morphs.</summary>
    public void MorphBrownout()
    {
        brownoutTimer = brownoutSeconds;
    }

    /// <summary>
    /// A distant scream is also a lighting event you feel: every practical
    /// within <see cref="flinchRadius"/> of a loud noise dips 30% for 0.2 s.
    /// </summary>
    public void NoiseFlinch(Vector3 position, float loudness)
    {
        if (loudness < flinchMinLoudness) return;

        float radiusSqr = flinchRadius * flinchRadius;
        for (int i = 0; i < LightFlicker.Active.Count; i++)
        {
            LightFlicker flicker = LightFlicker.Active[i];
            if ((flicker.transform.position - position).sqrMagnitude <= radiusSqr)
                flicker.Flinch(flinchFraction, flinchSeconds);
        }
    }

    /// <summary>Red lock light while locked; GhostMint flood (with a power-surge double-flicker) on finale.</summary>
    public void SetDoorFinaleState(bool finale)
    {
        if (doorFinale == finale)
        {
            ApplyDoorState(instant: false);
            return;
        }

        doorFinale = finale;
        doorSurgeTimer = finale ? doorSurgeSeconds : 0f;
        ApplyDoorState(instant: !finale);
    }

    /// <summary>Rematch: clears every envelope, strobe, tell and flinch, and re-locks the door light.</summary>
    public void ResetRoundLighting()
    {
        taskPulseTimer = 0f;
        surgeTimer = 0f;
        brownoutTimer = 0f;
        GlobalIntensityMultiplier = 1f;

        for (int i = 0; i < LightFlicker.Active.Count; i++)
            LightFlicker.Active[i].ResetDynamicState();

        doorFinale = false;
        doorSurgeTimer = 0f;
        ApplyDoorState(instant: true);

        if (ScreenFxOverlay.Instance != null)
            ScreenFxOverlay.Instance.ResetRoundFx();
    }

    // ------------------------- Self-wiring -------------------------

    void HandleStateChanged(GameManager.GameState state)
    {
        switch (state)
        {
            case GameManager.GameState.Lockdown:
                // Countdown just hit zero: the pawn is morphing in public view.
                MorphBrownout();
                break;

            case GameManager.GameState.Finale:
                FinaleSurge();
                SetDoorFinaleState(true);
                break;

            case GameManager.GameState.Lobby:
                // Rematch reset path - also covers first boot.
                ResetRoundLighting();
                break;
        }
    }

    void HandleTaskCompleted(TaskBase task)
    {
        TaskCompletePulse();
    }

    void HandleNoiseVisible(ulong sourceActorId, Vector3 position, float loudness, NoiseType type)
    {
        NoiseFlinch(position, loudness);

        // Diegetic event strobes (GDD 6.2), keyed off the same noise events
        // every client already receives - no extra network traffic.
        switch (type)
        {
            case NoiseType.Music when loudness >= 0.85f:
                // Karaoke wrong note: the toy-neon practical snaps MonsterRed for 0.15 s.
                StrobeIfNear(karaokeLight, position, ScreamerPalette.MonsterRed, 0f, 0.15f);
                break;

            case NoiseType.Alarm when loudness >= 0.95f:
                // Noodle burn: the over-stove light strobes MonsterRed at 2 Hz for 3 s.
                StrobeIfNear(stoveLight, position, ScreamerPalette.MonsterRed, 2f, 3f);
                break;

            case NoiseType.Plumbing:
                // The fluorescent tube reads the toilet's rhythm: ambient
                // plunging jitters it a little, a geyser sends it haywire.
                if (bathroomLight != null && WithinStrobeRange(bathroomLight, position))
                    bathroomLight.BoostFlickerSpeed(loudness >= 0.95f ? 6f : 2.5f, loudness >= 0.95f ? 3f : 1.5f);
                break;
        }
    }

    static bool WithinStrobeRange(LightFlicker flicker, Vector3 position)
    {
        return (flicker.transform.position - position).sqrMagnitude <= 64f; // 8 units
    }

    static void StrobeIfNear(LightFlicker flicker, Vector3 position, Color color, float hz, float seconds)
    {
        if (flicker != null && WithinStrobeRange(flicker, position))
            flicker.Strobe(color, hz, seconds);
    }

    // ------------------------- Door light -------------------------

    void UpdateDoorLight(float dt)
    {
        if (doorLight == null || doorSurgeTimer <= 0f) return;

        doorSurgeTimer -= dt;

        // Power-surge double-flicker: two hard off-blinks, then the mint flood.
        float elapsed = doorSurgeSeconds - doorSurgeTimer;
        bool dark = (elapsed >= 0.08f && elapsed < 0.16f) || (elapsed >= 0.28f && elapsed < 0.36f);
        doorLight.intensity = dark ? 0f : doorFinaleIntensity;

        if (doorSurgeTimer <= 0f) ApplyDoorState(instant: true);
    }

    void ApplyDoorState(bool instant)
    {
        if (doorLight == null) return;

        doorLight.color = doorFinale ? ScreamerPalette.GhostMint : ScreamerPalette.MonsterRed;
        if (instant || doorSurgeTimer <= 0f)
            doorLight.intensity = doorFinale ? doorFinaleIntensity : doorLockedIntensity;
    }

    // ------------------------- Camera background -------------------------

    /// <summary>
    /// The "skybox" is a solid MidnightPlum camera background (GDD 6.1). The
    /// main camera is created and re-parented by other modules, so re-apply
    /// whenever the cached reference goes stale.
    /// </summary>
    void EnforceCameraBackground(float dt)
    {
        cameraRecheckTimer -= dt;
        if (backgroundCamera != null && cameraRecheckTimer > 0f) return;
        cameraRecheckTimer = 2f;

        if (backgroundCamera == null) backgroundCamera = Camera.main;
        if (backgroundCamera == null) return;

        backgroundCamera.clearFlags = CameraClearFlags.SolidColor;
        backgroundCamera.backgroundColor = ScreamerPalette.MidnightPlum;
    }
}
