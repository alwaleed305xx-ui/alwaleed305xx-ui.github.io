#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;

/// <summary>
/// ⭐ أداة التجهيز بضغطة وحدة ⭐
/// من القائمة العلوية: Sayeh → Setup Everything
/// تبني لك: الخريطة، البريفابات، المهام الست، الواجهة، الشبكة — كل شي!
/// بعدها اضغط Play مباشرة وجرب (Host بنافذة و Client بنافذة ثانية).
/// الموديلات مؤقتة (أشكال ملونة) — بدّلها بأسيتاتك الحقيقية وقت ما تبي.
/// </summary>
public static class SayehSetupWizard
{
    const string Root = "Assets/Sayeh";

    [MenuItem("Sayeh/🎮 Setup Everything (جهز اللعبة كاملة)")]
    public static void SetupEverything()
    {
        EnsureFolders();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        SetupLighting();
        GameObject survivorPrefab = BuildSurvivorPrefab();
        GameObject monsterPrefab = BuildMonsterPrefab();
        BuildMap();
        TaskBase[] tasks = BuildTasks();
        EscapeDoor door = BuildEscapeDoor();
        Transform[] survivorSpawns; Transform monsterSpawn;
        BuildSpawnPoints(out survivorSpawns, out monsterSpawn);
        BuildTaskManager(tasks, door);
        BuildGameManager(survivorPrefab, monsterPrefab, survivorSpawns, monsterSpawn);
        BuildNetworkManager(survivorPrefab, monsterPrefab);
        BuildUI();

        string scenePath = Root + "/Scenes/Game.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        AssetDatabase.SaveAssets();

        EditorUtility.DisplayDialog("صايح 🧟",
            "تم تجهيز كل شي!\n\nاضغط Play وجرب:\n- زر Host يستضيف\n- افتح نسخة ثانية (Build) وادخل بزر Join\n- المضيف يضغط Start Round\n\nبدّل الأشكال المؤقتة بأسيتاتك وقت ما تبي (شوف README)",
            "يلا نلعب!");
    }

