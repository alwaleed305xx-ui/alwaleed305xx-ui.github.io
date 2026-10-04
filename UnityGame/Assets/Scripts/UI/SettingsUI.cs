using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The tabbed settings panel (GDD 7.5): AUDIO / VIDEO / CONTROLS / GAMEPLAY on
/// one 900x620 cream plate, reachable from both the main menu and pause.
/// Every control applies live - it writes straight into ScreamerSettings and
/// saves, and ScreamerSettings.OnChanged fans the change out to every system.
/// Rebinding is read-only by design (cut from v1).
/// </summary>
public class SettingsUI : MonoBehaviour
{
    public static SettingsUI Instance { get; private set; }

    [Header("Roots")]
    public GameObject settingsRoot;
    public RectTransform panelContainer;

    [Header("Tabs")]
    public Button audioTabButton;
    public Button videoTabButton;
    public Button controlsTabButton;
    public Button gameplayTabButton;
    public GameObject audioPanel;
    public GameObject videoPanel;
    public GameObject controlsPanel;
    public GameObject gameplayPanel;
    public Button backButton;

    [Header("Audio")]
    public Slider masterSlider;
    public Slider sfxSlider;
    public Slider musicSlider;
    public Slider ambienceSlider;
    public Slider voiceSlider;

    [Header("Video")]
    public Dropdown resolutionDropdown;
    public Dropdown fullscreenDropdown;
    public Toggle vsyncToggle;
    public Dropdown qualityDropdown;
    public Slider fovSlider;
    public Text fovValueText;
    public Slider fxSlider;
    public Text fxValueText;
    public Slider gammaSlider;
    public Image calibrationImage;

    [Header("Controls")]
    public Slider sensitivitySlider;
    public Text sensitivityValueText;
    public Toggle invertToggle;

    [Header("Gameplay")]
    public Slider shakeSlider;
    public Text shakeValueText;
    public Toggle colorblindToggle;

    public bool IsOpen => settingsRoot != null && settingsRoot.activeSelf;

    /// <summary>Frame on which the panel last closed, so sibling screens can ignore the same Esc.</summary>
    public static int LastCloseFrame { get; private set; } = -1;

    readonly List<Vector2Int> resolutionList = new List<Vector2Int>();
    bool suppressCallbacks;

    void Awake()
    {
        Instance = this;
        InitVisuals();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        InitVisuals();
        WireTabs();
        WireAudio();
        WireVideo();
        WireControls();
        WireGameplay();
        ShowTab(audioPanel);
    }

    void InitVisuals()
    {
        ScreamerUIStyle.ApplyFonts(gameObject);
        if (calibrationImage != null) calibrationImage.sprite = ScreamerUIStyle.CalibrationGhost();
        if (settingsRoot != null) settingsRoot.SetActive(false);
    }

    void Update()
    {
        if (IsOpen && Input.GetKeyDown(KeyCode.Escape))
            Close();
    }

