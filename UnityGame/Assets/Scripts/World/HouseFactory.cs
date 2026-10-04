using System;
using UnityEngine;

/// <summary>
/// Builds the entire Henderson House from art-directed primitives (GDD
/// sections 3, 6 and 13): floors, walls, fence, the Screaming Closet, den
/// furniture (tagged SlappableFurniture), the hearth, window glows, every
/// named MapAnchor, global lighting, all practicals with their behaviors,
/// the lighting/tell/mood directors and the ScreenFxOverlay canvas.
///
/// Runtime-compilable factory: the editor wizard calls <see cref="BuildAll"/>
/// with an asset-backed material delegate and saves the scene; play-mode
/// tests pass ScreamerPalette.MakeRuntimeMaterial. Never references
/// UnityEditor. All coordinates come from HouseLayout - no inline map math
/// beyond local prop assembly.
/// </summary>
public static class HouseFactory
{
    const string RootName = "HendersonHouse";

    /// <summary>
    /// Builds the whole map under <paramref name="mapRoot"/>. Idempotent: a
    /// previous build under the same root is removed first.
    /// </summary>
    public static void BuildAll(Transform mapRoot, Func<string, Color, Material> mat)
    {
        Func<string, Color, Material> material = mat ?? ScreamerPalette.MakeRuntimeMaterial;

        // Rebuild cleanly if the wizard runs twice.
        if (mapRoot != null)
        {
            Transform stale = mapRoot.Find(RootName);
            if (stale != null) SafeDestroy(stale.gameObject);
        }

        var root = new GameObject(RootName);
        if (mapRoot != null) root.transform.SetParent(mapRoot, false);

        BuildGeometry(root.transform, material);
        BuildFurniture(root.transform, material);
        BuildFireplaceProp(root.transform, material);
        BuildWindowGlows(root.transform, material);
        BuildClosetSign(root.transform, material);
        BuildAnchors(root.transform);
        ApplyGlobalLighting();
        BuildLightsAndDirectors(root.transform, material);
        BuildScreenFxOverlay(root.transform);
    }

    // ------------------------- Geometry -------------------------

    static void BuildGeometry(Transform root, Func<string, Color, Material> mat)
    {
        Transform geometry = Group(root, "Geometry");

        // Warm materials - a sitcom set, never gray (GDD 13).
        Material floorHouse = mat("Mat_FloorHouse", ScreamerPalette.ShagRust);
        Material floorYard = mat("Mat_FloorYard", Dim(ScreamerPalette.ShagRust, 0.4f));
        Material wallWarm = mat("Mat_WallWarm", Color.Lerp(ScreamerPalette.NoodleCream, ScreamerPalette.ShagRust, 0.35f));
        Material fenceWood = mat("Mat_FenceWood", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.InkBlack, 0.45f));

        Transform floors = Group(geometry, "Floors");
        foreach (HouseLayout.Box box in HouseLayout.Floors)
            Cube(floors, box, box.name == "Floor_Yard" ? floorYard : floorHouse);

        Transform walls = Group(geometry, "Walls");
        foreach (HouseLayout.Box box in HouseLayout.Walls)
            Cube(walls, box, wallWarm);