    // ───────────────────────── مجلدات وخامات ─────────────────────────

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(Root)) AssetDatabase.CreateFolder("Assets", "Sayeh");
        foreach (string sub in new[] { "Prefabs", "Scenes", "Materials" })
            if (!AssetDatabase.IsValidFolder(Root + "/" + sub))
                AssetDatabase.CreateFolder(Root, sub);
    }

    static Shader LitShader()
    {
        Shader s = Shader.Find("Universal Render Pipeline/Lit");
        if (s == null) s = Shader.Find("HDRP/Lit");
        if (s == null) s = Shader.Find("Standard");
        return s;
    }

    static Material Mat(string name, Color color)
    {
        string path = Root + "/Materials/" + name + ".mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null)
        {
            m = new Material(LitShader());
            AssetDatabase.CreateAsset(m, path);
        }
        m.color = color;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ───────────────────────── إضاءة رعب 🕯️ ─────────────────────────

    static void SetupLighting()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.10f, 0.11f, 0.17f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.015f;
        RenderSettings.fogColor = new Color(0.05f, 0.05f, 0.09f);

        GameObject lightGO = GameObject.Find("Directional Light");
        if (lightGO != null)
        {
            Light l = lightGO.GetComponent<Light>();
            l.intensity = 0.35f;
            l.color = new Color(0.75f, 0.7f, 0.9f); // ضوء قمر
            lightGO.transform.rotation = Quaternion.Euler(55, -30, 0);
        }
    }

    // ───────────────────────── أدوات بناء ─────────────────────────

    static GameObject Cube(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent = null)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static GameObject Visual(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale, Material mat)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>()); // شكل فقط بدون تصادم
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static void AddLabel(GameObject target, string text, Color color, float height = 2.4f)
    {
        GameObject go = new GameObject("Label");
        go.transform.SetParent(target.transform, false);
        go.transform.localPosition = new Vector3(0, height, 0);
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 48;
        tm.characterSize = 0.12f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = color;
        go.AddComponent<Billboard>();
    }

    static GameObject SavePrefab(GameObject go, string name)
    {
        string path = Root + "/Prefabs/" + name + ".prefab";
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
        Object.DestroyImmediate(go);
        return prefab;
    }

    // ───────────────────────── البريفابات ─────────────────────────

    static GameObject BuildSurvivorPrefab()
    {
        GameObject root = new GameObject("Survivor");
        CharacterController cc = root.AddComponent<CharacterController>();
        cc.height = 2f; cc.radius = 0.45f; cc.center = new Vector3(0, 1f, 0);
        root.AddComponent<NetworkObject>();
        root.AddComponent<ClientNetworkTransform>();

        Visual(PrimitiveType.Capsule, "Body", root.transform,
            new Vector3(0, 1f, 0), Vector3.one, Mat("Survivor", new Color(0.3f, 0.64f, 1f)));
        // كشخة بسيطة: طاقية 🧢
        Visual(PrimitiveType.Cube, "Cap", root.transform,
            new Vector3(0, 1.95f, 0.1f), new Vector3(0.55f, 0.15f, 0.7f), Mat("Cap", new Color(1f, 0.85f, 0.2f)));

        GameObject camHolder = new GameObject("CameraHolder");
        camHolder.transform.SetParent(root.transform, false);
        camHolder.transform.localPosition = new Vector3(0, 1.65f, 0);

        PlayerController pc = root.AddComponent<PlayerController>();
        pc.cameraHolder = camHolder.transform;

        return SavePrefab(root, "Survivor");
    }

    static GameObject BuildMonsterPrefab()
    {
        GameObject root = new GameObject("Monster");
        CharacterController cc = root.AddComponent<CharacterController>();
        cc.height = 2.3f; cc.radius = 0.55f; cc.center = new Vector3(0, 1.15f, 0);
        root.AddComponent<NetworkObject>();
        root.AddComponent<ClientNetworkTransform>();

        GameObject camHolder = new GameObject("CameraHolder");
        camHolder.transform.SetParent(root.transform, false);
        camHolder.transform.localPosition = new Vector3(0, 1.9f, 0);

        // ثلاث أشكال مؤقتة — بدّلها بموديلات: الزومبي / المسخ / الميميك من أسيتاتك
        GameObject skinsRoot = new GameObject("Skins");
        skinsRoot.transform.SetParent(root.transform, false);

        GameObject zombie = new GameObject("Skin_Zombie_PLACEHOLDER");
        zombie.transform.SetParent(skinsRoot.transform, false);
        Visual(PrimitiveType.Capsule, "ZBody", zombie.transform, new Vector3(0, 1.15f, 0),
            new Vector3(1.1f, 1.15f, 1.1f), Mat("Zombie", new Color(0.35f, 0.55f, 0.25f)));
        Visual(PrimitiveType.Sphere, "ZEyeL", zombie.transform, new Vector3(-0.2f, 1.9f, 0.45f),
            Vector3.one * 0.18f, Mat("EyeRed", new Color(1f, 0.1f, 0.1f)));
        Visual(PrimitiveType.Sphere, "ZEyeR", zombie.transform, new Vector3(0.2f, 1.9f, 0.45f),
            Vector3.one * 0.18f, Mat("EyeRed", new Color(1f, 0.1f, 0.1f)));

        GameObject mutant = new GameObject("Skin_Mutant_PLACEHOLDER");
        mutant.transform.SetParent(skinsRoot.transform, false);
        Visual(PrimitiveType.Cube, "MBody", mutant.transform, new Vector3(0, 1.15f, 0),
            new Vector3(1.3f, 2.1f, 0.9f), Mat("Mutant", new Color(0.72f, 0.42f, 0.3f)));
        Visual(PrimitiveType.Sphere, "MHead", mutant.transform, new Vector3(0, 2.45f, 0),
            Vector3.one * 0.7f, Mat("Mutant", new Color(0.72f, 0.42f, 0.3f)));

        GameObject mimic = new GameObject("Skin_Mimic_PLACEHOLDER");
        mimic.transform.SetParent(skinsRoot.transform, false);
        Visual(PrimitiveType.Sphere, "MimBody", mimic.transform, new Vector3(0, 1f, 0),
            new Vector3(1.4f, 1.1f, 1.4f), Mat("Mimic", new Color(0.08f, 0.06f, 0.1f)));
        Visual(PrimitiveType.Sphere, "MimEye", mimic.transform, new Vector3(0, 1.4f, 0.55f),
            Vector3.one * 0.3f, Mat("EyeYellow", new Color(1f, 0.85f, 0.1f)));

        MonsterController mc = root.AddComponent<MonsterController>();
        mc.cameraHolder = camHolder.transform;

        MonsterSkinSelector skins = root.AddComponent<MonsterSkinSelector>();
        skins.skins = new[] { zombie, mutant, mimic };

        return SavePrefab(root, "Monster");
    }

    // ───────────────────────── الخريطة ─────────────────────────

    static void BuildMap()
    {
        GameObject map = new GameObject("Map");
        Material floorM = Mat("Floor", new Color(0.16f, 0.16f, 0.2f));
        Material wallM = Mat("Wall", new Color(0.23f, 0.22f, 0.28f));

        Cube("Floor", new Vector3(0, -0.25f, 0), new Vector3(41, 0.5f, 41), floorM, map.transform);

        // جدران محيطة — الشمال فيه فتحة لباب الهروب
        Cube("Wall_S", new Vector3(0, 2, -20.5f), new Vector3(41, 4, 1), wallM, map.transform);
        Cube("Wall_E", new Vector3(20.5f, 2, 0), new Vector3(1, 4, 41), wallM, map.transform);
        Cube("Wall_W", new Vector3(-20.5f, 2, 0), new Vector3(1, 4, 41), wallM, map.transform);
        Cube("Wall_N_Left", new Vector3(-12.25f, 2, 20.5f), new Vector3(16.5f, 4, 1), wallM, map.transform);
        Cube("Wall_N_Right", new Vector3(12.25f, 2, 20.5f), new Vector3(16.5f, 4, 1), wallM, map.transform);

        // جدران داخلية — غرفة وسطية بممرات جانبية
        Cube("InnerWall_N", new Vector3(0, 2, 10), new Vector3(24, 4, 1), wallM, map.transform);
        Cube("InnerWall_S", new Vector3(0, 2, -10), new Vector3(24, 4, 1), wallM, map.transform);
        Cube("InnerWall_E", new Vector3(12, 2, 0), new Vector3(1, 4, 12), wallM, map.transform);
        Cube("InnerWall_W", new Vector3(-12, 2, 0), new Vector3(1, 4, 12), wallM, map.transform);
    }

    // ───────────────────────── المهام الست ─────────────────────────

    static TaskBase[] BuildTasks()
    {
        GameObject tasksRoot = new GameObject("Tasks");
        var list = new System.Collections.Generic.List<TaskBase>();

        // (مكان، لون، اسم عالمي للافتة)
        list.Add(PlaceTask<TaskScream>(tasksRoot.transform, new Vector3(-16, 0.5f, 16),
            Mat("TScream", new Color(0.95f, 0.3f, 0.3f)), "SCREAM!"));
        list.Add(PlaceTask<TaskKaraoke>(tasksRoot.transform, new Vector3(0, 0.5f, 0),
            Mat("TKaraoke", new Color(0.7f, 0.4f, 0.95f)), "KARAOKE"));
        list.Add(PlaceTask<TaskDance>(tasksRoot.transform, new Vector3(16, 0.5f, 16),
            Mat("TDance", new Color(0.95f, 0.6f, 0.2f)), "DANCE"));
        list.Add(PlaceTask<TaskNoodles>(tasksRoot.transform, new Vector3(-16, 0.5f, -16),
            Mat("TNoodles", new Color(0.95f, 0.85f, 0.3f)), "NOODLES"));
        list.Add(PlaceTask<TaskToilet>(tasksRoot.transform, new Vector3(16, 0.5f, -16),
            Mat("TToilet", new Color(0.4f, 0.75f, 0.95f)), "TOILET"));

        // الدجاجة 🐔 تتحرك — تحتاج NetworkTransform (السيرفر يحركها)
        GameObject chicken = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        chicken.name = "Task_Chicken";
        chicken.transform.SetParent(tasksRoot.transform, false);
        chicken.transform.position = new Vector3(0, 0.4f, 16);
        chicken.transform.localScale = Vector3.one * 0.8f;
        chicken.GetComponent<Renderer>().sharedMaterial = Mat("Chicken", Color.white);
        Visual(PrimitiveType.Cube, "Beak", chicken.transform, new Vector3(0, 0.1f, 0.5f),
            new Vector3(0.25f, 0.2f, 0.35f), Mat("Beak", new Color(1f, 0.6f, 0.1f)));
        chicken.AddComponent<NetworkObject>();
        chicken.AddComponent<Unity.Netcode.Components.NetworkTransform>();
        TaskChicken tc = chicken.AddComponent<TaskChicken>();
        AddLabel(chicken, "CHICKEN", Color.white, 1.6f);
        list.Add(tc);

        return list.ToArray();
    }

    static TaskBase PlaceTask<T>(Transform parent, Vector3 pos, Material mat, string label) where T : TaskBase
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "Task_" + typeof(T).Name;
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        go.AddComponent<NetworkObject>();
        T task = go.AddComponent<T>();
        AddLabel(go, label, mat.color);
        return task;
    }

    // ───────────────────────── باب الهروب ─────────────────────────

    static EscapeDoor BuildEscapeDoor()
    {
        GameObject root = new GameObject("EscapeDoor");
        root.transform.position = new Vector3(0, 0, 20.5f);

        // الضلفة اللي تسد الفتحة (تختفي لما تخلص المهام)
        GameObject block = Cube("DoorBlock", new Vector3(0, 2, 20.4f),
            new Vector3(8, 4, 0.6f), Mat("Door", new Color(0.45f, 0.12f, 0.12f)), root.transform);

        // بوابة النجاة خلفها — تضغط E عليها بعد الفتح
        Cube("Portal", new Vector3(0, 2, 21.1f),
            new Vector3(8, 4, 0.2f), Mat("Portal", new Color(0.2f, 0.85f, 0.4f)), root.transform);

        AddLabel(root, "EXIT", new Color(0.2f, 0.9f, 0.4f), 4.6f);

        EscapeDoor door = root.AddComponent<EscapeDoor>();
        door.closedVisual = block;
        return door;
    }

    // ───────────────────────── نقاط الظهور ─────────────────────────

    static void BuildSpawnPoints(out Transform[] survivorSpawns, out Transform monsterSpawn)
    {
        GameObject root = new GameObject("SpawnPoints");
        Vector3[] sPos = {
            new Vector3(-4, 0.1f, 4), new Vector3(4, 0.1f, 4),
            new Vector3(-4, 0.1f, -4), new Vector3(4, 0.1f, -4),
            new Vector3(0, 0.1f, 6), new Vector3(0, 0.1f, -6), new Vector3(6, 0.1f, 0)
        };
        survivorSpawns = new Transform[sPos.Length];
        for (int i = 0; i < sPos.Length; i++)
        {
            GameObject sp = new GameObject("SurvivorSpawn_" + (i + 1));
            sp.transform.SetParent(root.transform, false);
            sp.transform.position = sPos[i];
            survivorSpawns[i] = sp.transform;
        }

        GameObject ms = new GameObject("MonsterSpawn");
        ms.transform.SetParent(root.transform, false);
        ms.transform.position = new Vector3(0, 0.1f, -17); // أقصى الجنوب — بعيد عن الباب
        monsterSpawn = ms.transform;
    }

    // ───────────────────────── المدراء والشبكة ─────────────────────────

    static void BuildTaskManager(TaskBase[] tasks, EscapeDoor door)
    {
        GameObject go = new GameObject("TaskManager");
        TaskManager tm = go.AddComponent<TaskManager>();
        tm.allTasks = tasks;
        tm.escapeDoor = door;
    }

    static void BuildGameManager(GameObject survivorPrefab, GameObject monsterPrefab,
        Transform[] survivorSpawns, Transform monsterSpawn)
    {
        GameObject go = new GameObject("GameManager");
        go.AddComponent<NetworkObject>();
        GameManager gm = go.AddComponent<GameManager>();
        gm.survivorPrefab = survivorPrefab;
        gm.monsterPrefab = monsterPrefab;
        gm.survivorSpawns = survivorSpawns;
        gm.monsterSpawn = monsterSpawn;
        go.AddComponent<NoiseSystem>();
    }

    static void BuildNetworkManager(GameObject survivorPrefab, GameObject monsterPrefab)
    {
        GameObject go = new GameObject("NetworkManager");
        NetworkManager nm = go.AddComponent<NetworkManager>();
        UnityTransport utp = go.AddComponent<UnityTransport>();
        nm.NetworkConfig.NetworkTransport = utp;

        SayehBootstrap boot = go.AddComponent<SayehBootstrap>();
        boot.networkPrefabs = new[] { survivorPrefab, monsterPrefab };
    }

    // ───────────────────────── الواجهة ─────────────────────────

    static Font UiFont()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
        catch { return Resources.GetBuiltinResource<Font>("Arial.ttf"); }
    }

    static void SetRect(RectTransform rt, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = anchor; rt.anchorMax = anchor; rt.pivot = anchor;
        rt.anchoredPosition = pos; rt.sizeDelta = size;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
    }

    static Image Panel(Transform parent, string name, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    static Text Txt(Transform parent, string name, string content, int size, Color color)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        Text t = go.GetComponent<Text>();
        t.font = UiFont();
        t.text = content;
        t.fontSize = size;
        t.color = color;
        t.alignment = TextAnchor.MiddleCenter;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        return t;
    }

    static Button Btn(Transform parent, string name, string label, Color color)
    {
        Image img = Panel(parent, name, color);
        Button b = img.gameObject.AddComponent<Button>();
        b.targetGraphic = img;
        Text t = Txt(img.transform, "Text", label, 26, Color.white);
        Stretch(t.rectTransform);
        return b;
    }

    static Slider ProgressBar(Transform parent)
    {
        GameObject go = new GameObject("ProgressBar", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        Image bg = Panel(go.transform, "BG", new Color(0, 0, 0, 0.65f));
        Stretch(bg.rectTransform);

        GameObject fillArea = new GameObject("FillArea", typeof(RectTransform));
        fillArea.transform.SetParent(go.transform, false);
        Stretch((RectTransform)fillArea.transform);

        Image fill = Panel(fillArea.transform, "Fill", new Color(0.3f, 0.85f, 0.45f));
        RectTransform fillRT = fill.rectTransform;
        fillRT.anchorMin = Vector2.zero; fillRT.anchorMax = new Vector2(0, 1);
        fillRT.offsetMin = Vector2.zero; fillRT.offsetMax = Vector2.zero;

        Slider s = go.AddComponent<Slider>();
        s.fillRect = fillRT;
        s.transition = Selectable.Transition.None;
        s.interactable = false;
        s.minValue = 0; s.maxValue = 1;
        return s;
    }

    static InputField Input(Transform parent, string name, string defaultText)
    {
        Image img = Panel(parent, name, new Color(1, 1, 1, 0.12f));
        InputField f = img.gameObject.AddComponent<InputField>();
        Text t = Txt(img.transform, "Text", "", 24, Color.white);
        Stretch(t.rectTransform);
        t.supportRichText = false;
        f.textComponent = t;
        f.targetGraphic = img;
        f.text = defaultText;
        return f;
    }

    static void BuildUI()
    {
        // الكانفس + نظام الإدخال
        GameObject canvasGO = new GameObject("GameCanvas",
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasGO.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        CanvasScaler scaler = canvasGO.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);

        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));

        Transform c = canvasGO.transform;
        Vector2 mid = new Vector2(0.5f, 0.5f);

        GameUI ui = canvasGO.AddComponent<GameUI>();

        // كشف الدور
        Image rolePanel = Panel(c, "RolePanel", new Color(0, 0, 0, 0.85f));
        SetRect(rolePanel.rectTransform, mid, new Vector2(0, 120), new Vector2(760, 220));
        Text roleText = Txt(rolePanel.transform, "RoleText", "", 40, Color.white);
        Stretch(roleText.rectTransform);
        ui.rolePanel = rolePanel.gameObject; ui.roleText = roleText;

        // شريط الأحداث
        Text eventText = Txt(c, "EventText", "", 30, new Color(1f, 0.9f, 0.4f));
        SetRect(eventText.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -60), new Vector2(1400, 80));
        ui.eventText = eventText;

        // عداد المهام
        Text counter = Txt(c, "TaskCounter", "", 28, Color.white);
        SetRect(counter.rectTransform, new Vector2(1, 1), new Vector2(-30, -30), new Vector2(320, 44));
        counter.alignment = TextAnchor.UpperRight;
        ui.taskCounterText = counter;

        // لوحة المهمة
        Image taskPanel = Panel(c, "TaskPanel", new Color(0, 0, 0, 0.78f));
        SetRect(taskPanel.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 180), new Vector2(680, 310));
        Text tName = Txt(taskPanel.transform, "TaskName", "", 32, new Color(1f, 0.75f, 0.3f));
        SetRect(tName.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -34), new Vector2(620, 46));
        Text tDesc = Txt(taskPanel.transform, "TaskDesc", "", 22, Color.white);
        SetRect(tDesc.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -130), new Vector2(620, 130));
        Text tHint = Txt(taskPanel.transform, "TaskHint", "", 26, new Color(1f, 0.45f, 0.3f));
        SetRect(tHint.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 66), new Vector2(620, 44));
        Slider bar = ProgressBar(taskPanel.transform);
        SetRect((RectTransform)bar.transform, new Vector2(0.5f, 0), new Vector2(0, 28), new Vector2(600, 24));
        ui.taskPanel = taskPanel.gameObject;
        ui.taskNameText = tName; ui.taskDescText = tDesc; ui.taskHintText = tHint;
        ui.taskProgressBar = bar;

        // سهم الضجة (للوحش)
        GameObject arrowGO = new GameObject("NoiseArrow", typeof(RectTransform));
        arrowGO.transform.SetParent(c, false);
        SetRect((RectTransform)arrowGO.transform, mid, new Vector2(0, 170), new Vector2(130, 130));
        Text arrowGlyph = Txt(arrowGO.transform, "Glyph", "▲", 100, new Color(1f, 0.2f, 0.2f));
        Stretch(arrowGlyph.rectTransform);
        Text noiseLabel = Txt(c, "NoiseLabel", "", 28, new Color(1f, 0.35f, 0.3f));
        SetRect(noiseLabel.rectTransform, mid, new Vector2(0, 80), new Vector2(900, 44));
        ui.noiseArrow = (RectTransform)arrowGO.transform;
        ui.noiseLabelText = noiseLabel;

        // نهاية الجولة
        Image overPanel = Panel(c, "GameOverPanel", new Color(0, 0, 0, 0.9f));
        SetRect(overPanel.rectTransform, mid, Vector2.zero, new Vector2(860, 320));
        Text overText = Txt(overPanel.transform, "GameOverText", "", 38, Color.white);
        Stretch(overText.rectTransform);
        ui.gameOverPanel = overPanel.gameObject; ui.gameOverText = overText;

        // لوحة الاتصال
        Image connect = Panel(c, "ConnectPanel", new Color(0.04f, 0.04f, 0.1f, 0.96f));
        SetRect(connect.rectTransform, mid, Vector2.zero, new Vector2(640, 500));
        Text title = Txt(connect.transform, "Title", "SAYEH 🧟 صايح", 44, new Color(1f, 0.3f, 0.3f));
        SetRect(title.rectTransform, mid, new Vector2(0, 190), new Vector2(600, 60));
        Text status = Txt(connect.transform, "Status", "العب مع ربعك — استضف أو ادخل عليهم", 22, new Color(0.8f, 0.8f, 0.9f));
        SetRect(status.rectTransform, mid, new Vector2(0, 125), new Vector2(580, 70));
        InputField addr = Input(connect.transform, "Address", "127.0.0.1");
        SetRect((RectTransform)addr.transform, mid, new Vector2(0, 55), new Vector2(420, 52));
        Button host = Btn(connect.transform, "HostBtn", "Host — استضف", new Color(0.75f, 0.25f, 0.25f));
        SetRect((RectTransform)host.transform, mid, new Vector2(0, -20), new Vector2(420, 58));
        Button join = Btn(connect.transform, "JoinBtn", "Join — ادخل", new Color(0.25f, 0.4f, 0.75f));
        SetRect((RectTransform)join.transform, mid, new Vector2(0, -92), new Vector2(420, 58));
        Button start = Btn(connect.transform, "StartBtn", "Start Round — ابدأ الجولة 🎮", new Color(0.2f, 0.65f, 0.35f));
        SetRect((RectTransform)start.transform, mid, new Vector2(0, -180), new Vector2(460, 66));

        ConnectUI cui = canvasGO.AddComponent<ConnectUI>();
        cui.panel = connect.gameObject;
        cui.addressInput = addr;
        cui.hostButton = host;
        cui.clientButton = join;
        cui.startButton = start;
        cui.statusText = status;
    }
}
#endif
