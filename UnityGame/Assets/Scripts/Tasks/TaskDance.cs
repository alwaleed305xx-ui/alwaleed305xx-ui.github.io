using UnityEngine;

/// <summary>
/// DANCE FLOOR - stand on the 16-tile pad and mash the arrow keys. Dancing
/// runs at 2.5x progress, standing still at 0.2x, and the music never stops
/// being a homing beacon (ambient loudness 0.7 every second). No fail
/// condition; the bass is the fail condition. GDD 4.3 / 4.2.
///
/// The pad's tiles are beat-cycled through warm palette tints on every
/// client - purely cosmetic, driven locally via MaterialPropertyBlock so the
/// shared tile materials stay untouched.
/// </summary>
public class TaskDance : TaskBase
{
    [Header("Dance floor")]
    [Tooltip("Seconds one arrow press counts as 'still dancing'.")]
    public float danceWindowSeconds = 0.5f;
    [Tooltip("Consecutive presses that trigger the UNSTOPPABLE gag.")]
    public int streakForGag = 8;
    [Tooltip("Seconds between beat steps of the tile light cycle.")]
    public float beatSeconds = 0.4f;

    [Header("Pad tiles (wired by TaskFactory, 16 tiles)")]
    public Renderer[] padTiles = new Renderer[0];

    static readonly KeyCode[] Arrows =
        { KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.UpArrow, KeyCode.DownArrow };

    float danceWindow;
    int streak;
    float lastPressTime;

    float beatTimer;
    int beatStep;
    MaterialPropertyBlock tileBlock;
    static readonly int ColorId = Shader.PropertyToID("_Color");

    public override void ApplyBalanceDefaults()
    {
        taskName = "DANCE FLOOR";
        flavorText = GameCopy.FlavorDance;
        duration = 7f;
        noiseInterval = 1f;
        noiseLoudness = 0.7f;
        noiseType = NoiseType.Music;
        noiseLabel = GameCopy.NoiseDance;
    }

    protected override void OnTaskStart()
    {
        danceWindow = 0f;
        streak = 0;
        lastPressTime = -99f;

        if (GameUI.Instance != null)
            GameUI.Instance.ShowTaskHint("Mash the arrow keys. Any of them. All of them.");
    }

    protected override float ProgressMultiplier()
    {
        danceWindow -= Time.deltaTime;

        for (int i = 0; i < Arrows.Length; i++)
        {
            if (!Input.GetKeyDown(Arrows[i])) continue;

            // Streaks only survive tight footwork.
            streak = Time.time - lastPressTime <= 0.6f ? streak + 1 : 1;
            lastPressTime = Time.time;
            danceWindow = danceWindowSeconds;

            if (AudioDirector.Instance != null)
                AudioDirector.Instance.Play(Sfx.BassKick, transform.position);
            if (ScreamerCam.Instance != null)
                ScreamerCam.Instance.Shake(0.03f, 0.06f);

            if (streak == streakForGag && GagFeedback.Instance != null)
                GagFeedback.Instance.Gag("UNSTOPPABLE.", transform.position,
                    Sfx.Chime, ScreamerPalette.ScreamYellow, 0.1f);
            break;
        }

        return danceWindow > 0f ? 2.5f : 0.2f;
    }

    protected override void ClientCosmeticTick()
    {
        if (padTiles == null || padTiles.Length == 0) return;

        beatTimer += Time.deltaTime;
        if (beatTimer < Mathf.Max(0.1f, beatSeconds)) return;
        beatTimer = 0f;
        beatStep++;

        tileBlock ??= new MaterialPropertyBlock();

        for (int i = 0; i < padTiles.Length; i++)
        {
            Renderer tile = padTiles[i];
            if (tile == null) continue;

            // A diagonal wave of warm highlights sweeps the 4x4 pad.
            int row = i / 4, column = i % 4;
            bool lit = (row + column + beatStep) % 4 == 0;
            Color color = lit ? ScreamerPalette.ScreamYellow
                              : (row + column) % 2 == 0 ? ScreamerPalette.LamplightAmber
                                                        : ScreamerPalette.ShagRust;

            tile.GetPropertyBlock(tileBlock);
            tileBlock.SetColor(ColorId, color);
            tile.SetPropertyBlock(tileBlock);
        }
    }

    protected override float ServerBotWorkStep(ulong botId, float deltaTime, float chaosMultiplier)
    {
        // Bots dance with commitment but no technique: solid, never perfect.
        return deltaTime * 2f;
    }
}
