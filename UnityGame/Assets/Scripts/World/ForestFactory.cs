using System;
using UnityEngine;

/// <summary>
/// The Whispering Woods - SCREAMER's second map. Same bones, different nightmare:
/// every blocking rect from HouseLayout (walls, fences) becomes a dense treeline
/// with an identical box collider, so pathing, line of sight, bot waypoints, the
/// closets and every task position carry over untouched. The chores now live in
/// a moonlit forest clearing: lantern posts mark the stations, a campfire
/// replaces the hearth, slappable stumps replace the couch, and the cellar door
/// is a padlocked gate at the end of the clearing. Backdrop cabins past the
/// north treeline carry MedievalBuildingAnchor1-3 for the lowpoly medieval
/// buildings pack.
///
/// Built by ScreamerSetupWizard (menu: Screamer/Build Everything (Forest Map))
/// as a drop-in replacement for HouseFactory.BuildAll.
/// </summary>
public static class ForestFactory
{
    const string RootName = "WhisperingWoods";

    public static void BuildAll(Transform mapRoot, Func<string, Color, Material> mat)
    {
        Func<string, Color, Material> material = mat ?? ScreamerPalette.MakeRuntimeMaterial;

        if (mapRoot != null)
        {
            Transform stale = mapRoot.Find(RootName);
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale.gameObject);
        }

        var root = new GameObject(RootName);
        if (mapRoot != null) root.transform.SetParent(mapRoot, false);

        UnityEngine.Random.InitState(20261004); // deterministic woods: same forest every build

