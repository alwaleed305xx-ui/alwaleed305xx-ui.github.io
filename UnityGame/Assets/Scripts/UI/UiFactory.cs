using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Builds the complete UI layer in one call: the shared overlay canvas and
/// EventSystem, the ScreamerSettings singleton, every screen (GameUI, LobbyUI,
/// ResultsUI, MenuUI, PauseUI, SettingsUI - bottom to top in that order) and
/// the menu camera dolly. Runtime-compilable: the editor wizard calls it while
/// assembling the scene, and it can equally run at play time for tests.
/// Calling it twice replaces the previous UI cleanly.
/// </summary>
public static class UiFactory
{
    public const string CanvasName = "ScreamerUiCanvas";
    public const string SettingsName = "ScreamerSettings";

    /// <summary>Canvas + EventSystem + every screen + all wiring + ScreamerSettings.</summary>
    public static void BuildAll()
    {
        RemoveExisting();
        EnsureEventSystem();
        EnsureSettings();

        Canvas canvas = ScreamerUIStyle.MakeCanvas(CanvasName, 100);

        // Sibling order is draw order: HUD at the bottom, settings on top.
        GameUI.Build(canvas.transform);
        LobbyUI.Build(canvas.transform);
        ResultsUI.Build(canvas.transform);
        MenuUI.Build(canvas.transform);
        PauseUI.Build(canvas.transform);
        SettingsUI.Build(canvas.transform);

        EnsureMenuDolly();
    }

    static void RemoveExisting()
    {
        GameObject old = GameObject.Find(CanvasName);
        if (old != null) SafeDestroy(old);
    }

    static void EnsureEventSystem()
    {
        if (Object.FindObjectOfType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<StandaloneInputModule>();
    }

    static void EnsureSettings()
    {
        if (Object.FindObjectOfType<ScreamerSettings>() != null) return;

        var go = new GameObject(SettingsName);
        go.AddComponent<ScreamerSettings>();
    }

    /// <summary>
    /// The menu backdrop needs a camera gliding through the den. Prefer the
    /// scene's main camera; if the scene has none yet, provide one that at
    /// least clears to MidnightPlum so the menu never stares into the void.
    /// MenuUI enables/disables the dolly as the menu comes and goes.
    /// </summary>
    static void EnsureMenuDolly()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            var go = new GameObject("MenuCamera");
            go.tag = "MainCamera";
            cam = go.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = ScreamerPalette.MidnightPlum;
            if (Object.FindObjectOfType<AudioListener>() == null)
                go.AddComponent<AudioListener>();
        }

        if (cam.GetComponent<MenuCameraDolly>() == null)
        {
            MenuCameraDolly dolly = cam.gameObject.AddComponent<MenuCameraDolly>();
            dolly.enabled = false; // MenuUI switches it on when the menu shows
        }
    }

    static void SafeDestroy(Object obj)
    {
        if (Application.isPlaying) Object.Destroy(obj);
        else Object.DestroyImmediate(obj);
    }
}
