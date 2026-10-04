using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The one-click project assembler (menu: Screamer/Build Everything).
///
/// GDD pillar 5: the entire game - map, pawns, the six task stations, the
/// cellar door, networking, bots, audio, feel systems and every uGUI screen -
/// is built from code before any art asset is imported. This wizard owns the
/// orchestration ONLY: it calls each module's runtime factory in the binding
/// order, supplies an AssetDatabase-backed material delegate, saves the pawn
/// prefabs and the scene, and wires the final TaskManager references.
/// Anything gameplay-specific that is missing or wrong is a bug in the owning
/// factory, never something to patch here.
///
/// Build order (binding, per the architecture plan):
///   1. HouseFactory.BuildAll            - the Henderson House + lighting + FX overlay
///   2. CharacterFactory pawns           - saved as prefabs under Assets/Screamer/Prefabs
///   3. TaskFactory stations + cellar door
///   4. RoundFlowFactory.BuildNetworkAndManagers (HouseLayout spawns)
///   5. BotFactory.Install
///   6. AudioFactory.Install
///   7. FeelFactory.Install
///   8. UiFactory.BuildAll
///   9. Final wiring of TaskManager.allTasks / escapeDoor
///  10. Save Assets/Screamer/Scenes/Game.unity + build settings
/// </summary>
public static class ScreamerSetupWizard
{
    /// <summary>Root folder for every generated asset. Public contract - other tooling may rely on it.</summary>
    public const string Root = "Assets/Screamer";

    const string ScenePath = Root + "/Scenes/Game.unity";
    const string PrefabsFolder = Root + "/Prefabs";
    const string MaterialsFolder = Root + "/Materials";
    const string ProgressTitle = "SCREAMER - Build Everything";

    static readonly string[] SubFolders = { "Prefabs", "Scenes", "Materials", "Fonts" };

    /// <summary>
    /// The placeholder-contract anchors (GDD section 13) that HouseFactory must
    /// have created. Verified after the build; a missing anchor is logged as an
    /// integration bug against the world module, not fixed here.
    /// </summary>
    static readonly string[] RequiredAnchors =
    {
        "FireplaceAnchor", "CouchAnchor", "ArmchairAnchor1", "ArmchairAnchor2",
        "TVAnchor", "KaraokeAnchor", "DancePadAnchor", "StoveAnchor",
        "ToiletAnchor", "BedAnchor",
        "GarageJunkAnchor1", "GarageJunkAnchor2", "GarageJunkAnchor3",
        "YardShedAnchor",
        "YardFenceDressingAnchor1", "YardFenceDressingAnchor2",
        "YardFenceDressingAnchor3", "YardFenceDressingAnchor4",
        "CellarDoorAnchor"
    };

    [MenuItem("Screamer/Build Everything")]
    public static void BuildEverything()
    {
        // Never silently throw away someone's open scene work.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        EnsureFolders();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        Func<string, Color, Material> mat = AssetBackedMaterial;

        // The default scene ships a plain white sun. The house's only
        // directional light is the moon (GDD 6.1); a second one would wash
        // out the whole warm-rooms / teal-shadows grade, so it goes now.
        // The default Main Camera stays - it carries the AudioListener and
        // the menu dolly rides it.
        RemoveDefaultDirectionalLight();

        try
        {
            Step(0.05f, "Building the Henderson House...");
            var mapRoot = new GameObject("Map");
            HouseFactory.BuildAll(mapRoot.transform, mat);

            Step(0.25f, "Building pawn prefabs...");
            GameObject survivorPrefab = SavePawnPrefab(CharacterFactory.BuildSurvivorPawn(mat), "Survivor");
            GameObject monsterPrefab = SavePawnPrefab(CharacterFactory.BuildMonsterPawn(mat), "Monster");

            Step(0.40f, "Building task stations and the cellar door...");
            var tasksRoot = new GameObject("Tasks");
            TaskBase[] tasks = TaskFactory.BuildAllStations(tasksRoot.transform, mat);
            EscapeDoor cellarDoor = TaskFactory.BuildCellarDoor(tasksRoot.transform, mat);

            Step(0.50f, "Building the hide-and-shriek closets...");
            HideSpotFactory.BuildAll(mapRoot.transform, mat);

            Step(0.55f, "Building network and round-flow managers...");
            RoundFlowFactory.BuildNetworkAndManagers(survivorPrefab, monsterPrefab,
                HouseLayout.SurvivorSpawns, HouseLayout.MonsterSpawn);

            Step(0.65f, "Installing bots...");
            BotFactory.Install();

            Step(0.72f, "Installing the audio bank...");
            AudioFactory.Install();

            Step(0.80f, "Installing feel and killcam systems...");
            FeelFactory.Install(mat);

            Step(0.88f, "Building all UI screens...");
            UiFactory.BuildAll();

            Step(0.94f, "Final wiring and verification...");
            WireTaskManager(tasks, cellarDoor);
            VerifyAnchors();

            Step(0.97f, "Saving scene and build settings...");
            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        EditorUtility.DisplayDialog("SCREAMER",
            "Everything is built.\n\n" +
            "Scene: " + ScenePath + "\n" +
            "Prefabs: " + PrefabsFolder + "\n\n" +
            "Press Play, HOST GAME, READY UP, START ROUND.\n" +
            "Bots fill the lobby after 10 seconds, so a solo round works immediately.\n\n" +
            "To ship: Screamer > Build Windows x64 (the shipped-string audit runs first).",
            "Go scream");
    }

    // ------------------------- Scene preparation -------------------------

    /// <summary>
    /// Deletes every directional light the fresh DefaultGameObjects scene came
    /// with, so HouseFactory's moon is the only sun the house ever sees.
    /// </summary>
    static void RemoveDefaultDirectionalLight()
    {
        foreach (Light light in UnityEngine.Object.FindObjectsOfType<Light>())
            if (light != null && light.type == LightType.Directional)
                UnityEngine.Object.DestroyImmediate(light.gameObject);
    }

    // ------------------------- Folders and materials -------------------------

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(Root))
            AssetDatabase.CreateFolder("Assets", "Screamer");

