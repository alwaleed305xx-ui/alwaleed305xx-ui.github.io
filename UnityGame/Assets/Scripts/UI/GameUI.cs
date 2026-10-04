using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The in-round HUD, both faces of it (GDD 7.3 / 7.4):
///
/// SURVIVOR - six diegetic padlock icons mirroring the cellar door, the
/// typewriter event feed, the self-noise meter with its crosshair snitch
/// bloom, the context prompt plate, the VU-styled task panel, and the finale
/// scream meter.
///
/// MONSTER - a swapped (never overlaid) red HUD: heartbeat vignette through
/// ScreenFxOverlay, screen-edge directional smear pings with shaped rings and
/// a 3 s ghost echo, the lockdown countdown, kill tallies, and Mimic prompts.
///
/// GHOST - the BOO charge indicator; ghosts also watch every noise ping.
///
/// Built entirely in code by UiFactory/GameUI.Build. All text is written
/// directly (t.text = s); every string comes from GameCopy.
/// </summary>
public class GameUI : MonoBehaviour
{
    public static GameUI Instance { get; private set; }

    const float EventFeedSeconds = 4f;
    const float TypewriterCharsPerSecond = 40f;
    const float SelfNoiseDecaySeconds = 1.5f;
    const float PingEchoSeconds = 3f;
    const float PingEdgeRadius = 430f;
    const int PadlockCount = 6;

    // ------------------------- Wired by Build (serialized) -------------------------

    [Header("Roots")]
    public GameObject survivorRoot;
    public GameObject monsterRoot;
    public GameObject ghostRoot;
    public RectTransform pingLayer;

    [Header("Padlocks")]
    public RectTransform padlockRow;
    public Image[] padlockBodies = new Image[PadlockCount];
    public RectTransform[] padlockShackles = new RectTransform[PadlockCount];

    [Header("Event feed")]
    public Text eventText;

    [Header("Self-noise meter")]
    public Image selfNoiseFill;
    public RectTransform selfNoiseMouth;
    public Image selfNoiseMouthImage;
    public Image crosshairDot;
    public Image crosshairBloom;

    [Header("Prompt")]
    public GameObject promptRoot;
    public Text promptText;

    [Header("Task panel")]
    public GameObject taskPanelRoot;
    public RectTransform taskPanelRect;
    public Text taskNameText;
    public Text taskFlavorText;
    public Text taskHintText;
    public Text taskWalkAwayText;
    public RectTransform vuFillRect;
    public Image vuFillImage;

    [Header("Finale meter")]
    public GameObject finaleRoot;
    public RectTransform finaleFillRect;
    public Text finalePromptText;
    public Text finaleSignText;

    [Header("Monster HUD")]
    public GameObject lockdownRoot;
    public Text lockdownHeaderText;
    public Text lockdownCountdownText;
    public RectTransform tallyContainer;
    public GameObject mimicHintRoot;
    public Text mimicHintText;

    [Header("Ghost HUD")]
    public Text ghostBooText;

    [Header("Center card")]
    public RectTransform centerCardContainer;
    public Text centerCardText;

    // ------------------------- Runtime state -------------------------

    class PingWidget
    {
        public GameObject go;
        public RectTransform pivot;
        public Image smear;
        public Image ring;
        public Text label;
        public Vector3 worldPos;
        public float age;
        public float holdSeconds;
    }

    readonly List<PingWidget> activePings = new List<PingWidget>();
    readonly Stack<PingWidget> pingPool = new Stack<PingWidget>();
    readonly Queue<string> eventQueue = new Queue<string>();

    Coroutine eventRoutine;
    Coroutine centerCardRoutine;
    Coroutine bloomRoutine;

    bool monsterHudOn;
    bool ghostHudOn;
    bool hudVisible;
    bool roleCardShown;
    int shownPadlocksBroken;
    int killTallies;
    float selfNoiseLevel;
    float vuFlashTimer;
    float vuProgress;
    float finaleMeterValue;
    GameManager.GameState lastState = GameManager.GameState.Lobby;

    // ------------------------- Lifecycle -------------------------

    void Awake()
    {
        Instance = this;
        InitVisuals();
    }

    void Start()
    {
        // AddComponent runs Awake before Build wires any field, so the
        // runtime-built path finishes its visual setup here. Idempotent.
        InitVisuals();
    }

    void InitVisuals()
    {
        ScreamerUIStyle.ApplyFonts(gameObject);
        if (crosshairBloom != null)
        {
            crosshairBloom.sprite = ScreamerUIStyle.RingSprite();
            crosshairBloom.color = ScreamerUIStyle.WithAlpha(ScreamerPalette.NoodleCream, 0f);
        }
        if (selfNoiseMouthImage != null) selfNoiseMouthImage.sprite = ScreamerUIStyle.Disc();
        foreach (RectTransform shackle in padlockShackles)
        {
            Image image = shackle != null ? shackle.GetComponent<Image>() : null;
            if (image != null) image.sprite = ScreamerUIStyle.RingSprite(64, 0.78f, 0.22f);
        }

        if (survivorRoot != null) survivorRoot.SetActive(hudVisible && !monsterHudOn);
        if (monsterRoot != null) monsterRoot.SetActive(hudVisible && monsterHudOn);
        if (ghostRoot != null) ghostRoot.SetActive(false);
        if (pingLayer != null) pingLayer.gameObject.SetActive(false);
        if (promptRoot != null) promptRoot.SetActive(false);
        if (taskPanelRoot != null) taskPanelRoot.SetActive(false);
        if (finaleRoot != null) finaleRoot.SetActive(false);
        if (lockdownRoot != null) lockdownRoot.SetActive(false);
        if (mimicHintRoot != null) mimicHintRoot.SetActive(false);
        if (centerCardContainer != null) centerCardContainer.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        GameManager.OnClientStateChanged += HandleStateChanged;
        NoiseSystem.OnMonsterHeardNoise += HandleMonsterHeardNoise;
        NoiseSystem.OnNoiseVisible += HandleNoiseVisible;
        MimicDisguise.OnLocalDisguiseChanged += HandleMimicDisguiseChanged;
        EscapeDoor.OnFinaleMeterChanged += HandleFinaleMeterChanged;
    }

