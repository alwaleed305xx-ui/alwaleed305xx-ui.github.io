using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// One-click wiring for the owned Asset Store packs
/// (menu: Screamer/Auto-Install My Assets).
///
/// After the user imports their purchases through Package Manager > My Assets
/// (Zombie Male, Morbid Creatures Mutant, Mimic prototype, Vintage Living
/// Room, lowpoly medieval buildings), this tool finds the imported models by
/// name, dresses the three Monster prefab skins with them (scaled to pawn
/// height, grounded, placeholders disabled - never deleted), and plants
/// furniture and buildings on the MapAnchors of the OPEN scene. Anything it
/// cannot find it reports and leaves on placeholders, so the game always
/// stays playable. Running it twice is safe: anchors and skins that already
/// carry an installed model are skipped.
/// </summary>
public static class ScreamerAssetInstaller
{
    const string MonsterPrefabPath = ScreamerSetupWizard.Root + "/Prefabs/Monster.prefab";
    const string InstalledPrefix = "Installed_";

    static readonly string[] ExcludedRoots =
    {
        "Assets/Screamer", "Assets/Scripts", "Assets/Editor"
    };

    // (skin root name, search terms, pawn height)
    static readonly (string skin, string[] terms, float height)[] SkinPlan =
    {
        ("Skin_Zombie", new[] { "zombie" }, 2.05f),
        ("Skin_Mutant", new[] { "mutant", "morbid", "creature" }, 2.25f),
        ("Skin_Mimic", new[] { "mimic" }, 2.0f),
    };

    // (anchor name, search terms, max footprint in units, required anchor)
    static readonly (string anchor, string[] terms, float footprint)[] AnchorPlan =
    {
        ("CouchAnchor", new[] { "sofa", "couch" }, 2.8f),
        ("ArmchairAnchor1", new[] { "armchair", "chair" }, 1.5f),
        ("ArmchairAnchor2", new[] { "armchair", "chair" }, 1.5f),
        ("TVAnchor", new[] { "television", "tv" }, 1.7f),
        ("FireplaceAnchor", new[] { "fireplace", "chimney" }, 2.6f),
        ("BedAnchor", new[] { "bed" }, 2.6f),
        ("YardShedAnchor", new[] { "shed", "barn", "house" }, 7f),
        ("GarageJunkAnchor1", new[] { "barrel", "crate" }, 1.4f),
        ("GarageJunkAnchor2", new[] { "crate", "barrel", "box" }, 1.4f),
        ("GarageJunkAnchor3", new[] { "barrel", "sack", "crate" }, 1.4f),
        ("MedievalBuildingAnchor1", new[] { "house", "building", "cottage", "tavern" }, 9f),
        ("MedievalBuildingAnchor2", new[] { "house", "building", "cottage", "tavern" }, 9f),
        ("MedievalBuildingAnchor3", new[] { "house", "building", "cottage", "tavern" }, 9f),
        // The Wasteland pack: ruins past the south treeline, junk on the fence line.
        ("RuinAnchor1", new[] { "tower", "ruin", "wall" }, 8f),
        ("RuinAnchor2", new[] { "ruin", "building", "shack" }, 8f),
        ("RuinAnchor3", new[] { "wall", "tower", "debris" }, 8f),
        ("YardFenceDressingAnchor1", new[] { "rock", "debris", "cart" }, 3.5f),
        ("YardFenceDressingAnchor2", new[] { "debris", "rock", "crate" }, 3.5f),
        ("YardFenceDressingAnchor3", new[] { "cart", "wagon", "rock" }, 3.5f),
        ("YardFenceDressingAnchor4", new[] { "rock", "barrel", "debris" }, 3.5f),
    };

