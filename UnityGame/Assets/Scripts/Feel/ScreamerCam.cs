using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// All camera feel in one component on the main camera (GDD section 12):
/// rotational Perlin shake, FOV kicks (sprint 70 to 76), timed roll tilts,
/// and the catch hitstop. Everything scales with the "Screen Shake" setting
/// (0-200%, where 200% is labeled YES), and everything runs on unscaled time
/// so juice still plays through hitstops and the photo-finish slow-mo.
///
/// The camera's local rotation belongs to whoever parented it (pawn scripts
/// set it once; the menu dolly rewrites it per frame). This component applies
/// its offset in LateUpdate and remembers what it wrote: next frame, if the
/// rotation is untouched it restores the baseline first, and if someone else
/// rewrote it, that rewrite becomes the new baseline. Shake therefore never
/// drifts and never fights another controller.
/// </summary>
public class ScreamerCam : MonoBehaviour
{
    public static ScreamerCam Instance { get; private set; }

    [Header("Shake tuning")]
    [Tooltip("Degrees of rotational shake at amplitude 1 (before the settings scale).")]
    public float maxShakeDegrees = 4.5f;
    [Tooltip("Perlin sampling frequency; higher = angrier rattle.")]
    public float shakeFrequency = 11f;
    [Tooltip("Stacked shakes are capped at this combined amplitude.")]
    public float maxAmplitude = 1.2f;

    [Header("Hitstop")]
    [Tooltip("Hard cap on any single hitstop request.")]
    public float maxHitstopSeconds = 0.35f;

    struct ShakeEvent
    {
        public float amplitude;
        public float remaining;
        public float duration;
    }

    readonly List<ShakeEvent> shakes = new List<ShakeEvent>();

    Camera cam;

    // Tilt state: a sine arc from 0 up to the requested roll and back.
    float tiltDegrees;
    float tiltRemaining;
    float tiltDuration;

    // FOV kick state: ease from the fov at call time to the target, then hold.
    float fovFrom;
    float fovTo;
    float fovElapsed;
    float fovDuration = -1f;

    // Baseline bookkeeping (see class comment).
    Quaternion lastWrittenLocal = Quaternion.identity;
    Quaternion baselineLocal = Quaternion.identity;
    bool hasWritten;

    float seedX, seedY, seedZ;
    bool hitstopRunning;

    void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
        seedX = Random.Range(0f, 100f);
        seedY = Random.Range(100f, 200f);
        seedZ = Random.Range(200f, 300f);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------- Public API (inter-module contract) -------------------------

    /// <summary>Adds a rotational Perlin shake. Amplitude 0..1; stacks with running shakes.</summary>
    public void Shake(float amplitude, float duration)
    {
        if (amplitude <= 0f || duration <= 0f) return;
        shakes.Add(new ShakeEvent
        {
            amplitude = Mathf.Clamp01(amplitude),
            remaining = duration,
            duration = duration
        });
    }

    /// <summary>
    /// Eases the field of view to <paramref name="targetFov"/> over
    /// <paramref name="duration"/> seconds and holds it there - callers kick
    /// back by asking for their base FOV again (sprint on: 76, sprint off: 70).
    /// </summary>
    public void FovKick(float targetFov, float duration)
    {
        if (cam == null) return;
        fovFrom = cam.fieldOfView;
        fovTo = Mathf.Clamp(targetFov, 30f, 130f);
        fovElapsed = 0f;
        fovDuration = Mathf.Max(0.01f, duration);
    }

    /// <summary>One roll arc: leans to <paramref name="degrees"/> and settles back within <paramref name="duration"/>.</summary>
    public void Tilt(float degrees, float duration)
    {
        if (duration <= 0f) return;
        tiltDegrees = degrees;
        tiltDuration = duration;
        tiltRemaining = duration;
    }