    void OnDisable()
    {
        GameManager.OnClientStateChanged -= HandleStateChanged;
        NoiseSystem.OnMonsterHeardNoise -= HandleMonsterHeardNoise;
        NoiseSystem.OnNoiseVisible -= HandleNoiseVisible;
        MimicDisguise.OnLocalDisguiseChanged -= HandleMimicDisguiseChanged;
        EscapeDoor.OnFinaleMeterChanged -= HandleFinaleMeterChanged;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        UpdateSelfNoise();
        UpdatePings();
        UpdatePadlockPoll();
        UpdateVuFlash();
        UpdateMonsterOverlays();
        UpdateFinaleMeter();
    }

    /// <summary>All text goes straight in. No fixers, no detours.</summary>
    static void Set(Text t, string s)
    {
        if (t != null) t.text = s ?? "";
    }

    // ------------------------- State plumbing -------------------------

    void HandleStateChanged(GameManager.GameState state)
    {
        GameManager.GameState previous = lastState;
        lastState = state;

        switch (state)
        {
            case GameManager.GameState.Lobby:
                ResetRoundHud();
                hudVisible = false;
                break;

            case GameManager.GameState.Countdown:
                ResetRoundHud();
                hudVisible = false;
                break;

            case GameManager.GameState.Lockdown:
                hudVisible = true;
                if (!roleCardShown)
                {
                    roleCardShown = true;
                    StartCoroutine(RoleCardRoutine());
                }
                break;

            case GameManager.GameState.Playing:
                hudVisible = true;
                if (previous == GameManager.GameState.Lockdown &&
                    GameManager.Instance != null && GameManager.Instance.IAmMonster)
                {
                    ShowLockdownCountdown(-1f);
                    ShowCenterCard(GameCopy.MonsterRelease, ScreamerPalette.MonsterRed, 1.5f);
                }
                break;

            case GameManager.GameState.Finale:
                hudVisible = true;
                break;

            case GameManager.GameState.Results:
                hudVisible = false;
                if (taskPanelRoot != null) taskPanelRoot.SetActive(false);
                ShowPrompt(null);
                ShowLockdownCountdown(-1f);
                break;
        }

        RefreshRoots();
    }

    void RefreshRoots()
    {
        if (survivorRoot != null) survivorRoot.SetActive(hudVisible && !monsterHudOn);
        if (monsterRoot != null) monsterRoot.SetActive(hudVisible && monsterHudOn);
        if (ghostRoot != null) ghostRoot.SetActive(hudVisible && ghostHudOn && !monsterHudOn);
        if (pingLayer != null) pingLayer.gameObject.SetActive(hudVisible && (monsterHudOn || ghostHudOn));
    }

    // The private "IT'S YOU." flash arrives mid-countdown through a ClientRpc
    // the server targets at the chosen client only (GameManager calls
    // ShowRoleReveal(true, -1) from its handler): MonsterClientId stays unset
    // until the public morph, so no other client ever holds the identity early.

    IEnumerator RoleCardRoutine()
    {
        GameManager gm = GameManager.Instance;
        bool monster = gm != null && gm.IAmMonster;

        if (!monster)
        {
            ShowRoleReveal(false, -1);
            yield break;
        }

        // The skin intro needs the monster body; wait briefly for it to sync.
        float deadline = Time.unscaledTime + 1.5f;
        while (Time.unscaledTime < deadline &&
               (MonsterController.ActiveMonster == null || MonsterController.ActiveMonster.SkinIndex < 0))
            yield return null;

        int skin = MonsterController.ActiveMonster != null ? MonsterController.ActiveMonster.SkinIndex : -1;
        // Out-of-range index = full monster card with no tagline (skin never synced).
        ShowRoleReveal(true, skin < 0 ? int.MaxValue : skin);
    }

    // ------------------------- Public contract -------------------------

    /// <summary>
    /// Role cards. Monster with skinIndex &lt; 0 = the private "IT'S YOU."
    /// flash; with a skin = the full card plus the skin tagline (GDD 11.3/11.4).
    /// </summary>
    public void ShowRoleReveal(bool iAmMonster, int monsterSkinIndex)
    {
        if (iAmMonster && monsterSkinIndex < 0)
        {
            ShowCenterCard(GameCopy.RoleItsYou, ScreamerPalette.MonsterRed, 1.1f);
        }
        else if (iAmMonster)
        {
            string tagline = GameCopy.SkinIntro(monsterSkinIndex);
            string card = string.IsNullOrEmpty(tagline)
                ? GameCopy.RoleMonsterCard
                : GameCopy.RoleMonsterCard + "\n\n" + tagline;
            ShowCenterCard(card, ScreamerPalette.MonsterRed, 3f);
        }
        else
        {
            ShowCenterCard(GameCopy.RoleSurvivorCard, ScreamerPalette.ScreamYellow, 3f);
        }
    }

