using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The killcam's client half (GDD 12.2): restages a <see cref="KillcamClip"/>
/// as a live cinematic in the real map. Two proxy pawns - a googly-eyed
/// victim and a hulking red-rimmed monster - are re-posed along the recorded
/// 10 Hz path while a second camera films them from the nearest wizard-placed
/// cinematic wall anchor with line of sight, rendering to a 640x360
/// RenderTexture with letterbox bars and the "REPLAY - 11:4X PM" timestamp
/// baked in. ResultsUI shows the texture and owns the caption text.
///
/// The whole rig (camera, overlay canvas, proxies, texture) is built lazily at
/// play time, so the saved scene carries only this component and its anchor
/// transforms. The replay loops with a short freeze-frame on the bite.
/// </summary>
public class KillcamPlayer : MonoBehaviour
{
    public static KillcamPlayer Instance { get; private set; }

    /// <summary>Output size (GDD 12.2: RenderTexture-safe at 640x360).</summary>
    public const int Width = 640;
    public const int Height = 360;

    [Header("Cinematic anchors (wired by FeelFactory: 4 per room)")]
    public Transform[] anchors = new Transform[0];

    [Header("Framing")]
    public float cameraFov = 50f;
    [Tooltip("Fraction of the frame each letterbox bar covers.")]
    public float letterboxFraction = 0.12f;
    [Tooltip("How quickly the replay camera re-aims (slerp factor per second).")]
    public float aimSpeed = 4f;

    [Header("Replay")]
    [Tooltip("Freeze-frame on the kill before the loop restarts.")]
    public float endHoldSeconds = 0.9f;

    RenderTexture renderTexture;
    Camera replayCamera;
    GameObject rigRoot;
    Transform victimProxy;
    Transform monsterProxy;
    Text timestampText;

    KillcamClip clip;
    bool playing;
    float playhead;
    Vector3 cameraPosition;

    void Awake()
    {
        Instance = this;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (renderTexture != null)
        {
            renderTexture.Release();
            Destroy(renderTexture);
        }
    }

    // ------------------------- Public API (inter-module contract) -------------------------

    /// <summary>
    /// Starts replaying a clip and returns the 640x360 letterboxed texture to
    /// show, or null when the clip has nothing worth staging.
    /// </summary>
    public RenderTexture Play(KillcamClip killcamClip)
    {
        if (!killcamClip.IsValid) return null;

        EnsureRig();
        clip = killcamClip;
        playhead = 0f;
        playing = true;

        // "REPLAY - 11:4X PM": haunted-house prime time, randomized per showing.
        if (timestampText != null)
            timestampText.text = "REPLAY - 11:4" + Random.Range(0, 10) + " PM";

        cameraPosition = PickCameraPosition();
        replayCamera.transform.position = cameraPosition;
        replayCamera.transform.rotation = Quaternion.LookRotation(
            (FocusPoint(0f) - cameraPosition).normalized, Vector3.up);

        rigRoot.SetActive(true);
        replayCamera.enabled = true;
        ApplyPoses(0f);
        return renderTexture;
    }

    /// <summary>Stops the replay and powers the rig down (ResultsUI calls this on hide).</summary>
    public void Stop()
    {
        playing = false;
        if (replayCamera != null) replayCamera.enabled = false;
        if (rigRoot != null) rigRoot.SetActive(false);
    }

    // ------------------------- Replay loop -------------------------

    void Update()
    {
        if (!playing || rigRoot == null) return;

        float duration = (clip.SampleCount - 1) * KillcamRecorder.SampleInterval;
        playhead += Time.unscaledDeltaTime;
        if (playhead > duration + endHoldSeconds) playhead = 0f; // loop after the freeze-frame

        float t = Mathf.Min(playhead, duration);
        ApplyPoses(t);

        // The camera stays bolted to its wall anchor and just re-aims.
        Vector3 focus = FocusPoint(t);
        Quaternion aim = Quaternion.LookRotation((focus - cameraPosition).normalized, Vector3.up);
        replayCamera.transform.rotation = Quaternion.Slerp(
            replayCamera.transform.rotation, aim, 1f - Mathf.Exp(-aimSpeed * Time.unscaledDeltaTime));
    }

