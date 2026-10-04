using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Textureless particles for the whole game (GDD section 12): colored quad and
/// sphere bursts, multi-color confetti, feather puffs, and the map-wide black
/// smoke column of noodle shame. Every effect is a code-configured
/// ParticleSystem rendering plain meshes with palette-colored materials - no
/// textures, no shader keywords, nothing to import.
///
/// Counts respect the quality tier (GDD 8.5): LOW halves every burst.
/// Materials come from the installed wizard factory when one is present
/// (asset-backed, so the lit shader always ships in builds) and fall back to
/// <see cref="ScreamerPalette.MakeRuntimeMaterial"/> at play time.
/// </summary>
public static class ParticleFactory
{
    static System.Func<string, Color, Material> materialFactory;
    static readonly Dictionary<Color, Material> materialCache = new Dictionary<Color, Material>();
    static Mesh quadMesh;
    static Mesh sphereMesh;

    /// <summary>
    /// Called by FeelFactory with the wizard's material delegate. Optional:
    /// everything works without it through the runtime material fallback.
    /// </summary>
    public static void Install(System.Func<string, Color, Material> mat)
    {
        materialFactory = mat;
        materialCache.Clear();
    }

    // ------------------------- Public API (inter-module contract) -------------------------

    /// <summary>A radial burst of tumbling colored quads.</summary>
    public static void Burst(Vector3 pos, Color color, int count, float speed = 3f, float life = 0.8f)
    {
        count = ScaleCount(count);
        if (count <= 0) return;

        ParticleSystem system = NewSystem("Fx_Burst", pos, QuadMesh(), MaterialFor(color), count);

        ParticleSystem.MainModule main = system.main;
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.5f, speed * 1.3f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.65f, life * 1.1f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.22f);
        main.gravityModifier = 0.55f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;

        system.Play();
        system.Emit(count);
        Object.Destroy(system.gameObject, life * 1.2f + 0.5f);
    }

    /// <summary>
    /// Celebration rain: an upward cone of small quads cycling through the
    /// given colors (survivor colors for the scream-therapy finish).
    /// </summary>
    public static void Confetti(Vector3 pos, Color[] colors, int count)
    {
        if (colors == null || colors.Length == 0)
        {
            Burst(pos, ScreamerPalette.ScreamYellow, count, 3.5f, 1.4f);
            return;
        }

        count = ScaleCount(count);
        if (count <= 0) return;

        // One system per color: the color is baked into the shared material,
        // which keeps the whole pipeline free of vertex-color shaders.
        int perColor = Mathf.Max(1, count / colors.Length);
        for (int c = 0; c < colors.Length; c++)
        {
            int emit = c == colors.Length - 1 ? count - perColor * (colors.Length - 1) : perColor;
            if (emit <= 0) continue;

            ParticleSystem system = NewSystem("Fx_Confetti", pos, QuadMesh(), MaterialFor(colors[c]), emit);
            system.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // cone points up

            ParticleSystem.MainModule main = system.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
            main.gravityModifier = 0.9f;

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 32f;
            shape.radius = 0.15f;

            ParticleSystem.RotationOverLifetimeModule spin = system.rotationOverLifetime;
            spin.enabled = true;
            spin.separateAxes = true;
            spin.x = new ParticleSystem.MinMaxCurve(-7f, 7f);
            spin.y = new ParticleSystem.MinMaxCurve(-7f, 7f);
            spin.z = new ParticleSystem.MinMaxCurve(-7f, 7f);

            system.Play();
            system.Emit(emit);
            Object.Destroy(system.gameObject, 2.2f);
        }
    }

    /// <summary>
    /// The public shaming geometry: a dark smoke column rising
    /// <paramref name="height"/> units for <paramref name="seconds"/>, visible
    /// over the roofline from anywhere on the map. Never skipped on LOW - the
    /// smoke column IS task feedback.
    /// </summary>
    public static void SmokeColumn(Vector3 pos, float height, float seconds)
    {
        height = Mathf.Max(2f, height);
        seconds = Mathf.Max(0.5f, seconds);
        float climbSeconds = Mathf.Clamp(height / 3.5f, 1f, 6f);

        Color smoke = Color.Lerp(ScreamerPalette.InkBlack, ScreamerPalette.MidnightPlum, 0.4f);
        ParticleSystem system = NewSystem("Fx_SmokeColumn", pos, SphereMesh(), MaterialFor(smoke), 160);
        system.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // cone points up

        ParticleSystem.MainModule main = system.main;
        main.loop = false;
        main.duration = seconds;
        main.startSpeed = new ParticleSystem.MinMaxCurve(height / climbSeconds * 0.9f, height / climbSeconds * 1.1f);
        main.startLifetime = new ParticleSystem.MinMaxCurve(climbSeconds * 0.9f, climbSeconds * 1.15f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.9f, 1.6f);
        main.gravityModifier = 0f;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = true;
        emission.rateOverTime = IsLowQuality() ? 7f : 14f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 6f;
        shape.radius = 0.5f;

        // Puffs fatten as they climb - a proper cartoon chimney.
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

        system.Play();
        Object.Destroy(system.gameObject, seconds + climbSeconds * 1.2f + 0.5f);
    }

    // ------------------------- Module extras (not part of the shared contract) -------------------------

    /// <summary>
    /// The module's shared solid-color material cache - also used by the ring
    /// and morph effects so every piece of juice pulls from one place.
    /// </summary>
    public static Material MaterialFor(Color color)
    {
        if (materialCache.TryGetValue(color, out Material cached) && cached != null)
            return cached;

        string name = "Mat_Fx_" + ColorUtility.ToHtmlStringRGB(color);
        Material material = materialFactory != null
            ? materialFactory(name, color)
            : ScreamerPalette.MakeRuntimeMaterial(name, color);

        materialCache[color] = material;
        return material;
    }

    // ------------------------- Internals -------------------------

    static int ScaleCount(int count)
    {
        return IsLowQuality() ? Mathf.Max(1, count / 2) : count;
    }

    static bool IsLowQuality()
    {
        return ScreamerSettings.Instance != null && ScreamerSettings.Instance.QualityTier <= 0;
    }

    static ParticleSystem NewSystem(string name, Vector3 pos, Mesh mesh, Material material, int maxParticles)
    {
        var go = new GameObject(name);
        go.transform.position = pos;

        var system = go.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = system.main;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = Mathf.Max(maxParticles, 8);
        main.startRotation3D = true;
        main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);

        ParticleSystem.EmissionModule emission = system.emission;
        emission.enabled = false; // bursts Emit() explicitly; SmokeColumn re-enables

        // Shrink-out so quads never pop off mid-air.
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0f));

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = mesh;
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        return system;
    }

    /// <summary>A unit quad mesh with both windings, so tumbling quads never vanish edge-on.</summary>
    static Mesh QuadMesh()
    {
        if (quadMesh != null) return quadMesh;

        var mesh = new Mesh { name = "Mesh_FxQuad" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f)
        };
        mesh.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 0, 1, 2, 0, 2, 3 }; // front + back faces
        mesh.RecalculateBounds();
        quadMesh = mesh;
        return quadMesh;
    }

    /// <summary>The built-in sphere mesh, borrowed from a temporary primitive once.</summary>
    static Mesh SphereMesh()
    {
        if (sphereMesh != null) return sphereMesh;

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphereMesh = temp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Object.Destroy(temp);
        else Object.DestroyImmediate(temp);
        return sphereMesh;
    }
}