    // Unity Gaming Services building-block widgets (Player Account, Sessions,
    // Matchmaker, Leaderboards, Achievements). They stack on a hidden overlay
    // canvas toggled with [F1] in Play mode; each becomes functional once the
    // project is linked under Project Settings > Services.
    static readonly (string label, string[] terms)[] WidgetPlan =
    {
        ("PlayerAccount", new[] { "sign in", "signin", "player account", "login" }),
        ("MultiplayerSession", new[] { "create session", "join session", "session" }),
        ("Matchmaker", new[] { "matchmaker", "matchmaking" }),
        ("Leaderboards", new[] { "leaderboard" }),
        ("Achievements", new[] { "achievement" }),
    };

    [MenuItem("Screamer/Auto-Install My Assets")]
    public static void InstallEverything()
    {
        var report = new List<string>();

        int skins = InstallMonsterSkins(report);
        int props = InstallSceneProps(report);
        int widgets = InstallUgsWidgets(report);
        int layers = PaintTerrain(report);
        if (layers > 0) report.Add("OK   terrain repainted with " + layers + " imported layer(s).");

        Debug.Log("SCREAMER asset install report:\n  " + string.Join("\n  ", report));

        EditorUtility.DisplayDialog("Auto-Install My Assets",
            "Installed " + skins + " monster skin(s), " + props + " scene prop(s) and " +
            widgets + " UGS widget(s) (press F1 in Play mode; link the project under " +
            "Project Settings > Services to make them live).\n\n" +
            "Anything not found stays on its placeholder - import the pack via " +
            "Package Manager > My Assets and run this again.\n\n" +
            "Full detail is in the Console.",
            "Nice");
    }

    // ------------------------- Monster skins -------------------------

