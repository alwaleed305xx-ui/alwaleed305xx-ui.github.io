using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The pause overlay (GDD 7.6). [Esc] during a round brings up edge-darkening
/// only (a radial shade at 0.5 alpha - the world stays visible) with
/// RESUME / SETTINGS / ABANDON FRIENDS, which reads FLEE LIKE A COWARD when
/// you are the monster, matching the forfeit logic. It never pauses the
/// server: THE MONSTER DOESN'T PAUSE.
/// </summary>
public class PauseUI : MonoBehaviour
{
    public static PauseUI Instance { get; private set; }

    [Header("Roots")]
    public GameObject pauseRoot;
    public Image shadeImage;
    public RectTransform stackContainer;

    [Header("Buttons")]
    public Button resumeButton;
    public Button settingsButton;
    public Button abandonButton;
    public Text abandonLabel;
    public Text footerText;

    public bool IsOpen => pauseRoot != null && pauseRoot.activeSelf;

    void Awake()
    {
        Instance = this;
        InitVisuals();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void InitVisuals()
    {
        ScreamerUIStyle.ApplyFonts(gameObject);
        if (shadeImage != null) shadeImage.sprite = ScreamerUIStyle.VignetteSprite();
        if (pauseRoot != null) pauseRoot.SetActive(false);
    }

    void Start()
    {
        InitVisuals();
        if (resumeButton != null) resumeButton.onClick.AddListener(Close);
        if (settingsButton != null) settingsButton.onClick.AddListener(() => SettingsUI.Instance?.Open());
        if (abandonButton != null) abandonButton.onClick.AddListener(AbandonToMenu);

        WireFeel(resumeButton);
        WireFeel(settingsButton);
        WireFeel(abandonButton);
    }

    static void WireFeel(Button button)
    {
        if (button == null) return;
        RectTransform scaleTarget = button.transform.parent as RectTransform;
        ScreamerUIStyle.WireButtonFeel(button, scaleTarget != null ? scaleTarget : (RectTransform)button.transform,
            button.GetComponentInChildren<Text>());
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.Escape)) return;

        // Settings swallows its own Esc; we only act when it is closed, and
        // never on the very frame it closed (script order is not guaranteed).
        if (SettingsUI.Instance != null &&
            (SettingsUI.Instance.IsOpen || SettingsUI.LastCloseFrame == Time.frameCount)) return;

        if (IsOpen)
        {
            Close();
        }
        else if (InRound())
        {
            Open();
        }
    }

    static bool InRound()
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening) return false;
        GameManager gm = GameManager.Instance;
        if (gm == null) return false;
        GameManager.GameState s = gm.State.Value;
        return s == GameManager.GameState.Countdown || s == GameManager.GameState.Lockdown ||
               s == GameManager.GameState.Playing || s == GameManager.GameState.Finale;
    }

    public void Open()
    {
        if (pauseRoot == null) return;

        // The forfeit label matches who you are tonight.
        if (abandonLabel != null)
        {
            bool monster = GameManager.Instance != null && GameManager.Instance.IAmMonster;
            abandonLabel.text = monster ? GameCopy.PauseAbandonMonster : GameCopy.PauseAbandon;
        }

        pauseRoot.SetActive(true);
        if (stackContainer != null) StartCoroutine(ScreamerUIStyle.PopIn(stackContainer));
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        if (pauseRoot != null) pauseRoot.SetActive(false);

        // Hand the camera back to the round (the server never stopped).
        if (InRound())
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void AbandonToMenu()
    {
        Close();
        MenuUI.NotifyIntentionalDisconnect();
        BackendSelector.Active.Shutdown();
    }

    // ------------------------- Construction (called by UiFactory) -------------------------

    public static PauseUI Build(Transform canvasRoot)
    {
        RectTransform root = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(canvasRoot, "PauseUI"));
        PauseUI ui = root.gameObject.AddComponent<PauseUI>();

        RectTransform pause = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(root, "PauseRoot"));
        ui.pauseRoot = pause.gameObject;

        // Edge darkening only - the den stays on screen behind it.
        Image shade = ScreamerUIStyle.Img(pause, "Shade", ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.5f));
        ScreamerUIStyle.Stretch(shade.rectTransform);
        shade.raycastTarget = true; // block clicks into the world while paused
        ui.shadeImage = shade;

        RectTransform stack = ScreamerUIStyle.Place(ScreamerUIStyle.Rect(pause, "Stack"),
            new Vector2(0.5f, 0.5f), new Vector2(0f, 30f), new Vector2(340f, 320f));
        ui.stackContainer = stack;

        ui.resumeButton = ScreamerUIStyle.Btn(stack, "Resume", GameCopy.PauseResume,
            new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(340f, 64f));
        ui.settingsButton = ScreamerUIStyle.Btn(stack, "Settings", GameCopy.PauseSettings,
            new Vector2(0.5f, 1f), new Vector2(0f, -78f), new Vector2(340f, 64f));
        ui.abandonButton = ScreamerUIStyle.Btn(stack, "Abandon", GameCopy.PauseAbandon,
            new Vector2(0.5f, 1f), new Vector2(0f, -156f), new Vector2(340f, 64f));
        ui.abandonLabel = ui.abandonButton.GetComponentInChildren<Text>();

        ui.footerText = ScreamerUIStyle.Header(stack, "Footer", GameCopy.PauseFooter, 20,
            ScreamerPalette.NoodleCream, TextAnchor.MiddleCenter);
        ScreamerUIStyle.Place(ui.footerText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -248f), new Vector2(500f, 30f));

        return ui;
    }
}
