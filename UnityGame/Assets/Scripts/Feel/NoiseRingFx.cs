using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The sound-as-light doctrine (GDD 9.3): every audible event spawns a visible
/// shockwave ring in-world at its source - a flat procedural annulus that
/// expands from scale 0.5 to loudness x 8 over 0.6 seconds, colored by noise
/// type. Survivors see their own sins, ghosts and spectators see everything,
/// and house events are public knowledge; the monster reads its shaped HUD
/// pings instead (module 4's half of the doctrine).
///
/// Hangs entirely off <see cref="NoiseSystem.OnNoiseVisible"/>. Rings are
/// pooled; the mesh is built once in Awake.
/// </summary>
public class NoiseRingFx : MonoBehaviour
{
    [Header("Ring animation (GDD 9.3)")]
    [Tooltip("Lifetime of one shockwave ring.")]
    public float ringSeconds = 0.6f;
    [Tooltip("Final ring scale per point of loudness (loudness 1 = scale 8).")]
    public float scalePerLoudness = 8f;
    [Tooltip("Scale every ring starts from.")]
    public float startScale = 0.5f;
    [Tooltip("Even a whisper's ring grows to at least this.")]
    public float minimumEndScale = 1.5f;

    class ActiveRing
    {
        public Transform transform;
        public Renderer renderer;
        public float age;
        public float endScale;
    }

    readonly List<ActiveRing> active = new List<ActiveRing>();
    readonly Stack<ActiveRing> pool = new Stack<ActiveRing>();
    Mesh ringMesh;

    void Awake()
    {
        ringMesh = BuildRingMesh(48, 1f, 0.84f);
    }

    void OnEnable()
    {
        NoiseSystem.OnNoiseVisible += HandleNoiseVisible;
    }

    void OnDisable()
    {
        NoiseSystem.OnNoiseVisible -= HandleNoiseVisible;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        for (int i = active.Count - 1; i >= 0; i--)
        {
            ActiveRing ring = active[i];
            ring.age += dt;
            float k = Mathf.Clamp01(ring.age / ringSeconds);

            if (k >= 1f || ring.transform == null)
            {
                Park(ring);
                active.RemoveAt(i);
                continue;
            }

            float eased = 1f - (1f - k) * (1f - k); // ease-out: shockwaves start fast
            float scale = Mathf.Lerp(startScale, ring.endScale, eased);
            ring.transform.localScale = new Vector3(scale, 1f, scale);
        }
    }

    // ------------------------- Event handling -------------------------

    void HandleNoiseVisible(ulong sourceActorId, Vector3 position, float loudness, NoiseType type)
    {
        if (!ShouldLocalViewerSee(sourceActorId)) return;

        ActiveRing ring = TakeRing();
        ring.age = 0f;
        ring.endScale = Mathf.Max(minimumEndScale, Mathf.Clamp01(loudness) * scalePerLoudness);
        ring.transform.position = new Vector3(position.x, Mathf.Max(0.06f, position.y - 0.8f), position.z);
        ring.transform.localScale = new Vector3(startScale, 1f, startScale);
        ring.renderer.sharedMaterial = ParticleFactory.MaterialFor(ColorFor(type));
        ring.transform.gameObject.SetActive(true);
        active.Add(ring);
    }

    /// <summary>
    /// Who sees a ring (GDD 9.3): house events are public; you always see your
    /// own sins; ghosts and pawn-less spectators see everything.
    /// </summary>
    bool ShouldLocalViewerSee(ulong sourceActorId)
    {
        if (sourceActorId == ulong.MaxValue) return true; // the house snitching on itself

        var network = Unity.Netcode.NetworkManager.Singleton;
        if (network == null || !network.IsListening) return true; // isolated module test scene

        if (sourceActorId == network.LocalClientId) return true; // your own sin

        PlayerController local = PlayerController.Local;
        if (local != null) return local.IsGhost; // dead commentators see all

        // No pawn of our own: a spectator (join-in-progress ghost) mid-round
        // sees everything; the monster client reads its HUD pings instead.
        GameManager gm = GameManager.Instance;
        if (gm != null && gm.IAmMonster) return false;
        return gm != null
            && gm.State.Value != GameManager.GameState.Lobby
            && gm.State.Value != GameManager.GameState.Countdown;
    }

