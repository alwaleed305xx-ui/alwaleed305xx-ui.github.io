using System.Collections;
using UnityEngine;

/// <summary>
/// The public monster morph (GDD 9.1), client-side theater: at countdown zero
/// the chosen pawn's spot erupts in a MonsterRed smoke burst and a 1.2-second
/// silhouette swap - a survivor-shaped shadow shrinks away while a bigger,
/// wrong-shaped one grows in its place - in full view of everyone. The house
/// brown-out is module 6's half; the actual pawn despawn/spawn is module 1's.
///
/// The morph position arrives through
/// <see cref="GameManager.OnClientMorphPosition"/>: the server broadcasts the
/// chosen pawn's spot just before despawning it (pawns are frozen through the
/// countdown), so the smoke goes off exactly where the body vanished - without
/// the monster's identity ever replicating before the morph.
/// </summary>
public class MonsterMorphFx : MonoBehaviour
{
    [Header("Morph staging (GDD 9.1)")]
    [Tooltip("Seconds of silhouette swap.")]
    public float morphSeconds = 1.2f;
    [Tooltip("Red smoke quads in the burst (the GDD's 40, red and plum mixed).")]
    public int smokeQuadCount = 40;
    [Tooltip("Viewers within this distance get the morph camera shake, falling off with range.")]
    public float shakeRadius = 20f;

    Vector3 lastKnownPawnPosition;
    bool hasTrackedPosition;
    bool sawCountdown;
    Coroutine morphRoutine;

    void OnEnable()
    {
        GameManager.OnClientStateChanged += HandleStateChanged;
        GameManager.OnClientMorphPosition += HandleMorphPosition;
    }

    void OnDisable()
    {
        GameManager.OnClientStateChanged -= HandleStateChanged;
        GameManager.OnClientMorphPosition -= HandleMorphPosition;
    }

    void HandleMorphPosition(Vector3 at)
    {
        lastKnownPawnPosition = at;
        hasTrackedPosition = true;
    }

    void HandleStateChanged(GameManager.GameState state)
    {
        switch (state)
        {
            case GameManager.GameState.Countdown:
                sawCountdown = true;
                break;
            case GameManager.GameState.Lockdown:
                // Fire only on a witnessed countdown-zero transition. A client
                // joining mid-lockdown syncs straight into this state and must
                // not get a ghost-of-a-morph at the fallback position.
                if (sawCountdown) PlayMorph();
                sawCountdown = false;
                break;
            case GameManager.GameState.Lobby:
                hasTrackedPosition = false; // rematch: forget last round's spot
                sawCountdown = false;
                break;
        }
    }

    // ------------------------- The show -------------------------

    void PlayMorph()
    {
        // Fallback: the lobby crowd stands around the couch, so the couch is
        // the least wrong place for smoke if the pawn was never seen.
        Vector3 at = hasTrackedPosition
            ? lastKnownPawnPosition
            : HouseLayout.Couch + new Vector3(0f, 0.1f, 1.5f);
        Vector3 chest = at + Vector3.up * 1.1f;

        // MonsterRed smoke - its one sanctioned non-monster-HUD appearance,
        // because this IS the monster (GDD section 2). Plum undersmoke fills it out.
        ParticleFactory.Burst(chest, ScreamerPalette.MonsterRed, Mathf.Max(1, smokeQuadCount * 2 / 3), 3.2f, 1.1f);
        ParticleFactory.Burst(chest, ScreamerPalette.MidnightPlum, Mathf.Max(1, smokeQuadCount / 3), 2.2f, 1.3f);

        if (AudioDirector.Instance != null)
        {
            AudioDirector.Instance.Play(Sfx.MonsterDrone, at, 1f);          // something low arrives
            AudioDirector.Instance.Play(Sfx.Scream, at, 0.9f, 0.7f);        // something human leaves
        }

        // Everyone flinches a little; whoever stood next to it flinches a lot.
        if (ScreamerCam.Instance != null && Camera.main != null)
        {
            float distance = Vector3.Distance(Camera.main.transform.position, at);
            float proximity = Mathf.Clamp01(1f - distance / shakeRadius);
            ScreamerCam.Instance.Shake(Mathf.Lerp(0.12f, 0.45f, proximity), 0.45f);
        }

        if (morphRoutine != null) StopCoroutine(morphRoutine);
        morphRoutine = StartCoroutine(SilhouetteRoutine(at));
    }

    /// <summary>
    /// The 1.2 s scale-lerp swap: a survivor-shaped silhouette shrinks into the
    /// floor while a bulkier monster-shaped one overshoots up out of it. Both
    /// are temporary dressing; the real monster pawn is already spawning in the
    /// garage.
    /// </summary>
    IEnumerator SilhouetteRoutine(Vector3 at)
    {
        Material plum = ParticleFactory.MaterialFor(ScreamerPalette.MidnightPlum);

        GameObject survivorShape = MakeSilhouette("Morph_Survivor", at, plum, new Vector3(0.8f, 0.95f, 0.8f));
        GameObject monsterShape = MakeSilhouette("Morph_Monster", at, plum, Vector3.zero);

        // The monster silhouette gets the red rim that the real pawn carries.
        var lightGo = new GameObject("Morph_RedRim");
        lightGo.transform.SetParent(monsterShape.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        Light rim = lightGo.AddComponent<Light>();
        rim.type = LightType.Point;
        rim.color = ScreamerPalette.MonsterRed;
        rim.range = 5f;
        rim.intensity = 0f;
        rim.shadows = LightShadows.None;

        Vector3 survivorScale = new Vector3(0.8f, 0.95f, 0.8f);
        Vector3 monsterScale = new Vector3(1.45f, 1.3f, 1.45f);

        float t = 0f;
        while (t < morphSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / morphSeconds);

            if (survivorShape != null)
                survivorShape.transform.localScale = Vector3.Lerp(survivorScale, Vector3.zero,
                    Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(k * 1.6f)));

            if (monsterShape != null)
            {
                float grow = Mathf.SmoothStep(0f, 1f, k);
                float overshoot = 1f + 0.15f * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI); // lands with a pop
                monsterShape.transform.localScale = Vector3.Lerp(Vector3.zero, monsterScale, grow) * overshoot;
            }
            rim.intensity = Mathf.Sin(k * Mathf.PI) * 2.2f; // flares and dies with the smoke

            yield return null;
        }

        if (survivorShape != null) Destroy(survivorShape);
        if (monsterShape != null) Destroy(monsterShape);
        morphRoutine = null;
    }

    GameObject MakeSilhouette(string name, Vector3 at, Material material, Vector3 scale)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = name;
        go.transform.position = at + Vector3.up * 0.95f;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;

        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider); // pure theater never blocks anyone

        return go;
    }
}
