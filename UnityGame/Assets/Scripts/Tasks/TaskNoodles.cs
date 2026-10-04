using UnityEngine;

/// <summary>
/// INSTANT NOODLES - stir with [E] inside every 2-second window. Miss one and
/// the pot burns: a loudness-1.0 smoke alarm tells the whole house where you
/// live, and progress visibly rewinds at -1x/s for two seconds. The last 0.6 s
/// of each window gets a rising kettle whine as a mercy tell. GDD 4.3 / 12.1.
/// </summary>
public class TaskNoodles : TaskBase
{
    [Header("Noodles")]
    [Tooltip("A stir ([E]) is required at least this often.")]
    public float stirEverySeconds = 2f;
    [Tooltip("How long the visible -1x rollback lasts after a burn.")]
    public float burnRollbackSeconds = 2f;
    [Tooltip("The kettle whine warning starts this long before a burn.")]
    public float nearBurnWarningSeconds = 0.6f;
    [Tooltip("Base chance per bot stir window of missing it (scaled by chaos).")]
    public float botBurnChance = 0.05f;

    float sinceStir;
    float burnTimer;
    bool kettleWarned;

    float botStirTimer;
    float botBurnTimer;

    public override void ApplyBalanceDefaults()
    {
        taskName = "INSTANT NOODLES";
        flavorText = GameCopy.FlavorNoodles;
        duration = 9f;
        noiseInterval = 3f;
        noiseLoudness = 0.3f;
        noiseType = NoiseType.Cooking;
        noiseLabel = GameCopy.NoiseCooking;
    }

    protected override void OnTaskStart()
    {
        sinceStir = 0f;
        burnTimer = 0f;
        kettleWarned = false;

        if (GameUI.Instance != null)
            GameUI.Instance.ShowTaskHint("Stir with [E] every couple of seconds.");
    }

    protected override float ProgressMultiplier()
    {
        // Burned: the bar rolls backwards while the kitchen reflects on it.
        if (burnTimer > 0f)
        {
            burnTimer -= Time.deltaTime;
            return -1f;
        }

        sinceStir += Time.deltaTime;

        if (Input.GetKeyDown(KeyCode.E))
        {
            sinceStir = 0f;
            kettleWarned = false;
            if (AudioDirector.Instance != null)
                AudioDirector.Instance.Play(Sfx.Glorp, transform.position, 0.5f, 1.2f); // a gentle bubble
        }

        if (!kettleWarned && sinceStir > stirEverySeconds - nearBurnWarningSeconds)
        {
            kettleWarned = true;
            if (AudioDirector.Instance != null)
                AudioDirector.Instance.Play(Sfx.KettleWhine, transform.position, 0.8f);
        }

        if (sinceStir > stirEverySeconds)
        {
            Burn();
            return -1f;
        }

        return 1f;
    }

    void Burn()
    {
        sinceStir = 0f;
        kettleWarned = false;
        burnTimer = burnRollbackSeconds;

        // The smoke alarm files the noise complaint on your behalf.
        EmitTaskNoise(1f, NoiseType.Alarm, GameCopy.NoiseNoodleBurn);
        if (GameManager.Instance != null)
            GameManager.Instance.ReportStatServerRpc(StatKind.NoodleBurns, 1f);

        if (AudioDirector.Instance != null)
        {
            AudioDirector.Instance.Play(Sfx.SmokeAlarm, transform.position);
            AudioDirector.Instance.Play(Sfx.SlideWhistle, transform.position); // the bar's sad rewind
        }
        if (ScreamerCam.Instance != null)
            ScreamerCam.Instance.Shake(0.4f, 0.3f);
        if (GagFeedback.Instance != null)
            GagFeedback.Instance.Popup("PROGRESS: GONE. DIGNITY: ALSO GONE.", ScreamerPalette.ScreamYellow);
        if (GameUI.Instance != null)
            GameUI.Instance.ShowTaskHint("IT'S BURNING. STIR. [E]. NOW.");
    }

    protected override float ServerBotWorkStep(ulong botId, float deltaTime, float chaosMultiplier)
    {
        // Bots burn pots too - honestly, loudly, and on the scoreboard.
        if (botBurnTimer > 0f)
        {
            botBurnTimer -= deltaTime;
            return -deltaTime;
        }

        botStirTimer += deltaTime;
        if (botStirTimer >= stirEverySeconds)
        {
            botStirTimer = 0f;
            if (Random.value < Mathf.Clamp01(botBurnChance * chaosMultiplier))
            {
                botBurnTimer = burnRollbackSeconds;
                ServerEmitTaskNoise(botId, 1f, NoiseType.Alarm, GameCopy.NoiseNoodleBurn);
                if (GameManager.Instance != null)
                    GameManager.Instance.ServerAddStat(botId, StatKind.NoodleBurns, 1f);
                return -deltaTime;
            }
        }

        return deltaTime;
    }

    protected override void OnServerReset()
    {
        botStirTimer = 0f;
        botBurnTimer = 0f;
    }
}
