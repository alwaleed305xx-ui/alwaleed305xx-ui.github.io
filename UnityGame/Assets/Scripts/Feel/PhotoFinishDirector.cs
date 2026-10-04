using System.Collections;
using UnityEngine;

/// <summary>
/// The photo-finish (GDD 9.5 / 12.1): when an escape lands within two seconds
/// of the door blowing open - the monster was rounding the corner during the
/// group scream - every screen drops to 0.5x for 1.2 seconds, the audio pitch
/// sags with it, the letterbox bars slide in for the cinema of it all, and the
/// round ends on the "CLIP THAT." toast.
///
/// Purely cosmetic and purely client-side: each client lerps its own
/// Time.timeScale off the shared <see cref="EscapeDoor.OnClientEscape"/>
/// event, server logic keys off server time throughout, and every duration
/// here is measured in unscaled time. Triggers at most once per round.
/// </summary>
public class PhotoFinishDirector : MonoBehaviour
{
    /// <summary>True while the slow-mo owns Time.timeScale (ScreamerCam's hitstop yields to it).</summary>
    public static bool SlowMoActive { get; private set; }

    [Header("Photo-finish window (GDD 9.5)")]
    [Tooltip("An escape this soon after the door blows open counts as a photo finish.")]
    public float photoFinishWindowSeconds = 2f;

    [Header("Slow-mo (GDD 12.1)")]
    public float slowMoScale = 0.5f;
    public float slowMoSeconds = 1.2f;
    public float rampInSeconds = 0.15f;
    public float rampOutSeconds = 0.25f;
    [Tooltip("World audio pitch during the slow-mo.")]
    public float audioPitchScale = 0.72f;

    bool triggeredThisRound;
    Coroutine routine;

    void OnEnable()
    {
        EscapeDoor.OnClientEscape += HandleEscape;
        GameManager.OnClientStateChanged += HandleStateChanged;
    }

    void OnDisable()
    {
        EscapeDoor.OnClientEscape -= HandleEscape;
        GameManager.OnClientStateChanged -= HandleStateChanged;
        StopSlowMoImmediately();
    }

    // ------------------------- Event handling -------------------------

    void HandleEscape(ulong actorId, float secondsSinceDoorOpened)
    {
        if (triggeredThisRound) return;
        if (secondsSinceDoorOpened > photoFinishWindowSeconds) return;

        GameManager gm = GameManager.Instance;
        if (gm != null && gm.State.Value != GameManager.GameState.Finale) return;

        triggeredThisRound = true;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(SlowMoRoutine());
    }

    void HandleStateChanged(GameManager.GameState state)
    {
        switch (state)
        {
            case GameManager.GameState.Finale:
                triggeredThisRound = false; // armed fresh for every finale
                break;
            case GameManager.GameState.Results:
            case GameManager.GameState.Lobby:
                // The results screen and the lobby run at full speed, always.
                StopSlowMoImmediately();
                break;
        }
    }

    // ------------------------- The moment -------------------------

    IEnumerator SlowMoRoutine()
    {
        SlowMoActive = true;
        if (ScreenFxOverlay.Instance != null) ScreenFxOverlay.Instance.SetLetterbox(true);
        if (AudioDirector.Instance != null) AudioDirector.Instance.SetWorldPitchScale(audioPitchScale);

        float holdSeconds = Mathf.Max(0f, slowMoSeconds - rampInSeconds - rampOutSeconds);
        float elapsed = 0f;

        // Ramp in, hold, ramp out - reasserted every frame so a stray hitstop
        // or anything else poking the clock cannot leave it stuck.
        while (elapsed < slowMoSeconds)
        {
            elapsed += Time.unscaledDeltaTime;

            float scale;
            if (elapsed < rampInSeconds)
                scale = Mathf.Lerp(1f, slowMoScale, elapsed / rampInSeconds);
            else if (elapsed < rampInSeconds + holdSeconds)
                scale = slowMoScale;
            else
                scale = Mathf.Lerp(slowMoScale, 1f,
                    (elapsed - rampInSeconds - holdSeconds) / Mathf.Max(0.01f, rampOutSeconds));

            Time.timeScale = scale;
            yield return null;
        }

        Time.timeScale = 1f;
        if (ScreenFxOverlay.Instance != null) ScreenFxOverlay.Instance.SetLetterbox(false);
        if (AudioDirector.Instance != null) AudioDirector.Instance.SetWorldPitchScale(1f);
        SlowMoActive = false;
        routine = null;

        // The nudge for everyone whose recorder was rolling (GDD 9.5).
        if (GagFeedback.Instance != null)
            GagFeedback.Instance.Popup(GameCopy.ClipToast, ScreamerPalette.ScreamYellow);
    }

    void StopSlowMoImmediately()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        if (SlowMoActive)
        {
            Time.timeScale = 1f;
            if (ScreenFxOverlay.Instance != null) ScreenFxOverlay.Instance.SetLetterbox(false);
            if (AudioDirector.Instance != null) AudioDirector.Instance.SetWorldPitchScale(1f);
            SlowMoActive = false;
        }
    }
}