    void ApplyPoses(float t)
    {
        Pose(victimProxy, clip.victimPos, clip.victimYaw, t);
        Pose(monsterProxy, clip.monsterPos, clip.monsterYaw, t);
    }

    static void Pose(Transform proxy, Vector3[] positions, float[] yaws, float t)
    {
        if (proxy == null || positions == null || positions.Length == 0) return;

        float sample = t / KillcamRecorder.SampleInterval;
        int index = Mathf.Clamp(Mathf.FloorToInt(sample), 0, positions.Length - 1);
        int next = Mathf.Min(index + 1, positions.Length - 1);
        float blend = Mathf.Clamp01(sample - index);

        Vector3 position = Vector3.Lerp(positions[index], positions[next], blend);
        float yaw = Mathf.LerpAngle(yaws[index], yaws[next], blend);
        proxy.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
    }

    /// <summary>What the camera watches: mostly the victim, a little of the doom.</summary>
    Vector3 FocusPoint(float t)
    {
        float sample = Mathf.Clamp(t / KillcamRecorder.SampleInterval, 0f, clip.SampleCount - 1);
        int index = Mathf.Clamp(Mathf.FloorToInt(sample), 0, clip.SampleCount - 1);
        return Vector3.Lerp(clip.victimPos[index], clip.monsterPos[index], 0.35f) + Vector3.up * 1.1f;
    }

    // ------------------------- Anchor selection -------------------------

    Vector3 PickCameraPosition()
    {
        // Aim decisions use the middle of the chase, not just the final corner.
        Vector3 focus = Vector3.zero;
        for (int i = 0; i < clip.SampleCount; i++) focus += clip.victimPos[i];
        focus /= clip.SampleCount;
        focus += Vector3.up * 1.2f;

        Transform best = null;
        Transform nearest = null;
        float bestDistance = float.MaxValue;
        float nearestDistance = float.MaxValue;

        foreach (Transform anchor in anchors)
        {
            if (anchor == null) continue;
            float distance = Vector3.Distance(anchor.position, focus);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = anchor;
            }
            // Line of sight from the wall mount to the scene of the crime.
            if (distance < bestDistance &&
                !Physics.Linecast(anchor.position, focus, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                bestDistance = distance;
                best = anchor;
            }
        }

        if (best != null) return best.position;
        if (nearest != null) return nearest.position; // through-the-wall beats no shot at all
        return focus + new Vector3(2.5f, 2.8f, -2.5f); // anchor-less module test scene
    }

    // ------------------------- Rig construction (lazy, runtime only) -------------------------

    void EnsureRig()
    {
        if (rigRoot != null) return;

        rigRoot = new GameObject("KillcamRig");
        rigRoot.transform.SetParent(transform, false);

        if (renderTexture == null)
        {
            renderTexture = new RenderTexture(Width, Height, 16) { name = "RT_Killcam" };
            renderTexture.Create();
        }

        // The second camera: renders only to the texture, never to the screen.
        var cameraGo = new GameObject("KillcamCamera");
        cameraGo.transform.SetParent(rigRoot.transform, false);
        replayCamera = cameraGo.AddComponent<Camera>();
        replayCamera.targetTexture = renderTexture;
        replayCamera.fieldOfView = cameraFov;
        replayCamera.nearClipPlane = 0.05f;
        replayCamera.clearFlags = CameraClearFlags.SolidColor;
        replayCamera.backgroundColor = ScreamerPalette.MidnightPlum;
        replayCamera.enabled = false;

        BuildOverlay(cameraGo);

        victimProxy = BuildVictimProxy(rigRoot.transform);
        monsterProxy = BuildMonsterProxy(rigRoot.transform);

        rigRoot.SetActive(false);
    }

