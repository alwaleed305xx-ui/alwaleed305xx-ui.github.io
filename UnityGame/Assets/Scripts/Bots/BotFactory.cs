using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime-compilable factory for the bot layer; the editor wizard calls
/// <see cref="Install"/> once while assembling the scene (after
/// RoundFlowFactory, so the GameManager object exists). Never references
/// UnityEditor.
///
/// Creates:
/// - "WaypointGraph": the A* graph, fed the hand-authored HouseLayout nodes
///   and edges and serialized into the scene;
/// - "BotManager" (+NetworkObject): the server-driven bot roster;
/// - "BotPawn_1..7": the pooled survivor bodies, parked hidden under the map.
///   They are in-scene placed NetworkObjects - the same replication mechanism
///   the GameManager and task stations use - because no prefab asset can be
///   authored from runtime code. Saving the wizard's scene is what stamps
///   their NetworkObject identity, so Install is meant to run through the
///   wizard; a play-mode Install still works for single-process module tests.
///
/// Materials use ScreamerPalette.MakeRuntimeMaterial (the sanctioned fallback;
/// Install's contract takes no material delegate) and serialize with the scene.
/// </summary>
public static class BotFactory
{
    /// <summary>Seven bodies: a full 8-seat lobby minus the one guaranteed human.</summary>
    public const int PawnPoolSize = 7;

    /// <summary>Hidden holding row for idle bodies, well below the house floor.</summary>
    static Vector3 ParkPosition(int index) => new Vector3(-9f + index * 3f, -25f, -30f);

    public static void Install()
    {
        if (Object.FindObjectOfType<BotManager>() != null)
            return; // already installed in this scene

        // ------------------------- waypoint graph -------------------------
        var graphGo = new GameObject("WaypointGraph");
        WaypointGraph graph = graphGo.AddComponent<WaypointGraph>();
        graph.SetGraph(HouseLayout.WaypointNodes, HouseLayout.WaypointEdges);

        // ------------------------- bot manager -------------------------
        var managerGo = new GameObject("BotManager");
        managerGo.AddComponent<NetworkObject>();
        BotManager manager = managerGo.AddComponent<BotManager>();

        // ------------------------- body pool -------------------------
        var pool = new BotPawn[PawnPoolSize];
        for (int i = 0; i < PawnPoolSize; i++)
            pool[i] = BuildPooledPawn(i);
        manager.pawnPool = pool;
    }

    // ------------------------- pawn construction -------------------------

