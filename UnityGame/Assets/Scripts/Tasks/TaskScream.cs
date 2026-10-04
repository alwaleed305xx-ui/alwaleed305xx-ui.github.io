using UnityEngine;

/// <summary>
/// SCREAM THERAPY - the dumbest, best chore in the house. Mash [Space] to let
/// it all out: ten screams finish the session, and every single one is a
/// loudness-1.0 ping the monster hears from the far end of the map. There is
/// no fail condition; the screams ARE the cost (GDD 4.3).
/// </summary>
public class TaskScream : TaskBase
{
    [Header("Scream therapy")]
    [Tooltip("Mashes of [Space] required to complete the session.")]
    public int screamsNeeded = 10;
    [Tooltip("Simulated bot mash cadence, screams per second.")]
    public float botScreamsPerSecond = 1.6f;

    int screams;
    float botScreamTimer;

    public override void ApplyBalanceDefaults()
    {
        taskName = "SCREAM THERAPY";
        flavorText = GameCopy.FlavorScreamTherapy;
        duration = 10f;            // one progress unit per scream, ten screams
        noiseInterval = 0f;        // no ambient ping - each scream pings on its own
        noiseLoudness = 1f;        // the loudest thing in the game
        noiseType = NoiseType.Scream;
        noiseLabel = GameCopy.NoiseScream;
        screamsNeeded = 10;
    }

    protected override void OnTaskStart()
    {
        screams = 0;
        ShowCounterHint();
    }

    protected override float ProgressMultiplier()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            screams++;
            AddProgress(duration / Mathf.Max(1, screamsNeeded));
            EmitTaskNoise(noiseLoudness, noiseType, noiseLabel);
            ShowCounterHint();

            // The combo raises the pitch; the therapist raises an eyebrow.
            if (AudioDirector.Instance != null)
            {
                float pitch = 0.9f + 0.05f * screams;
                AudioDirector.Instance.Play(Sfx.Scream, transform.position, 1f, pitch);
            }
            if (ScreamerCam.Instance != null)
            {
                ScreamerCam.Instance.Shake(0.05f, 0.08f);
                ScreamerCam.Instance.Tilt(Random.Range(-15f, 15f), 0.15f);
            }
        }

        // Progress is tied to screams, never to patience.
        return 0f;
    }

    void ShowCounterHint()
    {
        if (GameUI.Instance != null)
            GameUI.Instance.ShowTaskHint("Mash [SPACE]   AAAH x" + screams);
    }

    protected override void OnLocalCompleted()
    {
        if (GagFeedback.Instance != null)
            GagFeedback.Instance.Gag("THERAPIST FIRED", transform.position,
                Sfx.Scream, ScreamerPalette.ScreamYellow, 0.3f);
    }

    protected override float ServerBotWorkStep(ulong botId, float deltaTime, float chaosMultiplier)
    {
        // Bots scream at a human-ish mash cadence; every scream is real noise.
        botScreamTimer += deltaTime;
        float gap = 1f / Mathf.Max(0.1f, botScreamsPerSecond);
        if (botScreamTimer < gap) return 0f;

        botScreamTimer -= gap;
        ServerEmitTaskNoise(botId, noiseLoudness, noiseType, noiseLabel);
        return duration / Mathf.Max(1, screamsNeeded);
    }

    protected override void OnServerReset()
    {
        botScreamTimer = 0f;
    }
}
