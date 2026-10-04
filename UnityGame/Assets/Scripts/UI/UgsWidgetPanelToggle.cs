using UnityEngine;

/// <summary>
/// [F1] shows and hides the Unity Gaming Services building-block widgets
/// (Player Account sign-in, Multiplayer Session, Matchmaker, Leaderboards,
/// Achievements) that ScreamerAssetInstaller stacks on this canvas. The panel
/// starts hidden so the game HUD stays clean; the widgets go live once the
/// project is linked under Project Settings > Services.
/// </summary>
public class UgsWidgetPanelToggle : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.F1;

    bool shown;

    void Start()
    {
        Apply(false);
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            Apply(!shown);
    }

    void Apply(bool show)
    {
        shown = show;
        foreach (Transform child in transform)
            child.gameObject.SetActive(show);
    }
}
