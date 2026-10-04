using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Code-builds the survivor and monster pawns as scene objects; the setup wizard
/// saves them as prefabs under Assets/Screamer/Prefabs and registers them with the
/// NetworkManager. Art-directed primitives per GDD 13: huge googly eyes on the
/// survivors, final silhouette scale and accent rim lights on the monster skins, so
/// real models drop onto the skin roots with the mood already finished.
///
/// Runtime-compilable: never references UnityEditor. <c>mat</c> maps
/// (materialName, color) to a Material -- the wizard passes an asset-backed version,
/// play mode uses ScreamerPalette.MakeRuntimeMaterial.
/// </summary>
public static class CharacterFactory
{
    // ------------------------- survivor -------------------------

    /// <summary>
    /// Builds the survivor pawn: CharacterController + NetworkObject +
    /// ClientNetworkTransform + PlayerController, a CameraHolder at head height,
    /// googly eyes, a color cap, a rim light, and the ready-pose arm.
    /// </summary>
    public static GameObject BuildSurvivorPawn(Func<string, Color, Material> mat)
    {
        GameObject root = new GameObject("Survivor");

        CharacterController cc = root.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 0.95f, 0f);
        cc.height = 1.9f;
        cc.radius = 0.4f;

        root.AddComponent<NetworkObject>();
        root.AddComponent<ClientNetworkTransform>(); // owner-authoritative movement
        PlayerController player = root.AddComponent<PlayerController>();

        Material bodyMat = mat("SurvivorBody", Color.Lerp(ScreamerPalette.NoodleCream, ScreamerPalette.ShagRust, 0.25f));
        Prim(PrimitiveType.Capsule, "Body", root.transform,
            new Vector3(0f, 0.95f, 0f), new Vector3(0.8f, 0.95f, 0.8f), bodyMat);

        // Googly eyes (GDD 13) -- NoodleCream sclera, InkBlack pupils; pure white/black are banned.
        Material eyeWhite = mat("SurvivorEyeWhite", ScreamerPalette.NoodleCream);
        Material pupil = mat("SurvivorPupil", ScreamerPalette.InkBlack);
        Prim(PrimitiveType.Sphere, "Eye_L", root.transform, new Vector3(-0.14f, 1.52f, 0.30f), Vector3.one * 0.22f, eyeWhite);
        Prim(PrimitiveType.Sphere, "Eye_R", root.transform, new Vector3(0.14f, 1.52f, 0.30f), Vector3.one * 0.22f, eyeWhite);
        Prim(PrimitiveType.Sphere, "Pupil_L", root.transform, new Vector3(-0.14f, 1.53f, 0.40f), Vector3.one * 0.10f, pupil);
        Prim(PrimitiveType.Sphere, "Pupil_R", root.transform, new Vector3(0.14f, 1.53f, 0.40f), Vector3.one * 0.10f, pupil);

        // The cap takes the player color at runtime (PlayerController.ApplyColor).
        GameObject cap = Prim(PrimitiveType.Sphere, "Cap", root.transform,
            new Vector3(0f, 1.86f, 0.02f), new Vector3(0.52f, 0.20f, 0.52f), mat("SurvivorCap", ScreamerPalette.ScreamYellow));
        Prim(PrimitiveType.Cube, "CapBrim", root.transform,
            new Vector3(0f, 1.82f, 0.32f), new Vector3(0.4f, 0.05f, 0.28f), mat("SurvivorCapBrim", ScreamerPalette.ShagRust));

        // Ready-pose arm: rotated up and frozen when ReadyPose flips true.
        GameObject arm = Prim(PrimitiveType.Capsule, "ArmR", root.transform,
            new Vector3(0.5f, 1.15f, 0f), new Vector3(0.18f, 0.35f, 0.18f), bodyMat);
        arm.transform.localRotation = Quaternion.Euler(0f, 0f, -15f);

        // Player-color rim light (colorblind-safe identity at distance).
        GameObject rimGo = new GameObject("RimLight");
        rimGo.transform.SetParent(root.transform, false);
        rimGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        Light rim = rimGo.AddComponent<Light>();
        rim.type = LightType.Point;
        rim.range = 2.5f;
        rim.intensity = 0.7f;
        rim.color = ScreamerPalette.ScreamYellow;
        rim.shadows = LightShadows.None;

        GameObject camHolder = new GameObject("CameraHolder");
        camHolder.transform.SetParent(root.transform, false);
        camHolder.transform.localPosition = new Vector3(0f, 1.58f, 0.12f);