    public void Open()
    {
        if (settingsRoot == null) return;
        RefreshFromSettings();
        settingsRoot.SetActive(true);
        if (panelContainer != null) StartCoroutine(ScreamerUIStyle.PopIn(panelContainer));
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void Close()
    {
        if (settingsRoot != null) settingsRoot.SetActive(false);
        LastCloseFrame = Time.frameCount;
    }

    // ------------------------- Wiring -------------------------

    void WireTabs()
    {
        if (audioTabButton != null) audioTabButton.onClick.AddListener(() => ShowTab(audioPanel));
        if (videoTabButton != null) videoTabButton.onClick.AddListener(() => ShowTab(videoPanel));
        if (controlsTabButton != null) controlsTabButton.onClick.AddListener(() => ShowTab(controlsPanel));
        if (gameplayTabButton != null) gameplayTabButton.onClick.AddListener(() => ShowTab(gameplayPanel));
        if (backButton != null) backButton.onClick.AddListener(Close);

        WireFeel(audioTabButton); WireFeel(videoTabButton); WireFeel(controlsTabButton);
        WireFeel(gameplayTabButton); WireFeel(backButton);
    }

    static void WireFeel(Button button)
    {
        if (button == null) return;
        RectTransform scaleTarget = button.transform.parent as RectTransform;
        ScreamerUIStyle.WireButtonFeel(button, scaleTarget != null ? scaleTarget : (RectTransform)button.transform,
            button.GetComponentInChildren<Text>());
    }

    void ShowTab(GameObject tab)
    {
        if (audioPanel != null) audioPanel.SetActive(tab == audioPanel);
        if (videoPanel != null) videoPanel.SetActive(tab == videoPanel);
        if (controlsPanel != null) controlsPanel.SetActive(tab == controlsPanel);
        if (gameplayPanel != null) gameplayPanel.SetActive(tab == gameplayPanel);
    }

    void WireAudio()
    {
        Bind(masterSlider, v => { ScreamerSettings.Instance.MasterVolume = v; });
        Bind(sfxSlider, v => { ScreamerSettings.Instance.SfxVolume = v; });
        Bind(musicSlider, v => { ScreamerSettings.Instance.MusicVolume = v; });
        Bind(ambienceSlider, v => { ScreamerSettings.Instance.AmbienceVolume = v; });
        Bind(voiceSlider, v => { ScreamerSettings.Instance.VoiceVolume = v; });
    }

    void WireVideo()
    {
        BuildResolutionOptions();
        BuildFixedOptions();

        if (resolutionDropdown != null)
            resolutionDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressCallbacks || index < 0 || index >= resolutionList.Count) return;
                Vector2Int res = resolutionList[index];
                ScreamerSettings s = ScreamerSettings.Instance;
                s.ResolutionWidth = res.x;
                s.ResolutionHeight = res.y;
                Screen.SetResolution(res.x, res.y, (FullScreenMode)s.FullscreenMode);
                s.Save();
            });

        if (fullscreenDropdown != null)
            fullscreenDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressCallbacks) return;
                ScreamerSettings s = ScreamerSettings.Instance;
                s.FullscreenMode = (int)ModeFromOption(index);
                Screen.fullScreenMode = ModeFromOption(index);
                s.Save();
            });

        if (vsyncToggle != null)
            vsyncToggle.onValueChanged.AddListener(on =>
            {
                if (suppressCallbacks) return;
                ScreamerSettings.Instance.VSync = on;
                ScreamerSettings.Instance.Save();
            });

        if (qualityDropdown != null)
            qualityDropdown.onValueChanged.AddListener(index =>
            {
                if (suppressCallbacks) return;
                ScreamerSettings.Instance.QualityTier = index;
                ScreamerSettings.Instance.Save();
            });

        Bind(fovSlider, v =>
        {
            ScreamerSettings.Instance.Fov = v;
            if (fovValueText != null) fovValueText.text = ((int)v).ToString();
        });

        Bind(fxSlider, v =>
        {
            ScreamerSettings.Instance.ScreenEffects = v;
            if (fxValueText != null) fxValueText.text = (int)(v * 100f) + "%";
        });

        Bind(gammaSlider, v => { ScreamerSettings.Instance.Gamma = v; });
    }

    void WireControls()
    {
        Bind(sensitivitySlider, v =>
        {
            ScreamerSettings.Instance.MouseSensitivity = v;
            if (sensitivityValueText != null) sensitivityValueText.text = v.ToString("0.0");
        });

        if (invertToggle != null)
            invertToggle.onValueChanged.AddListener(on =>
            {
                if (suppressCallbacks) return;
                ScreamerSettings.Instance.InvertY = on;
                ScreamerSettings.Instance.Save();
            });
    }

    void WireGameplay()
    {
        Bind(shakeSlider, v =>
        {
            ScreamerSettings.Instance.ScreenShake = v;
            if (shakeValueText != null)
                shakeValueText.text = v >= 1.995f ? GameCopy.ShakeMaxLabel : (int)(v * 100f) + "%";
        });

        if (colorblindToggle != null)
            colorblindToggle.onValueChanged.AddListener(on =>
            {
                if (suppressCallbacks) return;
                ScreamerSettings.Instance.ColorblindPalette = on;
                ScreamerSettings.Instance.Save();
            });
    }

    void Bind(Slider slider, System.Action<float> apply)
    {
        if (slider == null) return;
        slider.onValueChanged.AddListener(v =>
        {
            if (suppressCallbacks || ScreamerSettings.Instance == null) return;
            apply(v);
            ScreamerSettings.Instance.Save();
        });
    }

    // ------------------------- Options -------------------------

    void BuildResolutionOptions()
    {
        if (resolutionDropdown == null) return;

        resolutionList.Clear();
        var options = new List<Dropdown.OptionData>();
        foreach (Resolution res in Screen.resolutions)
        {
            var size = new Vector2Int(res.width, res.height);
            if (resolutionList.Contains(size)) continue; // collapse refresh-rate duplicates
            resolutionList.Add(size);
            options.Add(new Dropdown.OptionData(size.x + " x " + size.y));
        }
        if (resolutionList.Count == 0)
        {
            resolutionList.Add(new Vector2Int(Screen.width, Screen.height));
            options.Add(new Dropdown.OptionData(Screen.width + " x " + Screen.height));
        }
        resolutionDropdown.options = options;
    }

    void BuildFixedOptions()
    {
        if (fullscreenDropdown != null)
            fullscreenDropdown.options = new List<Dropdown.OptionData>
            {
                new Dropdown.OptionData("FULLSCREEN"),
                new Dropdown.OptionData("BORDERLESS"),
                new Dropdown.OptionData("WINDOWED")
            };

        if (qualityDropdown != null)
            qualityDropdown.options = new List<Dropdown.OptionData>
            {
                new Dropdown.OptionData(GameCopy.QualityLow),
                new Dropdown.OptionData(GameCopy.QualityMedium),
                new Dropdown.OptionData(GameCopy.QualityCozy)
            };
    }

    static FullScreenMode ModeFromOption(int index)
    {
        switch (index)
        {
            case 0: return FullScreenMode.ExclusiveFullScreen;
            case 2: return FullScreenMode.Windowed;
            default: return FullScreenMode.FullScreenWindow;
        }
    }

    static int OptionFromMode(FullScreenMode mode)
    {
        switch (mode)
        {
            case FullScreenMode.ExclusiveFullScreen: return 0;
            case FullScreenMode.Windowed: return 2;
            default: return 1;
        }
    }

    /// <summary>Pushes the stored settings into every control without firing callbacks.</summary>
    void RefreshFromSettings()
    {
        ScreamerSettings s = ScreamerSettings.Instance;
        if (s == null) return;

        suppressCallbacks = true;

        if (masterSlider != null) masterSlider.SetValueWithoutNotify(s.MasterVolume);
        if (sfxSlider != null) sfxSlider.SetValueWithoutNotify(s.SfxVolume);
        if (musicSlider != null) musicSlider.SetValueWithoutNotify(s.MusicVolume);
        if (ambienceSlider != null) ambienceSlider.SetValueWithoutNotify(s.AmbienceVolume);
        if (voiceSlider != null) voiceSlider.SetValueWithoutNotify(s.VoiceVolume);

        if (resolutionDropdown != null)
        {
            int current = resolutionList.IndexOf(new Vector2Int(Screen.width, Screen.height));
            if (current < 0 && s.ResolutionWidth > 0)
                current = resolutionList.IndexOf(new Vector2Int(s.ResolutionWidth, s.ResolutionHeight));
            if (current < 0) current = Mathf.Max(0, resolutionList.Count - 1);
            resolutionDropdown.SetValueWithoutNotify(current);
            resolutionDropdown.RefreshShownValue();
        }
        if (fullscreenDropdown != null)
        {
            fullscreenDropdown.SetValueWithoutNotify(OptionFromMode((FullScreenMode)s.FullscreenMode));
            fullscreenDropdown.RefreshShownValue();
        }
        if (vsyncToggle != null) vsyncToggle.SetIsOnWithoutNotify(s.VSync);
        if (qualityDropdown != null)
        {
            qualityDropdown.SetValueWithoutNotify(s.QualityTier);
            qualityDropdown.RefreshShownValue();
        }
        if (fovSlider != null) fovSlider.SetValueWithoutNotify(s.Fov);
        if (fovValueText != null) fovValueText.text = ((int)s.Fov).ToString();
        if (fxSlider != null) fxSlider.SetValueWithoutNotify(s.ScreenEffects);
        if (fxValueText != null) fxValueText.text = (int)(s.ScreenEffects * 100f) + "%";
        if (gammaSlider != null) gammaSlider.SetValueWithoutNotify(s.Gamma);

        if (sensitivitySlider != null) sensitivitySlider.SetValueWithoutNotify(s.MouseSensitivity);
        if (sensitivityValueText != null) sensitivityValueText.text = s.MouseSensitivity.ToString("0.0");
        if (invertToggle != null) invertToggle.SetIsOnWithoutNotify(s.InvertY);

        if (shakeSlider != null) shakeSlider.SetValueWithoutNotify(s.ScreenShake);
        if (shakeValueText != null)
            shakeValueText.text = s.ScreenShake >= 1.995f ? GameCopy.ShakeMaxLabel : (int)(s.ScreenShake * 100f) + "%";
        if (colorblindToggle != null) colorblindToggle.SetIsOnWithoutNotify(s.ColorblindPalette);

        suppressCallbacks = false;
    }

    // ------------------------- Construction (called by UiFactory) -------------------------

    public static SettingsUI Build(Transform canvasRoot)
    {
        RectTransform root = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(canvasRoot, "SettingsUI"));
        SettingsUI ui = root.gameObject.AddComponent<SettingsUI>();

        RectTransform settingsRoot = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(root, "SettingsRoot"));
        ui.settingsRoot = settingsRoot.gameObject;

        // Click-blocking dim behind the panel.
        Image blocker = ScreamerUIStyle.Img(settingsRoot, "Blocker", ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.45f));
        ScreamerUIStyle.Stretch(blocker.rectTransform);
        blocker.raycastTarget = true;

        RectTransform plate = ScreamerUIStyle.Plate(settingsRoot, "SettingsPanel",
            new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(900f, 620f));
        ui.panelContainer = (RectTransform)plate.parent;

        // ---------- Tabs ----------
        ui.audioTabButton = TabButton(plate, GameCopy.TabAudio, 0);
        ui.videoTabButton = TabButton(plate, GameCopy.TabVideo, 1);
        ui.controlsTabButton = TabButton(plate, GameCopy.TabControls, 2);
        ui.gameplayTabButton = TabButton(plate, GameCopy.TabGameplay, 3);

        ui.backButton = ScreamerUIStyle.Btn(plate, "Back", GameCopy.MenuBack,
            new Vector2(0.5f, 0f), new Vector2(0f, 16f), new Vector2(220f, 48f));

        // ---------- AUDIO ----------
        RectTransform audio = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(plate, "AudioPanel"));
        ui.audioPanel = audio.gameObject;
        ui.masterSlider = SliderRow(audio, GameCopy.SettingMaster, 0, 0f, 1f, out _);
        ui.sfxSlider = SliderRow(audio, GameCopy.SettingSfx, 1, 0f, 1f, out _);
        ui.musicSlider = SliderRow(audio, GameCopy.SettingMusic, 2, 0f, 1f, out _);
        ui.ambienceSlider = SliderRow(audio, GameCopy.SettingAmbience, 3, 0f, 1f, out _);
        ui.voiceSlider = SliderRow(audio, GameCopy.SettingScreamVolume, 4, 0f, 1f, out _);
        Caption(audio, GameCopy.ScreamVolumeCaption, 4);

        // ---------- VIDEO ----------
        RectTransform video = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(plate, "VideoPanel"));
        ui.videoPanel = video.gameObject;
        ui.resolutionDropdown = DropdownRow(video, GameCopy.SettingResolution, 0);
        ui.fullscreenDropdown = DropdownRow(video, GameCopy.SettingFullscreen, 1);
        ui.vsyncToggle = ToggleRow(video, GameCopy.SettingVsync, 2);
        ui.qualityDropdown = DropdownRow(video, GameCopy.SettingQuality, 3);
        ui.fovSlider = SliderRow(video, GameCopy.SettingFov, 4, 60f, 100f, out ui.fovValueText);
        ui.fxSlider = SliderRow(video, GameCopy.SettingScreenEffects, 5, 0f, 1f, out ui.fxValueText);
        ui.gammaSlider = SliderRow(video, GameCopy.SettingGamma, 6, -0.5f, 0.5f, out _);

        // Gamma calibration: the barely-visible little guy + his caption.
        // Near-neutral tint so the sprite's own 3% contrast stays honest.
        Image calibration = ScreamerUIStyle.Img(video, "Calibration", new Color(0.98f, 0.98f, 0.97f, 1f));
        ScreamerUIStyle.Place(calibration.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, RowY(6) - 6f), new Vector2(72f, 72f));
        calibration.rectTransform.pivot = new Vector2(1f, 1f);
        ui.calibrationImage = calibration;
        Caption(video, GameCopy.GammaCaption, 7);

        // ---------- CONTROLS ----------
        RectTransform controls = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(plate, "ControlsPanel"));
        ui.controlsPanel = controls.gameObject;
        ui.sensitivitySlider = SliderRow(controls, GameCopy.SettingSensitivity, 0, 0.5f, 5f, out ui.sensitivityValueText);
        ui.invertToggle = ToggleRow(controls, GameCopy.SettingInvertY, 1);

        Text keys = ScreamerUIStyle.Txt(controls, "KeyReference", GameCopy.KeyReference, 17,
            ScreamerPalette.InkBlack, TextAnchor.UpperLeft);
        ScreamerUIStyle.Place(keys.rectTransform, new Vector2(0f, 1f), new Vector2(40f, RowY(2)), new Vector2(820f, 320f));
        keys.rectTransform.pivot = new Vector2(0f, 1f);

        // ---------- GAMEPLAY ----------
        RectTransform gameplay = ScreamerUIStyle.Stretch(ScreamerUIStyle.Rect(plate, "GameplayPanel"));
        ui.gameplayPanel = gameplay.gameObject;
        ui.shakeSlider = SliderRow(gameplay, GameCopy.SettingShake, 0, 0f, 2f, out ui.shakeValueText);
        ui.colorblindToggle = ToggleRow(gameplay, GameCopy.SettingColorblind, 1);

        return ui;
    }

    // ------------------------- Row builders -------------------------

    static float RowY(int index) => -96f - index * 56f;

    static Button TabButton(RectTransform plate, string label, int index)
    {
        return ScreamerUIStyle.Btn(plate, "Tab_" + label, label,
            new Vector2(0f, 1f), new Vector2(34f + index * 182f, -16f), new Vector2(172f, 44f));
    }

    static Text RowLabel(RectTransform panel, string label, int index)
    {
        Text text = ScreamerUIStyle.Header(panel, "Label_" + label, label, 18,
            ScreamerPalette.InkBlack, TextAnchor.MiddleLeft);
        ScreamerUIStyle.Place(text.rectTransform, new Vector2(0f, 1f), new Vector2(40f, RowY(index)), new Vector2(300f, 32f));
        text.rectTransform.pivot = new Vector2(0f, 1f);
        return text;
    }

    static Slider SliderRow(RectTransform panel, string label, int index, float min, float max, out Text valueText)
    {
        RowLabel(panel, label, index);
        Slider slider = ScreamerUIStyle.HSlider(panel, "Slider_" + label,
            new Vector2(0f, 1f), new Vector2(360f, RowY(index)), new Vector2(380f, 28f), min, max, min);
        ((RectTransform)slider.transform).pivot = new Vector2(0f, 1f);

        valueText = ScreamerUIStyle.Txt(panel, "Value_" + label, "", 17,
            ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.7f), TextAnchor.MiddleLeft);
        ScreamerUIStyle.Place(valueText.rectTransform, new Vector2(0f, 1f), new Vector2(756f, RowY(index)), new Vector2(100f, 28f));
        valueText.rectTransform.pivot = new Vector2(0f, 1f);
        return slider;
    }

    static Dropdown DropdownRow(RectTransform panel, string label, int index)
    {
        RowLabel(panel, label, index);
        Dropdown dropdown = ScreamerUIStyle.MakeDropdown(panel, "Dropdown_" + label,
            new Vector2(0f, 1f), new Vector2(360f, RowY(index)), new Vector2(380f, 36f));
        ((RectTransform)dropdown.transform).pivot = new Vector2(0f, 1f);
        return dropdown;
    }

    static Toggle ToggleRow(RectTransform panel, string label, int index)
    {
        RowLabel(panel, label, index);
        Toggle toggle = ScreamerUIStyle.MakeToggle(panel, "Toggle_" + label, "",
            new Vector2(0f, 1f), new Vector2(360f, RowY(index)), true);
        ((RectTransform)toggle.transform).pivot = new Vector2(0f, 1f);
        return toggle;
    }

    static void Caption(RectTransform panel, string caption, int index)
    {
        Text text = ScreamerUIStyle.Txt(panel, "Caption" + index, caption, 15,
            ScreamerUIStyle.WithAlpha(ScreamerPalette.InkBlack, 0.6f), TextAnchor.MiddleLeft);
        ScreamerUIStyle.Place(text.rectTransform, new Vector2(0f, 1f), new Vector2(360f, RowY(index) - 32f), new Vector2(480f, 24f));
        text.rectTransform.pivot = new Vector2(0f, 1f);
        text.fontStyle = FontStyle.Italic;
    }
}