    /// <summary>
    /// Freeze-frame: time stops for <paramref name="duration"/> real seconds
    /// (chicken catch, monster bite). Skipped while the photo-finish slow-mo
    /// owns the clock, so the two can never fight over Time.timeScale.
    /// </summary>
    public void Hitstop(float duration)
    {
        if (hitstopRunning || PhotoFinishDirector.SlowMoActive) return;
        if (duration <= 0f) return;
        StartCoroutine(HitstopRoutine(Mathf.Min(duration, maxHitstopSeconds)));
    }

    // ------------------------- Per-frame -------------------------

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;

        // 1. Recover the baseline rotation (see class comment).
        Quaternion current = transform.localRotation;
        if (hasWritten && QuaternionsClose(current, lastWrittenLocal))
            current = baselineLocal; // nobody else touched it; peel our offset off
        baselineLocal = current;

        // 2. Decay running shakes and find the loudest one.
        float amplitude = 0f;
        for (int i = shakes.Count - 1; i >= 0; i--)
        {
            ShakeEvent shake = shakes[i];
            shake.remaining -= dt;
            if (shake.remaining <= 0f)
            {
                shakes.RemoveAt(i);
                continue;
            }
            shakes[i] = shake;

            float falloff = shake.remaining / shake.duration;
            amplitude = Mathf.Max(amplitude, shake.amplitude * falloff * falloff);
        }
        amplitude = Mathf.Min(amplitude, maxAmplitude) * ShakeSetting();

        // 3. Tilt arc: 0 -> degrees -> 0 across its duration.
        float tilt = 0f;
        if (tiltRemaining > 0f)
        {
            tiltRemaining -= dt;
            float progress = 1f - Mathf.Clamp01(tiltRemaining / tiltDuration);
            tilt = tiltDegrees * Mathf.Sin(progress * Mathf.PI) * Mathf.Clamp(ShakeSetting(), 0f, 1f);
        }

        // 4. Compose and write the offset.
        Quaternion offset = Quaternion.identity;
        if (amplitude > 0.0001f || Mathf.Abs(tilt) > 0.0001f)
        {
            float time = Time.unscaledTime * shakeFrequency;
            float pitch = PerlinSigned(seedX, time) * maxShakeDegrees * amplitude;
            float yaw = PerlinSigned(seedY, time) * maxShakeDegrees * amplitude;
            float roll = PerlinSigned(seedZ, time) * maxShakeDegrees * 0.6f * amplitude + tilt;
            offset = Quaternion.Euler(pitch, yaw, roll);
        }

        transform.localRotation = baselineLocal * offset;
        lastWrittenLocal = transform.localRotation;
        hasWritten = true;

        // 5. FOV ease-and-hold.
        if (cam != null && fovDuration > 0f)
        {
            fovElapsed += dt;
            float k = Mathf.Clamp01(fovElapsed / fovDuration);
            cam.fieldOfView = Mathf.Lerp(fovFrom, fovTo, Mathf.SmoothStep(0f, 1f, k));
            if (k >= 1f) fovDuration = -1f; // arrived; hold until the next kick
        }
    }

    IEnumerator HitstopRoutine(float duration)
    {
        hitstopRunning = true;
        float previous = Time.timeScale;
        Time.timeScale = 0f;

        yield return new WaitForSecondsRealtime(duration);

        // The slow-mo director reasserts its own scale each frame if it started
        // meanwhile; otherwise return the clock exactly as we found it.
        if (!PhotoFinishDirector.SlowMoActive)
            Time.timeScale = previous > 0f ? previous : 1f;
        hitstopRunning = false;
    }

    // ------------------------- Helpers -------------------------

    static float ShakeSetting()
    {
        return ScreamerSettings.Instance != null ? ScreamerSettings.Instance.ScreenShake : 1f;
    }

    static float PerlinSigned(float seed, float time)
    {
        return Mathf.PerlinNoise(seed, time) * 2f - 1f;
    }

    static bool QuaternionsClose(Quaternion a, Quaternion b)
    {
        return Mathf.Abs(Quaternion.Dot(a, b)) > 0.999999f;
    }
}