        Transform fence = Group(geometry, "Fence");
        foreach (HouseLayout.Box box in HouseLayout.Fence)
            Cube(fence, box, fenceWood);
    }

    // ------------------------- Furniture (every piece slappable) -------------------------

    static void BuildFurniture(Transform root, Func<string, Color, Material> mat)
    {
        Transform furniture = Group(root, "Furniture");

        Material couchRust = mat("Mat_CouchRust", ScreamerPalette.ShagRust);
        Material cushion = mat("Mat_CouchCushion", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.NoodleCream, 0.3f));
        Material woodDark = mat("Mat_WoodDark", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.InkBlack, 0.5f));
        Material cream = mat("Mat_FurnitureCream", ScreamerPalette.NoodleCream);
        Material brass = mat("Mat_LampBrass", Dim(ScreamerPalette.LamplightAmber, 0.8f));

        // The couch (GDD 3.4: lobby seating + the Mimic's disguise prop).
        GameObject couch = Prop(furniture, "Couch", HouseLayout.Couch, Quaternion.identity);
        Part(couch.transform, "Seat", PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(4f, 0.8f, 1.4f), cushion);
        Part(couch.transform, "Backrest", PrimitiveType.Cube, new Vector3(0f, 0.55f, -0.55f), new Vector3(4f, 1.1f, 0.35f), couchRust);
        Part(couch.transform, "Armrest_L", PrimitiveType.Cube, new Vector3(-1.85f, 0.25f, 0f), new Vector3(0.35f, 0.6f, 1.4f), couchRust);
        Part(couch.transform, "Armrest_R", PrimitiveType.Cube, new Vector3(1.85f, 0.25f, 0f), new Vector3(0.35f, 0.6f, 1.4f), couchRust);

        // Two armchairs flanking the couch, angled toward the TV wall.
        BuildArmchair(furniture, "Armchair_L", new Vector3(-3.4f, 0.4f, 1.6f), 22f, couchRust, cushion);
        BuildArmchair(furniture, "Armchair_R", new Vector3(3.4f, 0.4f, 1.6f), -22f, couchRust, cushion);

        // Floor lamp in the den's southwest reading corner.
        GameObject lamp = Prop(furniture, "FloorLamp", new Vector3(-10.5f, 0f, 1.5f), Quaternion.identity);
        Part(lamp.transform, "Base", PrimitiveType.Cylinder, new Vector3(0f, 0.05f, 0f), new Vector3(0.5f, 0.05f, 0.5f), woodDark);
        Part(lamp.transform, "Pole", PrimitiveType.Cylinder, new Vector3(0f, 0.9f, 0f), new Vector3(0.08f, 0.9f, 0.08f), brass);
        Part(lamp.transform, "Shade", PrimitiveType.Cylinder, new Vector3(0f, 1.8f, 0f), new Vector3(0.55f, 0.3f, 0.55f), cream);

        // TV stand + the emissive static screen (GDD 6.2).
        GameObject tvStand = Prop(furniture, "TVStand", new Vector3(HouseLayout.Tv.x, 0f, 11.55f), Quaternion.identity);
        Part(tvStand.transform, "Shelf", PrimitiveType.Cube, new Vector3(0f, 0.25f, 0f), new Vector3(2.4f, 0.5f, 0.7f), woodDark);
        Part(tvStand.transform, "TvBody", PrimitiveType.Cube,
            new Vector3(0f, HouseLayout.Tv.y, -0.03f), new Vector3(2f, 1.1f, 0.14f), woodDark);

        Material tvScreen = Emissive(mat, "Mat_TvScreen", ScreamerPalette.TvStaticBlue, 1f);
        GameObject screen = Part(tvStand.transform, "TvScreen", PrimitiveType.Quad,
            new Vector3(0f, HouseLayout.Tv.y, HouseLayout.Tv.z - tvStand.transform.position.z),
            new Vector3(1.8f, 0.95f, 1f), tvScreen);
        RemoveCollider(screen); // decor; the quad's default normal (-Z) already faces the den
        screen.AddComponent<TvStaticFlicker>();

        // Bedroom: bed + bedside table under the weak amber lamp.
        GameObject bed = Prop(furniture, "Bed", new Vector3(-9f, 0f, -15.5f), Quaternion.identity);
        Part(bed.transform, "Frame", PrimitiveType.Cube, new Vector3(0f, 0.25f, 0f), new Vector3(2.2f, 0.5f, 3.4f), woodDark);
        Part(bed.transform, "Mattress", PrimitiveType.Cube, new Vector3(0f, 0.58f, 0f), new Vector3(2f, 0.25f, 3.2f), cream);
        Part(bed.transform, "Pillow", PrimitiveType.Cube, new Vector3(0f, 0.75f, 1.25f), new Vector3(1.4f, 0.18f, 0.6f), cushion);

        GameObject bedside = Prop(furniture, "BedsideTable", new Vector3(-10.3f, 0f, -14f), Quaternion.identity);
        Part(bedside.transform, "Top", PrimitiveType.Cube, new Vector3(0f, 0.3f, 0f), new Vector3(0.6f, 0.6f, 0.6f), woodDark);

        // Kitchen dressing: counter run and a fridge along the west wall.
        GameObject counter = Prop(furniture, "KitchenCounter", new Vector3(-19.1f, 0f, 2f), Quaternion.identity);
        Part(counter.transform, "Cabinets", PrimitiveType.Cube, new Vector3(0f, 0.45f, 0f), new Vector3(1.6f, 0.9f, 7f), woodDark);
        Part(counter.transform, "Top", PrimitiveType.Cube, new Vector3(0f, 0.93f, 0f), new Vector3(1.7f, 0.06f, 7.1f), cream);

        GameObject fridge = Prop(furniture, "Fridge", new Vector3(-19.2f, 0f, 10.8f), Quaternion.identity);
        Part(fridge.transform, "Body", PrimitiveType.Cube, new Vector3(0f, 1.1f, 0f), new Vector3(1.2f, 2.2f, 1.2f), cream);

        // Garage junk clutter (GDD 3.2) on the junk anchor spots.
        BuildJunkPile(furniture, "GarageJunk_1", new Vector3(12f, 0f, -12f), woodDark, cushion);
        BuildJunkPile(furniture, "GarageJunk_2", new Vector3(16f, 0f, -17f), woodDark, couchRust);
        BuildJunkPile(furniture, "GarageJunk_3", new Vector3(5f, 0f, -18f), woodDark, cream);
    }

    static void BuildArmchair(Transform parent, string name, Vector3 position, float yaw,
        Material frame, Material cushion)
    {
        GameObject chair = Prop(parent, name, position, Quaternion.Euler(0f, yaw, 0f));
        Part(chair.transform, "Seat", PrimitiveType.Cube, Vector3.zero, new Vector3(1.2f, 0.8f, 1.2f), cushion);
        Part(chair.transform, "Backrest", PrimitiveType.Cube, new Vector3(0f, 0.5f, -0.45f), new Vector3(1.2f, 1f, 0.3f), frame);
        Part(chair.transform, "Armrest_L", PrimitiveType.Cube, new Vector3(-0.55f, 0.2f, 0f), new Vector3(0.2f, 0.5f, 1.2f), frame);
        Part(chair.transform, "Armrest_R", PrimitiveType.Cube, new Vector3(0.55f, 0.2f, 0f), new Vector3(0.2f, 0.5f, 1.2f), frame);
    }

    static void BuildJunkPile(Transform parent, string name, Vector3 position, Material a, Material b)
    {
        GameObject pile = Prop(parent, name, position, Quaternion.Euler(0f, position.x * 7f, 0f));
        Part(pile.transform, "Crate_Big", PrimitiveType.Cube, new Vector3(0f, 0.4f, 0f), new Vector3(1.1f, 0.8f, 0.9f), a);
        Part(pile.transform, "Crate_Small", PrimitiveType.Cube, new Vector3(0.2f, 1.05f, -0.1f), new Vector3(0.7f, 0.5f, 0.6f), b);
        Part(pile.transform, "Crate_Lean", PrimitiveType.Cube, new Vector3(-0.8f, 0.3f, 0.3f), new Vector3(0.6f, 0.6f, 0.6f), b);
    }

    // ------------------------- The hearth -------------------------

    static void BuildFireplaceProp(Transform root, Func<string, Color, Material> mat)
    {
        Material brick = mat("Mat_FireBrick", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.MidnightPlum, 0.35f));
        Material glow = Emissive(mat, "Mat_FireGlow", ScreamerPalette.LamplightAmber, 1.6f);

        // Grounded at the HouseLayout hearth slot (see the note on
        // HouseLayout.Fireplace about why it sits east of the back door).
        Vector3 basePosition = new Vector3(HouseLayout.Fireplace.x, 0f, HouseLayout.Fireplace.z);
        GameObject hearth = Prop(root, "Fireplace", basePosition, Quaternion.identity, slappable: false);

        Part(hearth.transform, "Column_L", PrimitiveType.Cube, new Vector3(-1.15f, 1.2f, 0f), new Vector3(0.5f, 2.4f, 0.8f), brick);
        Part(hearth.transform, "Column_R", PrimitiveType.Cube, new Vector3(1.15f, 1.2f, 0f), new Vector3(0.5f, 2.4f, 0.8f), brick);
        Part(hearth.transform, "Mantel", PrimitiveType.Cube, new Vector3(0f, 2.55f, 0f), new Vector3(2.8f, 0.3f, 0.9f), brick);
        Part(hearth.transform, "BackPanel", PrimitiveType.Cube, new Vector3(0f, 1.2f, 0.45f), new Vector3(2.3f, 2.4f, 0.2f), brick);
        Part(hearth.transform, "EmberBed", PrimitiveType.Cube, new Vector3(0f, 0.12f, 0.1f), new Vector3(1.8f, 0.24f, 0.7f), brick);

        // The firebox: an emissive quad facing the den (default quad normal is -Z).
        GameObject firebox = Part(hearth.transform, "FireboxGlow", PrimitiveType.Quad,
            new Vector3(0f, HouseLayout.Fireplace.y + 0.15f, 0.25f), new Vector3(1.6f, 1.25f, 1f), glow);
        RemoveCollider(firebox);
    }

    // ------------------------- Window glows -------------------------

    static void BuildWindowGlows(Transform root, Func<string, Color, Material> mat)
    {
        // Four emissive window quads on the house's north face: cozy = safety,
        // visible from danger (GDD 6.2). Emissive only, never real lights.
        Transform windows = Group(root, "WindowGlows");
        Material glow = Emissive(mat, "Mat_WindowGlow", ScreamerPalette.LamplightAmber, 1.2f);

        float faceZ = 13.01f; // just outside the north wall's outer face
        float[] slots = { -11f, -5f, 5f, 11f };
        for (int i = 0; i < slots.Length; i++)
        {
            var window = GameObject.CreatePrimitive(PrimitiveType.Quad);
            window.name = "WindowGlow_" + (i + 1);
            window.transform.SetParent(windows, false);
            window.transform.position = new Vector3(slots[i], 2.4f, faceZ);
            // Default quad normal is -Z; flip it to face the yard (+Z).
            window.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            window.transform.localScale = new Vector3(1.6f, 1.4f, 1f);
            window.GetComponent<Renderer>().sharedMaterial = glow;
            RemoveCollider(window);
        }
    }

    // ------------------------- The Screaming Closet sign -------------------------

    static void BuildClosetSign(Transform root, Func<string, Color, Material> mat)
    {
        // World prop label defined by the map spec (GDD 3.3 / section 11).
        Material plateMat = mat("Mat_SignPlate", ScreamerPalette.InkBlack);

        // The closet opening faces east; the sign reads to players approaching
        // from inside the bedroom (viewer looks toward -X).
        var sign = new GameObject("ScreamingClosetSign");
        sign.transform.SetParent(root, false);
        sign.transform.position = new Vector3(-15.8f, 3.2f, -14f);
        sign.transform.rotation = Quaternion.Euler(0f, -90f, 0f);

        GameObject plate = Part(sign.transform, "Plate", PrimitiveType.Cube,
            new Vector3(0f, 0f, 0.07f), new Vector3(2.4f, 0.55f, 0.08f), plateMat);
        RemoveCollider(plate);

        // Same world-space uGUI label path as every station and door sign
        // (project standard: uGUI only - no TextMesh). The sign is a fixed
        // wall prop, so no billboard; BuildWorldLabel's own WorldLabelFont
        // re-acquires the runtime font across scene saves.
        TaskFactory.BuildWorldLabel(sign.transform, "THE SCREAMING CLOSET",
            Vector3.zero, 2.2f, 34, billboard: false);
    }

    // ------------------------- Anchors (GDD section 13) -------------------------

    static void BuildAnchors(Transform root)
    {
        var anchorsObject = new GameObject("MapAnchors");
        anchorsObject.transform.SetParent(root, false);
        anchorsObject.AddComponent<MapAnchors>();
        Transform anchors = anchorsObject.transform;

        Anchor(anchors, "FireplaceAnchor", Grounded(HouseLayout.Fireplace));
        Anchor(anchors, "CouchAnchor", Grounded(HouseLayout.Couch));
        Anchor(anchors, "ArmchairAnchor1", new Vector3(-3.4f, 0f, 1.6f));
        Anchor(anchors, "ArmchairAnchor2", new Vector3(3.4f, 0f, 1.6f));
        Anchor(anchors, "TVAnchor", Grounded(HouseLayout.Tv));
        Anchor(anchors, "KaraokeAnchor", Grounded(HouseLayout.KaraokeStation));
        Anchor(anchors, "DancePadAnchor", Grounded(HouseLayout.DancePad));
        Anchor(anchors, "StoveAnchor", Grounded(HouseLayout.NoodleStove));
        Anchor(anchors, "ToiletAnchor", Grounded(HouseLayout.ToiletStation));
        Anchor(anchors, "BedAnchor", new Vector3(-9f, 0f, -15.5f));
        Anchor(anchors, "GarageJunkAnchor1", new Vector3(12f, 0f, -12f));
        Anchor(anchors, "GarageJunkAnchor2", new Vector3(16f, 0f, -17f));
        Anchor(anchors, "GarageJunkAnchor3", new Vector3(5f, 0f, -18f));
        Anchor(anchors, "YardShedAnchor", new Vector3(-11f, 0f, 25f));
        Anchor(anchors, "YardFenceDressingAnchor1", new Vector3(-18f, 0f, 24f));
        Anchor(anchors, "YardFenceDressingAnchor2", new Vector3(18f, 0f, 24f));
        Anchor(anchors, "YardFenceDressingAnchor3", new Vector3(-10f, 0f, 31f));
        Anchor(anchors, "YardFenceDressingAnchor4", new Vector3(10f, 0f, 31f));
        Anchor(anchors, "CellarDoorAnchor", Grounded(HouseLayout.CellarDoor));
    }

    // ------------------------- Global lighting (GDD 6.1) -------------------------

    static void ApplyGlobalLighting()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Dim(ScreamerPalette.HauntedTeal, 0.35f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.012f;
        RenderSettings.fogColor = Dim(ScreamerPalette.MidnightPlum, 0.6f);

        QualitySettings.shadowDistance = 50f;
    }

    // ------------------------- Lights, directors, overlay -------------------------

    static void BuildLightsAndDirectors(Transform root, Func<string, Color, Material> mat)
    {
        Transform lights = Group(root, "Lights");

        // The moon: the only directional light.
        var moonObject = new GameObject("Moonlight");
        moonObject.transform.SetParent(lights, false);
        moonObject.transform.rotation = Quaternion.Euler(55f, -30f, 0f);
        var moon = moonObject.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = ScreamerPalette.MoonlightBlue;
        moon.intensity = 0.35f;
        moon.shadows = LightShadows.Soft;

        Vector3 denCenter = HouseLayout.Rooms[0].center;
        Vector3 kitchenCenter = HouseLayout.Rooms[1].center;
        Vector3 bathroomCenter = HouseLayout.Rooms[2].center;
        Vector3 bedroomCenter = HouseLayout.Rooms[5].center;
        Vector3 garageCenter = HouseLayout.Rooms[6].center;

        // Den hearth practical: the hero light, Perlin flicker 0.8-1.2x.
        LightFlicker fireplace = Practical(lights, "Light_Fireplace",
            new Vector3(HouseLayout.Fireplace.x, 1.2f, 10.8f),
            ScreamerPalette.LamplightAmber, 1.4f, 12f,
            usePerlin: true, flickerMin: 0.8f, flickerMax: 1.2f, flickerSpeed: 1.4f,
            roomCenter: denCenter, roomName: "Den");
        fireplace.isFireplace = true;

        // Karaoke toy-neon (the one palette exception; strobes red on wrong notes).
        LightFlicker karaoke = Practical(lights, "Light_Karaoke",
            new Vector3(-9f, 3.2f, 9f),
            ScreamerPalette.ToyNeonMagenta, 0.9f, 6f,
            usePerlin: false, flickerMin: 1f, flickerMax: 1f, flickerSpeed: 1f,
            roomCenter: denCenter, roomName: "Den");

        // Over-stove cream practical (steady; strobes red at 2 Hz on a burn).
        LightFlicker stove = Practical(lights, "Light_Stove",
            new Vector3(-16f, 3.4f, 8f),
            ScreamerPalette.NoodleCream, 1.1f, 7f,
            usePerlin: false, flickerMin: 1f, flickerMax: 1f, flickerSpeed: 1f,
            roomCenter: kitchenCenter, roomName: "Kitchen");

        // Bathroom fluorescent: fast nervous flicker, worsened by plumbing noise.
        LightFlicker bathroom = Practical(lights, "Light_Bathroom",
            new Vector3(16f, 3.6f, 9f),
            ScreamerPalette.FluorescentGreen, 1f, 6f,
            usePerlin: true, flickerMin: 0.85f, flickerMax: 1.08f, flickerSpeed: 7f,
            roomCenter: bathroomCenter, roomName: "Bathroom");

        // Bedside lamp: steady and weak.
        Practical(lights, "Light_Bedside",
            new Vector3(-10f, 1.2f, -14f),
            Dim(ScreamerPalette.LamplightAmber, 0.6f), 0.7f, 6f,
            usePerlin: false, flickerMin: 1f, flickerMax: 1f, flickerSpeed: 1f,
            roomCenter: bedroomCenter, roomName: "Bedroom");

        // Garage bare bulb on a swinging pendulum: moving shadows for free dread.
        var pivot = new GameObject("GarageBulbPivot");
        pivot.transform.SetParent(lights, false);
        pivot.transform.position = new Vector3(8f, 4f, -14f);
        var swing = pivot.AddComponent<SwingingLight>();
        swing.swingDegrees = 15f;
        swing.periodSeconds = 3f;

        Material cord = mat("Mat_BulbCord", ScreamerPalette.InkBlack);
        Material bulbGlow = Emissive(mat, "Mat_BulbGlow", ScreamerPalette.LamplightAmber, 1.4f);
        GameObject cordPart = Part(pivot.transform, "Cord", PrimitiveType.Cylinder,
            new Vector3(0f, -0.2f, 0f), new Vector3(0.04f, 0.2f, 0.04f), cord);
        RemoveCollider(cordPart);
        GameObject bulbPart = Part(pivot.transform, "Bulb", PrimitiveType.Sphere,
            new Vector3(0f, -0.45f, 0f), new Vector3(0.24f, 0.24f, 0.24f), bulbGlow);
        RemoveCollider(bulbPart);

        LightFlicker garageBulb = Practical(pivot.transform, "Light_GarageBulb",
            pivot.transform.position + new Vector3(0f, -0.42f, 0f),
            Dim(ScreamerPalette.LamplightAmber, 0.5f), 0.8f, 8f,
            usePerlin: true, flickerMin: 0.9f, flickerMax: 1.1f, flickerSpeed: 1.2f,
            roomCenter: garageCenter, roomName: "Garage");
        garageBulb.GetComponent<Light>().shadows = LightShadows.Soft;

        // Escape-door state light: MonsterRed while locked, GhostMint on finale.
        var doorLightObject = new GameObject("Light_EscapeDoor");
        doorLightObject.transform.SetParent(lights, false);
        doorLightObject.transform.position = new Vector3(0f, 1.6f, 27f);
        var doorLight = doorLightObject.AddComponent<Light>();
        doorLight.type = LightType.Point;
        doorLight.color = ScreamerPalette.MonsterRed;
        doorLight.intensity = 0.8f;
        doorLight.range = 5f;

        // Directors: event lighting, house tells, mood rounds.
        var directors = new GameObject("WorldDirectors");
        directors.transform.SetParent(root, false);

        var lightsDirector = directors.AddComponent<HouseLightsDirector>();
        lightsDirector.doorLight = doorLight;
        lightsDirector.karaokeLight = karaoke;
        lightsDirector.stoveLight = stove;
        lightsDirector.bathroomLight = bathroom;

        directors.AddComponent<HouseTells>();
        directors.AddComponent<HouseMoodDirector>();
    }

    static void BuildScreenFxOverlay(Transform root)
    {
        var overlayObject = new GameObject("ScreenFxOverlay");
        overlayObject.transform.SetParent(root, false);

        // Configure the canvas here as well so the saved scene is already
        // correct; the component re-asserts both values at runtime.
        var canvas = overlayObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5000;

        overlayObject.AddComponent<ScreenFxOverlay>();
    }

    // ------------------------- Small helpers -------------------------

    static Transform Group(Transform parent, string name)
    {
        var group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    static GameObject Cube(Transform parent, HouseLayout.Box box, Material material)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = box.name;
        cube.transform.SetParent(parent, false);
        cube.transform.position = box.center;
        cube.transform.localScale = box.size;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        return cube;
    }

    /// <summary>A furniture/prop root; slappable props carry the module-2 marker.</summary>
    static GameObject Prop(Transform parent, string name, Vector3 position, Quaternion rotation, bool slappable = true)
    {
        var prop = new GameObject(name);
        prop.transform.SetParent(parent, false);
        prop.transform.SetPositionAndRotation(position, rotation);
        if (slappable) prop.AddComponent<SlappableFurniture>();
        return prop;
    }

    /// <summary>One art-directed primitive part, local to its prop root.</summary>
    static GameObject Part(Transform parent, string name, PrimitiveType type,
        Vector3 localPosition, Vector3 localScale, Material material)
    {
        var part = GameObject.CreatePrimitive(type);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }

    static LightFlicker Practical(Transform parent, string name, Vector3 position,
        Color color, float intensity, float range,
        bool usePerlin, float flickerMin, float flickerMax, float flickerSpeed,
        Vector3 roomCenter, string roomName)
    {
        var lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent, true);
        lightObject.transform.position = position;

        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = intensity;
        light.range = range;
        light.shadows = LightShadows.None;

        var flicker = lightObject.AddComponent<LightFlicker>();
        flicker.baseIntensity = intensity;
        flicker.baseColor = color;
        flicker.usePerlin = usePerlin;
        flicker.flickerMin = flickerMin;
        flicker.flickerMax = flickerMax;
        flicker.flickerSpeed = flickerSpeed;
        flicker.roomCenter = roomCenter;
        flicker.roomName = roomName;
        return flicker;
    }

    static void Anchor(Transform parent, string name, Vector3 position)
    {
        var anchor = new GameObject(name);
        anchor.transform.SetParent(parent, false);
        anchor.transform.position = position;
    }

    static Vector3 Grounded(Vector3 position) => new Vector3(position.x, 0f, position.z);

    static Material Emissive(Func<string, Color, Material> mat, string name, Color color, float strength)
    {
        Material material = mat(name, color);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * strength);
        return material;
    }

    static Color Dim(Color color, float factor) =>
        new Color(color.r * factor, color.g * factor, color.b * factor, 1f);

    static void RemoveCollider(GameObject gameObject)
    {
        var collider = gameObject.GetComponent<Collider>();
        if (collider == null) return;

        if (Application.isPlaying) UnityEngine.Object.Destroy(collider);
        else UnityEngine.Object.DestroyImmediate(collider);
    }

    static void SafeDestroy(GameObject gameObject)
    {
        if (Application.isPlaying) UnityEngine.Object.Destroy(gameObject);
        else UnityEngine.Object.DestroyImmediate(gameObject);
    }
}