        player.cameraHolder = camHolder.transform;
        player.readyArm = arm.transform;
        player.capRenderer = cap.GetComponent<Renderer>();
        player.rimLight = rim;

        return root;
    }

    // ------------------------- monster -------------------------

    /// <summary>
    /// Builds the monster pawn with its three staged skin roots under "Skins":
    /// Skin_Zombie, Skin_Mutant, Skin_Mimic. Real models drop in as children of
    /// those roots and inherit the code-built rim lights.
    /// </summary>
    public static GameObject BuildMonsterPawn(Func<string, Color, Material> mat)
    {
        GameObject root = new GameObject("Monster");

        CharacterController cc = root.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 1.1f, 0f);
        cc.height = 2.2f;
        cc.radius = 0.5f;

        root.AddComponent<NetworkObject>();
        root.AddComponent<ClientNetworkTransform>();
        MonsterController monster = root.AddComponent<MonsterController>();
        MonsterSkinSelector selector = root.AddComponent<MonsterSkinSelector>();
        root.AddComponent<MimicDisguise>();
        root.AddComponent<MonsterAbilities>(); // per-skin [F] ability + closet [E] (GDD 14.1)

        GameObject camHolder = new GameObject("CameraHolder");
        camHolder.transform.SetParent(root.transform, false);
        camHolder.transform.localPosition = new Vector3(0f, 1.85f, 0.1f);
        monster.cameraHolder = camHolder.transform;

        GameObject skinsRoot = new GameObject("Skins");
        skinsRoot.transform.SetParent(root.transform, false);

        GameObject zombie = BuildZombieSkin(skinsRoot.transform, mat);
        GameObject mutant = BuildMutantSkin(skinsRoot.transform, mat);
        GameObject mimic = BuildMimicSkin(skinsRoot.transform, mat);
        selector.skins = new[] { zombie, mutant, mimic };

        // Only the zombie stays visible in the saved prefab; the selector applies
        // the synced pick on spawn.
        mutant.SetActive(false);
        mimic.SetActive(false);

        return root;
    }

    /// <summary>ZOMBIE (Shambler): sickly teal, forward shamble arms, a lazy fly halo.</summary>
    static GameObject BuildZombieSkin(Transform parent, Func<string, Color, Material> mat)
    {
        GameObject skin = NewChild("Skin_Zombie", parent);
        Material flesh = mat("MonsterZombieFlesh", Color.Lerp(ScreamerPalette.HauntedTeal, ScreamerPalette.NoodleCream, 0.35f));
        Material eye = mat("MonsterEyeRed", ScreamerPalette.MonsterRed);

        Prim(PrimitiveType.Capsule, "Body", skin.transform, new Vector3(0f, 1.0f, 0f), new Vector3(0.95f, 1.0f, 0.95f), flesh);
        Prim(PrimitiveType.Sphere, "Head", skin.transform, new Vector3(0f, 2.1f, 0.05f), Vector3.one * 0.55f, flesh);
        Prim(PrimitiveType.Sphere, "Eye_L", skin.transform, new Vector3(-0.12f, 2.16f, 0.3f), Vector3.one * 0.12f, eye);
        Prim(PrimitiveType.Sphere, "Eye_R", skin.transform, new Vector3(0.12f, 2.16f, 0.3f), Vector3.one * 0.12f, eye);

        GameObject armL = Prim(PrimitiveType.Capsule, "Arm_L", skin.transform, new Vector3(-0.42f, 1.45f, 0.45f), new Vector3(0.16f, 0.42f, 0.16f), flesh);
        armL.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        GameObject armR = Prim(PrimitiveType.Capsule, "Arm_R", skin.transform, new Vector3(0.42f, 1.45f, 0.45f), new Vector3(0.16f, 0.42f, 0.16f), flesh);
        armR.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

        // Flies halo: dark dots around the head (static staging, GDD 9.2).
        Material fly = mat("MonsterZombieFly", ScreamerPalette.InkBlack);
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.PI * 2f / 5f;
            Prim(PrimitiveType.Sphere, "Fly_" + i, skin.transform,
                new Vector3(Mathf.Cos(a) * 0.5f, 2.45f + 0.06f * (i % 2), Mathf.Sin(a) * 0.5f),
                Vector3.one * 0.06f, fly);
        }

        AddRimLight(skin.transform, "RimLight", ScreamerPalette.HauntedTeal, 0.9f, 4f);
        return skin;
    }

    /// <summary>MUTANT (Bruiser): the biggest silhouette, hot amber accents, heavy shoulders.</summary>
    static GameObject BuildMutantSkin(Transform parent, Func<string, Color, Material> mat)
    {
        GameObject skin = NewChild("Skin_Mutant", parent);
        Material hide = mat("MonsterMutantHide", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.MidnightPlum, 0.35f));
        Material hot = mat("MonsterMutantHot", ScreamerPalette.LamplightAmber);

        Prim(PrimitiveType.Capsule, "Body", skin.transform, new Vector3(0f, 1.1f, 0f), new Vector3(1.15f, 1.15f, 1.15f), hide);
        Prim(PrimitiveType.Cube, "Shoulder_L", skin.transform, new Vector3(-0.62f, 1.85f, 0f), new Vector3(0.5f, 0.35f, 0.55f), hide);
        Prim(PrimitiveType.Cube, "Shoulder_R", skin.transform, new Vector3(0.62f, 1.85f, 0f), new Vector3(0.5f, 0.35f, 0.55f), hide);
        Prim(PrimitiveType.Sphere, "Head", skin.transform, new Vector3(0f, 2.35f, 0.08f), Vector3.one * 0.5f, hide);
        Prim(PrimitiveType.Sphere, "Eye_L", skin.transform, new Vector3(-0.11f, 2.4f, 0.3f), Vector3.one * 0.11f, hot);
        Prim(PrimitiveType.Sphere, "Eye_R", skin.transform, new Vector3(0.11f, 2.4f, 0.3f), Vector3.one * 0.11f, hot);
        Prim(PrimitiveType.Cube, "ChestVent", skin.transform, new Vector3(0f, 1.35f, 0.5f), new Vector3(0.35f, 0.5f, 0.1f), hot);

        AddRimLight(skin.transform, "RimLight", ScreamerPalette.LamplightAmber, 1.0f, 4.5f);
        return skin;
    }

    /// <summary>MIMIC (Liar): furniture-flavored geometry with a toothy seam -- a liar at rest.</summary>
    static GameObject BuildMimicSkin(Transform parent, Func<string, Color, Material> mat)
    {
        GameObject skin = NewChild("Skin_Mimic", parent);
        Material shell = mat("MonsterMimicShell", ScreamerPalette.ShagRust);
        Material lid = mat("MonsterMimicLid", ScreamerPalette.MidnightPlum);
        Material tooth = mat("MonsterMimicTooth", ScreamerPalette.NoodleCream);
        Material eye = mat("MonsterEyeRed", ScreamerPalette.MonsterRed);

        Prim(PrimitiveType.Cube, "Torso", skin.transform, new Vector3(0f, 0.9f, 0f), new Vector3(1.3f, 1.1f, 1.0f), shell);
        Prim(PrimitiveType.Cube, "Lid", skin.transform, new Vector3(0f, 1.75f, 0.1f), new Vector3(1.25f, 0.5f, 0.95f), lid);
        for (int i = 0; i < 4; i++)
        {
            float x = -0.45f + i * 0.3f;
            Prim(PrimitiveType.Cube, "Tooth_" + i, skin.transform, new Vector3(x, 1.47f, 0.5f), new Vector3(0.12f, 0.18f, 0.08f), tooth);
        }
        Prim(PrimitiveType.Sphere, "Eye_L", skin.transform, new Vector3(-0.3f, 1.82f, 0.55f), Vector3.one * 0.1f, eye);
        Prim(PrimitiveType.Sphere, "Eye_R", skin.transform, new Vector3(0.3f, 1.82f, 0.55f), Vector3.one * 0.1f, eye);

        AddRimLight(skin.transform, "RimLight", ScreamerPalette.MonsterRed, 0.7f, 3.5f);
        return skin;
    }

    // ------------------------- helpers -------------------------

    static GameObject NewChild(string name, Transform parent)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go;
    }

    static void AddRimLight(Transform parent, string name, Color color, float intensity, float range)
    {
        GameObject go = NewChild(name, parent);
        go.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;
    }

    /// <summary>
    /// Creates a primitive child and strips its collider: the pawn's single hit target
    /// is the root CharacterController, so attack raycasts and the character physics
    /// never fight over child colliders.
    /// </summary>
    static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 localScale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        Renderer r = go.GetComponent<Renderer>();
        if (r != null && material != null) r.sharedMaterial = material;

        Collider c = go.GetComponent<Collider>();
        if (c != null)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(c);
            else UnityEngine.Object.DestroyImmediate(c);
        }
        return go;
    }
}
