using UnityEngine;

/// <summary>
/// The one settings singleton. Holds every player preference, persists through
/// PlayerPrefs, and raises <see cref="OnChanged"/> whenever anything changes so
/// every system (camera, audio buses, overlay FX, palette) self-applies live.
///
/// Usage: mutate the public fields, then call <see cref="Save"/>. Values are
/// clamped on both load and save, so a hand-edited registry cannot push the
/// game outside its supported ranges. Created once by UiFactory.BuildAll()
/// and kept alive across the whole session.
/// </summary>
public class ScreamerSettings : MonoBehaviour
{
    public static ScreamerSettings Instance { get; private set; }

    /// <summary>Raised after every Save() (and once after the initial Load()).</summary>
    public static event System.Action OnChanged;

    // ------------------------- Gameplay / controls -------------------------

    /// <summary>Mouse look sensitivity, 0.5..5.0. Default 2.2.</summary>
    public float MouseSensitivity = 2.2f;

    public bool InvertY = false;

    /// <summary>Camera field of view, 60..100. Default 70.</summary>
    public float Fov = 70f;

    /// <summary>Screen shake scale, 0..2. The 200% tick is labeled "YES".</summary>
    public float ScreenShake = 1f;

    /// <summary>Overlay FX (vignette, grain, flashes) scale, 0..1.</summary>
    public float ScreenEffects = 1f;

    /// <summary>0 LOW, 1 MEDIUM, 2 COZY (GDD 8.5).</summary>
    public int QualityTier = 1;

    /// <summary>Swaps survivor colors for the deuteranopia-safe set.</summary>
    public bool ColorblindPalette = false;

    /// <summary>Gamma offset, -0.5..+0.5. Applied by the screen FX overlay.</summary>
    public float Gamma = 0f;

    // ------------------------- Audio (0..1 each) -------------------------

    public float MasterVolume = 1f;
    public float SfxVolume = 1f;
    public float MusicVolume = 1f;
    public float AmbienceVolume = 1f;

    /// <summary>The "Scream Volume" gag slider. Genuinely maps to voice-SFX gain.</summary>
    public float VoiceVolume = 1f;

    // ------------------------- Display (persisted here, applied by SettingsUI) -------------------------

    /// <summary>Saved display width. 0 = never set, keep whatever the OS gave us.</summary>
    public int ResolutionWidth = 0;
    public int ResolutionHeight = 0;

    /// <summary>Cast of UnityEngine.FullScreenMode. Defaults to fullscreen window.</summary>
    public int FullscreenMode = (int)UnityEngine.FullScreenMode.FullScreenWindow;

    public bool VSync = true;

    const string Prefix = "Screamer.";

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        Load();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Reads every preference, clamps it, applies globals, raises OnChanged.</summary>
    public void Load()
    {
        MouseSensitivity = PlayerPrefs.GetFloat(Prefix + "MouseSensitivity", 2.2f);
        InvertY = PlayerPrefs.GetInt(Prefix + "InvertY", 0) != 0;
        Fov = PlayerPrefs.GetFloat(Prefix + "Fov", 70f);
        ScreenShake = PlayerPrefs.GetFloat(Prefix + "ScreenShake", 1f);
        ScreenEffects = PlayerPrefs.GetFloat(Prefix + "ScreenEffects", 1f);
        QualityTier = PlayerPrefs.GetInt(Prefix + "QualityTier", 1);
        ColorblindPalette = PlayerPrefs.GetInt(Prefix + "ColorblindPalette", 0) != 0;
        Gamma = PlayerPrefs.GetFloat(Prefix + "Gamma", 0f);

        MasterVolume = PlayerPrefs.GetFloat(Prefix + "MasterVolume", 1f);
        SfxVolume = PlayerPrefs.GetFloat(Prefix + "SfxVolume", 1f);
        MusicVolume = PlayerPrefs.GetFloat(Prefix + "MusicVolume", 1f);
        AmbienceVolume = PlayerPrefs.GetFloat(Prefix + "AmbienceVolume", 1f);
        VoiceVolume = PlayerPrefs.GetFloat(Prefix + "VoiceVolume", 1f);

        ResolutionWidth = PlayerPrefs.GetInt(Prefix + "ResolutionWidth", 0);
        ResolutionHeight = PlayerPrefs.GetInt(Prefix + "ResolutionHeight", 0);
        FullscreenMode = PlayerPrefs.GetInt(Prefix + "FullscreenMode", (int)UnityEngine.FullScreenMode.FullScreenWindow);
        VSync = PlayerPrefs.GetInt(Prefix + "VSync", 1) != 0;

        Clamp();
        ApplyGlobals();
        OnChanged?.Invoke();
    }

