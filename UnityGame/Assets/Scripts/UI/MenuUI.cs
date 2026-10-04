using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The main menu (GDD 7.1), drawn over the live den at dusk while
/// MenuCameraDolly glides the camera around. Rattling title, the left button
/// stack (PLAY / HOW TO DIE / SETTINGS / SNEAK AWAY), host/join/practice
/// flows through BackendSelector.Active, the rules page, the quit confirm,
/// the version footer and the rotating sticky-note patch notes.
///
/// Shows itself whenever no network session is running and gets out of the
/// way the moment one starts (LobbyUI takes over).
/// </summary>
public class MenuUI : MonoBehaviour
{
    public static MenuUI Instance { get; private set; }

    [Header("Roots")]
    public GameObject menuRoot;
    public GameObject rootPanel;
    public GameObject playPanel;
    public GameObject howToDiePanel;
    public GameObject quitConfirmPanel;

    [Header("Title & chrome")]
    public RectTransform titleRect;
    public Text titleText;
    public Text statusText;
    public Text versionText;
    public RectTransform stickyRoot;
    public Text stickyText;

    [Header("Root buttons")]
    public Button playButton;
    public Button howToDieButton;
    public Button settingsButton;
    public Button quitButton;

    [Header("Play panel")]
    public Button hostButton;
    public Button joinButton;
    public Button practiceButton;
    public Button playBackButton;
    public InputField joinAddressInput;
    public Text joinFieldLabel;

    [Header("Sub-panel back buttons")]
    public Button howBackButton;
    public Button quitYesButton;
    public Button quitNoButton;

    static bool intentionalDisconnect;

    bool wasListening;
    int stickyIndex;

    void Awake()
    {
        Instance = this;
        ScreamerUIStyle.ApplyFonts(gameObject);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        WireButtons();

        if (versionText != null)
            versionText.text = GameCopy.VersionFooter(Application.version);

        ShowRootPanel();
        SetMenuVisible(!IsSessionRunning());
        StartCoroutine(TitleRattleLoop());
        StartCoroutine(StickyNoteLoop());
    }

    void WireButtons()
    {
        if (playButton != null) playButton.onClick.AddListener(ShowPlayPanel);
        if (howToDieButton != null) howToDieButton.onClick.AddListener(ShowHowToDiePanel);
        if (settingsButton != null) settingsButton.onClick.AddListener(() => SettingsUI.Instance?.Open());
        if (quitButton != null) quitButton.onClick.AddListener(ShowQuitConfirm);

        if (hostButton != null) hostButton.onClick.AddListener(Host);
        if (joinButton != null) joinButton.onClick.AddListener(Join);
        if (practiceButton != null) practiceButton.onClick.AddListener(Practice);
        if (playBackButton != null) playBackButton.onClick.AddListener(ShowRootPanel);

        if (howBackButton != null) howBackButton.onClick.AddListener(ShowRootPanel);
        if (quitYesButton != null) quitYesButton.onClick.AddListener(() => Application.Quit());
        if (quitNoButton != null) quitNoButton.onClick.AddListener(ShowRootPanel);

        WireFeel(playButton); WireFeel(howToDieButton); WireFeel(settingsButton); WireFeel(quitButton);
        WireFeel(hostButton); WireFeel(joinButton); WireFeel(practiceButton); WireFeel(playBackButton);
        WireFeel(howBackButton); WireFeel(quitYesButton); WireFeel(quitNoButton);
    }

    static void WireFeel(Button button)
    {
        if (button == null) return;
        RectTransform scaleTarget = button.transform.parent as RectTransform;
        Text label = button.GetComponentInChildren<Text>();
        ScreamerUIStyle.WireButtonFeel(button, scaleTarget != null ? scaleTarget : (RectTransform)button.transform, label);
    }