    static int InstallMonsterSkins(List<string> report)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(MonsterPrefabPath) == null)
        {
            report.Add("SKIP skins: no Monster prefab yet - run Screamer > Build Everything first.");
            return 0;
        }

        int installed = 0;
        GameObject root = PrefabUtility.LoadPrefabContents(MonsterPrefabPath);
        try
        {
            foreach (var plan in SkinPlan)
            {
                Transform skinRoot = FindDeep(root.transform, plan.skin);
                if (skinRoot == null)
                {
                    report.Add("MISS " + plan.skin + ": skin root not found in the Monster prefab.");
                    continue;
                }
                if (HasInstalled(skinRoot))
                {
                    report.Add("OK   " + plan.skin + ": already has an installed model.");
                    continue;
                }

                GameObject asset = FindBestAsset(plan.terms, out string assetPath);
                if (asset == null)
                {
                    report.Add("MISS " + plan.skin + ": no asset found for [" +
                        string.Join(", ", plan.terms) + "] - import the pack, then rerun.");
                    continue;
                }

                // Placeholders off (kept for instant rollback), model in.
                var placeholders = new List<GameObject>();
                foreach (Transform child in skinRoot) placeholders.Add(child.gameObject);

                GameObject instance = Instantiate(asset, skinRoot);
                instance.name = InstalledPrefix + asset.name;
                FitToHeight(instance.transform, skinRoot, plan.height);

                foreach (GameObject placeholder in placeholders) placeholder.SetActive(false);

                installed++;
                report.Add("OK   " + plan.skin + " <- " + assetPath);
            }

            if (installed > 0) PrefabUtility.SaveAsPrefabAsset(root, MonsterPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        return installed;
    }

    // ------------------------- Scene props on anchors -------------------------

    static int InstallSceneProps(List<string> report)
    {
        MapAnchors anchors = Object.FindObjectOfType<MapAnchors>();
        if (anchors == null)
        {
            report.Add("SKIP props: no MapAnchors in the open scene - run Screamer > Build Everything first.");
            return 0;
        }

        int installed = 0;
        foreach (var plan in AnchorPlan)
        {
            Transform anchor = anchors.transform.Find(plan.anchor);
            if (anchor == null) continue; // forest-only anchors are absent on the house map
            if (HasInstalled(anchor))
            {
                report.Add("OK   " + plan.anchor + ": already dressed.");
                continue;
            }

            GameObject asset = FindBestAsset(plan.terms, out string assetPath);
            if (asset == null)
            {
                report.Add("MISS " + plan.anchor + ": no asset for [" + string.Join(", ", plan.terms) + "].");
                continue;
            }

            GameObject instance = Instantiate(asset, anchor);
            instance.name = InstalledPrefix + asset.name;
            FitToFootprint(instance.transform, anchor, plan.footprint);

            // Face the middle of the map so showpieces read from the play area.
            Vector3 toCenter = -anchor.position; toCenter.y = 0f;
            if (toCenter.sqrMagnitude > 0.01f)
                instance.transform.rotation = Quaternion.LookRotation(toCenter.normalized);

            installed++;
            report.Add("OK   " + plan.anchor + " <- " + assetPath);
        }

        if (installed > 0)
        {
            EditorSceneManager.MarkSceneDirty(anchors.gameObject.scene);
            EditorSceneManager.SaveScene(anchors.gameObject.scene);
        }
        return installed;
    }

    // ------------------------- Terrain repaint (terrain sample pack) -------------------------

    /// <summary>
    /// Repaints the Dead Hills terrain with imported TerrainLayer assets
    /// (the terrain sample pack ships several): first layer everywhere,
    /// second layer on steep slopes. No terrain in the scene, or no layers
    /// imported yet, is a reported no-op.
    /// </summary>
    static int PaintTerrain(List<string> report)
    {
        Terrain terrain = Object.FindObjectOfType<Terrain>();
        if (terrain == null)
        {
            report.Add("SKIP terrain paint: this map has no terrain (Dead Hills only).");
            return 0;
        }

        var layers = new List<TerrainLayer>();
        foreach (string guid in AssetDatabase.FindAssets("t:TerrainLayer"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Excluded(path)) continue;
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer != null && layer.diffuseTexture != null) layers.Add(layer);
            if (layers.Count == 4) break;
        }

        if (layers.Count == 0)
        {
            report.Add("MISS terrain paint: no imported TerrainLayer assets found yet.");
            return 0;
        }

        TerrainData data = terrain.terrainData;
        data.terrainLayers = layers.ToArray();

        if (layers.Count > 1)
        {
            int res = data.alphamapResolution;
            var alpha = new float[res, res, layers.Count];
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float nx = x / (float)(res - 1);
                    float nz = z / (float)(res - 1);
                    bool steep = data.GetSteepness(nx, nz) > 18f;
                    alpha[z, x, 0] = steep ? 0f : 1f;
                    alpha[z, x, 1] = steep ? 1f : 0f;
                }
            }
            data.SetAlphamaps(0, 0, alpha);
        }

        EditorUtility.SetDirty(data);
        AssetDatabase.SaveAssets();
        return layers.Count;
    }

    // ------------------------- UGS building-block widgets -------------------------

    /// <summary>
    /// Stacks every imported Unity Building Block widget on a hidden overlay
    /// canvas ([F1] toggles it in Play mode). The blocks ship their own logic;
    /// they go live once the project is linked under Project Settings > Services.
    /// </summary>
    static int InstallUgsWidgets(List<string> report)
    {
        int installed = 0;
        Transform stack = null;

        foreach (var plan in WidgetPlan)
        {
            GameObject asset = FindBestAsset(plan.terms, out string assetPath);
            if (asset == null)
            {
                report.Add("MISS widget " + plan.label + ": block not imported yet (My Assets > Import).");
                continue;
            }

            if (stack == null) stack = FindOrBuildWidgetStack();
            if (HasNamedChild(stack, InstalledPrefix + plan.label))
            {
                report.Add("OK   widget " + plan.label + ": already placed.");
                continue;
            }

            GameObject instance = Instantiate(asset, stack);
            instance.name = InstalledPrefix + plan.label;
            if (instance.transform is RectTransform rect)
            {
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-24f, -24f - 180f * (stack.childCount - 1));
            }

            installed++;
            report.Add("OK   widget " + plan.label + " <- " + assetPath);
        }

        if (installed > 0)
        {
            EditorSceneManager.MarkSceneDirty(stack.gameObject.scene);
            EditorSceneManager.SaveScene(stack.gameObject.scene);
        }
        return installed;
    }

    static Transform FindOrBuildWidgetStack()
    {
        GameObject existing = GameObject.Find("UgsWidgetsCanvas");
        if (existing != null) return existing.transform;

        var canvasObject = new GameObject("UgsWidgetsCanvas");
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 4500;
        var scaler = canvasObject.AddComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        canvasObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        canvasObject.AddComponent<UgsWidgetPanelToggle>();
        return canvasObject.transform;
    }

    static bool HasNamedChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
            if (child.name == name) return true;
        return false;
    }

    // ------------------------- Asset search -------------------------

    /// <summary>
    /// Best prefab (then model) whose name matches a term, searched across the
    /// imported packs only; our own generated folders are excluded.
    /// </summary>
    static GameObject FindBestAsset(string[] terms, out string bestPath)
    {
        bestPath = null;
        int bestScore = 0;

        foreach (string typeFilter in new[] { "t:Prefab ", "t:Model " })
        {
            foreach (string term in terms)
            {
                foreach (string guid in AssetDatabase.FindAssets(typeFilter + term))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Excluded(path)) continue;

                    int score = Score(System.IO.Path.GetFileNameWithoutExtension(path), term);
                    if (typeFilter.StartsWith("t:Prefab")) score += 10;
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestPath = path;
                    }
                }
            }
            if (bestPath != null) break; // prefer any prefab hit over model hits
        }

        return bestPath == null ? null : AssetDatabase.LoadAssetAtPath<GameObject>(bestPath);
    }

    static int Score(string assetName, string term)
    {
        string name = assetName.ToLowerInvariant();
        term = term.ToLowerInvariant();
        if (name == term) return 100;
        foreach (string token in name.Split(' ', '_', '-', '(', ')', '.'))
            if (token == term) return 80;
        if (name.StartsWith(term)) return 60;
        if (name.Contains(term)) return 30 - Mathf.Min(20, name.Length - term.Length);
        return 0;
    }

    static bool Excluded(string path)
    {
        foreach (string root in ExcludedRoots)
            if (path.StartsWith(root)) return true;
        return false;
    }

    // ------------------------- Fitting -------------------------

    static void FitToHeight(Transform instance, Transform ground, float targetHeight)
    {
        Bounds bounds = RenderBounds(instance);
        if (bounds.size.y > 0.01f)
            instance.localScale = instance.localScale * (targetHeight / bounds.size.y);
        Settle(instance, ground);
    }

    static void FitToFootprint(Transform instance, Transform ground, float maxFootprint)
    {
        Bounds bounds = RenderBounds(instance);
        float footprint = Mathf.Max(bounds.size.x, bounds.size.z);
        if (footprint > 0.01f && footprint > maxFootprint)
            instance.localScale = instance.localScale * (maxFootprint / footprint);
        Settle(instance, ground);
    }

    /// <summary>Feet on the anchor's ground, bounds centered over it.</summary>
    static void Settle(Transform instance, Transform ground)
    {
        Bounds bounds = RenderBounds(instance);
        if (bounds.size == Vector3.zero) return;
        Vector3 anchorPos = ground.position;
        instance.position += new Vector3(
            anchorPos.x - bounds.center.x,
            anchorPos.y - bounds.min.y,
            anchorPos.z - bounds.center.z);
    }

    static Bounds RenderBounds(Transform instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(instance.position, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    // ------------------------- Small helpers -------------------------

    static GameObject Instantiate(GameObject asset, Transform parent)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent);
        if (instance == null)
        {
            instance = Object.Instantiate(asset, parent); // models without prefab links
            instance.name = asset.name;
        }
        return instance;
    }

    static bool HasInstalled(Transform parent)
    {
        foreach (Transform child in parent)
            if (child.name.StartsWith(InstalledPrefix)) return true;
        return false;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
        {
            Transform hit = FindDeep(child, name);
            if (hit != null) return hit;
        }
        return null;
    }
}
