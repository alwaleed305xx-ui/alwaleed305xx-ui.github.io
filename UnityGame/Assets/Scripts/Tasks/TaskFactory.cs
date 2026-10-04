using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds every task station and the escape cellar door from art-directed
/// primitives, at the binding HouseLayout positions (GDD 3.4). Runtime-safe
/// (no UnityEditor): the setup wizard calls it in edit mode and saves the
/// scene; play-mode bootstrapping may call it too.
///
/// Materials come through the provided factory delegate (name + color ->
/// material); shader resolution is the wizard's / palette's problem, never
/// this file's. Stations are in-scene placed NetworkObjects; the chicken
/// additionally gets a server-authoritative NetworkTransform so its panicked
/// sprints sync to every client.
/// </summary>
public static class TaskFactory
{
    // ------------------------- Public build API -------------------------

    /// <summary>
    /// Builds all six stations at their HouseLayout positions and registers
    /// them on a TaskManager (found or created). Order matches the padlock
    /// row: scream, karaoke, dance, noodles, chicken, toilet.
    /// </summary>
    public static TaskBase[] BuildAllStations(Transform parent, Func<string, Color, Material> mat)
    {
        var tasks = new TaskBase[]
        {
            BuildScreamStation(parent, mat),
            BuildKaraokeStation(parent, mat),
            BuildDancePad(parent, mat),
            BuildNoodleStove(parent, mat),
            BuildChicken(parent, mat),
            BuildToilet(parent, mat)
        };

        TaskManager manager = EnsureTaskManager();
        manager.allTasks = tasks;
        return tasks;
    }

    /// <summary>
    /// Builds the angled storm-cellar double door with its six oversized
    /// padlocks and the hydraulics sign, and wires it into the TaskManager.
    /// </summary>
    public static EscapeDoor BuildCellarDoor(Transform parent, Func<string, Color, Material> mat)
    {
        GameObject root = NewRoot("EscapeDoor", parent, HouseLayout.CellarDoor);

        // One generous collider: the [E] target and the thing you sprint into.
        BoxCollider interact = root.AddComponent<BoxCollider>();
        interact.center = new Vector3(0f, 0.4f, 0f);
        interact.size = new Vector3(3.9f, 2.2f, 1.8f);

        Material frameMat = mat("Mat_DoorFrame", Dim(ScreamerPalette.ShagRust, 0.55f));
        Material woodMat = mat("Mat_DoorWood", Dim(ScreamerPalette.ShagRust, 0.85f));
        Material brassMat = mat("Mat_PadlockBrass", ScreamerPalette.LamplightAmber);
        Material ironMat = mat("Mat_PadlockIron", ScreamerPalette.InkBlack);

        // The tilted body: a slab leaning toward the fence like a storm door.
        GameObject body = NewChild("DoorBody", root.transform, Vector3.zero);
        body.transform.localRotation = Quaternion.Euler(-40f, 0f, 0f);

        Part(PrimitiveType.Cube, "Frame", body.transform,
            new Vector3(0f, -0.05f, 0f), new Vector3(3.9f, 0.12f, 2.5f), frameMat);

        // Panels swing on outer-edge hinges when the finale blows them open.
        Transform leftHinge = NewChild("Hinge_L", body.transform, new Vector3(-1.75f, 0.08f, 0f)).transform;
        Part(PrimitiveType.Cube, "Panel_L", leftHinge,
            new Vector3(0.86f, 0f, 0f), new Vector3(1.68f, 0.12f, 2.3f), woodMat);

        Transform rightHinge = NewChild("Hinge_R", body.transform, new Vector3(1.75f, 0.08f, 0f)).transform;
        Part(PrimitiveType.Cube, "Panel_R", rightHinge,
            new Vector3(-0.86f, 0f, 0f), new Vector3(1.68f, 0.12f, 2.3f), woodMat);

        // Six oversized padlocks across the seam: X -1.25..+1.25, world y 1.0
        // (GDD 3.4). They hover on the face and vanish one per finished chore.
        var padlocks = new GameObject[6];
        for (int i = 0; i < 6; i++)
        {
            float x = -1.25f + 0.5f * i;
            padlocks[i] = BuildPadlock("Padlock_" + (i + 1), root.transform,
                new Vector3(x, 0.4f, -0.62f), brassMat, ironMat);
        }

        // The sign faces the yard; it is load-bearing for the joke.
        Text sign = BuildWorldLabel(root.transform, GameCopy.DoorFinaleSign,
            new Vector3(0f, 1.7f, -0.9f), 4.6f, 34, billboard: false);

        EscapeDoor door = root.AddComponent<EscapeDoor>();
        door.padlocks = padlocks;
        door.leftHinge = leftHinge;
        door.rightHinge = rightHinge;
        door.signLabels = new[] { sign };

        root.AddComponent<NetworkObject>();

        TaskManager manager = EnsureTaskManager();
        manager.escapeDoor = door;
        return door;
    }