    /// <summary>Letterbox bars and timestamp, rendered into the texture by the replay camera.</summary>
    void BuildOverlay(GameObject cameraGo)
    {
        var canvasGo = new GameObject("KillcamOverlay");
        canvasGo.transform.SetParent(cameraGo.transform, false);

        Canvas canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = replayCamera;
        canvas.planeDistance = 0.5f;

        Image top = Bar(canvas.transform, "LetterboxTop");
        top.rectTransform.anchorMin = new Vector2(0f, 1f - letterboxFraction);
        top.rectTransform.anchorMax = Vector2.one;

        Image bottom = Bar(canvas.transform, "LetterboxBottom");
        bottom.rectTransform.anchorMin = Vector2.zero;
        bottom.rectTransform.anchorMax = new Vector2(1f, letterboxFraction);

        // The timestamp sits inside the top bar, pushed to the right.
        var textGo = new GameObject("Timestamp", typeof(RectTransform));
        var textRect = (RectTransform)textGo.transform;
        textRect.SetParent(top.rectTransform, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(8f, 0f);
        textRect.offsetMax = new Vector2(-10f, 0f);

        timestampText = textGo.AddComponent<Text>();
        timestampText.font = Font.CreateDynamicFontFromOSFont("Arial", 18);
        timestampText.fontSize = 18;
        timestampText.fontStyle = FontStyle.Bold;
        timestampText.alignment = TextAnchor.MiddleRight;
        timestampText.horizontalOverflow = HorizontalWrapMode.Overflow;
        timestampText.color = ScreamerPalette.NoodleCream;
        timestampText.raycastTarget = false;
        timestampText.text = "REPLAY - 11:47 PM";
    }

    static Image Bar(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = go.AddComponent<Image>();
        image.color = ScreamerPalette.MidnightPlum;
        image.raycastTarget = false;
        return image;
    }

    // ------------------------- Proxy pawns -------------------------

    /// <summary>The doomed one: same googly-eyed capsule silhouette as a live pawn.</summary>
    static Transform BuildVictimProxy(Transform parent)
    {
        Transform root = NewProxyRoot(parent, "Proxy_Victim");
        Color bodyColor = Color.Lerp(ScreamerPalette.NoodleCream, ScreamerPalette.ShagRust, 0.25f);

        Prim(PrimitiveType.Capsule, root, new Vector3(0f, 0.95f, 0f), new Vector3(0.8f, 0.95f, 0.8f), bodyColor);
        Prim(PrimitiveType.Sphere, root, new Vector3(-0.14f, 1.52f, 0.3f), Vector3.one * 0.22f, ScreamerPalette.NoodleCream);
        Prim(PrimitiveType.Sphere, root, new Vector3(0.14f, 1.52f, 0.3f), Vector3.one * 0.22f, ScreamerPalette.NoodleCream);
        Prim(PrimitiveType.Sphere, root, new Vector3(-0.12f, 1.53f, 0.4f), Vector3.one * 0.1f, ScreamerPalette.InkBlack);
        Prim(PrimitiveType.Sphere, root, new Vector3(0.12f, 1.53f, 0.4f), Vector3.one * 0.1f, ScreamerPalette.InkBlack);
        return root;
    }

    /// <summary>The doom: bulkier, plum-dark, with the monster's red rim and eyes.</summary>
    static Transform BuildMonsterProxy(Transform parent)
    {
        Transform root = NewProxyRoot(parent, "Proxy_Monster");
        Color bodyColor = Color.Lerp(ScreamerPalette.MidnightPlum, ScreamerPalette.InkBlack, 0.35f);

        Prim(PrimitiveType.Capsule, root, new Vector3(0f, 1.1f, 0f), new Vector3(1.3f, 1.15f, 1.3f), bodyColor);
        Prim(PrimitiveType.Sphere, root, new Vector3(-0.18f, 1.75f, 0.45f), Vector3.one * 0.14f, ScreamerPalette.MonsterRed);
        Prim(PrimitiveType.Sphere, root, new Vector3(0.18f, 1.75f, 0.45f), Vector3.one * 0.14f, ScreamerPalette.MonsterRed);

        var lightGo = new GameObject("RedRim");
        lightGo.transform.SetParent(root, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        Light rim = lightGo.AddComponent<Light>();
        rim.type = LightType.Point;
        rim.color = ScreamerPalette.MonsterRed;
        rim.range = 3.5f;
        rim.intensity = 1.1f;
        rim.shadows = LightShadows.None;
        return root;
    }

    static Transform NewProxyRoot(Transform parent, string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static void Prim(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 localScale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        go.GetComponent<Renderer>().sharedMaterial = ParticleFactory.MaterialFor(color);

        // Proxies are scenery: they must never catch raycasts or block pawns.
        Collider collider = go.GetComponent<Collider>();
        if (collider != null) Destroy(collider);
    }
}