    /// <summary>
    /// One pooled survivor body: matches the human pawn's silhouette (capsule,
    /// googly eyes, color cap - GDD 13's self-aware placeholder comedy) with
    /// bot plumbing instead of input: BotPawn + SurvivorBotBrain and a plain
    /// server-authoritative NetworkTransform, never ClientNetworkTransform.
    /// </summary>
    static BotPawn BuildPooledPawn(int index)
    {
        Vector3 park = ParkPosition(index);

        var root = new GameObject("BotPawn_" + (index + 1));
        root.transform.position = park;

        CharacterController cc = root.AddComponent<CharacterController>();
        cc.center = new Vector3(0f, 0.95f, 0f);
        cc.height = 1.9f;
        cc.radius = 0.4f;
        cc.enabled = false; // parked; BotPawn enables it on assignment

        root.AddComponent<NetworkObject>();
        root.AddComponent<NetworkTransform>(); // stock = server authoritative

        BotPawn pawn = root.AddComponent<BotPawn>();
        root.AddComponent<SurvivorBotBrain>();
        pawn.parkPosition = park;

        // ------------------------- visuals -------------------------
        var visual = new GameObject("Visual");
        visual.transform.SetParent(root.transform, false);

        Material body = ScreamerPalette.MakeRuntimeMaterial(
            "BotBody", Color.Lerp(ScreamerPalette.NoodleCream, ScreamerPalette.ShagRust, 0.25f));
        Material eyeWhite = ScreamerPalette.MakeRuntimeMaterial("BotEyeWhite", ScreamerPalette.NoodleCream);
        Material pupil = ScreamerPalette.MakeRuntimeMaterial("BotPupil", ScreamerPalette.InkBlack);

        Prim(PrimitiveType.Capsule, "Body", visual.transform,
            new Vector3(0f, 0.95f, 0f), new Vector3(0.8f, 0.95f, 0.8f), body);

        // Googly eyes, slightly crossed: these people are not okay.
        Prim(PrimitiveType.Sphere, "Eye_L", visual.transform, new Vector3(-0.14f, 1.52f, 0.30f), Vector3.one * 0.22f, eyeWhite);
        Prim(PrimitiveType.Sphere, "Eye_R", visual.transform, new Vector3(0.14f, 1.52f, 0.30f), Vector3.one * 0.22f, eyeWhite);
        Prim(PrimitiveType.Sphere, "Pupil_L", visual.transform, new Vector3(-0.12f, 1.53f, 0.40f), Vector3.one * 0.10f, pupil);
        Prim(PrimitiveType.Sphere, "Pupil_R", visual.transform, new Vector3(0.12f, 1.53f, 0.40f), Vector3.one * 0.10f, pupil);

        // The cap takes the roster color at runtime (BotPawn.ApplyColor).
        GameObject cap = Prim(PrimitiveType.Sphere, "Cap", visual.transform,
            new Vector3(0f, 1.86f, 0.02f), new Vector3(0.52f, 0.20f, 0.52f),
            ScreamerPalette.MakeRuntimeMaterial("BotCap_" + index, ScreamerPalette.ScreamYellow));
        Prim(PrimitiveType.Cube, "CapBrim", visual.transform,
            new Vector3(0f, 1.82f, 0.32f), new Vector3(0.4f, 0.05f, 0.28f),
            ScreamerPalette.MakeRuntimeMaterial("BotCapBrim", ScreamerPalette.ShagRust));

        visual.SetActive(false); // hidden until a bot wears the body

        // Roster-color rim light, same identity language as human pawns.
        var rimGo = new GameObject("RimLight");
        rimGo.transform.SetParent(root.transform, false);
        rimGo.transform.localPosition = new Vector3(0f, 1.4f, 0f);
        Light rim = rimGo.AddComponent<Light>();
        rim.type = LightType.Point;
        rim.range = 2.5f;
        rim.intensity = 0.7f;
        rim.color = ScreamerPalette.ScreamYellow;
        rim.shadows = LightShadows.None;
        rim.enabled = false;

        // ------------------------- floating name tag -------------------------
        Text nameLabel = BuildNameTag(root.transform, out Transform tagRoot);

        pawn.visualRoot = visual;
        pawn.capRenderer = cap.GetComponent<Renderer>();
        pawn.rimLight = rim;
        pawn.nameLabel = nameLabel;
        pawn.nameTagRoot = tagRoot;

        return pawn;
    }

    static Text BuildNameTag(Transform parent, out Transform tagRoot)
    {
        var canvasGo = new GameObject("NameTag");
        canvasGo.transform.SetParent(parent, false);
        canvasGo.transform.localPosition = new Vector3(0f, 2.3f, 0f);
        canvasGo.transform.localScale = Vector3.one * 0.01f;

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var canvasRect = (RectTransform)canvasGo.transform;
        canvasRect.sizeDelta = new Vector2(240f, 50f);

        var textGo = new GameObject("Name");
        textGo.transform.SetParent(canvasGo.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Text label = textGo.AddComponent<Text>();
        label.font = Font.CreateDynamicFontFromOSFont("Arial", 36);
        label.fontSize = 36;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.color = ScreamerPalette.NoodleCream;
        label.text = "";

        canvasGo.SetActive(false); // BotPawn shows it with the body
        tagRoot = canvasGo.transform;
        return label;
    }

    /// <summary>
    /// Primitive child with its collider stripped: the pawn's single hit target
    /// is the root CharacterController, so the monster's attack raycast and the
    /// movement physics never fight over child colliders.
    /// </summary>
    static GameObject Prim(PrimitiveType type, string name, Transform parent,
        Vector3 localPos, Vector3 localScale, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;

        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer != null && material != null) renderer.sharedMaterial = material;

        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
        {
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
        }
        return go;
    }
}