    // ------------------------- Stations -------------------------

    static TaskBase BuildScreamStation(Transform parent, Func<string, Color, Material> mat)
    {
        GameObject root = NewStationRoot("Task_ScreamTherapy", parent, HouseLayout.ScreamStation,
            new Vector3(1.1f, 1.3f, 1.1f), new Vector3(0f, 0.3f, 0f));

        Material rug = mat("Mat_TherapyRug", Dim(ScreamerPalette.ShagRust, 0.7f));
        Material base_ = mat("Mat_TherapyBase", ScreamerPalette.ShagRust);
        Material dark = mat("Mat_TaskDark", ScreamerPalette.InkBlack);

        Part(PrimitiveType.Cube, "Rug", root.transform,
            new Vector3(0f, -0.48f, 0f), new Vector3(1.5f, 0.04f, 1.5f), rug);
        Part(PrimitiveType.Cube, "Base", root.transform,
            new Vector3(0f, -0.25f, 0f), new Vector3(0.9f, 0.5f, 0.9f), base_);
        Part(PrimitiveType.Cylinder, "MicStand", root.transform,
            new Vector3(0f, 0.3f, 0f), new Vector3(0.08f, 0.35f, 0.08f), dark);
        Part(PrimitiveType.Sphere, "Mic", root.transform,
            new Vector3(0f, 0.75f, 0f), new Vector3(0.28f, 0.28f, 0.28f), dark);

        TaskScream task = root.AddComponent<TaskScream>();
        FinishStation(task, root, 1.9f);
        return task;
    }

    static TaskBase BuildKaraokeStation(Transform parent, Func<string, Color, Material> mat)
    {
        GameObject root = NewStationRoot("Task_Karaoke", parent, HouseLayout.KaraokeStation,
            new Vector3(1.1f, 1.3f, 0.9f), new Vector3(0f, 0.15f, 0f));

        Material box = mat("Mat_KaraokeBox", ScreamerPalette.ShagRust);
        Material panel = mat("Mat_KaraokePanel", ScreamerPalette.ScreamYellow);
        Material cream = mat("Mat_TaskCream", ScreamerPalette.NoodleCream);
        Material dark = mat("Mat_TaskDark", ScreamerPalette.InkBlack);

        Part(PrimitiveType.Cube, "Body", root.transform,
            new Vector3(0f, 0f, 0f), new Vector3(0.9f, 1f, 0.7f), box);
        Part(PrimitiveType.Cube, "FrontPanel", root.transform,
            new Vector3(0f, 0.12f, -0.38f), new Vector3(0.7f, 0.5f, 0.06f), panel);
        GameObject micArm = Part(PrimitiveType.Cylinder, "Mic", root.transform,
            new Vector3(0.28f, 0.68f, 0f), new Vector3(0.06f, 0.22f, 0.06f), cream);
        micArm.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
        Part(PrimitiveType.Sphere, "MicTip", root.transform,
            new Vector3(0.38f, 0.88f, 0f), new Vector3(0.14f, 0.14f, 0.14f), dark);

        TaskKaraoke task = root.AddComponent<TaskKaraoke>();
        FinishStation(task, root, 1.8f);
        return task;
    }