    /// <summary>Event feed: one line, typewriter-in at 40 chars/s, 4 s on screen.</summary>
    public void ShowEvent(string line)
    {
        if (string.IsNullOrEmpty(line) || eventText == null) return;
        eventQueue.Enqueue(line);
        while (eventQueue.Count > 4) eventQueue.Dequeue(); // never backlog a whole massacre
        if (eventRoutine == null) eventRoutine = StartCoroutine(EventFeedRoutine());
    }

    IEnumerator EventFeedRoutine()
    {
        while (eventQueue.Count > 0)
        {
            string line = eventQueue.Dequeue();
            float perChar = 1f / TypewriterCharsPerSecond;
            for (int i = 1; i <= line.Length; i++)
            {
                Set(eventText, line.Substring(0, i));
                yield return new WaitForSecondsRealtime(perChar);
            }
            float hold = eventQueue.Count > 0 ? 1.2f : EventFeedSeconds;
            yield return new WaitForSecondsRealtime(hold);
        }
        Set(eventText, "");
        eventRoutine = null;
    }

    public void ShowTaskPanel(string title, string flavor)
    {
        if (taskPanelRoot == null) return;
        taskPanelRoot.SetActive(true);
        Set(taskNameText, (title ?? "").ToUpperInvariant());
        Set(taskFlavorText, flavor);
        Set(taskHintText, "");
        Set(taskWalkAwayText, GameCopy.TaskWalkAway);
        UpdateTaskProgress(0f);
        if (taskPanelRect != null) StartCoroutine(ScreamerUIStyle.PopIn(taskPanelRect));
    }

    public void ShowTaskHint(string hint) => Set(taskHintText, hint);

    public void UpdateTaskProgress(float normalized)
    {
        vuProgress = Mathf.Clamp01(normalized);
        ApplyVuBar();
    }

    /// <summary>The task VU meter spikes and clips into MonsterRed for 0.2 s.</summary>
    public void NotifyTaskNoise()
    {
        vuFlashTimer = 0.2f;
        ApplyVuBar();
    }

    public void HideTaskPanel(string closingLine)
    {
        if (taskPanelRoot != null) taskPanelRoot.SetActive(false);
        if (!string.IsNullOrEmpty(closingLine)) ShowEvent(closingLine);
    }

    /// <summary>Bottom context plate. Null or empty hides it.</summary>
    public void ShowPrompt(string prompt)
    {
        bool show = !string.IsNullOrEmpty(prompt);
        if (promptRoot != null) promptRoot.SetActive(show);
        if (show) Set(promptText, prompt);
    }

    /// <summary>Self-noise meter kick + the crosshair ring bloom: you just snitched on yourself.</summary>
    public void PulseSelfNoise(float loudness)
    {
        selfNoiseLevel = Mathf.Max(selfNoiseLevel, Mathf.Clamp01(loudness));
        if (crosshairBloom != null)
        {
            if (bloomRoutine != null) StopCoroutine(bloomRoutine);
            bloomRoutine = StartCoroutine(CrosshairBloomRoutine());
        }
    }