    /// <summary>Clamps, writes every preference, applies globals, raises OnChanged.</summary>
    public void Save()
    {
        Clamp();

        PlayerPrefs.SetFloat(Prefix + "MouseSensitivity", MouseSensitivity);
        PlayerPrefs.SetInt(Prefix + "InvertY", InvertY ? 1 : 0);
        PlayerPrefs.SetFloat(Prefix + "Fov", Fov);
        PlayerPrefs.SetFloat(Prefix + "ScreenShake", ScreenShake);
        PlayerPrefs.SetFloat(Prefix + "ScreenEffects", ScreenEffects);
        PlayerPrefs.SetInt(Prefix + "QualityTier", QualityTier);
        PlayerPrefs.SetInt(Prefix + "ColorblindPalette", ColorblindPalette ? 1 : 0);
        PlayerPrefs.SetFloat(Prefix + "Gamma", Gamma);

        PlayerPrefs.SetFloat(Prefix + "MasterVolume", MasterVolume);
        PlayerPrefs.SetFloat(Prefix + "SfxVolume", SfxVolume);
        PlayerPrefs.SetFloat(Prefix + "MusicVolume", MusicVolume);
        PlayerPrefs.SetFloat(Prefix + "AmbienceVolume", AmbienceVolume);
        PlayerPrefs.SetFloat(Prefix + "VoiceVolume", VoiceVolume);

        PlayerPrefs.SetInt(Prefix + "ResolutionWidth", ResolutionWidth);
        PlayerPrefs.SetInt(Prefix + "ResolutionHeight", ResolutionHeight);
        PlayerPrefs.SetInt(Prefix + "FullscreenMode", FullscreenMode);
        PlayerPrefs.SetInt(Prefix + "VSync", VSync ? 1 : 0);

        PlayerPrefs.Save();
        ApplyGlobals();
        OnChanged?.Invoke();
    }

    void Clamp()
    {
        MouseSensitivity = Mathf.Clamp(MouseSensitivity, 0.5f, 5f);
        Fov = Mathf.Clamp(Fov, 60f, 100f);
        ScreenShake = Mathf.Clamp(ScreenShake, 0f, 2f);
        ScreenEffects = Mathf.Clamp01(ScreenEffects);
        QualityTier = Mathf.Clamp(QualityTier, 0, 2);
        Gamma = Mathf.Clamp(Gamma, -0.5f, 0.5f);

        MasterVolume = Mathf.Clamp01(MasterVolume);
        SfxVolume = Mathf.Clamp01(SfxVolume);
        MusicVolume = Mathf.Clamp01(MusicVolume);
        AmbienceVolume = Mathf.Clamp01(AmbienceVolume);
        VoiceVolume = Mathf.Clamp01(VoiceVolume);
    }

    /// <summary>
    /// Engine-level knobs that belong to nobody else: vsync and the quality
    /// tier's shadow/light budget (GDD 8.5). Particles, grain and flinch rates
    /// read QualityTier themselves through OnChanged.
    /// </summary>
    void ApplyGlobals()
    {
        QualitySettings.vSyncCount = VSync ? 1 : 0;

        switch (QualityTier)
        {
            case 0: // LOW: hard shadows, pixel light count 2, no grain (overlay reads the tier)
                QualitySettings.shadows = ShadowQuality.HardOnly;
                QualitySettings.pixelLightCount = 2;
                break;
            case 2: // COZY: everything on
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.pixelLightCount = 4;
                break;
            default: // MEDIUM: soft shadows, sane light budget
                QualitySettings.shadows = ShadowQuality.All;
                QualitySettings.pixelLightCount = 3;
                break;
        }
    }
}