    void Update()
    {
        bool listening = IsSessionRunning();
        if (listening != wasListening)
        {
            if (listening)
            {
                SetMenuVisible(false);
            }
            else
            {
                SetMenuVisible(true);
                ShowRootPanel();
                if (!intentionalDisconnect) SetStatus(GameCopy.HostQuit);
                intentionalDisconnect = false;
            }
            wasListening = listening;
        }

        if (menuRoot != null && menuRoot.activeSelf)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            bool settingsBusy = SettingsUI.Instance != null &&
                (SettingsUI.Instance.IsOpen || SettingsUI.LastCloseFrame == Time.frameCount);
            if (Input.GetKeyDown(KeyCode.Escape) && !settingsBusy)
            {
                if (quitConfirmPanel != null && quitConfirmPanel.activeSelf) ShowRootPanel();
                else if ((playPanel != null && playPanel.activeSelf) ||
                         (howToDiePanel != null && howToDiePanel.activeSelf)) ShowRootPanel();
            }
        }
    }

    static bool IsSessionRunning() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    /// <summary>
    /// Screens that shut the session down on purpose (pause quit, results
    /// sneak-away) call this first so the menu does not accuse the host.
    /// </summary>
    public static void NotifyIntentionalDisconnect() => intentionalDisconnect = true;

    void SetMenuVisible(bool visible)
    {
        if (menuRoot != null) menuRoot.SetActive(visible);
        if (MenuCameraDolly.Instance != null) MenuCameraDolly.Instance.enabled = visible;
        if (visible)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void SetStatus(string line) => SetText(statusText, line);

    static void SetText(Text t, string s)
    {
        if (t != null) t.text = s ?? "";
    }

    // ------------------------- Panel switching -------------------------

    void ShowRootPanel() => SwitchPanel(rootPanel);
    void ShowPlayPanel() => SwitchPanel(playPanel);
    void ShowHowToDiePanel() => SwitchPanel(howToDiePanel);
    void ShowQuitConfirm() => SwitchPanel(quitConfirmPanel);

    void SwitchPanel(GameObject target)
    {
        if (rootPanel != null) rootPanel.SetActive(target == rootPanel);
        if (playPanel != null) playPanel.SetActive(target == playPanel);
        if (howToDiePanel != null) howToDiePanel.SetActive(target == howToDiePanel);
        if (quitConfirmPanel != null) quitConfirmPanel.SetActive(target == quitConfirmPanel);

        if (target != null && target != rootPanel)
        {
            var rt = target.transform as RectTransform;
            if (rt != null) StartCoroutine(ScreamerUIStyle.PopIn(rt));
        }
    }

    // ------------------------- Session flows -------------------------

    void Host()
    {
        SetStatus("");
        if (!BackendSelector.Active.Host())
            SetStatus(GameCopy.HostFailed);
        // Success is handled by the listening watcher - the menu simply leaves.
    }

    void Join()
    {
        string address = joinAddressInput != null ? joinAddressInput.text.Trim() : "";
        if (string.IsNullOrEmpty(address)) address = "127.0.0.1:7777";

        SetStatus(BackendSelector.Active.Join(address) ? GameCopy.Joining : GameCopy.JoinFailed);
    }

    void Practice()
    {
        // Practice is a genuinely offline host (GDD 7.1): the transport binds
        // to loopback so nobody on the LAN can wander in, while bots fill to
        // four after ten seconds and a bot monster is allowed - one human
        // still gets a complete round. On the Steam backend, practice stays a
        // friends-only Steam lobby (already private to the friends list).
        if (BackendSelector.Active is UtpBackend utp)
            utp.HostLocalOnly = true;
        Host();
    }

    // ------------------------- Ambience -------------------------

    IEnumerator TitleRattleLoop()
    {
        while (true)
        {
            yield return new WaitForSecondsRealtime(Random.Range(6.5f, 9.5f));
            if (menuRoot == null || !menuRoot.activeSelf || titleRect == null) continue;

            AudioDirector.Instance?.PlayUI(Sfx.Scream, 0.2f);
            Vector2 basePos = titleRect.anchoredPosition;
            float t = 0f;
            while (t < 0.3f)
            {
                t += Time.unscaledDeltaTime;
                titleRect.anchoredPosition = basePos + Random.insideUnitCircle * 3f;
                yield return null;
            }
            titleRect.anchoredPosition = basePos;
        }
    }

    IEnumerator StickyNoteLoop()
    {
        while (true)
        {
            if (stickyText != null && GameCopy.StickyPatchNotes.Length > 0)
            {
                SetText(stickyText, GameCopy.StickyPatchNotes[stickyIndex % GameCopy.StickyPatchNotes.Length]);
                stickyIndex++;
            }
            yield return new WaitForSecondsRealtime(12f);
        }
    }

    // ------------------------- Construction (called by UiFactory) -------------------------

    public static MenuUI Build(Transform canvasRoot)
    {
        RectTransform root = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(canvasRoot, "MenuUI"));
        MenuUI ui = root.gameObject.AddComponent<MenuUI>();

        RectTransform menu = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(root, "MenuRoot"));
        ui.menuRoot = menu.gameObject;

        // Title, rattling in ScreamYellow over the live den.
        ui.titleText = ScreamerUIStyle.Header(menu, "Title", GameCopy.TitleWordmark, 140,
            ScreamerPalette.ScreamYellow, TextAnchor.MiddleCenter);
        ui.titleRect = ui.titleText.rectTransform;
        ScreamerUIStyle.Place(ui.titleRect, new Vector2(0.5f, 0.78f), Vector2.zero, new Vector2(1400f, 170f));

        // Status line under the title (host failed / host quit / joining).
        ui.statusText = ScreamerUIStyle.Txt(menu, "Status", "", 20, ScreamerPalette.NoodleCream, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(ui.statusText.rectTransform, new Vector2(0.5f, 0.64f), Vector2.zero, new Vector2(1000f, 30f));

        // Version footer, bottom-left.
        ui.versionText = ScreamerUIStyle.Txt(menu, "Version", "", 18,
            ScreamerUIStyle.WithAlpha(ScreamerPalette.NoodleCream, 0.6f), TextAnchor.LowerLeft);
        ScreamerUIStyle.Place(ui.versionText.rectTransform, new Vector2(0f, 0f), new Vector2(24f, 18f), new Vector2(900f, 26f));
        ui.versionText.rectTransform.pivot = new Vector2(0f, 0f);

        // Sticky note, bottom-right, tilted.
        RectTransform sticky = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(menu, "StickyNote"),
            new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(160f, 120f));
        sticky.pivot = new Vector2(1f, 0f);
        sticky.localEulerAngles = new Vector3(0f, 0f, -4f);
        ui.stickyRoot = sticky;
        Image stickyBg = ScreamerUIStyle.Img(sticky, "Paper", ScreamerPalette.ScreamYellow);
        ScreamerUIStyle.Stretch(stickyBg.rectTransform);
        ui.stickyText = ScreamerUIStyle.Txt(sticky, "Note", "", 14, ScreamerPalette.InkBlack, TextAnchor.UpperLeft);
        ScreamerUIStyle.Stretch(ui.stickyText.rectTransform);
        ui.stickyText.rectTransform.offsetMin = new Vector2(10f, 8f);
        ui.stickyText.rectTransform.offsetMax = new Vector2(-10f, -10f);

        // ---------- Root button stack ----------
        RectTransform rootStack = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(menu, "RootPanel"),
            new Vector2(0.18f, 0.45f), Vector2.zero, new Vector2(340f, 320f));
        ui.rootPanel = rootStack.gameObject;

        ui.playButton = StackButton(rootStack, GameCopy.MenuPlay, 0);
        ui.howToDieButton = StackButton(rootStack, GameCopy.MenuHowToDie, 1);
        ui.settingsButton = StackButton(rootStack, GameCopy.MenuSettings, 2);
        ui.quitButton = StackButton(rootStack, GameCopy.MenuQuit, 3);

        // ---------- Play sub-panel ----------
        RectTransform playStack = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(menu, "PlayPanel"),
            new Vector2(0.18f, 0.45f), Vector2.zero, new Vector2(340f, 400f));
        ui.playPanel = playStack.gameObject;

        ui.hostButton = StackButton(playStack, GameCopy.MenuHost, 0);
        ui.joinButton = StackButton(playStack, GameCopy.MenuJoin, 1);

        ui.joinFieldLabel = ScreamerUIStyle.Header(playStack, "JoinLabel", GameCopy.JoinFieldLabel, 14,
            ScreamerPalette.ScreamYellow, TextAnchor.MiddleLeft);
        ScreamerUIStyle.Place(ui.joinFieldLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -2f * 78f), new Vector2(340f, 20f));

        ui.joinAddressInput = ScreamerUIStyle.MakeInput(playStack, "JoinAddress", GameCopy.JoinFieldPlaceholder,
            new Vector2(0.5f, 1f), new Vector2(0f, -2f * 78f - 24f), new Vector2(340f, 44f));

        ui.practiceButton = StackButton(playStack, GameCopy.MenuPractice, 3);
        ui.playBackButton = StackButton(playStack, GameCopy.MenuBack, 4);

        // ---------- HOW TO DIE ----------
        RectTransform howPlate = ScreamerUIStyle.Plate(menu, "HowToDie",
            new Vector2(0.5f, 0.5f), new Vector2(0f, -20f), new Vector2(1160f, 700f));
        ui.howToDiePanel = howPlate.parent.gameObject;

        ScreamerUIStyle.Place(
            ScreamerUIStyle.Header(howPlate, "Header", GameCopy.HowToDieHeader, 40,
                ScreamerPalette.InkBlack, TextAnchor.MiddleCenter).rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0f, -20f), new Vector2(1100f, 50f));

        Color[] taskChips =
        {
            ScreamerPalette.ScreamYellow, ScreamerPalette.ShagRust, ScreamerPalette.GhostMint,
            ScreamerPalette.NoodleCream, ScreamerPalette.LamplightAmber, ScreamerPalette.HauntedTeal
        };
        for (int i = 0; i < GameCopy.HowToDieLines.Length; i++)
        {
            float y = -100f - i * 78f;

            Image chip = ScreamerUIStyle.Img(howPlate, "Chip" + i, taskChips[i % taskChips.Length]);
            ScreamerUIStyle.Place(chip.rectTransform, new Vector2(0f, 1f), new Vector2(44f, y), new Vector2(40f, 40f));
            chip.rectTransform.pivot = new Vector2(0f, 1f);

            Image chipFrame = ScreamerUIStyle.Img(chip.transform, "Frame", ScreamerPalette.InkBlack);
            ScreamerUIStyle.Stretch(chipFrame.rectTransform);
            chipFrame.rectTransform.offsetMin = new Vector2(-2f, -2f);
            chipFrame.rectTransform.offsetMax = new Vector2(2f, 2f);
            chipFrame.transform.SetAsFirstSibling();

            Text line = ScreamerUIStyle.Txt(howPlate, "Line" + i, GameCopy.HowToDieLines[i], 19,
                ScreamerPalette.InkBlack, TextAnchor.UpperLeft);
            ScreamerUIStyle.Place(line.rectTransform, new Vector2(0f, 1f), new Vector2(104f, y), new Vector2(1010f, 64f));
            line.rectTransform.pivot = new Vector2(0f, 1f);
        }

        Text laws = ScreamerUIStyle.Header(howPlate, "Laws", GameCopy.HowToDieLaws, 22,
            ScreamerPalette.ShagRust, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(laws.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 76f), new Vector2(1100f, 34f));

        ui.howBackButton = ScreamerUIStyle.Btn(howPlate, "Back", GameCopy.MenuBack,
            new Vector2(0.5f, 0f), new Vector2(0f, 18f), new Vector2(220f, 48f));

        // ---------- Quit confirm ----------
        RectTransform quitPlate = ScreamerUIStyle.Plate(menu, "QuitConfirm",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(620f, 220f));
        ui.quitConfirmPanel = quitPlate.parent.gameObject;

        ScreamerUIStyle.Place(
            ScreamerUIStyle.Txt(quitPlate, "Question", GameCopy.QuitConfirm, 26,
                ScreamerPalette.InkBlack, TextAnchor.MiddleCenter).rectTransform,
            new Vector2(0.5f, 1f), new Vector2(0f, -28f), new Vector2(580f, 60f));

        ui.quitYesButton = ScreamerUIStyle.Btn(quitPlate, "Yes", GameCopy.QuitYes,
            new Vector2(0.5f, 0f), new Vector2(-130f, 26f), new Vector2(220f, 52f));
        ui.quitNoButton = ScreamerUIStyle.Btn(quitPlate, "No", GameCopy.QuitNo,
            new Vector2(0.5f, 0f), new Vector2(130f, 26f), new Vector2(220f, 52f));

        return ui;
    }

    /// <summary>One 340x64 button in a left-anchored vertical stack, 14 px spacing.</summary>
    static Button StackButton(RectTransform stack, string label, int index)
    {
        return ScreamerUIStyle.Btn(stack, "Button_" + label, label,
            new Vector2(0.5f, 1f), new Vector2(0f, -index * 78f), new Vector2(340f, 64f));
    }
}