    IEnumerator CrosshairBloomRoutine()
    {
        float t = 0f;
        const float duration = 0.45f;
        while (t < duration && crosshairBloom != null)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            crosshairBloom.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.6f, 2.4f, k);
            crosshairBloom.color = ScreamerUIStyle.WithAlpha(ScreamerPalette.NoodleCream, (1f - k) * 0.8f);
            yield return null;
        }
        if (crosshairBloom != null)
            crosshairBloom.color = ScreamerUIStyle.WithAlpha(ScreamerPalette.NoodleCream, 0f);
        bloomRoutine = null;
    }

    public void ShowCenterCard(string text, Color color, float seconds)
    {
        if (centerCardContainer == null) return;
        if (centerCardRoutine != null) StopCoroutine(centerCardRoutine);
        centerCardRoutine = StartCoroutine(CenterCardRoutine(text, color, seconds));
    }

    IEnumerator CenterCardRoutine(string text, Color color, float seconds)
    {
        centerCardContainer.gameObject.SetActive(true);
        Set(centerCardText, text);
        if (centerCardText != null) centerCardText.color = color;
        yield return StartCoroutine(ScreamerUIStyle.PopIn(centerCardContainer));
        yield return new WaitForSecondsRealtime(Mathf.Max(0.1f, seconds));
        centerCardContainer.gameObject.SetActive(false);
        centerCardRoutine = null;
    }

    /// <summary>Swaps to the monster HUD (never overlays the survivor one).</summary>
    public void SetMonsterHud(bool on)
    {
        monsterHudOn = on;
        RefreshRoots();

        if (!on)
        {
            ShowLockdownCountdown(-1f);
            ScreenFxOverlay.Instance?.SetVignette(0.25f, ScreamerPalette.InkBlack);
        }
    }

    public void SetGhostHud(bool on, bool booArmed)
    {
        ghostHudOn = on;
        Set(ghostBooText, booArmed ? GameCopy.GhostBooArmed : GameCopy.GhostBooSpent);
        RefreshRoots();
    }

    /// <summary>
    /// A noise ping on the monster (or ghost) HUD: a directional smear on the
    /// compass-accurate screen edge, a type-shaped ring sized by loudness, the
    /// label in caps, and a 3 s fading ghost echo afterwards.
    /// </summary>
    public void ShowNoisePing(Vector3 worldPos, float loudness, NoiseType type, string label)
    {
        if (pingLayer == null) return;

        PingWidget ping = pingPool.Count > 0 ? pingPool.Pop() : CreatePingWidget();
        ping.go.SetActive(true);
        ping.worldPos = worldPos;
        ping.age = 0f;
        ping.holdSeconds = Mathf.Lerp(1.5f, 5f, Mathf.Clamp01(loudness));

        Color accent = monsterHudOn ? ScreamerPalette.MonsterRed : ScreamerPalette.GhostMint;

        float ringSize = Mathf.Lerp(40f, 160f, Mathf.Clamp01(loudness));
        ping.ring.sprite = ScreamerUIStyle.PingSprite(type);
        ping.ring.rectTransform.sizeDelta = new Vector2(ringSize, ringSize);
        ping.ring.color = accent;

        ping.smear.sprite = ScreamerUIStyle.SmearSprite();
        ping.smear.rectTransform.sizeDelta = new Vector2(Mathf.Lerp(220f, 480f, loudness), Mathf.Lerp(60f, 110f, loudness));
        ping.smear.color = ScreamerUIStyle.WithAlpha(accent, 0.55f);

        Set(ping.label, (label ?? "").ToUpperInvariant());
        ping.label.color = accent;

        activePings.Add(ping);
    }

    PingWidget CreatePingWidget()
    {
        var ping = new PingWidget();

        RectTransform pivot = ScreamerUIStyle.Rect(pingLayer, "Ping");
        ScreamerUIStyle.Place(pivot, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        ping.pivot = pivot;
        ping.go = pivot.gameObject;

        RectTransform holder = ScreamerUIStyle.Rect(pivot, "Holder");
        ScreamerUIStyle.Place(holder, new Vector2(0.5f, 0.5f), new Vector2(0f, PingEdgeRadius), Vector2.zero);

        ping.smear = ScreamerUIStyle.Img(holder, "Smear", ScreamerPalette.MonsterRed);
        ScreamerUIStyle.Place(ping.smear.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 80f));

        ping.ring = ScreamerUIStyle.Img(holder, "Ring", ScreamerPalette.MonsterRed);
        ScreamerUIStyle.Place(ping.ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -70f), new Vector2(100f, 100f));

        ping.label = ScreamerUIStyle.Txt(holder, "Label", "", 20, ScreamerPalette.MonsterRed, TextAnchor.MiddleCenter);
        ping.label.fontStyle = FontStyle.Bold;
        ScreamerUIStyle.Place(ping.label.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -150f), new Vector2(420f, 30f));

        return ping;
    }

    void UpdatePings()
    {
        if (activePings.Count == 0) return;

        Camera cam = Camera.main;
        for (int i = activePings.Count - 1; i >= 0; i--)
        {
            PingWidget ping = activePings[i];
            ping.age += Time.deltaTime;

            float totalLife = ping.holdSeconds + PingEchoSeconds;
            if (ping.age >= totalLife)
            {
                ping.go.SetActive(false);
                activePings.RemoveAt(i);
                pingPool.Push(ping);
                continue;
            }

            // Compass-accurate: keep rotating toward the source as the camera turns.
            if (cam != null)
            {
                Vector3 to = ping.worldPos - cam.transform.position;
                to.y = 0f;
                Vector3 forward = cam.transform.forward;
                forward.y = 0f;
                if (to.sqrMagnitude > 0.001f && forward.sqrMagnitude > 0.001f)
                {
                    float angle = Vector3.SignedAngle(forward, to, Vector3.up);
                    ping.pivot.localEulerAngles = new Vector3(0f, 0f, -angle);
                }
            }

            // Fresh and bright while held; a fading ghost echo after (noise history).
            float alpha = ping.age <= ping.holdSeconds
                ? 1f
                : Mathf.Lerp(0.45f, 0f, (ping.age - ping.holdSeconds) / PingEchoSeconds);

            Color ringColor = ping.ring.color;
            ping.ring.color = ScreamerUIStyle.WithAlpha(ringColor, alpha);
            Color smearColor = ping.smear.color;
            ping.smear.color = ScreamerUIStyle.WithAlpha(smearColor, alpha * 0.55f);
            ping.label.color = ScreamerUIStyle.WithAlpha(ping.label.color, alpha);

            // Labels stay upright no matter where the ring sits on the edge.
            ping.label.rectTransform.rotation = Quaternion.identity;
        }
    }

    /// <summary>Lockdown HUD countdown; secondsLeft &lt; 0 hides it.</summary>
    public void ShowLockdownCountdown(float secondsLeft)
    {
        if (lockdownRoot == null) return;
        if (secondsLeft < 0f)
        {
            lockdownRoot.SetActive(false);
            return;
        }
        lockdownRoot.SetActive(true);
        Set(lockdownHeaderText, GameCopy.LockdownHeader);
        Set(lockdownCountdownText, GameCopy.LockdownReleasedIn(secondsLeft));
    }

    /// <summary>One more tally stroke, top right. Every fifth crosses the previous four.</summary>
    public void AddKillTally()
    {
        if (tallyContainer == null) return;
        killTallies++;

        int group = (killTallies - 1) / 5;
        int indexInGroup = (killTallies - 1) % 5;
        bool diagonal = indexInGroup == 4;

        Image stroke = ScreamerUIStyle.Img(tallyContainer, "Tally" + killTallies, ScreamerPalette.MonsterRed);
        float x = -group * 64f - (diagonal ? 24f : indexInGroup * 14f);
        ScreamerUIStyle.Place(stroke.rectTransform, new Vector2(1f, 1f), new Vector2(x, 0f),
            diagonal ? new Vector2(6f, 58f) : new Vector2(6f, 44f));
        stroke.rectTransform.localEulerAngles = new Vector3(0f, 0f, diagonal ? 65f : Random.Range(-4f, 4f));
    }

    /// <summary>Back to a clean HUD for the next round (the rematch reset path).</summary>
    public void ResetRoundHud()
    {
        // Pings & tallies.
        for (int i = activePings.Count - 1; i >= 0; i--)
        {
            activePings[i].go.SetActive(false);
            pingPool.Push(activePings[i]);
        }
        activePings.Clear();

        if (tallyContainer != null)
            for (int i = tallyContainer.childCount - 1; i >= 0; i--)
                Destroy(tallyContainer.GetChild(i).gameObject);
        killTallies = 0;

        // Feed.
        eventQueue.Clear();
        if (eventRoutine != null)
        {
            StopCoroutine(eventRoutine);
            eventRoutine = null;
        }
        Set(eventText, "");

        // Panels and meters.
        if (taskPanelRoot != null) taskPanelRoot.SetActive(false);
        ShowPrompt(null);
        ShowLockdownCountdown(-1f);
        if (mimicHintRoot != null) mimicHintRoot.SetActive(false);
        if (centerCardContainer != null) centerCardContainer.gameObject.SetActive(false);
        if (finaleRoot != null) finaleRoot.SetActive(false);
        selfNoiseLevel = 0f;
        vuFlashTimer = 0f;
        vuProgress = 0f;
        finaleMeterValue = 0f;

        // Padlocks back on the door.
        shownPadlocksBroken = 0;
        for (int i = 0; i < PadlockCount; i++)
            SetPadlockVisual(i, false, false);

        // Last round's ghost overlay (and its all-seeing noise feed) never
        // follows a living player into the next round.
        SetGhostHud(false, true);

        roleCardShown = false;
    }

    // ------------------------- Event handlers -------------------------

    void HandleMonsterHeardNoise(ulong sourceActorId, Vector3 position, float loudness, NoiseType type, string label)
    {
        if (!monsterHudOn) return;
        ShowNoisePing(position, loudness, type, label);
    }

    void HandleNoiseVisible(ulong sourceActorId, Vector3 position, float loudness, NoiseType type)
    {
        // Ghosts see everything - the backseat commentary channel.
        if (!ghostHudOn || monsterHudOn) return;
        ShowNoisePing(position, loudness, type, GameCopy.NoiseLabel(type));
    }

    void HandleMimicDisguiseChanged(bool disguised)
    {
        if (mimicHintRoot != null) mimicHintRoot.SetActive(disguised);
        if (disguised) Set(mimicHintText, GameCopy.MimicWhileDisguised);
    }

    void HandleFinaleMeterChanged(float normalized)
    {
        finaleMeterValue = Mathf.Clamp01(normalized);
    }

    // ------------------------- Per-frame internals -------------------------

    void UpdateSelfNoise()
    {
        if (selfNoiseLevel > 0f)
            selfNoiseLevel = Mathf.Max(0f, selfNoiseLevel - Time.deltaTime / SelfNoiseDecaySeconds);

        if (selfNoiseFill != null)
        {
            RectTransform rt = selfNoiseFill.rectTransform;
            rt.sizeDelta = new Vector2(220f * selfNoiseLevel, rt.sizeDelta.y);
            selfNoiseFill.color = Color.Lerp(ScreamerPalette.NoodleCream, ScreamerPalette.MonsterRed, selfNoiseLevel);
        }

        if (selfNoiseMouth != null)
            selfNoiseMouth.localScale = new Vector3(1f, 0.25f + 0.75f * selfNoiseLevel, 1f);
    }

    void UpdatePadlockPoll()
    {
        EscapeDoor door = EscapeDoor.Instance;
        if (door == null) return;

        int broken = Mathf.Clamp(door.PadlocksBroken.Value, 0, PadlockCount);
        if (broken == shownPadlocksBroken) return;

        if (broken < shownPadlocksBroken)
        {
            // Rematch relock.
            for (int i = 0; i < PadlockCount; i++)
                SetPadlockVisual(i, false, false);
        }
        else
        {
            for (int i = shownPadlocksBroken; i < broken; i++)
                SetPadlockVisual(i, true, hudVisible);
        }
        shownPadlocksBroken = broken;
    }

    void SetPadlockVisual(int index, bool broken, bool withJuice)
    {
        if (index < 0 || index >= PadlockCount) return;
        Image body = padlockBodies[index];
        RectTransform shackle = padlockShackles[index];
        if (body == null || shackle == null) return;

        if (broken)
        {
            body.color = ScreamerUIStyle.WithAlpha(ScreamerPalette.GhostMint, 0.9f);
            shackle.localEulerAngles = new Vector3(0f, 0f, -55f);
            shackle.anchoredPosition = new Vector2(10f, 24f);
            Image shackleImage = shackle.GetComponent<Image>();
            if (shackleImage != null) shackleImage.color = ScreamerUIStyle.WithAlpha(ScreamerPalette.GhostMint, 0.9f);

            if (withJuice)
            {
                StartCoroutine(ShakePadlockRow());
                AudioDirector.Instance?.PlayUI(Sfx.GlassBreak, 0.8f);
            }
        }
        else
        {
            body.color = ScreamerPalette.NoodleCream;
            shackle.localEulerAngles = Vector3.zero;
            shackle.anchoredPosition = new Vector2(0f, 16f);
            Image shackleImage = shackle.GetComponent<Image>();
            if (shackleImage != null) shackleImage.color = ScreamerPalette.NoodleCream;
        }
    }

    IEnumerator ShakePadlockRow()
    {
        if (padlockRow == null) yield break;
        Vector2 basePos = padlockRow.anchoredPosition;
        float t = 0f;
        const float duration = 0.3f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float falloff = 1f - t / duration;
            padlockRow.anchoredPosition = basePos + Random.insideUnitCircle * (6f * falloff);
            yield return null;
        }
        padlockRow.anchoredPosition = basePos;
    }

    void UpdateVuFlash()
    {
        if (vuFlashTimer <= 0f) return;
        vuFlashTimer -= Time.deltaTime;
        ApplyVuBar();
    }

    void ApplyVuBar()
    {
        if (vuFillRect != null)
        {
            // The clip spike pushes the bar past its honest value for a beat.
            float display = vuFlashTimer > 0f ? Mathf.Min(1f, vuProgress + 0.06f) : vuProgress;
            vuFillRect.sizeDelta = new Vector2(640f * display, vuFillRect.sizeDelta.y);
        }
        if (vuFillImage != null)
            vuFillImage.color = vuFlashTimer > 0f ? ScreamerPalette.MonsterRed : ScreamerPalette.ScreamYellow;
    }

    void UpdateMonsterOverlays()
    {
        if (!monsterHudOn || !hudVisible)
            return;

        // Heartbeat vignette: 0.45 with a +/-0.05 pulse at 1.2 Hz.
        float alpha = 0.45f + 0.05f * Mathf.Sin(Time.time * Mathf.PI * 2f * 1.2f);
        ScreenFxOverlay.Instance?.SetVignette(alpha, ScreamerPalette.MonsterRed);

        // Lockdown countdown, straight off the server clock.
        GameManager gm = GameManager.Instance;
        if (gm != null && gm.State.Value == GameManager.GameState.Lockdown &&
            Unity.Netcode.NetworkManager.Singleton != null)
        {
            float left = (float)(gm.StateEndsAtServerTime.Value - Unity.Netcode.NetworkManager.Singleton.ServerTime.Time);
            ShowLockdownCountdown(Mathf.Max(0f, left));
        }
    }

    void UpdateFinaleMeter()
    {
        if (finaleRoot == null) return;

        GameManager gm = GameManager.Instance;
        bool doorOpen = EscapeDoor.Instance != null && EscapeDoor.Instance.Open.Value;
        bool show = hudVisible && !monsterHudOn && gm != null &&
                    gm.State.Value == GameManager.GameState.Finale && !doorOpen;

        if (finaleRoot.activeSelf != show)
        {
            finaleRoot.SetActive(show);
            if (show)
            {
                Set(finalePromptText, GameCopy.FinalePrompt);
                Set(finaleSignText, GameCopy.DoorFinaleSign);
            }
        }

        if (show && finaleFillRect != null)
            finaleFillRect.sizeDelta = new Vector2(500f * finaleMeterValue, finaleFillRect.sizeDelta.y);
    }

    // ------------------------- Construction (called by UiFactory) -------------------------

    /// <summary>Builds the whole HUD hierarchy under the shared canvas and wires every field.</summary>
    public static GameUI Build(Transform canvasRoot)
    {
        RectTransform root = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(canvasRoot, "GameUI"));
        GameUI ui = root.gameObject.AddComponent<GameUI>();

        // ---------- Ping layer (monster + ghosts), lowest HUD layer ----------
        ui.pingLayer = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(root, "PingLayer"));

        // ---------- Survivor root ----------
        RectTransform survivor = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(root, "SurvivorRoot"));
        ui.survivorRoot = survivor.gameObject;

        // Padlock row, top center.
        ui.padlockRow = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(survivor, "PadlockRow"),
            new Vector2(0.5f, 1f), new Vector2(0f, -18f), new Vector2(PadlockCount * 48f, 56f));
        for (int i = 0; i < PadlockCount; i++)
        {
            RectTransform lockRoot = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(ui.padlockRow, "Padlock" + (i + 1)),
                new Vector2(0.5f, 0.5f), new Vector2((i - (PadlockCount - 1) * 0.5f) * 48f, 0f), new Vector2(40f, 48f));

            Image shackle = ScreamerUIStyle.Img(lockRoot, "Shackle", ScreamerPalette.NoodleCream);
            ScreamerUIStyle.Place(shackle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 16f), new Vector2(26f, 26f));

            Image body = ScreamerUIStyle.Img(lockRoot, "Body", ScreamerPalette.NoodleCream);
            ScreamerUIStyle.Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -8f), new Vector2(40f, 30f));

            Image keyhole = ScreamerUIStyle.Img(body.transform, "Keyhole", ScreamerPalette.InkBlack);
            ScreamerUIStyle.Place(keyhole.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -2f), new Vector2(7f, 12f));

            ui.padlockBodies[i] = body;
            ui.padlockShackles[i] = shackle.rectTransform;
        }

        // Self-noise meter, bottom left.
        RectTransform noiseRoot = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(survivor, "SelfNoise"),
            new Vector2(0f, 0f), new Vector2(42f, 42f), new Vector2(260f, 36f));

        // The little "MIC" tag over the meter (GDD 7.3 HUD chrome).
        Text micLabel = ScreamerUIStyle.Header(noiseRoot, "MicLabel", GameCopy.SelfNoiseLabel, 13,
            ScreamerUIStyle.WithAlpha(ScreamerPalette.NoodleCream, 0.7f), TextAnchor.LowerLeft);
        ScreamerUIStyle.Place(micLabel.rectTransform, new Vector2(0f, 1f), new Vector2(34f, 16f), new Vector2(80f, 16f));

        Image mouth = ScreamerUIStyle.Img(noiseRoot, "Mouth", ScreamerPalette.NoodleCream);
        ScreamerUIStyle.Place(mouth.rectTransform, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(26f, 26f));
        ui.selfNoiseMouth = mouth.rectTransform;
        ui.selfNoiseMouthImage = mouth;

        Image noiseBg = ScreamerUIStyle.Img(noiseRoot, "Track", ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.45f));
        ScreamerUIStyle.Place(noiseBg.rectTransform, new Vector2(0f, 0.5f), new Vector2(34f, 0f), new Vector2(220f, 18f));
        noiseBg.rectTransform.pivot = new Vector2(0f, 0.5f);

        Image noiseFill = ScreamerUIStyle.Img(noiseBg.transform, "Fill", ScreamerPalette.NoodleCream);
        noiseFill.rectTransform.anchorMin = new Vector2(0f, 0f);
        noiseFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        noiseFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        noiseFill.rectTransform.anchoredPosition = Vector2.zero;
        noiseFill.rectTransform.sizeDelta = new Vector2(0f, 0f);
        ui.selfNoiseFill = noiseFill;

        // Crosshair: 4 px cream dot + snitch bloom ring.
        Image dot = ScreamerUIStyle.Img(survivor, "Crosshair", ScreamerPalette.NoodleCream);
        ScreamerUIStyle.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(4f, 4f));
        ui.crosshairDot = dot;

        Image bloom = ScreamerUIStyle.Img(survivor, "CrosshairBloom", ScreamerUIStyle.WithAlpha(ScreamerPalette.NoodleCream, 0f));
        ScreamerUIStyle.Place(bloom.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 48f));
        ui.crosshairBloom = bloom;

        // Context prompt plate, bottom center.
        RectTransform promptPlate = ScreamerUIStyle.Plate(survivor, "PromptPlate",
            new Vector2(0.5f, 0f), new Vector2(0f, 96f), new Vector2(460f, 48f));
        ui.promptRoot = promptPlate.parent.gameObject;
        ui.promptText = ScreamerUIStyle.Header(promptPlate, "PromptText", "", 22, ScreamerPalette.InkBlack, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Stretch(ui.promptText.rectTransform);

        // Task panel, docked bottom third.
        RectTransform taskPlate = ScreamerUIStyle.Plate(survivor, "TaskPanel",
            new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(720f, 190f));
        ui.taskPanelRoot = taskPlate.parent.gameObject;
        ui.taskPanelRect = (RectTransform)taskPlate.parent;

        ui.taskNameText = ScreamerUIStyle.Header(taskPlate, "TaskName", "", 36, ScreamerPalette.InkBlack, TextAnchor.UpperLeft);
        ScreamerUIStyle.Place(ui.taskNameText.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -14f), new Vector2(672f, 42f));
        ui.taskNameText.rectTransform.pivot = new Vector2(0f, 1f);

        ui.taskFlavorText = ScreamerUIStyle.Txt(taskPlate, "TaskFlavor", "", 18, ScreamerPalette.InkBlack, TextAnchor.UpperLeft);
        ScreamerUIStyle.Place(ui.taskFlavorText.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -58f), new Vector2(672f, 26f));
        ui.taskFlavorText.rectTransform.pivot = new Vector2(0f, 1f);

        // The VU recording-level bar.
        Image vuBg = ScreamerUIStyle.Img(taskPlate, "VuTrack", ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.4f));
        ScreamerUIStyle.Place(vuBg.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -92f), new Vector2(640f, 24f));
        vuBg.rectTransform.pivot = new Vector2(0f, 1f);

        Image vuFill = ScreamerUIStyle.Img(vuBg.transform, "VuFill", ScreamerPalette.ScreamYellow);
        vuFill.rectTransform.anchorMin = new Vector2(0f, 0f);
        vuFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        vuFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        vuFill.rectTransform.anchoredPosition = new Vector2(0f, 0f);
        vuFill.rectTransform.sizeDelta = Vector2.zero;
        ui.vuFillRect = vuFill.rectTransform;
        ui.vuFillImage = vuFill;

        ui.taskHintText = ScreamerUIStyle.Txt(taskPlate, "TaskHint", "", 20, ScreamerPalette.ShagRust, TextAnchor.UpperLeft);
        ui.taskHintText.fontStyle = FontStyle.Bold;
        ScreamerUIStyle.Place(ui.taskHintText.rectTransform, new Vector2(0f, 1f), new Vector2(24f, -126f), new Vector2(500f, 26f));
        ui.taskHintText.rectTransform.pivot = new Vector2(0f, 1f);

        ui.taskWalkAwayText = ScreamerUIStyle.Txt(taskPlate, "WalkAway", GameCopy.TaskWalkAway, 16,
            ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.7f), TextAnchor.LowerRight);
        ScreamerUIStyle.Place(ui.taskWalkAwayText.rectTransform, new Vector2(1f, 0f), new Vector2(-18f, 12f), new Vector2(300f, 22f));
        ui.taskWalkAwayText.rectTransform.pivot = new Vector2(1f, 0f);

        // Finale group-scream meter.
        RectTransform finalePlate = ScreamerUIStyle.Plate(survivor, "FinaleMeter",
            new Vector2(0.5f, 0f), new Vector2(0f, 280f), new Vector2(560f, 96f));
        ui.finaleRoot = finalePlate.parent.gameObject;

        ui.finalePromptText = ScreamerUIStyle.Header(finalePlate, "FinalePrompt", GameCopy.FinalePrompt, 24,
            ScreamerPalette.InkBlack, TextAnchor.UpperCenter);
        ScreamerUIStyle.Place(ui.finalePromptText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(540f, 30f));

        Image finaleTrack = ScreamerUIStyle.Img(finalePlate, "FinaleTrack", ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.4f));
        ScreamerUIStyle.Place(finaleTrack.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 34f), new Vector2(500f, 22f));

        Image finaleFill = ScreamerUIStyle.Img(finaleTrack.transform, "FinaleFill", ScreamerPalette.GhostMint);
        finaleFill.rectTransform.anchorMin = new Vector2(0f, 0f);
        finaleFill.rectTransform.anchorMax = new Vector2(0f, 1f);
        finaleFill.rectTransform.pivot = new Vector2(0f, 0.5f);
        finaleFill.rectTransform.anchoredPosition = Vector2.zero;
        finaleFill.rectTransform.sizeDelta = Vector2.zero;
        ui.finaleFillRect = finaleFill.rectTransform;

        ui.finaleSignText = ScreamerUIStyle.Txt(finalePlate, "FinaleSign", GameCopy.DoorFinaleSign, 14,
            ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.7f), TextAnchor.LowerCenter);
        ScreamerUIStyle.Place(ui.finaleSignText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 8f), new Vector2(540f, 20f));

        // ---------- Monster root ----------
        RectTransform monster = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(root, "MonsterRoot"));
        ui.monsterRoot = monster.gameObject;

        RectTransform lockdown = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(monster, "Lockdown"),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 120f), new Vector2(900f, 140f));
        ui.lockdownRoot = lockdown.gameObject;

        ui.lockdownHeaderText = ScreamerUIStyle.Header(lockdown, "Header", GameCopy.LockdownHeader, 40,
            ScreamerPalette.MonsterRed, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(ui.lockdownHeaderText.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(900f, 56f));

        ui.lockdownCountdownText = ScreamerUIStyle.Header(lockdown, "Countdown", "", 64,
            ScreamerPalette.MonsterRed, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(ui.lockdownCountdownText.rectTransform, new Vector2(0.5f, 0f), Vector2.zero, new Vector2(900f, 80f));

        ui.tallyContainer = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(monster, "KillTallies"),
            new Vector2(1f, 1f), new Vector2(-48f, -48f), Vector2.zero);

        RectTransform mimicHint = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(monster, "MimicHint"),
            new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(700f, 30f));
        ui.mimicHintRoot = mimicHint.gameObject;
        ui.mimicHintText = ScreamerUIStyle.Txt(mimicHint, "Text", GameCopy.MimicWhileDisguised, 16,
            ScreamerUIStyle.WithAlpha(ScreamerPalette.MonsterRed, 0.5f), TextAnchor.MiddleCenter);
        ScreamerUIStyle.Stretch(ui.mimicHintText.rectTransform);

        // ---------- Ghost root ----------
        RectTransform ghost = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(root, "GhostRoot"));
        ui.ghostRoot = ghost.gameObject;
        ui.ghostBooText = ScreamerUIStyle.Header(ghost, "BooState", GameCopy.GhostBooArmed, 26,
            ScreamerPalette.GhostMint, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(ui.ghostBooText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -96f), new Vector2(400f, 36f));

        // ---------- Shared: event feed + center card ----------
        ui.eventText = ScreamerUIStyle.Txt(root, "EventFeed", "", 22, ScreamerPalette.ScreamYellow, TextAnchor.UpperCenter);
        ui.eventText.fontStyle = FontStyle.Bold;
        ScreamerUIStyle.Place(ui.eventText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -84f), new Vector2(1100f, 32f));

        RectTransform cardPlate = ScreamerUIStyle.Plate(root, "CenterCard",
            new Vector2(0.5f, 0.5f), new Vector2(0f, 140f), new Vector2(760f, 200f));
        ui.centerCardContainer = (RectTransform)cardPlate.parent;
        ui.centerCardText = ScreamerUIStyle.Header(cardPlate, "CardText", "", 34, ScreamerPalette.InkBlack, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Stretch(ui.centerCardText.rectTransform);
        ui.centerCardText.rectTransform.offsetMin = new Vector2(24f, 16f);
        ui.centerCardText.rectTransform.offsetMax = new Vector2(-24f, -16f);

        return ui;
    }
}
