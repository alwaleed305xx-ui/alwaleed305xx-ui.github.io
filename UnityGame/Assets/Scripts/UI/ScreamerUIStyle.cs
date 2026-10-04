using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// The code-built uGUI style kit every SCREAMER screen is assembled from:
/// cream plates on ink drop-shadows with rust borders, squash-pop buttons,
/// back-ease panel entrances, and the procedural sprites (rings, smears,
/// vignettes, shaped noise pings) the HUD draws with. No prefabs, no image
/// assets, no TextMeshPro - the whole look exists in this file.
///
/// Font contract (GDD 7): Arial via Font.CreateDynamicFontFromOSFont. If an
/// OFL display font is dropped into Assets/Screamer/Fonts/ (editor) or a
/// Resources folder as "ScreamerFont", <see cref="UiFont"/> prefers it - one
/// function, zero other changes.
///
/// Builders only create hierarchy and serializable component state, so the
/// editor wizard can run them and save the scene. Runtime-only state (fonts,
/// procedural sprites, listeners) is applied by each screen in Awake.
/// </summary>
public static class ScreamerUIStyle
{
    public const float ReferenceWidth = 1920f;
    public const float ReferenceHeight = 1080f;

    const float PanelAlpha = 0.96f;
    const float ShadowOffset = 4f;
    const float BorderSize = 2f;

    static Font cachedFont;
    static readonly Dictionary<string, Sprite> spriteCache = new Dictionary<string, Sprite>();

    // ------------------------- Font -------------------------

    /// <summary>
    /// The project font. Preference order: OFL override in Assets/Screamer/Fonts
    /// (editor), a Resources font named "ScreamerFont", then OS Arial.
    /// </summary>
    public static Font UiFont()
    {
        if (cachedFont != null) return cachedFont;

#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Font", new[] { "Assets" });
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Replace('\\', '/').Contains("Assets/Screamer/Fonts/")) continue;
            cachedFont = UnityEditor.AssetDatabase.LoadAssetAtPath<Font>(path);
            if (cachedFont != null) return cachedFont;
        }