    /// <summary>Ring color per noise type. MonsterRed stays the monster's alone.</summary>
    static Color ColorFor(NoiseType type)
    {
        switch (type)
        {
            case NoiseType.Scream: return ScreamerPalette.ScreamYellow;
            case NoiseType.Music: return ScreamerPalette.ScreamYellow;
            case NoiseType.Alarm: return ScreamerPalette.LamplightAmber;
            case NoiseType.Chicken: return ScreamerPalette.NoodleCream;  // feather-white (pure white is forbidden)
            case NoiseType.Cooking: return ScreamerPalette.NoodleCream;
            case NoiseType.Plumbing: return ScreamerPalette.HauntedTeal;
            case NoiseType.Slap: return ScreamerPalette.ShagRust;
            case NoiseType.Boo: return ScreamerPalette.GhostMint;
            case NoiseType.DeathScream: return ScreamerPalette.GhostMint; // someone just became one
            default: return ScreamerPalette.NoodleCream;                  // footsteps and friends
        }
    }

    // ------------------------- Pool -------------------------

    ActiveRing TakeRing()
    {
        while (pool.Count > 0)
        {
            ActiveRing pooled = pool.Pop();
            if (pooled.transform != null) return pooled;
        }

        var go = new GameObject("Fx_NoiseRing");
        go.transform.SetParent(transform, false);
        var filter = go.AddComponent<MeshFilter>();
        filter.sharedMesh = ringMesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        go.SetActive(false);

        return new ActiveRing { transform = go.transform, renderer = renderer };
    }

    void Park(ActiveRing ring)
    {
        if (ring.transform != null) ring.transform.gameObject.SetActive(false);
        pool.Push(ring);
    }

    // ------------------------- Mesh -------------------------

    /// <summary>A flat annulus in the XZ plane, faced both up and down.</summary>
    static Mesh BuildRingMesh(int segments, float outerRadius, float innerRadius)
    {
        var vertices = new Vector3[segments * 2];
        var normals = new Vector3[segments * 2];
        var triangles = new int[segments * 12]; // 2 triangles per segment per face

        for (int i = 0; i < segments; i++)
        {
            float angle = i * Mathf.PI * 2f / segments;
            float cos = Mathf.Cos(angle);
            float sin = Mathf.Sin(angle);
            vertices[i * 2] = new Vector3(cos * outerRadius, 0f, sin * outerRadius);
            vertices[i * 2 + 1] = new Vector3(cos * innerRadius, 0f, sin * innerRadius);
            normals[i * 2] = Vector3.up;
            normals[i * 2 + 1] = Vector3.up;
        }

        int t = 0;
        for (int i = 0; i < segments; i++)
        {
            int o0 = i * 2;
            int i0 = i * 2 + 1;
            int o1 = ((i + 1) % segments) * 2;
            int i1 = ((i + 1) % segments) * 2 + 1;

            // Top face.
            triangles[t++] = o0; triangles[t++] = o1; triangles[t++] = i0;
            triangles[t++] = i0; triangles[t++] = o1; triangles[t++] = i1;
            // Bottom face (reversed winding) so low camera angles still see it.
            triangles[t++] = o0; triangles[t++] = i0; triangles[t++] = o1;
            triangles[t++] = i0; triangles[t++] = i1; triangles[t++] = o1;
        }

        var mesh = new Mesh { name = "Mesh_NoiseRing" };
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.triangles = triangles;
        mesh.RecalculateBounds();
        return mesh;
    }
}