        BuildGround(root.transform, material);
        BuildTreelines(root.transform, material);
        BuildClearingDressing(root.transform, material);
        BuildCampfireAndStumps(root.transform, material);
        BuildBackdropCabins(root.transform, material);
        BuildAnchors(root.transform);
        ApplyForestLighting();
        BuildLightsAndDirectors(root.transform, material);
        HouseFactory.BuildScreenFxOverlay(root.transform);
    }

    // ------------------------- Ground -------------------------

    static void BuildGround(Transform root, Func<string, Color, Material> mat)
    {
        Transform ground = Group(root, "Ground");

        Material clearing = mat("Mat_ForestClearing",
            Color.Lerp(ScreamerPalette.HauntedTeal, ScreamerPalette.InkBlack, 0.55f));
        Material deepWoods = mat("Mat_DeepWoods",
            Color.Lerp(ScreamerPalette.HauntedTeal, ScreamerPalette.InkBlack, 0.72f));

        foreach (HouseLayout.Box box in HouseLayout.Floors)
            Cube(ground, box, box.name == "Floor_Yard" ? deepWoods : clearing);
    }

    // ------------------------- Treelines (the walls, but alive) -------------------------

    static void BuildTreelines(Transform root, Func<string, Color, Material> mat)
    {
        Transform trees = Group(root, "Treelines");

        Material bark = mat("Mat_Bark", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.InkBlack, 0.5f));
        Material canopy = mat("Mat_Canopy", Color.Lerp(ScreamerPalette.FluorescentGreen, ScreamerPalette.InkBlack, 0.68f));
        Material canopyDark = mat("Mat_CanopyDark", Color.Lerp(ScreamerPalette.HauntedTeal, ScreamerPalette.InkBlack, 0.62f));
        Material log = mat("Mat_FallenLog", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.InkBlack, 0.42f));

        // Every wall rect becomes a treeline with the SAME collider footprint,
        // so collision, hearing line-of-sight and bot waypoints stay identical.
        foreach (HouseLayout.Box box in HouseLayout.Walls)
            Treeline(trees, box, bark, canopy, canopyDark);

        // The yard fence becomes fallen logs - still exactly fence-sized.
        foreach (HouseLayout.Box box in HouseLayout.Fence)
            FallenLog(trees, box, log);
    }

    static void Treeline(Transform parent, HouseLayout.Box box, Material bark, Material canopyA, Material canopyB)
    {
        var line = new GameObject("TreeWall_" + box.name);
        line.transform.SetParent(parent, false);
        line.transform.position = box.center;

        // The invisible truth: one collider identical to the old wall.
        var collider = line.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = box.size;

        // The visible lie: trunks and canopies crowded along the rect.
        bool alongX = box.size.x >= box.size.z;
        float length = alongX ? box.size.x : box.size.z;
        float thickness = alongX ? box.size.z : box.size.x;
        int count = Mathf.Max(1, Mathf.CeilToInt(length / 1.3f));

        for (int i = 0; i < count; i++)
        {
            float t = count == 1 ? 0f : (i / (float)(count - 1)) - 0.5f;
            float along = t * (length - 0.6f);
            float across = UnityEngine.Random.Range(-0.25f, 0.25f) * Mathf.Max(0.2f, thickness - 0.4f);

            Vector3 local = alongX ? new Vector3(along, 0f, across) : new Vector3(across, 0f, along);
            Tree(line.transform, local - new Vector3(0f, box.center.y, 0f), bark,
                (i % 2 == 0) ? canopyA : canopyB);
        }
    }

    static void Tree(Transform parent, Vector3 localPos, Material bark, Material canopy)
    {
        float height = UnityEngine.Random.Range(2.6f, 3.6f);
        float radius = UnityEngine.Random.Range(0.16f, 0.26f);

        var tree = new GameObject("Tree");
        tree.transform.SetParent(parent, false);
        tree.transform.localPosition = localPos;
        tree.transform.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);

        Part(tree.transform, "Trunk", PrimitiveType.Cylinder,
            new Vector3(0f, height * 0.5f, 0f), new Vector3(radius * 2f, height * 0.5f, radius * 2f), bark);

        float crown = UnityEngine.Random.Range(1.5f, 2.1f);
        Part(tree.transform, "CanopyLow", PrimitiveType.Sphere,
            new Vector3(0f, height + 0.1f, 0f), new Vector3(crown, crown * 0.8f, crown), canopy);
        Part(tree.transform, "CanopyHigh", PrimitiveType.Sphere,
            new Vector3(0.15f, height + crown * 0.55f, -0.1f),
            new Vector3(crown * 0.62f, crown * 0.55f, crown * 0.62f), canopy);
    }

    static void FallenLog(Transform parent, HouseLayout.Box box, Material log)
    {
        var line = new GameObject("LogFence_" + box.name);
        line.transform.SetParent(parent, false);
        line.transform.position = box.center;

        var collider = line.AddComponent<BoxCollider>();
        collider.center = Vector3.zero;
        collider.size = box.size;

        bool alongX = box.size.x >= box.size.z;
        float length = alongX ? box.size.x : box.size.z;

        // Two stacked trunks spanning the old fence rect.
        for (int level = 0; level < 2; level++)
        {
            GameObject trunk = Part(line.transform, "Log" + level, PrimitiveType.Cylinder,
                new Vector3(0f, -box.size.y * 0.5f + 0.35f + level * 0.62f, 0f),
                new Vector3(0.6f, length * 0.5f, 0.6f), log);
            trunk.transform.localRotation = alongX
                ? Quaternion.Euler(0f, 0f, 90f)
                : Quaternion.Euler(90f, 0f, 0f);
        }
    }

    // ------------------------- Clearing dressing (pure decoration) -------------------------

    static void BuildClearingDressing(Transform root, Func<string, Color, Material> mat)
    {
        Transform dressing = Group(root, "Dressing");

        Material fern = mat("Mat_Fern", Color.Lerp(ScreamerPalette.FluorescentGreen, ScreamerPalette.InkBlack, 0.5f));
        Material rock = mat("Mat_Rock", Color.Lerp(ScreamerPalette.MidnightPlum, ScreamerPalette.InkBlack, 0.3f));
        Material stem = mat("Mat_MushroomStem", ScreamerPalette.NoodleCream);
        Material cap = mat("Mat_MushroomCap", ScreamerPalette.MonsterRed);

        for (int i = 0; i < 46; i++)
        {
            Vector3 pos = new Vector3(UnityEngine.Random.Range(-19f, 19f), 0f, UnityEngine.Random.Range(-19f, 27f));
            if (Blocked(pos)) continue;

            int kind = i % 3;
            if (kind == 0) // fern tuft: three tilted blades
            {
                var tuft = new GameObject("Fern");
                tuft.transform.SetParent(dressing, false);
                tuft.transform.position = pos;
                for (int b = 0; b < 3; b++)
                {
                    GameObject blade = Part(tuft.transform, "Blade" + b, PrimitiveType.Cube,
                        new Vector3(0f, 0.3f, 0f), new Vector3(0.08f, 0.6f, 0.02f), fern);
                    blade.transform.localRotation = Quaternion.Euler(UnityEngine.Random.Range(-25f, -5f), b * 120f, 0f);
                }
            }
            else if (kind == 1) // mossy rock
            {
                GameObject stone = Part(dressing, "Rock", PrimitiveType.Sphere,
                    pos + new Vector3(0f, 0.18f, 0f),
                    new Vector3(UnityEngine.Random.Range(0.5f, 0.9f), 0.4f, UnityEngine.Random.Range(0.5f, 0.9f)), rock);
                stone.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            }
            else // suspicious little mushroom
            {
                var shroom = new GameObject("Mushroom");
                shroom.transform.SetParent(dressing, false);
                shroom.transform.position = pos;
                Part(shroom.transform, "Stem", PrimitiveType.Cylinder,
                    new Vector3(0f, 0.09f, 0f), new Vector3(0.07f, 0.09f, 0.07f), stem);
                Part(shroom.transform, "Cap", PrimitiveType.Sphere,
                    new Vector3(0f, 0.2f, 0f), new Vector3(0.22f, 0.12f, 0.22f), cap);
            }
        }
    }

    /// <summary>True when a dressing spot would collide with a treeline, a station, or a spawn.</summary>
    static bool Blocked(Vector3 pos)
    {
        foreach (HouseLayout.Box box in HouseLayout.Walls)
            if (InsideInflated(pos, box, 1.0f)) return true;
        foreach (HouseLayout.Box box in HouseLayout.Fence)
            if (InsideInflated(pos, box, 1.0f)) return true;

        Vector3[] keepClear =
        {
            HouseLayout.ScreamStation, HouseLayout.KaraokeStation, HouseLayout.DancePad,
            HouseLayout.NoodleStove, HouseLayout.ToiletStation, HouseLayout.ChickenStart,
            HouseLayout.CellarDoor, HouseLayout.Couch, HouseLayout.Fireplace, HouseLayout.Tv,
            HouseLayout.MonsterSpawn
        };
        foreach (Vector3 point in keepClear)
            if (Flat(pos - point).sqrMagnitude < 2.4f * 2.4f) return true;
        foreach (Vector3 point in HouseLayout.SurvivorSpawns)
            if (Flat(pos - point).sqrMagnitude < 1.8f * 1.8f) return true;

        return false;
    }

    static bool InsideInflated(Vector3 pos, HouseLayout.Box box, float inflate)
    {
        return Mathf.Abs(pos.x - box.center.x) < box.size.x * 0.5f + inflate
            && Mathf.Abs(pos.z - box.center.z) < box.size.z * 0.5f + inflate;
    }

    static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

    // ------------------------- Campfire and slappable stumps -------------------------

    static void BuildCampfireAndStumps(Transform root, Func<string, Color, Material> mat)
    {
        Transform props = Group(root, "Props");

        Material stone = mat("Mat_Rock", Color.Lerp(ScreamerPalette.MidnightPlum, ScreamerPalette.InkBlack, 0.3f));
        Material bark = mat("Mat_Bark", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.InkBlack, 0.5f));
        Material ember = Emissive(mat, "Mat_Ember", ScreamerPalette.LamplightAmber, 1.6f);

        // Campfire where the hearth was - the clearing's heart.
        var campfire = new GameObject("Campfire");
        campfire.transform.SetParent(props, false);
        campfire.transform.position = Grounded(HouseLayout.Fireplace);
        for (int i = 0; i < 6; i++)
        {
            float angle = i * Mathf.PI * 2f / 6f;
            Part(campfire.transform, "Stone" + i, PrimitiveType.Sphere,
                new Vector3(Mathf.Cos(angle) * 0.7f, 0.12f, Mathf.Sin(angle) * 0.7f),
                new Vector3(0.34f, 0.24f, 0.34f), stone);
        }
        GameObject logA = Part(campfire.transform, "LogA", PrimitiveType.Cylinder,
            new Vector3(0f, 0.18f, 0f), new Vector3(0.16f, 0.45f, 0.16f), bark);
        logA.transform.localRotation = Quaternion.Euler(0f, 0f, 70f);
        GameObject logB = Part(campfire.transform, "LogB", PrimitiveType.Cylinder,
            new Vector3(0f, 0.18f, 0f), new Vector3(0.16f, 0.45f, 0.16f), bark);
        logB.transform.localRotation = Quaternion.Euler(70f, 90f, 0f);
        Part(campfire.transform, "Embers", PrimitiveType.Sphere,
            new Vector3(0f, 0.16f, 0f), new Vector3(0.4f, 0.18f, 0.4f), ember);

        // Slappable seating: stumps where the couch and armchairs sat, a
        // mossy barrel where the TV stood. The Mimic economy needs innocents.
        Stump(props, "Stump_Couch", Grounded(HouseLayout.Couch), 0.55f, bark);
        Stump(props, "Stump_ArmchairL", new Vector3(-3.4f, 0f, 1.6f), 0.4f, bark);
        Stump(props, "Stump_ArmchairR", new Vector3(3.4f, 0f, 1.6f), 0.4f, bark);
        Stump(props, "Barrel_Tv", Grounded(HouseLayout.Tv), 0.45f, mat("Mat_FallenLog",
            Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.InkBlack, 0.42f)));
    }

    static void Stump(Transform parent, string name, Vector3 position, float radius, Material material)
    {
        GameObject stump = Part(parent, name, PrimitiveType.Cylinder,
            position + new Vector3(0f, 0.35f, 0f), new Vector3(radius * 2f, 0.35f, radius * 2f),
            material, keepCollider: true);
        stump.AddComponent<SlappableFurniture>();
    }

    // ------------------------- Backdrop cabins (asset attachment points) -------------------------

    static void BuildBackdropCabins(Transform root, Func<string, Color, Material> mat)
    {
        Transform cabins = Group(root, "BackdropCabins");

        Material wallWood = mat("Mat_CabinWall", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.InkBlack, 0.35f));
        Material roofWood = mat("Mat_CabinRoof", Color.Lerp(ScreamerPalette.MidnightPlum, ScreamerPalette.InkBlack, 0.2f));
        Material glow = Emissive(mat, "Mat_CabinWindow", ScreamerPalette.LamplightAmber, 1.2f);

        // Past the north logs, out of play: pure silhouette, zero gameplay.
        Cabin(cabins, "Cabin1", new Vector3(-10f, 0f, 32.5f), 12f, wallWood, roofWood, glow);
        Cabin(cabins, "Cabin2", new Vector3(1f, 0f, 33.5f), -4f, wallWood, roofWood, glow);
        Cabin(cabins, "Cabin3", new Vector3(11f, 0f, 32f), -16f, wallWood, roofWood, glow);
    }

    static void Cabin(Transform parent, string name, Vector3 position, float yaw,
        Material wall, Material roof, Material glow)
    {
        var cabin = new GameObject(name);
        cabin.transform.SetParent(parent, false);
        cabin.transform.position = position;
        cabin.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        Part(cabin.transform, "Body", PrimitiveType.Cube,
            new Vector3(0f, 1.1f, 0f), new Vector3(3.4f, 2.2f, 2.6f), wall);
        GameObject roofPart = Part(cabin.transform, "Roof", PrimitiveType.Cube,
            new Vector3(0f, 2.5f, 0f), new Vector3(3.9f, 0.9f, 3.1f), roof);
        roofPart.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        Part(cabin.transform, "Window", PrimitiveType.Cube,
            new Vector3(0.9f, 1.2f, -1.32f), new Vector3(0.7f, 0.7f, 0.05f), glow);
    }

    // ------------------------- Anchors (GDD section 13, forest edition) -------------------------

    static void BuildAnchors(Transform root)
    {
        var anchorsObject = new GameObject("MapAnchors");
        anchorsObject.transform.SetParent(root, false);
        anchorsObject.AddComponent<MapAnchors>();
        Transform anchors = anchorsObject.transform;

        // The required contract set, at the same grounded positions as the house.
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

        // Forest extras: drop the medieval buildings pack here.
        Anchor(anchors, "MedievalBuildingAnchor1", new Vector3(-10f, 0f, 32.5f));
        Anchor(anchors, "MedievalBuildingAnchor2", new Vector3(1f, 0f, 33.5f));
        Anchor(anchors, "MedievalBuildingAnchor3", new Vector3(11f, 0f, 32f));

        // Wasteland pack backdrop: ruins looming past the south treeline.
        Anchor(anchors, "RuinAnchor1", new Vector3(-12f, 0f, -24f));
        Anchor(anchors, "RuinAnchor2", new Vector3(0f, 0f, -25f));
        Anchor(anchors, "RuinAnchor3", new Vector3(12f, 0f, -24f));
    }

    // ------------------------- Lighting -------------------------

    static void ApplyForestLighting()
    {
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Dim(ScreamerPalette.HauntedTeal, 0.28f);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.02f;
        RenderSettings.fogColor = Dim(ScreamerPalette.MidnightPlum, 0.45f);

        QualitySettings.shadowDistance = 60f;
    }

    static void BuildLightsAndDirectors(Transform root, Func<string, Color, Material> mat)
    {
        Transform lights = Group(root, "Lights");

        // A brighter moon: the forest has no ceiling to hide behind.
        var moonObject = new GameObject("Moonlight");
        moonObject.transform.SetParent(lights, false);
        moonObject.transform.rotation = Quaternion.Euler(52f, -24f, 0f);
        var moon = moonObject.AddComponent<Light>();
        moon.type = LightType.Directional;
        moon.color = ScreamerPalette.MoonlightBlue;
        moon.intensity = 0.5f;
        moon.shadows = LightShadows.Soft;

        // Campfire hero light at the old hearth position.
        LightFlicker campfire = Lantern(lights, "Light_Campfire",
            Grounded(HouseLayout.Fireplace) + Vector3.up * 1.0f,
            ScreamerPalette.LamplightAmber, 1.5f, 13f,
            usePerlin: true, flickerMin: 0.75f, flickerMax: 1.25f, flickerSpeed: 1.6f,
            roomName: "Clearing");
        campfire.isFireplace = true;

        // Lantern posts mark each chore in the dark. Same event-lighting roles
        // as the house practicals, so HouseLightsDirector drives them unchanged.
        LightFlicker karaoke = LanternPost(lights, mat, "Lantern_Karaoke",
            Grounded(HouseLayout.KaraokeStation) + new Vector3(1.2f, 0f, 0.8f),
            ScreamerPalette.ToyNeonMagenta, 0.9f, 6f, steady: true);
        LightFlicker stove = LanternPost(lights, mat, "Lantern_Noodles",
            Grounded(HouseLayout.NoodleStove) + new Vector3(0.9f, 0f, -1.1f),
            ScreamerPalette.NoodleCream, 1.1f, 7f, steady: true);
        LightFlicker bathroom = LanternPost(lights, mat, "Lantern_Toilet",
            Grounded(HouseLayout.ToiletStation) + new Vector3(-1.1f, 0f, -0.9f),
            ScreamerPalette.FluorescentGreen, 1f, 6f, steady: false);
        LanternPost(lights, mat, "Lantern_Scream",
            Grounded(HouseLayout.ScreamStation) + new Vector3(1.2f, 0f, 1.2f),
            ScreamerPalette.LamplightAmber, 0.9f, 6f, steady: true);
        LanternPost(lights, mat, "Lantern_Dance",
            Grounded(HouseLayout.DancePad) + new Vector3(-1.6f, 0f, 1.4f),
            ScreamerPalette.ToyNeonMagenta, 0.8f, 6f, steady: true);

        // Gate state light: MonsterRed while locked, GhostMint on the finale.
        var gateLightObject = new GameObject("Light_EscapeGate");
        gateLightObject.transform.SetParent(lights, false);
        gateLightObject.transform.position = new Vector3(0f, 1.6f, 27f);
        var gateLight = gateLightObject.AddComponent<Light>();
        gateLight.type = LightType.Point;
        gateLight.color = ScreamerPalette.MonsterRed;
        gateLight.intensity = 0.8f;
        gateLight.range = 5f;

        var directors = new GameObject("WorldDirectors");
        directors.transform.SetParent(root, false);

        var lightsDirector = directors.AddComponent<HouseLightsDirector>();
        lightsDirector.doorLight = gateLight;
        lightsDirector.karaokeLight = karaoke;
        lightsDirector.stoveLight = stove;
        lightsDirector.bathroomLight = bathroom;

        directors.AddComponent<HouseTells>();
        directors.AddComponent<HouseMoodDirector>();
    }

    /// <summary>A lantern post prop plus its practical light, LightFlicker-driven.</summary>
    static LightFlicker LanternPost(Transform parent, Func<string, Color, Material> mat,
        string name, Vector3 groundPos, Color color, float intensity, float range, bool steady)
    {
        Material post = mat("Mat_Bark", Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.InkBlack, 0.5f));
        Material glass = Emissive(mat, "Mat_Lantern_" + name, color, 1.3f);

        var prop = new GameObject(name + "_Post");
        prop.transform.SetParent(parent, false);
        prop.transform.position = groundPos;

        Part(prop.transform, "Post", PrimitiveType.Cylinder,
            new Vector3(0f, 1.1f, 0f), new Vector3(0.12f, 1.1f, 0.12f), post);
        Part(prop.transform, "Lantern", PrimitiveType.Cube,
            new Vector3(0f, 2.25f, 0f), new Vector3(0.26f, 0.3f, 0.26f), glass);

        return Lantern(parent, name, groundPos + Vector3.up * 2.25f, color, intensity, range,
            usePerlin: !steady, flickerMin: steady ? 1f : 0.85f, flickerMax: steady ? 1f : 1.08f,
            flickerSpeed: steady ? 1f : 6f, roomName: "Clearing");
    }

    static LightFlicker Lantern(Transform parent, string name, Vector3 position,
        Color color, float intensity, float range,
        bool usePerlin, float flickerMin, float flickerMax, float flickerSpeed, string roomName)
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
        flicker.roomCenter = position;
        flicker.roomName = roomName;
        return flicker;
    }

    // ------------------------- Small helpers (HouseFactory idiom) -------------------------

    static Transform Group(Transform parent, string name)
    {
        var group = new GameObject(name);
        group.transform.SetParent(parent, false);
        return group.transform;
    }

    static GameObject Cube(Transform parent, HouseLayout.Box box, Material material)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = box.name;
        cube.transform.SetParent(parent, false);
        cube.transform.position = box.center;
        cube.transform.localScale = box.size;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        return cube;
    }

    static GameObject Part(Transform parent, string name, PrimitiveType type,
        Vector3 localPos, Vector3 localScale, Material material, bool keepCollider = false)
    {
        GameObject part = GameObject.CreatePrimitive(type);
        part.name = name;
        if (!keepCollider)
        {
            Collider collider = part.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        }
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = localScale;
        part.GetComponent<Renderer>().sharedMaterial = material;
        return part;
    }

    static void Anchor(Transform parent, string name, Vector3 position)
    {
        var anchor = new GameObject(name);
        anchor.transform.SetParent(parent, false);
        anchor.transform.position = position;
    }

    static Material Emissive(Func<string, Color, Material> mat, string name, Color color, float strength)
    {
        Material material = mat(name, color);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * strength);
        return material;
    }

    static Vector3 Grounded(Vector3 position) => new Vector3(position.x, 0f, position.z);

    static Color Dim(Color color, float factor) =>
        new Color(color.r * factor, color.g * factor, color.b * factor, 1f);
}