    static TaskBase BuildDancePad(Transform parent, Func<string, Color, Material> mat)
    {
        // No root collider: the pad is walked on, the tiles are the ray target.
        GameObject root = NewRoot("Task_DanceFloor", parent, HouseLayout.DancePad);

        Material tileA = mat("Mat_DanceTileA", ScreamerPalette.LamplightAmber);
        Material tileB = mat("Mat_DanceTileB", ScreamerPalette.ShagRust);
        Material dark = mat("Mat_TaskDark", ScreamerPalette.InkBlack);

        TaskDance task = root.AddComponent<TaskDance>();

        // 4x4 beat-cycled pad; index = row * 4 + column, mirrored by the
        // cosmetic wave in TaskDance.ClientCosmeticTick.
        var tiles = new Renderer[16];
        for (int row = 0; row < 4; row++)
        {
            for (int column = 0; column < 4; column++)
            {
                int i = row * 4 + column;
                // Tiles keep their colliders: they are both the walkable floor
                // and the station's [E] raycast target.
                GameObject tile = Part(PrimitiveType.Cube, "Tile_" + i, root.transform,
                    new Vector3(-1.5f + column, 0.04f, -1.5f + row),
                    new Vector3(0.96f, 0.08f, 0.96f),
                    (row + column) % 2 == 0 ? tileA : tileB, keepCollider: true);
                tiles[i] = tile.GetComponent<Renderer>();
            }
        }
        task.padTiles = tiles;

        // Two speakers so the UNLICENSED DISCO has a license to stand near.
        Part(PrimitiveType.Cube, "Speaker_L", root.transform,
            new Vector3(-2.1f, 0.45f, 1.8f), new Vector3(0.5f, 0.95f, 0.4f), dark);
        Part(PrimitiveType.Cube, "Speaker_R", root.transform,
            new Vector3(2.1f, 0.45f, 1.8f), new Vector3(0.5f, 0.95f, 0.4f), dark);

        FinishStation(task, root, 2.3f);
        return task;
    }

    static TaskBase BuildNoodleStove(Transform parent, Func<string, Color, Material> mat)
    {
        GameObject root = NewStationRoot("Task_Noodles", parent, HouseLayout.NoodleStove,
            new Vector3(1.1f, 1.1f, 0.9f), new Vector3(0f, -0.2f, 0f));

        Material stove = mat("Mat_Stove", Dim(ScreamerPalette.ShagRust, 0.4f));
        Material burner = mat("Mat_TaskDark", ScreamerPalette.InkBlack);
        Material pot = mat("Mat_NoodlePot", ScreamerPalette.NoodleCream);
        Material noodles = mat("Mat_Noodles", ScreamerPalette.ScreamYellow);

        Part(PrimitiveType.Cube, "Stove", root.transform,
            new Vector3(0f, -0.45f, 0f), new Vector3(1f, 0.85f, 0.8f), stove);
        Part(PrimitiveType.Cylinder, "Burner", root.transform,
            new Vector3(0f, -0.01f, 0f), new Vector3(0.55f, 0.02f, 0.55f), burner);
        Part(PrimitiveType.Sphere, "Pot", root.transform,
            new Vector3(0f, 0.16f, 0f), new Vector3(0.55f, 0.34f, 0.55f), pot);
        Part(PrimitiveType.Sphere, "Noodles", root.transform,
            new Vector3(0f, 0.3f, 0f), new Vector3(0.3f, 0.12f, 0.3f), noodles);

        TaskNoodles task = root.AddComponent<TaskNoodles>();
        FinishStation(task, root, 1.6f);
        return task;
    }

