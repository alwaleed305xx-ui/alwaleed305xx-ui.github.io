using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One call per joke (GDD section 12): <see cref="Gag"/> fires the popup text,
/// a particle burst, the SFX and an optional camera shake together, so no
/// gameplay module ever has to choreograph its own punchline. <see cref="Popup"/>
/// alone handles the screen-space text.
///
/// Popups land on a dedicated overlay canvas (order 3000: above the HUD at
/// 100, under the ScreenFxOverlay at 5000), stamp in with a back-ease pop,
/// then drift up and fade. The canvas and font are built at runtime in Awake -
/// nothing non-serializable ever touches the saved scene.
/// </summary>
public class GagFeedback : MonoBehaviour
{
    public static GagFeedback Instance { get; private set; }

    [Header("Popup tuning")]
    [Tooltip("Seconds a popup stays before it has fully drifted away.")]
    public float popupSeconds = 1.8f;
    [Tooltip("Popups on screen at once; the oldest is evicted beyond this.")]
    public int maxPopups = 3;
    public int fontSize = 46;
    [Tooltip("Anchored Y of the newest popup (from the bottom-center of the screen).")]
    public float popupBaseY = 300f;
    [Tooltip("Vertical gap between stacked popups.")]
    public float popupSpacing = 70f;

    [Header("Gag defaults")]
    [Tooltip("Particles per Gag() burst (scaled by quality inside ParticleFactory).")]
    public int burstCount = 18;

    Canvas canvas;
    RectTransform popupRoot;
    Font font;
    readonly List<RectTransform> livePopups = new List<RectTransform>();

    void Awake()
    {
        Instance = this;
        font = Font.CreateDynamicFontFromOSFont("Arial", fontSize);
        BuildCanvas();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    // ------------------------- Public API (inter-module contract) -------------------------

    /// <summary>
    /// The full gag: popup text (ScreamYellow, per the palette's gag role),
    /// an accent-colored burst at the world position, the SFX, and an optional
    /// camera shake - one call, one joke.
    /// </summary>
    public void Gag(string popupText, Vector3 worldPos, Sfx sfx, Color accent, float shake = 0f)
    {
        if (AudioDirector.Instance != null)
            AudioDirector.Instance.Play(sfx, worldPos);

        ParticleFactory.Burst(worldPos, accent, burstCount);

        if (!string.IsNullOrEmpty(popupText))
            Popup(popupText, ScreamerPalette.ScreamYellow);

        if (shake > 0f && ScreamerCam.Instance != null)
            ScreamerCam.Instance.Shake(shake, 0.25f);
    }

    /// <summary>Screen-space gag text only: stamps in, lingers, drifts up and fades.</summary>
    public void Popup(string text, Color color)
    {
        if (string.IsNullOrEmpty(text) || popupRoot == null) return;

        // Push the stack up and evict the oldest beyond the cap.
        for (int i = livePopups.Count - 1; i >= 0; i--)
        {
            if (livePopups[i] == null) { livePopups.RemoveAt(i); continue; }
            livePopups[i].anchoredPosition += new Vector2(0f, popupSpacing);
        }
        while (livePopups.Count >= maxPopups)
        {
            if (livePopups[0] != null) Destroy(livePopups[0].gameObject);
            livePopups.RemoveAt(0);
        }

        RectTransform popup = BuildPopup(text, color);
        livePopups.Add(popup);
        StartCoroutine(PopupRoutine(popup));
    }

    // ------------------------- Popup construction -------------------------

    RectTransform BuildPopup(string text, Color color)
    {
        var go = new GameObject("Popup", typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(popupRoot, false);
        rect.anchorMin = new Vector2(0.5f, 0f);
        rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, popupBaseY);
        rect.sizeDelta = new Vector2(1400f, 90f);

        // Ink drop shadow behind the line - the house style, two stacked texts.
        Text shadow = MakeText(rect, "Shadow", text, ScreamerPalette.InkBlack);
        ((RectTransform)shadow.transform).anchoredPosition = new Vector2(4f, -4f);
        Text label = MakeText(rect, "Label", text, color);
        ((RectTransform)label.transform).anchoredPosition = Vector2.zero;

        return rect;
    }

    Text MakeText(RectTransform parent, string name, string content, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Text text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        text.color = color;
        text.text = content;
        return text;
    }

    IEnumerator PopupRoutine(RectTransform popup)
    {
        // Stamp in with a back-ease overshoot: 0.6 -> 1.08 -> 1.
        const float stampSeconds = 0.14f;
        float t = 0f;
        while (t < stampSeconds && popup != null)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / stampSeconds);
            float overshoot = 1f + 0.35f * Mathf.Sin(k * Mathf.PI);
            popup.localScale = Vector3.one * Mathf.Lerp(0.6f, 1f, k) * Mathf.Lerp(1f, overshoot, k * (1f - k) * 4f);
            yield return null;
        }
        if (popup != null) popup.localScale = Vector3.one;

        // Hold, then drift up and fade over the final third.
        float holdSeconds = popupSeconds * 0.65f;
        float fadeSeconds = Mathf.Max(0.2f, popupSeconds - stampSeconds - holdSeconds);
        yield return new WaitForSecondsRealtime(holdSeconds);

        Text[] texts = popup != null ? popup.GetComponentsInChildren<Text>() : null;
        t = 0f;
        while (t < fadeSeconds && popup != null)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / fadeSeconds);
            popup.anchoredPosition += new Vector2(0f, 45f * Time.unscaledDeltaTime);
            if (texts != null)
                foreach (Text text in texts)
                {
                    if (text == null) continue;
                    Color c = text.color;
                    c.a = 1f - k;
                    text.color = c;
                }
            yield return null;
        }

        if (popup != null)
        {
            livePopups.Remove(popup);
            Destroy(popup.gameObject);
        }
    }

    // ------------------------- Canvas construction -------------------------

    void BuildCanvas()
    {
        var go = new GameObject("GagCanvas");
        go.transform.SetParent(transform, false);

        canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 3000;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var rootGo = new GameObject("Popups", typeof(RectTransform));
        popupRoot = (RectTransform)rootGo.transform;
        popupRoot.SetParent(go.transform, false);
        popupRoot.anchorMin = Vector2.zero;
        popupRoot.anchorMax = Vector2.one;
        popupRoot.offsetMin = Vector2.zero;
        popupRoot.offsetMax = Vector2.zero;
    }
}
