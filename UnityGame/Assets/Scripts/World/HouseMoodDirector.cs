using Unity.Netcode;
using UnityEngine;

/// <summary>
/// 1-in-20 rounds, the house joins in (GDD 9.9; first cut if the schedule
/// slips): the TV blares, the smoke alarm chirps its low battery, a door
/// slams - each a FAKE noise ping, ~45 s apart, fired server-side through
/// NoiseSystem.ServerMakeNoise with no actor attached. The monster learns to
/// distrust the house; noise bluffing enters the meta.
///
/// The roll happens on the server as each lobby forms, so the lobby ticker
/// can carry the one permitted tell (GameCopy.TipHouseMood via
/// LobbyUI.PushTickerLine); in-round the events are indistinguishable from
/// real noise - that is the point.
/// </summary>
public class HouseMoodDirector : MonoBehaviour
{
    public static HouseMoodDirector Instance { get; private set; }

    /// <summary>
    /// True while the current round is a mood round. Meaningful on the
    /// server/host only - clients are deliberately kept in the dark.
    /// </summary>
    public static bool IsMoodRound { get; private set; }

    [Tooltip("Chance that a round is a mood round (GDD 9.9: 1 in 20).")]
    [Range(0f, 1f)] public float moodChance = 0.05f;

    [Tooltip("Average seconds between fake pings.")]
    public float eventIntervalSeconds = 45f;

    [Tooltip("Random interval jitter, fraction of the interval (0.2 = plus or minus 20%).")]
    [Range(0f, 0.5f)] public float intervalJitter = 0.2f;

    float nextEventTimer;
    int eventCursor;
    bool roundActive;

    void Awake()
    {
        Instance = this;
    }

    void OnEnable()
    {
        GameManager.OnClientStateChanged += HandleStateChanged;
    }

    void OnDisable()
    {
        GameManager.OnClientStateChanged -= HandleStateChanged;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        IsMoodRound = false;
    }

    static bool IsServer =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening && NetworkManager.Singleton.IsServer;

    void HandleStateChanged(GameManager.GameState state)
    {
        // The static event fires on every client; only the server runs moods.
        if (!IsServer)
        {
            roundActive = false;
            return;
        }

        switch (state)
        {
            case GameManager.GameState.Lobby:
                // Roll for the UPCOMING round while the lobby ticker is still
                // on screen - the ticker line is the only announcement the
                // house ever makes (GDD 9.9). The line surfaces on the host's
                // ticker; everyone else must learn to distrust the house.
                roundActive = false;
                IsMoodRound = Random.value < moodChance;
                eventCursor = Random.Range(0, 3);
                if (IsMoodRound) LobbyUI.PushTickerLine(GameCopy.TipHouseMood);
                break;

            case GameManager.GameState.Countdown:
                roundActive = false;
                break;

            case GameManager.GameState.Playing:
                roundActive = IsMoodRound;
                nextEventTimer = RollInterval() * 0.7f; // first bluff lands a touch early
                break;

            case GameManager.GameState.Results:
                roundActive = false;
                break;
        }
    }

    void Update()
    {
        if (!roundActive || !IsServer) return;

        nextEventTimer -= Time.deltaTime;
        if (nextEventTimer > 0f) return;
        nextEventTimer = RollInterval();

        FireNextFakePing();
    }

    float RollInterval()
    {
        return eventIntervalSeconds * Random.Range(1f - intervalJitter, 1f + intervalJitter);
    }

    /// <summary>
    /// The three bluffs, cycled so a round never repeats one back to back.
    /// Labels reuse the real noise labels - the monster must not be able to
    /// tell a fake from the real thing.
    /// </summary>
    void FireNextFakePing()
    {
        NoiseSystem noise = NoiseSystem.Instance;
        if (noise == null) return;

        eventCursor = (eventCursor + 1) % 3;
        switch (eventCursor)
        {
            case 0:
                // The TV randomly blares.
                noise.ServerMakeNoise(ulong.MaxValue, HouseLayout.Tv, 0.6f,
                    NoiseType.Music, GameCopy.NoiseLabel(NoiseType.Music));
                break;

            case 1:
                // Smoke alarm chirps its low battery in the kitchen.
                noise.ServerMakeNoise(ulong.MaxValue, HouseLayout.NoodleStove, 0.4f,
                    NoiseType.Alarm, GameCopy.NoiseLabel(NoiseType.Alarm));
                break;

            default:
                // A door slams somewhere in the hallway.
                Vector3 hallway = new Vector3(Random.Range(-14f, 14f), 1f, -6f);
                noise.ServerMakeNoise(ulong.MaxValue, hallway, 0.5f,
                    NoiseType.Slap, GameCopy.NoiseLabel(NoiseType.Slap));
                break;
        }
    }
}