        foreach (string sub in SubFolders)
            if (!AssetDatabase.IsValidFolder(Root + "/" + sub))
                AssetDatabase.CreateFolder(Root, sub);
    }

    /// <summary>
    /// The asset-backed implementation of the shared material delegate
    /// (name + color -> material). Materials live under
    /// Assets/Screamer/Materials and are reused across wizard runs, so scene
    /// renderers reference stable assets instead of scene-embedded materials.
    /// Shader resolution goes through ScreamerPalette.LitShader() so a future
    /// pipeline change needs zero wizard edits.
    /// </summary>
    static Material AssetBackedMaterial(string name, Color color)
    {
        string path = MaterialsFolder + "/" + SanitizeAssetName(name) + ".mat";

        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(ScreamerPalette.LitShader());
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            // A stale asset from an earlier run keeps its identity but follows
            // the current shader chain and palette.
            Shader lit = ScreamerPalette.LitShader();
            if (material.shader != lit)
                material.shader = lit;
        }

        material.color = color;
        EditorUtility.SetDirty(material);
        return material;
    }

    static string SanitizeAssetName(string name)
    {
        if (string.IsNullOrEmpty(name)) return "Mat_Unnamed";

        var builder = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            builder.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return builder.ToString();
    }

    // ------------------------- Prefabs -------------------------

    /// <summary>
    /// Saves a factory-built scene pawn as a prefab asset and removes the
    /// scene instance; pawns enter the scene only via network spawning.
    /// </summary>
    static GameObject SavePawnPrefab(GameObject sceneInstance, string fallbackName)
    {
        if (sceneInstance == null)
            throw new InvalidOperationException(
                "CharacterFactory returned null for '" + fallbackName + "' - file against the characters module.");

        string name = string.IsNullOrEmpty(sceneInstance.name) ? fallbackName : sceneInstance.name;
        string path = PrefabsFolder + "/" + SanitizeAssetName(name) + ".prefab";

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(sceneInstance, path);
        UnityEngine.Object.DestroyImmediate(sceneInstance);

        if (prefab == null)
            throw new InvalidOperationException("Failed to save prefab at '" + path + "'.");
        return prefab;
    }

    // ------------------------- Final wiring -------------------------

    /// <summary>
    /// The wizard owns the last-mile references: whatever TaskFactory wired
    /// on the way through, the saved scene must leave with TaskManager
    /// pointing at exactly this build's stations and door.
    /// </summary>
    static void WireTaskManager(TaskBase[] tasks, EscapeDoor cellarDoor)
    {
        TaskManager manager = UnityEngine.Object.FindObjectOfType<TaskManager>();
        if (manager == null)
            manager = new GameObject("TaskManager").AddComponent<TaskManager>();

        manager.allTasks = tasks ?? new TaskBase[0];
        manager.escapeDoor = cellarDoor;
        EditorUtility.SetDirty(manager);

        if (tasks == null || tasks.Length != 6)
            Debug.LogWarning("ScreamerSetupWizard: expected 6 task stations, got " +
                (tasks == null ? 0 : tasks.Length) + " - file against the tasks module.");
        if (cellarDoor == null)
            Debug.LogWarning("ScreamerSetupWizard: TaskFactory.BuildCellarDoor returned null - file against the tasks module.");
    }

    /// <summary>
    /// Verifies the GDD section 13 placeholder contract: every named anchor
    /// must exist under the MapAnchors object so asset packs can drop in
    /// without touching triggers, spawns or scripts. Missing anchors are
    /// integration bugs against the world module.
    /// </summary>
    static void VerifyAnchors()
    {
        MapAnchors anchors = UnityEngine.Object.FindObjectOfType<MapAnchors>();
        if (anchors == null)
        {
            Debug.LogWarning("ScreamerSetupWizard: no MapAnchors object in the built scene - file against the world module.");
            return;
        }

        // Component lookups rely on Awake, which does not run in edit mode;
        // read the hierarchy directly instead.
        var present = new HashSet<string>();
        foreach (Transform child in anchors.transform)
            present.Add(child.name);

        foreach (string required in RequiredAnchors)
            if (!present.Contains(required))
                Debug.LogWarning("ScreamerSetupWizard: missing map anchor '" + required +
                    "' (GDD section 13) - file against the world module.");
    }

    // ------------------------- Utilities -------------------------

    static void Step(float progress, string message)
    {
        EditorUtility.DisplayProgressBar(ProgressTitle, message, progress);
    }
}