    static TaskBase BuildChicken(Transform parent, Func<string, Color, Material> mat)
    {
        GameObject root = NewRoot("Task_Chicken", parent, HouseLayout.ChickenStart);

        Material feathers = mat("Mat_ChickenBody", ScreamerPalette.NoodleCream);
        Material beakMat = mat("Mat_ChickenBeak", ScreamerPalette.ScreamYellow);
        Material combMat = mat("Mat_ChickenComb", ScreamerPalette.ShagRust);
        Material pupilMat = mat("Mat_TaskDark", ScreamerPalette.InkBlack);

        Part(PrimitiveType.Sphere, "Body", root.transform,
            new Vector3(0f, 0f, 0f), new Vector3(0.55f, 0.5f, 0.65f), feathers);
        Part(PrimitiveType.Sphere, "Head", root.transform,
            new Vector3(0f, 0.35f, 0.25f), new Vector3(0.3f, 0.3f, 0.3f), feathers);
        Part(PrimitiveType.Cube, "Beak", root.transform,
            new Vector3(0f, 0.33f, 0.45f), new Vector3(0.09f, 0.08f, 0.18f), beakMat);
        Part(PrimitiveType.Cube, "Comb", root.transform,
            new Vector3(0f, 0.55f, 0.22f), new Vector3(0.06f, 0.16f, 0.2f), combMat);
        Part(PrimitiveType.Sphere, "Wing_L", root.transform,
            new Vector3(-0.28f, 0.02f, 0f), new Vector3(0.14f, 0.3f, 0.4f), feathers);
        Part(PrimitiveType.Sphere, "Wing_R", root.transform,
            new Vector3(0.28f, 0.02f, 0f), new Vector3(0.14f, 0.3f, 0.4f), feathers);
        Part(PrimitiveType.Cylinder, "Leg_L", root.transform,
            new Vector3(-0.12f, -0.33f, 0f), new Vector3(0.05f, 0.14f, 0.05f), beakMat);
        Part(PrimitiveType.Cylinder, "Leg_R", root.transform,
            new Vector3(0.12f, -0.33f, 0f), new Vector3(0.05f, 0.14f, 0.05f), beakMat);

        // Huge googly eyes: the honest placeholder-comedy contract (GDD 13).
        Part(PrimitiveType.Sphere, "Eye_L", root.transform,
            new Vector3(-0.1f, 0.42f, 0.38f), new Vector3(0.11f, 0.11f, 0.11f), feathers);
        Part(PrimitiveType.Sphere, "Eye_R", root.transform,
            new Vector3(0.1f, 0.42f, 0.38f), new Vector3(0.11f, 0.11f, 0.11f), feathers);
        Part(PrimitiveType.Sphere, "Pupil_L", root.transform,
            new Vector3(-0.1f, 0.42f, 0.43f), new Vector3(0.05f, 0.05f, 0.05f), pupilMat);
        Part(PrimitiveType.Sphere, "Pupil_R", root.transform,
            new Vector3(0.1f, 0.42f, 0.43f), new Vector3(0.05f, 0.05f, 0.05f), pupilMat);

        SphereCollider body = root.AddComponent<SphereCollider>();
        body.radius = 0.55f;
        body.center = new Vector3(0f, 0.05f, 0f);

        TaskChicken task = root.AddComponent<TaskChicken>();
        root.AddComponent<NetworkObject>();
        root.AddComponent<NetworkTransform>(); // server-authoritative sync of the panic

        task.ApplyBalanceDefaults();
        task.worldLabel = BuildWorldLabel(root.transform, task.taskName, new Vector3(0f, 1.15f, 0f), 2.6f, 30, billboard: true);
        return task;
    }

    static TaskBase BuildToilet(Transform parent, Func<string, Color, Material> mat)
    {
        GameObject root = NewStationRoot("Task_Toilet", parent, HouseLayout.ToiletStation,
            new Vector3(0.9f, 1.3f, 1.1f), new Vector3(0f, 0.1f, 0f));

        Material porcelain = mat("Mat_Porcelain", ScreamerPalette.NoodleCream);
        Material dark = mat("Mat_TaskDark", ScreamerPalette.InkBlack);
        Material plungerMat = mat("Mat_Plunger", ScreamerPalette.ShagRust);

        Part(PrimitiveType.Cube, "Base", root.transform,
            new Vector3(0f, -0.28f, 0f), new Vector3(0.5f, 0.45f, 0.7f), porcelain);
        Part(PrimitiveType.Cylinder, "Bowl", root.transform,
            new Vector3(0f, 0.02f, -0.08f), new Vector3(0.56f, 0.18f, 0.6f), porcelain);
        Part(PrimitiveType.Cylinder, "SeatRing", root.transform,
            new Vector3(0f, 0.21f, -0.08f), new Vector3(0.5f, 0.02f, 0.54f), dark);
        Part(PrimitiveType.Cube, "Tank", root.transform,
            new Vector3(0f, 0.25f, 0.32f), new Vector3(0.55f, 0.6f, 0.24f), porcelain);

        GameObject plungerHandle = Part(PrimitiveType.Cylinder, "PlungerHandle", root.transform,
            new Vector3(0.45f, -0.05f, -0.25f), new Vector3(0.04f, 0.3f, 0.04f), plungerMat);
        plungerHandle.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);
        Part(PrimitiveType.Sphere, "PlungerCup", root.transform,
            new Vector3(0.52f, -0.38f, -0.25f), new Vector3(0.22f, 0.12f, 0.22f), dark);

