using UnityEngine;

/// <summary>
/// KARAOKE NIGHT - press the shown key (J / K / L) on cue. A correct note
/// grants a 3x progress window, standing silent crawls at 0.5x, and a wrong
/// key is an instant loudness-0.9 feedback screech the whole house hears
/// (plus zero progress that frame). GDD 4.3 / 4.2.
/// </summary>
public class TaskKaraoke : TaskBase
{
    [Header("Karaoke")]
    [Tooltip("How long one correct note keeps the 3x groove alive.")]
    public float correctNoteBoostSeconds = 0.75f;
    public float wrongNoteLoudness = 0.9f;
    [Tooltip("Base chance per bot note of hitting a key that does not exist (scaled by chaos).")]
    public float botWrongNoteChance = 0.15f;
    [Tooltip("Seconds between simulated bot notes.")]
    public float botNoteSeconds = 1f;

    static readonly KeyCode[] Keys = { KeyCode.J, KeyCode.K, KeyCode.L };

    KeyCode currentKey;
    float boostTimer;

    float botNoteTimer;
    bool botInGroove;

    public override void ApplyBalanceDefaults()
    {
        taskName = "KARAOKE NIGHT";
        flavorText = GameCopy.FlavorKaraoke;
        duration = 8f;
        noiseInterval = 2f;
        noiseLoudness = 0.6f;
        noiseType = NoiseType.Music;
        noiseLabel = GameCopy.NoiseKaraokeAmbient;
    }

    protected override void OnTaskStart()
    {
        boostTimer = 0f;
        PickKey();
    }

    void PickKey()
    {
        currentKey = Keys[Random.Range(0, Keys.Length)];
        if (GameUI.Instance != null)
            GameUI.Instance.ShowTaskHint("PRESS: [" + currentKey + "]");
    }

    protected override float ProgressMultiplier()
    {
        boostTimer -= Time.deltaTime;

        if (Input.GetKeyDown(currentKey))
        {
            // Clean note: a chime stepped to the key, and the groove window opens.
            if (AudioDirector.Instance != null)
                AudioDirector.Instance.Play(Sfx.Chime, transform.position, 1f, NotePitch(currentKey));

            boostTimer = correctNoteBoostSeconds;
            PickKey();
            return 3f;
        }

        for (int i = 0; i < Keys.Length; i++)
        {
            KeyCode key = Keys[i];
            if (key == currentKey || !Input.GetKeyDown(key)) continue;

            // That was not a note. Everyone knows. Especially the monster.
            EmitTaskNoise(wrongNoteLoudness, NoiseType.Music, GameCopy.NoiseKaraokeWrongNote);
            if (GameManager.Instance != null)
                GameManager.Instance.ReportStatServerRpc(StatKind.WrongNotes, 1f);

            if (AudioDirector.Instance != null)
                AudioDirector.Instance.Play(Sfx.FeedbackScreech, transform.position);
            if (ScreamerCam.Instance != null)
                ScreamerCam.Instance.Tilt(3f, 0.2f);
            if (GagFeedback.Instance != null)
                GagFeedback.Instance.Popup("THAT WAS A J.", ScreamerPalette.ScreamYellow);

            boostTimer = 0f;
            PickKey();
            return 0f; // multiplier zero that frame (GDD 4.3)
        }

        return boostTimer > 0f ? 3f : 0.5f; // groove vs. mumbling at the mic
    }

    static float NotePitch(KeyCode key)
    {
        // The audio bank's chime sits on the J note; K and L step up from it
        // (523 / 659 / 784 Hz per GDD 13.2).
        switch (key)
        {
            case KeyCode.K: return 659f / 523f;
            case KeyCode.L: return 784f / 523f;
            default: return 1f;
        }
    }

    protected override float ServerBotWorkStep(ulong botId, float deltaTime, float chaosMultiplier)
    {
        botNoteTimer += deltaTime;
        if (botNoteTimer >= Mathf.Max(0.2f, botNoteSeconds))
        {
            botNoteTimer = 0f;
            bool wrong = Random.value < Mathf.Clamp01(botWrongNoteChance * chaosMultiplier);
            botInGroove = !wrong;

            if (wrong)
            {
                ServerEmitTaskNoise(botId, wrongNoteLoudness, NoiseType.Music, GameCopy.NoiseKaraokeWrongNote);
                if (GameManager.Instance != null)
                    GameManager.Instance.ServerAddStat(botId, StatKind.WrongNotes, 1f);
            }
        }

        return deltaTime * (botInGroove ? 3f : 0.5f);
    }

    protected override void OnServerReset()
    {
        botNoteTimer = 0f;
        botInGroove = false;
    }
}