#endif
        cachedFont = Resources.Load<Font>("ScreamerFont");
        if (cachedFont != null) return cachedFont;

        cachedFont = Font.CreateDynamicFontFromOSFont("Arial", 16);
        return cachedFont;
    }

    /// <summary>Runtime fixup: gives every Text under the root the project font.</summary>
    public static void ApplyFonts(GameObject root)
    {
        if (root == null) return;
        Font font = UiFont();
        foreach (Text text in root.GetComponentsInChildren<Text>(true))
            if (text.font == null || text.font != font)
                text.font = font;
    }

    // ------------------------- Canvas & rect plumbing -------------------------

    /// <summary>Screen-space overlay canvas with the 1920x1080 scaler (match 0.5).</summary>
    public static Canvas MakeCanvas(string name, int sortingOrder)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Canvas canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        CanvasScaler scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(ReferenceWidth, ReferenceHeight);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return canvas;
    }

    /// <summary>Bare RectTransform child.</summary>
    public static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    /// <summary>Anchors the rect to one point (anchor == pivot) with a position and size.</summary>
    public static RectTransform Place(RectTransform rt, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;
        return rt;
    }

    /// <summary>Stretches the rect over its whole parent.</summary>
    public static RectTransform Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return rt;
    }

    /// <summary>Plain tinted Image (no sprite; uGUI renders a tinted quad).</summary>
    public static Image Img(Transform parent, string name, Color color)
    {
        RectTransform rt = Rect(parent, name);
        Image image = rt.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    // ------------------------- The signature plate -------------------------

    /// <summary>
    /// Cream plate with a hard ink drop-shadow at (+4, -4) and a 2 px rust
    /// border (a slightly larger backing image - no shadow components, no
    /// 9-slice). Returns the plate; its parent is the movable container.
    /// Children of the plate are the panel's content.
    /// </summary>
    public static RectTransform Plate(Transform parent, string name, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        RectTransform container = Place(Rect(parent, name), anchor, anchoredPos, size);

        Image shadow = Img(container, "Shadow", WithAlpha(ScreamerPalette.InkBlack, 0.9f));
        Stretch(shadow.rectTransform);
        shadow.rectTransform.offsetMin = new Vector2(ShadowOffset, -ShadowOffset);
        shadow.rectTransform.offsetMax = new Vector2(ShadowOffset, -ShadowOffset);

        Image border = Img(container, "Border", ScreamerPalette.ShagRust);
        Stretch(border.rectTransform);
        border.rectTransform.offsetMin = new Vector2(-BorderSize, -BorderSize);
        border.rectTransform.offsetMax = new Vector2(BorderSize, BorderSize);

        Image plate = Img(container, "Plate", WithAlpha(ScreamerPalette.NoodleCream, PanelAlpha));
        Stretch(plate.rectTransform);
        plate.raycastTarget = true; // plates swallow clicks so the world never gets them
        return plate.rectTransform;
    }

    // ------------------------- Text -------------------------

    public static Text Txt(Transform parent, string name, string content, int size, Color color, TextAnchor anchor)
    {
        RectTransform rt = Rect(parent, name);
        Text text = rt.gameObject.AddComponent<Text>();
        if (Application.isPlaying) text.font = UiFont();
        text.text = content ?? "";
        text.fontSize = size;
        text.color = color;
        text.alignment = anchor;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>Bold ALL-CAPS header text (caps enforced in code, per the GDD).</summary>
    public static Text Header(Transform parent, string name, string content, int size, Color color, TextAnchor anchor)
    {
        Text text = Txt(parent, name, (content ?? "").ToUpperInvariant(), size, color, anchor);
        text.fontStyle = FontStyle.Bold;
        return text;
    }

    // ------------------------- Buttons -------------------------

    /// <summary>
    /// The squash-pop button: cream plate, ink label that flips to rust on
    /// hover with a 0.1 s squash (y 0.92), a 1.06x pop plus click-thunk on
    /// press. Pure uGUI; the animation coroutines ride the button's own
    /// EventTrigger so they die with it.
    /// </summary>
    public static Button Btn(Transform parent, string name, string label, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        RectTransform container = Place(Rect(parent, name), anchor, anchoredPos, size);

        Image shadow = Img(container, "Shadow", WithAlpha(ScreamerPalette.InkBlack, 0.9f));
        Stretch(shadow.rectTransform);
        shadow.rectTransform.offsetMin = new Vector2(3f, -3f);
        shadow.rectTransform.offsetMax = new Vector2(3f, -3f);

        Image plate = Img(container, "ButtonPlate", ScreamerPalette.NoodleCream);
        Stretch(plate.rectTransform);
        plate.raycastTarget = true;

        Text text = Header(plate.transform, "Label", label, Mathf.Max(14, (int)(size.y * 0.42f)), ScreamerPalette.InkBlack, TextAnchor.MiddleCenter);
        Stretch(text.rectTransform);

        Button button = plate.gameObject.AddComponent<Button>();
        button.targetGraphic = plate;
        button.transition = Selectable.Transition.None;

        if (Application.isPlaying)
            WireButtonFeel(button, container, text);

        return button;
    }

    /// <summary>Hooks the hover/squash/pop/thunk behavior onto an existing button.</summary>
    public static void WireButtonFeel(Button button, RectTransform scaleTarget, Text label)
    {
        if (button == null || scaleTarget == null) return;

        EventTrigger trigger = button.gameObject.GetComponent<EventTrigger>();
        if (trigger != null && trigger.triggers.Count > 0) return; // already wired
        if (trigger == null) trigger = button.gameObject.AddComponent<EventTrigger>();

        AddTrigger(trigger, EventTriggerType.PointerEnter, _ =>
        {
            if (!button.interactable) return;
            if (label != null) label.color = ScreamerPalette.ShagRust;
            trigger.StartCoroutine(ScaleTo(scaleTarget, new Vector3(1f, 0.92f, 1f), 0.1f));
        });
        AddTrigger(trigger, EventTriggerType.PointerExit, _ =>
        {
            if (label != null) label.color = ScreamerPalette.InkBlack;
            trigger.StartCoroutine(ScaleTo(scaleTarget, Vector3.one, 0.1f));
        });
        AddTrigger(trigger, EventTriggerType.PointerDown, _ =>
        {
            if (!button.interactable) return;
            trigger.StartCoroutine(ScaleTo(scaleTarget, new Vector3(1.06f, 1.06f, 1f), 0.06f));
            AudioDirector.Instance?.PlayUI(Sfx.UiThunk);
        });
        AddTrigger(trigger, EventTriggerType.PointerUp, _ =>
        {
            trigger.StartCoroutine(ScaleTo(scaleTarget, Vector3.one, 0.08f));
        });
    }

    static void AddTrigger(EventTrigger trigger, EventTriggerType type, UnityEngine.Events.UnityAction<BaseEventData> action)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(action);
        trigger.triggers.Add(entry);
    }

    static IEnumerator ScaleTo(RectTransform rt, Vector3 target, float duration)
    {
        if (rt == null) yield break;
        Vector3 start = rt.localScale;
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            if (rt == null) yield break;
            rt.localScale = Vector3.Lerp(start, target, Mathf.Clamp01(t / duration));
            yield return null;
        }
        if (rt != null) rt.localScale = target;
    }

    // ------------------------- Panel entrance -------------------------

    /// <summary>
    /// The 0.15 s back-ease overshoot entrance every panel uses:
    /// scale 0.9 -> 1.02 -> 1.0, plus a 2 px vertical jolt (the UI is
    /// possessed, not elegant - never crossfade).
    /// </summary>
    public static IEnumerator PopIn(RectTransform rt)
    {
        if (rt == null) yield break;
        Vector2 basePos = rt.anchoredPosition;
        float t = 0f;
        const float duration = 0.15f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            // Two-phase back ease: up through 1.02 then settle on 1.0.
            float scale = k < 0.7f
                ? Mathf.Lerp(0.9f, 1.02f, k / 0.7f)
                : Mathf.Lerp(1.02f, 1f, (k - 0.7f) / 0.3f);
            if (rt == null) yield break;
            rt.localScale = new Vector3(scale, scale, 1f);
            rt.anchoredPosition = basePos + new Vector2(0f, k < 0.5f ? 2f : 0f);
            yield return null;
        }
        if (rt == null) yield break;
        rt.localScale = Vector3.one;
        rt.anchoredPosition = basePos;
    }

    // ------------------------- Form controls -------------------------

    /// <summary>Horizontal slider: ink track, yellow fill, cream handle.</summary>
    public static Slider HSlider(Transform parent, string name, Vector2 anchor, Vector2 anchoredPos, Vector2 size,
        float min, float max, float value)
    {
        RectTransform root = Place(Rect(parent, name), anchor, anchoredPos, size);

        Image track = Img(root, "Track", WithAlpha(ScreamerPalette.InkBlack, 0.35f));
        Stretch(track.rectTransform);
        track.rectTransform.offsetMin = new Vector2(0f, size.y * 0.3f);
        track.rectTransform.offsetMax = new Vector2(0f, -size.y * 0.3f);

        RectTransform fillArea = Stretch(Rect(root, "FillArea"));
        fillArea.offsetMin = new Vector2(4f, size.y * 0.3f);
        fillArea.offsetMax = new Vector2(-4f, -size.y * 0.3f);
        Image fill = Img(fillArea, "Fill", ScreamerPalette.ScreamYellow);
        Stretch(fill.rectTransform);

        RectTransform handleArea = Stretch(Rect(root, "HandleArea"));
        handleArea.offsetMin = new Vector2(8f, 0f);
        handleArea.offsetMax = new Vector2(-8f, 0f);
        Image handle = Img(handleArea, "Handle", ScreamerPalette.NoodleCream);
        handle.rectTransform.sizeDelta = new Vector2(16f, size.y);
        handle.raycastTarget = true;

        Slider slider = root.gameObject.AddComponent<Slider>();
        slider.targetGraphic = handle;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        return slider;
    }

    /// <summary>Checkbox toggle: cream box, rust check block, side label.</summary>
    public static Toggle MakeToggle(Transform parent, string name, string label, Vector2 anchor, Vector2 anchoredPos, bool value)
    {
        RectTransform root = Place(Rect(parent, name), anchor, anchoredPos, new Vector2(320f, 28f));

        Image box = Img(root, "Box", ScreamerPalette.NoodleCream);
        Place(box.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
        box.raycastTarget = true;

        Image frame = Img(box.transform, "Frame", ScreamerPalette.ShagRust);
        Stretch(frame.rectTransform);
        frame.rectTransform.offsetMin = new Vector2(-2f, -2f);
        frame.rectTransform.offsetMax = new Vector2(2f, 2f);
        frame.transform.SetAsFirstSibling();

        Image check = Img(box.transform, "Check", ScreamerPalette.ShagRust);
        Place(check.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(14f, 14f));

        Text text = Txt(root, "Label", label, 18, ScreamerPalette.InkBlack, TextAnchor.MiddleLeft);
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(34f, 0f);

        Toggle toggle = root.gameObject.AddComponent<Toggle>();
        toggle.targetGraphic = box;
        toggle.graphic = check;
        toggle.isOn = value;
        return toggle;
    }

    /// <summary>
    /// Fully code-built uGUI Dropdown (caption, arrow, scrolling template).
    /// Options are supplied at runtime by the owning screen.
    /// </summary>
    public static Dropdown MakeDropdown(Transform parent, string name, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        RectTransform root = Place(Rect(parent, name), anchor, anchoredPos, size);
        Image plate = root.gameObject.AddComponent<Image>();
        plate.color = ScreamerPalette.NoodleCream;
        plate.raycastTarget = true;

        Text caption = Txt(root, "Caption", "", 18, ScreamerPalette.InkBlack, TextAnchor.MiddleLeft);
        Stretch(caption.rectTransform);
        caption.rectTransform.offsetMin = new Vector2(10f, 2f);
        caption.rectTransform.offsetMax = new Vector2(-28f, -2f);

        Text arrow = Txt(root, "Arrow", "v", 16, ScreamerPalette.ShagRust, TextAnchor.MiddleCenter);
        Place(arrow.rectTransform, new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(24f, 24f));
        arrow.fontStyle = FontStyle.Bold;

        // --- Template (inactive until the dropdown opens) ---
        RectTransform template = Rect(root, "Template");
        template.anchorMin = new Vector2(0f, 0f);
        template.anchorMax = new Vector2(1f, 0f);
        template.pivot = new Vector2(0.5f, 1f);
        template.anchoredPosition = new Vector2(0f, 2f);
        template.sizeDelta = new Vector2(0f, 160f);
        Image templateBg = template.gameObject.AddComponent<Image>();
        templateBg.color = ScreamerPalette.NoodleCream;
        templateBg.raycastTarget = true;
        ScrollRect scroll = template.gameObject.AddComponent<ScrollRect>();

        RectTransform viewport = Stretch(Rect(template, "Viewport"));
        viewport.pivot = new Vector2(0f, 1f);
        Image viewportImage = viewport.gameObject.AddComponent<Image>();
        viewportImage.color = ScreamerPalette.NoodleCream;
        Mask mask = viewport.gameObject.AddComponent<Mask>();
        mask.showMaskGraphic = true;

        RectTransform content = Rect(viewport, "Content");
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = new Vector2(0f, 28f);

        RectTransform item = Rect(content, "Item");
        item.anchorMin = new Vector2(0f, 0.5f);
        item.anchorMax = new Vector2(1f, 0.5f);
        item.sizeDelta = new Vector2(0f, 26f);
        Toggle itemToggle = item.gameObject.AddComponent<Toggle>();

        Image itemBg = Img(item, "Item Background", ScreamerPalette.NoodleCream);
        Stretch(itemBg.rectTransform);
        itemBg.raycastTarget = true;

        Image itemCheck = Img(item, "Item Checkmark", ScreamerPalette.ShagRust);
        Place(itemCheck.rectTransform, new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(10f, 10f));

        Text itemLabel = Txt(item, "Item Label", "", 16, ScreamerPalette.InkBlack, TextAnchor.MiddleLeft);
        Stretch(itemLabel.rectTransform);
        itemLabel.rectTransform.offsetMin = new Vector2(24f, 1f);
        itemLabel.rectTransform.offsetMax = new Vector2(-10f, -1f);

        itemToggle.targetGraphic = itemBg;
        itemToggle.graphic = itemCheck;
        itemToggle.isOn = true;

        scroll.content = content;
        scroll.viewport = viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        template.gameObject.SetActive(false);

        Dropdown dropdown = root.gameObject.AddComponent<Dropdown>();
        dropdown.targetGraphic = plate;
        dropdown.template = template;
        dropdown.captionText = caption;
        dropdown.itemText = itemLabel;
        return dropdown;
    }

    /// <summary>Single-line input field on a cream plate.</summary>
    public static InputField MakeInput(Transform parent, string name, string placeholder, Vector2 anchor, Vector2 anchoredPos, Vector2 size)
    {
        RectTransform root = Place(Rect(parent, name), anchor, anchoredPos, size);
        Image plate = root.gameObject.AddComponent<Image>();
        plate.color = ScreamerPalette.NoodleCream;
        plate.raycastTarget = true;

        Image frame = Img(root, "Frame", ScreamerPalette.ShagRust);
        Stretch(frame.rectTransform);
        frame.rectTransform.offsetMin = new Vector2(-2f, -2f);
        frame.rectTransform.offsetMax = new Vector2(2f, 2f);
        frame.transform.SetAsFirstSibling();

        Text placeholderText = Txt(root, "Placeholder", placeholder, 18, WithAlpha(ScreamerPalette.InkBlack, 0.4f), TextAnchor.MiddleLeft);
        Stretch(placeholderText.rectTransform);
        placeholderText.rectTransform.offsetMin = new Vector2(10f, 2f);
        placeholderText.rectTransform.offsetMax = new Vector2(-10f, -2f);
        placeholderText.fontStyle = FontStyle.Italic;

        Text text = Txt(root, "Text", "", 18, ScreamerPalette.InkBlack, TextAnchor.MiddleLeft);
        Stretch(text.rectTransform);
        text.rectTransform.offsetMin = new Vector2(10f, 2f);
        text.rectTransform.offsetMax = new Vector2(-10f, -2f);
        text.supportRichText = false;

        InputField field = root.gameObject.AddComponent<InputField>();
        field.targetGraphic = plate;
        field.textComponent = text;
        field.placeholder = placeholderText;
        return field;
    }

    // ------------------------- Procedural sprites -------------------------

    public static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, a);

    /// <summary>Soft-edged filled disc (mouth icon, color chips, dots).</summary>
    public static Sprite Disc(int size = 64)
    {
        return Cached("disc" + size, size, size, (x, y, w, h) =>
        {
            float d = Dist01(x, y, w, h);
            return Mathf.Clamp01((0.98f - d) / 0.08f);
        });
    }

    /// <summary>Soft ring (crosshair bloom, rematch radial, plain pings).</summary>
    public static Sprite RingSprite(int size = 128, float radius = 0.82f, float thickness = 0.12f)
    {
        return Cached("ring" + size + "_" + radius + "_" + thickness, size, size, (x, y, w, h) =>
        {
            float d = Dist01(x, y, w, h);
            return Mathf.Clamp01(1f - Mathf.Abs(d - radius) / thickness);
        });
    }

    /// <summary>Radial edge-darkening sprite (pause shade; alpha rises toward the edges).</summary>
    public static Sprite VignetteSprite(int size = 128)
    {
        return Cached("vignette" + size, size, size, (x, y, w, h) =>
        {
            float d = Dist01(x, y, w, h);
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.45f) / 0.55f));
        });
    }

    /// <summary>Elongated soft glow blob - the monster HUD's screen-edge smear.</summary>
    public static Sprite SmearSprite(int w = 256, int h = 64)
    {
        return Cached("smear" + w + "x" + h, w, h, (x, y, tw, th) =>
        {
            float nx = (x / (float)(tw - 1) - 0.5f) * 2f;
            float ny = (y / (float)(th - 1) - 0.5f) * 2f;
            float fall = nx * nx * 1.2f + ny * ny * 2.6f;
            return Mathf.Clamp01(1f - fall);
        });
    }

    /// <summary>
    /// Shaped ping ring by noise type (GDD 9.3): jagged = scream, dotted
    /// note-ring = music, double ring = alarm, feathered = chicken, smooth
    /// ring for everything else. The monster reads WHAT it hears.
    /// </summary>
    public static Sprite PingSprite(NoiseType type)
    {
        switch (type)
        {
            case NoiseType.Scream:
            case NoiseType.DeathScream:
                return Cached("pingJagged", 128, 128, (x, y, w, h) =>
                {
                    float d = Dist01(x, y, w, h);
                    float a = Mathf.Atan2(y - h * 0.5f, x - w * 0.5f);
                    float teeth = 0.07f * Mathf.Abs(Mathf.Repeat(a * 16f / (Mathf.PI * 2f), 1f) - 0.5f) * 2f;
                    return Mathf.Clamp01(1f - Mathf.Abs(d - (0.72f + teeth)) / 0.1f);
                });

            case NoiseType.Music:
                return Cached("pingNote", 128, 128, (x, y, w, h) =>
                {
                    float d = Dist01(x, y, w, h);
                    float a = Mathf.Atan2(y - h * 0.5f, x - w * 0.5f);
                    float dots = Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 6f)), 0.35f);
                    return Mathf.Clamp01(1f - Mathf.Abs(d - 0.78f) / 0.12f) * dots;
                });

            case NoiseType.Alarm:
                return Cached("pingDouble", 128, 128, (x, y, w, h) =>
                {
                    float d = Dist01(x, y, w, h);
                    float inner = Mathf.Clamp01(1f - Mathf.Abs(d - 0.6f) / 0.07f);
                    float outer = Mathf.Clamp01(1f - Mathf.Abs(d - 0.84f) / 0.07f);
                    return Mathf.Max(inner, outer);
                });

            case NoiseType.Chicken:
                return Cached("pingFeather", 128, 128, (x, y, w, h) =>
                {
                    float d = Dist01(x, y, w, h);
                    float a = Mathf.Atan2(y - h * 0.5f, x - w * 0.5f);
                    float strokes = Mathf.Pow(Mathf.Abs(Mathf.Sin(a * 10f)), 1.6f);
                    float band = Mathf.Clamp01(1f - Mathf.Abs(d - 0.76f) / 0.2f);
                    return band * strokes;
                });

            default:
                return RingSprite();
        }
    }

    /// <summary>
    /// The gamma calibration card: a near-black sprite hiding a slightly
    /// brighter little monster silhouette at roughly 3% contrast. He can
    /// always see you.
    /// </summary>
    public static Sprite CalibrationGhost(int size = 96)
    {
        string key = "calibration" + size;
        if (spriteCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };

        const float dark = 0.05f;
        const float lit = 0.08f; // ~3% contrast
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float nx = x / (float)(size - 1);
                float ny = y / (float)(size - 1);

                // Head (circle) + body (rounded block) silhouette.
                float head = Vector2.Distance(new Vector2(nx, ny), new Vector2(0.5f, 0.66f));
                bool inHead = head < 0.16f;
                bool inBody = Mathf.Abs(nx - 0.5f) < 0.13f && ny > 0.2f && ny < 0.58f;
                bool inEye = Vector2.Distance(new Vector2(nx, ny), new Vector2(0.45f, 0.68f)) < 0.025f
                          || Vector2.Distance(new Vector2(nx, ny), new Vector2(0.55f, 0.68f)) < 0.025f;

                float v = (inHead || inBody) ? lit : dark;
                if (inEye) v = dark;
                tex.SetPixel(x, y, new Color(v, v, v * 1.15f, 1f));
            }
        }
        tex.Apply();

        Sprite sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        spriteCache[key] = sprite;
        return sprite;
    }

    // ------------------------- Sprite plumbing -------------------------

    delegate float AlphaAt(int x, int y, int w, int h);

    static Sprite Cached(string key, int w, int h, AlphaAt alpha)
    {
        if (spriteCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        var tex = new Texture2D(w, h, TextureFormat.ARGB32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                pixels[y * w + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(alpha(x, y, w, h)));
        tex.SetPixels(pixels);
        tex.Apply();

        Sprite sprite = Sprite.Create(tex, new UnityEngine.Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
        spriteCache[key] = sprite;
        return sprite;
    }

    /// <summary>Distance from texture center, normalized so 1.0 = half the smaller side.</summary>
    static float Dist01(int x, int y, int w, int h)
    {
        float dx = (x - (w - 1) * 0.5f) / ((w - 1) * 0.5f);
        float dy = (y - (h - 1) * 0.5f) / ((h - 1) * 0.5f);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }
}