        TaskToilet task = root.AddComponent<TaskToilet>();
        FinishStation(task, root, 1.8f);
        return task;
    }

    // ------------------------- Shared plumbing -------------------------

    static TaskManager EnsureTaskManager()
    {
        TaskManager manager = UnityEngine.Object.FindObjectOfType<TaskManager>();
        if (manager == null)
            manager = new GameObject("TaskManager").AddComponent<TaskManager>();
        return manager;
    }

    static GameObject NewRoot(string name, Transform parent, Vector3 worldPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = worldPosition;
        return go;
    }

    static GameObject NewStationRoot(string name, Transform parent, Vector3 worldPosition,
        Vector3 colliderSize, Vector3 colliderCenter)
    {
        GameObject root = NewRoot(name, parent, worldPosition);
        BoxCollider interact = root.AddComponent<BoxCollider>();
        interact.size = colliderSize;
        interact.center = colliderCenter;
        return root;
    }

    static GameObject NewChild(string name, Transform parent, Vector3 localPosition)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        return go;
    }

    /// <summary>One art-directed primitive. Decorative parts lose their colliders.</summary>
    static GameObject Part(PrimitiveType type, string name, Transform parent,
        Vector3 localPosition, Vector3 localScale, Material material, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = localScale;

        if (!keepCollider)
        {
            Collider col = go.GetComponent<Collider>();
            if (col != null) Strip(col);
        }

        if (material != null)
            go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    static GameObject BuildPadlock(string name, Transform parent, Vector3 localPosition,
        Material brass, Material iron)
    {
        GameObject padlock = NewChild(name, parent, localPosition);
        Part(PrimitiveType.Cube, "Body", padlock.transform,
            Vector3.zero, new Vector3(0.26f, 0.3f, 0.12f), brass);
        Part(PrimitiveType.Cube, "Shackle_L", padlock.transform,
            new Vector3(-0.08f, 0.22f, 0f), new Vector3(0.05f, 0.16f, 0.05f), iron);
        Part(PrimitiveType.Cube, "Shackle_R", padlock.transform,
            new Vector3(0.08f, 0.22f, 0f), new Vector3(0.05f, 0.16f, 0.05f), iron);
        Part(PrimitiveType.Cube, "Shackle_Top", padlock.transform,
            new Vector3(0f, 0.3f, 0f), new Vector3(0.21f, 0.05f, 0.05f), iron);
        return padlock;
    }

    /// <summary>Applies balance defaults, hangs the name label, adds the NetworkObject.</summary>
    static void FinishStation(TaskBase task, GameObject root, float labelHeight)
    {
        task.ApplyBalanceDefaults();
        task.worldLabel = BuildWorldLabel(root.transform, task.taskName,
            new Vector3(0f, labelHeight, 0f), 3.4f, 34, billboard: true);
        root.AddComponent<NetworkObject>();
    }

    /// <summary>
    /// A world-space uGUI label (no TextMeshPro, no TextMesh - this is THE
    /// world-text path for the whole project; HouseFactory uses it too).
    /// The font is runtime-created and cannot survive a scene save, so an
    /// attached <see cref="WorldLabelFont"/> re-acquires it in Awake (owners
    /// like TaskBase/EscapeDoor also re-fix their own serialized references).
    /// </summary>
    public static Text BuildWorldLabel(Transform parent, string text, Vector3 localPosition,
        float worldWidth, int fontSize, bool billboard)
    {
        var canvasGo = new GameObject("Label");
        canvasGo.transform.SetParent(parent, false);
        canvasGo.transform.localPosition = localPosition;
        canvasGo.transform.localScale = Vector3.one * 0.01f;

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var canvasRect = canvasGo.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(worldWidth * 100f, 90f);

        if (billboard) canvasGo.AddComponent<Billboard>();

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(canvasGo.transform, false);
        Text label = textGo.AddComponent<Text>();
        var textRect = label.rectTransform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        label.text = text;
        label.font = RuntimeFont();
        label.fontSize = fontSize;
        label.fontStyle = FontStyle.Bold;
        label.alignment = TextAnchor.MiddleCenter;
        label.color = ScreamerPalette.NoodleCream;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;

        Outline outline = textGo.AddComponent<Outline>();
        outline.effectColor = ScreamerPalette.InkBlack;
        outline.effectDistance = new Vector2(2f, -2f);

        // Scene-save insurance: the runtime font dies with the editor session,
        // so the label re-acquires one itself on every boot.
        textGo.AddComponent<WorldLabelFont>();

        return label;
    }

    /// <summary>
    /// The project's dynamic UI font (GDD 7: OS Arial, no bundled font asset).
    /// Also used by task/door components to re-acquire label fonts at runtime.
    /// </summary>
    public static Font RuntimeFont()
    {
        Font font = null;
        try { font = Font.CreateDynamicFontFromOSFont("Arial", 32); }
        catch { /* headless or fontless platform; fall through */ }

        if (font == null)
        {
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { /* builtin missing; label stays silent rather than crashing */ }
        }
        return font;
    }

    static void Strip(UnityEngine.Object target)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(target);
        else UnityEngine.Object.DestroyImmediate(target);
    }

    static Color Dim(Color color, float factor) =>
        new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
}
