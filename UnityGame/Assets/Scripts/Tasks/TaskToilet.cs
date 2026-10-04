using UnityEngine;

/// <summary>
/// HAUNTED TOILET - plunge with [E] in a calm rhythm. A gap of at least 0.5 s
/// between plunges keeps a 5x progress groove; rushing it fires the geyser: a
/// loudness-1.0 plumbing event the whole neighborhood hears, and zero progress
/// while the bathroom recovers. Calm hands. Calm heart. Calm plunger.
/// GDD 4.3 / 4.2.
/// </summary>
public class TaskToilet : TaskBase
{
    [Header("Haunted toilet")]
    [Tooltip("Minimum seconds between plunges. Faster than this = geyser.")]
    public float minPlungeGap = 0.5f;
    [Tooltip("How long one calm plunge keeps the 5x groove alive.")]
    public float calmBoostSeconds = 0.75f;
    [Tooltip("Progress multiplier while idling between plunges.")]
    public float idleMultiplier = 0.3f;
    [Tooltip("Seconds of zero progress after angering the pipes.")]
    public float geyserPenaltySeconds = 1f;
    [Tooltip("Base chance per bot plunge of rushing it (scaled by chaos).")]
    public float botRushChance = 0.10f;
    [Tooltip("Seconds between simulated bot plunges.")]
    public float botPlungeSeconds = 0.9f;

    float lastPressTime = -99f;
    float boostTimer;
    float penaltyTimer;

    float botPlungeTimer;
    float botPenaltyTimer;

    public override void ApplyBalanceDefaults()
    {
        taskName = "HAUNTED TOILET";
        flavorText = GameCopy.FlavorToilet;
        duration = 8f;
        noiseInterval = 2.5f;
        noiseLoudness = 0.4f;
        noiseType = NoiseType.Plumbing;
        noiseLabel = GameCopy.NoisePlumbing;
    }

    protected override void OnTaskStart()
    {
        lastPressTime = -99f;
        boostTimer = 0f;
        penaltyTimer = 0f;

        if (GameUI.Instance != null)
            GameUI.Instance.ShowTaskHint("Plunge with [E]. Slowly. It can tell.");
    }

    protected override float ProgressMultiplier()
    {
        boostTimer -= Time.deltaTime;
        penaltyTimer -= Time.deltaTime;

        if (Input.GetKeyDown(KeyCode.E))
        {
            float gap = Time.time - lastPressTime;
            lastPressTime = Time.time;

            if (gap < minPlungeGap)
            {
                Geyser();
                return 0f;
            }

            // A good, respectful plunge.
            boostTimer = calmBoostSeconds;
            if (AudioDirector.Instance != null)
                AudioDirector.Instance.Play(Sfx.Glorp, transform.position, 1f, 0.85f);
            if (ScreamerCam.Instance != null)
                ScreamerCam.Instance.Shake(0.02f, 0.05f); // a subtle nod of approval
        }

        if (penaltyTimer > 0f) return 0f;
        return boostTimer > 0f ? 5f : idleMultiplier;
    }

    void Geyser()
    {
        boostTimer = 0f;
        penaltyTimer = geyserPenaltySeconds;

        // The pipes have opinions, and they share them with the monster.
        EmitTaskNoise(1f, NoiseType.Plumbing, GameCopy.NoiseToiletGeyser);
        if (IsSpawned) AngeredPipesServerRpc(); // the GDD 11.5 public-shaming line

        if (AudioDirector.Instance != null)
            AudioDirector.Instance.Play(Sfx.GeyserBurst, transform.position);
        if (ScreamerCam.Instance != null)
            ScreamerCam.Instance.Shake(0.5f, 0.4f);
        if (GagFeedback.Instance != null)
            GagFeedback.Instance.Popup(GameCopy.NoiseToiletGeyser, ScreamerPalette.ScreamYellow);
        if (GameUI.Instance != null)
            GameUI.Instance.ShowTaskHint("EASY. Calm rhythm. The pipes are listening.");
    }

    /// <summary>
    /// Client -> server: the local runner rushed a plunge. No geyser stat kind
    /// exists, so the event-feed line is routed here instead of the stat seam.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    void AngeredPipesServerRpc(ServerRpcParams p = default)
    {
        GameManager gm = GameManager.Instance;
        if (gm != null)
            gm.ServerAnnounce(GameCopy.EventAngeredPipes(gm.ActorName(p.Receive.SenderClientId)));
    }

    protected override float ServerBotWorkStep(ulong botId, float deltaTime, float chaosMultiplier)
    {
        botPenaltyTimer -= deltaTime;

        botPlungeTimer += deltaTime;
        if (botPlungeTimer >= Mathf.Max(0.2f, botPlungeSeconds))
        {
            botPlungeTimer = 0f;
            if (Random.value < Mathf.Clamp01(botRushChance * chaosMultiplier))
            {
                botPenaltyTimer = geyserPenaltySeconds;
                ServerEmitTaskNoise(botId, 1f, NoiseType.Plumbing, GameCopy.NoiseToiletGeyser);

                // Bots anger the pipes on the same public feed as humans.
                GameManager gm = GameManager.Instance;
                if (gm != null)
                    gm.ServerAnnounce(GameCopy.EventAngeredPipes(gm.ActorName(botId)));
            }
        }

        if (botPenaltyTimer > 0f) return 0f;
        return deltaTime * 5f * 0.6f; // calm-ish rhythm, bot-grade technique
    }

    protected override void OnServerReset()
    {
        botPlungeTimer = 0f;
        botPenaltyTimer = 0f;
    }
}
