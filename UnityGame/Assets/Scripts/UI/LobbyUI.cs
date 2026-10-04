using System.Collections;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The lobby overlay (GDD 7.2). The den itself IS the lobby - players walk
/// around as pawns - so this is chrome only: the right-rail roster (color
/// chip, name, ping, READY check, host-only bot controls), the giant room
/// code with COPY / INVITE, the READY UP button that becomes the host's
/// pulsing START ROUND, the cycling tip ticker, and the full-screen 5..1
/// countdown numbers that land with a camera thump.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    public static LobbyUI Instance { get; private set; }

    const float RosterRefreshInterval = 0.25f;
    const float TickerInterval = 6f;
    const float RowHeight = 38f;

    [Header("Roots")]
    public GameObject lobbyRoot;
    public GameObject countdownRoot;

    [Header("Roster rail")]
    public RectTransform rosterPlate;
    public RectTransform rosterRows;
    public Button addBotButton;

    [Header("Room code")]
    public Text roomCodeHeaderText;
    public Text roomCodeText;
    public Button copyButton;
    public Text copyButtonLabel;
    public Button inviteButton;

    [Header("Ready / start")]
    public Button readyButton;
    public Text readyButtonLabel;

    [Header("Ticker")]
    public Text tickerText;

    [Header("Countdown")]
    public Text countdownText;
    public RectTransform countdownRect;

    float rosterTimer;
    float tickerTimer;
    int tickerIndex;
    int lastCountdownNumber = -1;
    string rosterSignature = "";
    string pushedTickerLine;
    Coroutine copyFlashRoutine;
    GameManager.GameState lastSeenState = GameManager.GameState.Lobby;

    void Awake()
    {
        Instance = this;
        ScreamerUIStyle.ApplyFonts(gameObject);
        if (lobbyRoot != null) lobbyRoot.SetActive(false);
        if (countdownRoot != null) countdownRoot.SetActive(false);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        ScreamerSettings.OnChanged += HandleSettingsChanged;
    }

    void OnDisable()
    {
        ScreamerSettings.OnChanged -= HandleSettingsChanged;
    }

    void HandleSettingsChanged()
    {
        // The colorblind palette applies live (GDD 7.5): the roster signature
        // does not cover palette choice, so force the next refresh to rebuild.
        rosterSignature = "";
    }

    void Start()
    {
        if (readyButton != null) readyButton.onClick.AddListener(OnReadyClicked);
        if (copyButton != null) copyButton.onClick.AddListener(OnCopyClicked);
        if (inviteButton != null) inviteButton.onClick.AddListener(() => BackendSelector.Active.OpenInviteOverlay());
        if (addBotButton != null) addBotButton.onClick.AddListener(OnAddBotClicked);

        WireFeel(readyButton);
        WireFeel(copyButton);
        WireFeel(inviteButton);
        WireFeel(addBotButton);
    }

    static void WireFeel(Button button)
    {
        if (button == null) return;
        RectTransform scaleTarget = button.transform.parent as RectTransform;
        ScreamerUIStyle.WireButtonFeel(button, scaleTarget != null ? scaleTarget : (RectTransform)button.transform,
            button.GetComponentInChildren<Text>());
    }

    /// <summary>Lets world systems (house-in-a-mood) drop one line into the ticker rotation.</summary>
    public static void PushTickerLine(string line)
    {
        if (Instance != null) Instance.pushedTickerLine = line;
    }

    void Update()
    {
        bool listening = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
        GameManager gm = GameManager.Instance;
        GameManager.GameState state = gm != null ? gm.State.Value : GameManager.GameState.Lobby;

        bool showLobby = listening && gm != null && state == GameManager.GameState.Lobby;
        bool showCountdown = listening && gm != null && state == GameManager.GameState.Countdown;

        if (lobbyRoot != null && lobbyRoot.activeSelf != showLobby)
            lobbyRoot.SetActive(showLobby);
        if (countdownRoot != null && countdownRoot.activeSelf != showCountdown)
        {
            countdownRoot.SetActive(showCountdown);
            lastCountdownNumber = -1;
        }

        // The lobby needs a pointer; the round needs a locked camera. Lock
        // exactly once on the transition out of Lobby so Pause can still free it.
        if (listening && state != lastSeenState)
        {
            bool pauseOpen = PauseUI.Instance != null && PauseUI.Instance.IsOpen;
            if (!pauseOpen &&
                (state == GameManager.GameState.Countdown || state == GameManager.GameState.Lockdown))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
            lastSeenState = state;
        }

        if (showLobby)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            UpdateRoster();
            UpdateReadyButton();
            UpdateRoomCode();
            UpdateTicker();
        }

        if (showCountdown)
            UpdateCountdown(gm);
    }

    // ------------------------- Roster -------------------------

    void UpdateRoster()
    {
        rosterTimer -= Time.unscaledDeltaTime;
        if (rosterTimer > 0f) return;
        rosterTimer = RosterRefreshInterval;

        GameManager gm = GameManager.Instance;
        if (gm == null || gm.Roster == null || rosterRows == null) return;

        // Cheap change detection so eight rows are not rebuilt every tick.
        var sb = new StringBuilder();
        for (int i = 0; i < gm.Roster.Count; i++)
        {
            RosterEntry e = gm.Roster[i];
            sb.Append(e.actorId).Append('|').Append(e.name.ToString()).Append('|')
              .Append(e.ready ? 1 : 0).Append('|').Append(e.isBot ? 1 : 0).Append('|')
              .Append(e.colorIndex).Append('|').Append((int)e.pingMs).Append(';');
        }
        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        sb.Append(isHost ? "H" : "C");

        string signature = sb.ToString();
        if (signature == rosterSignature) return;
        rosterSignature = signature;

        RebuildRosterRows(gm, isHost);
    }

    void RebuildRosterRows(GameManager gm, bool isHost)
    {
        for (int i = rosterRows.childCount - 1; i >= 0; i--)
            Destroy(rosterRows.GetChild(i).gameObject);

        for (int i = 0; i < gm.Roster.Count; i++)
        {
            RosterEntry entry = gm.Roster[i];
            RectTransform row = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(rosterRows, "Row" + i),
                new Vector2(0.5f, 1f), new Vector2(0f, -i * RowHeight), new Vector2(340f, RowHeight - 4f));

            Image chip = ScreamerUIStyle.Img(row, "Chip", ScreamerPalette.Survivor(entry.colorIndex));
            ScreamerUIStyle.Place(chip.rectTransform, new Vector2(0f, 0.5f), new Vector2(4f, 0f), new Vector2(24f, 24f));
            chip.rectTransform.pivot = new Vector2(0f, 0.5f);

            Text name = ScreamerUIStyle.Txt(row, "Name", entry.name.ToString(), 18,
                ScreamerPalette.InkBlack, TextAnchor.MiddleLeft);
            ScreamerUIStyle.Place(name.rectTransform, new Vector2(0f, 0.5f), new Vector2(36f, 0f), new Vector2(170f, 30f));
            name.rectTransform.pivot = new Vector2(0f, 0.5f);

            if (!entry.isBot)
            {
                Text ping = ScreamerUIStyle.Txt(row, "Ping", (int)entry.pingMs + " ms", 14,
                    ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.55f), TextAnchor.MiddleRight);
                ScreamerUIStyle.Place(ping.rectTransform, new Vector2(1f, 0.5f), new Vector2(-104f, 0f), new Vector2(64f, 24f));
                ping.rectTransform.pivot = new Vector2(1f, 0.5f);
            }

            if (entry.ready)
            {
                Text ready = ScreamerUIStyle.Header(row, "Ready", GameCopy.ReadyCheck, 16,
                    ScreamerPalette.ScreamYellow, TextAnchor.MiddleRight);
                // Yellow needs an ink backing to read on the cream plate.
                Image backing = ScreamerUIStyle.Img(row, "ReadyBacking", ScreamerPalette.InkBlack);
                ScreamerUIStyle.Place(backing.rectTransform, new Vector2(1f, 0.5f), new Vector2(-34f, 0f), new Vector2(66f, 24f));
                backing.rectTransform.pivot = new Vector2(1f, 0.5f);
                ScreamerUIStyle.Place(ready.rectTransform, new Vector2(1f, 0.5f), new Vector2(-38f, 0f), new Vector2(60f, 24f));
                ready.rectTransform.pivot = new Vector2(1f, 0.5f);
            }

            if (entry.isBot && isHost)
            {
                ulong botId = entry.actorId;
                Button remove = ScreamerUIStyle.Btn(row, "RemoveBot", GameCopy.RemoveBot,
                    new Vector2(1f, 0.5f), new Vector2(-2f, 0f), new Vector2(26f, 26f));
                remove.onClick.AddListener(() => BotManager.Instance?.ServerRemoveBot(botId));
                WireFeel(remove);
            }
        }

        rosterRows.sizeDelta = new Vector2(rosterRows.sizeDelta.x, gm.Roster.Count * RowHeight);

        // The + ADD BOT row trails the list, host only, while seats remain.
        // It lives under rosterPlate, NOT rosterRows (which is cleared above),
        // so its position accounts for the rows rail's -16 offset in the plate.
        if (addBotButton != null)
        {
            bool canAdd = isHost && gm.Roster.Count < 8;
            addBotButton.transform.parent.gameObject.SetActive(canAdd);
            if (canAdd)
            {
                var addRt = (RectTransform)addBotButton.transform.parent;
                addRt.anchoredPosition = new Vector2(0f, -16f - gm.Roster.Count * RowHeight - 6f);
            }
        }
    }

    void OnAddBotClicked()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        BotManager.Instance?.ServerAddBot();
    }

    // ------------------------- Ready / start -------------------------

    void UpdateReadyButton()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || readyButton == null || readyButtonLabel == null) return;

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        bool myReady = MyRosterReady(gm);
        bool allHumansReady = AllHumansReady(gm);

        if (isHost && allHumansReady)
        {
            readyButtonLabel.text = GameCopy.StartRoundLabel;
            // The pulse: ScreamYellow breathing on the label.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f);
            readyButtonLabel.color = Color.Lerp(ScreamerPalette.ShagRust, ScreamerPalette.InkBlack, pulse);
            Image plate = readyButton.targetGraphic as Image;
            if (plate != null) plate.color = Color.Lerp(ScreamerPalette.NoodleCream, ScreamerPalette.ScreamYellow, pulse);
        }
        else
        {
            readyButtonLabel.text = myReady ? GameCopy.Unready : GameCopy.ReadyUp;
            Image plate = readyButton.targetGraphic as Image;
            if (plate != null) plate.color = ScreamerPalette.NoodleCream;
        }
    }

    void OnReadyClicked()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        bool isHost = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        if (isHost && AllHumansReady(gm))
        {
            gm.StartRound();
            return;
        }

        bool ready = !MyRosterReady(gm);
        gm.SetReadyServerRpc(ready);

        // Keep the diegetic pose in step with the roster flag ([R] does the
        // same in reverse): the pose is owner-written and this click is the owner.
        if (PlayerController.Local != null && !PlayerController.Local.IsGhost)
            PlayerController.Local.ReadyPose.Value = ready;
    }

    static bool MyRosterReady(GameManager gm)
    {
        if (NetworkManager.Singleton == null) return false;
        ulong localId = NetworkManager.Singleton.LocalClientId;
        for (int i = 0; i < gm.Roster.Count; i++)
            if (gm.Roster[i].actorId == localId)
                return gm.Roster[i].ready;
        return false;
    }

    static bool AllHumansReady(GameManager gm)
    {
        int humans = 0;
        for (int i = 0; i < gm.Roster.Count; i++)
        {
            if (gm.Roster[i].isBot) continue;
            humans++;
            if (!gm.Roster[i].ready) return false;
        }
        return humans > 0;
    }

    // ------------------------- Room code -------------------------

    void UpdateRoomCode()
    {
        if (roomCodeText != null)
            roomCodeText.text = BackendSelector.Active.LobbyCode ?? "";

        if (inviteButton != null)
        {
            bool invites = BackendSelector.Active.SupportsInvites;
            if (inviteButton.transform.parent.gameObject.activeSelf != invites)
                inviteButton.transform.parent.gameObject.SetActive(invites);
        }
    }

    void OnCopyClicked()
    {
        GUIUtility.systemCopyBuffer = BackendSelector.Active.LobbyCode ?? "";
        if (copyFlashRoutine != null) StopCoroutine(copyFlashRoutine);
        copyFlashRoutine = StartCoroutine(CopyFlashRoutine());
    }

    IEnumerator CopyFlashRoutine()
    {
        if (copyButtonLabel != null) copyButtonLabel.text = GameCopy.CodeCopied;
        yield return new WaitForSecondsRealtime(1.5f);
        if (copyButtonLabel != null) copyButtonLabel.text = GameCopy.CopyCode;
        copyFlashRoutine = null;
    }

    // ------------------------- Ticker -------------------------

    void UpdateTicker()
    {
        tickerTimer -= Time.unscaledDeltaTime;
        if (tickerTimer > 0f) return;
        tickerTimer = TickerInterval;

        if (tickerText == null) return;

        if (!string.IsNullOrEmpty(pushedTickerLine))
        {
            tickerText.text = pushedTickerLine;
            pushedTickerLine = null;
            return;
        }

        if (GameCopy.LobbyTips.Length > 0)
        {
            tickerText.text = GameCopy.LobbyTips[tickerIndex % GameCopy.LobbyTips.Length];
            tickerIndex++;
        }
    }

    // ------------------------- Countdown -------------------------

    void UpdateCountdown(GameManager gm)
    {
        if (countdownText == null || NetworkManager.Singleton == null) return;

        double endsAt = gm.StateEndsAtServerTime.Value;
        if (endsAt <= 0) return;

        float left = (float)(endsAt - NetworkManager.Singleton.ServerTime.Time);
        int number = Mathf.Clamp(Mathf.CeilToInt(left), 0, 9);

        if (number != lastCountdownNumber)
        {
            lastCountdownNumber = number;
            countdownText.text = number > 0 ? number.ToString() : "";
            if (number > 0)
            {
                if (countdownRect != null) StartCoroutine(CountdownThump());
                ScreamerCam.Instance?.Shake(0.08f, 0.1f);
                AudioDirector.Instance?.PlayUI(Sfx.StampThunk, 0.7f);
            }
        }
    }

    IEnumerator CountdownThump()
    {
        float t = 0f;
        const float duration = 0.18f;
        while (t < duration && countdownRect != null)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t / duration);
            countdownRect.localScale = Vector3.one * Mathf.Lerp(1.35f, 1f, k * k);
            yield return null;
        }
        if (countdownRect != null) countdownRect.localScale = Vector3.one;
    }

    // ------------------------- Construction (called by UiFactory) -------------------------

    public static LobbyUI Build(Transform canvasRoot)
    {
        RectTransform root = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(canvasRoot, "LobbyUI"));
        LobbyUI ui = root.gameObject.AddComponent<LobbyUI>();

        RectTransform lobby = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(root, "LobbyRoot"));
        ui.lobbyRoot = lobby.gameObject;

        // ---------- Top ticker strip ----------
        Image tickerStrip = ScreamerUIStyle.Img(lobby, "TickerStrip", ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.92f));
        tickerStrip.rectTransform.anchorMin = new Vector2(0f, 1f);
        tickerStrip.rectTransform.anchorMax = new Vector2(1f, 1f);
        tickerStrip.rectTransform.pivot = new Vector2(0.5f, 1f);
        tickerStrip.rectTransform.anchoredPosition = Vector2.zero;
        tickerStrip.rectTransform.sizeDelta = new Vector2(0f, 28f);

        ui.tickerText = ScreamerUIStyle.Txt(tickerStrip.transform, "TickerText", "", 17,
            ScreamerPalette.ScreamYellow, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Stretch(ui.tickerText.rectTransform);

        // ---------- Right rail roster ----------
        ui.rosterPlate = ScreamerUIStyle.Plate(lobby, "RosterRail",
            new Vector2(1f, 0.5f), new Vector2(-24f, 60f), new Vector2(380f, 480f));

        ui.rosterRows = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(ui.rosterPlate, "Rows"),
            new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(340f, 400f));

        // Sibling of the rows rail, never its child: RebuildRosterRows destroys
        // every child of rosterRows, and the AddBot control must survive that.
        ui.addBotButton = ScreamerUIStyle.Btn(ui.rosterPlate, "AddBot", GameCopy.AddBot,
            new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(340f, 34f));
        ui.addBotButton.transform.parent.gameObject.SetActive(false);

        // ---------- Ready / start ----------
        ui.readyButton = ScreamerUIStyle.Btn(lobby, "ReadyButton", GameCopy.ReadyUp,
            new Vector2(0.5f, 0f), new Vector2(0f, 190f), new Vector2(340f, 64f));
        ui.readyButtonLabel = ui.readyButton.GetComponentInChildren<Text>();

        // ---------- Room code card ----------
        RectTransform codePlate = ScreamerUIStyle.Plate(lobby, "RoomCode",
            new Vector2(0.5f, 0f), new Vector2(0f, 64f), new Vector2(560f, 110f));

        ui.roomCodeHeaderText = ScreamerUIStyle.Header(codePlate, "Header", GameCopy.RoomCodeHeader, 15,
            ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.6f), TextAnchor.UpperCenter);
        ScreamerUIStyle.Place(ui.roomCodeHeaderText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(520f, 20f));

        ui.roomCodeText = ScreamerUIStyle.Header(codePlate, "Code", "", 42,
            ScreamerPalette.InkBlack, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(ui.roomCodeText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(-50f, -8f), new Vector2(400f, 54f));

        ui.copyButton = ScreamerUIStyle.Btn(codePlate, "Copy", GameCopy.CopyCode,
            new Vector2(1f, 0.5f), new Vector2(-16f, -8f), new Vector2(96f, 44f));
        ui.copyButtonLabel = ui.copyButton.GetComponentInChildren<Text>();

        ui.inviteButton = ScreamerUIStyle.Btn(lobby, "Invite", GameCopy.InviteFriends,
            new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(280f, 44f));
        ui.inviteButton.transform.parent.gameObject.SetActive(false);

        // ---------- Countdown numbers ----------
        RectTransform countdown = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(root, "CountdownRoot"));
        ui.countdownRoot = countdown.gameObject;

        ui.countdownText = ScreamerUIStyle.Header(countdown, "Number", "", 320,
            ScreamerPalette.ScreamYellow, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(ui.countdownText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700f, 420f));
        ui.countdownRect = ui.countdownText.rectTransform;

        return ui;
    }
}
