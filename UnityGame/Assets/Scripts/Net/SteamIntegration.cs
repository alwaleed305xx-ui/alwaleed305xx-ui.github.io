// Entire file is Steam-conditional. Without the SCREAMER_STEAM scripting
// define (and the Steamworks.NET SDK it implies), this file compiles to
// nothing and the project stays green.
#if SCREAMER_STEAM
using Steamworks;
using UnityEngine;

/// <summary>
/// The Steam runtime layer: API lifecycle (init, callback pump, shutdown),
/// achievements, and rich presence. Created lazily by
/// <see cref="EnsureInitialized"/>; survives scene loads.
///
/// Achievements shipped at launch (jokes as beats, GDD 8.4):
///  - THERAPY COMPLETE   - scream 100 times (lifetime)
///  - IT WAS JUST A CHAIR - slap 50 innocent furniture pieces (lifetime)
///  - PACIFIST CHICKEN   - win a round without chasing the chicken yourself
///  - FINAL GIRL         - escape as the sole survivor 3 times (lifetime)
///  - NATURE IS HEALING  - win because the monster quit
/// </summary>
public class SteamIntegration : MonoBehaviour
{
    // Steam achievement API ids (configure the same ids in Steamworks partner site).
    const string AchTherapyComplete = "ACH_THERAPY_COMPLETE";
    const string AchJustAChair = "ACH_IT_WAS_JUST_A_CHAIR";
    const string AchPacifistChicken = "ACH_PACIFIST_CHICKEN";
    const string AchFinalGirl = "ACH_FINAL_GIRL";
    const string AchNatureIsHealing = "ACH_NATURE_IS_HEALING";

    // Lifetime counters persisted locally; achievements fire when thresholds cross.
    const string PrefScreams = "screamer.steam.screams";
    const string PrefSlaps = "screamer.steam.slaps";
    const string PrefSoleEscapes = "screamer.steam.soleEscapes";

    const int ScreamGoal = 100;
    const int SlapGoal = 50;
    const int SoleEscapeGoal = 3;

    public static SteamIntegration Instance { get; private set; }

    static bool initialized;
    static bool initAttempted;

    bool statsReady;
    Callback<UserStatsReceived_t> statsReceivedCallback;

    /// <summary>True once SteamAPI.Init() has succeeded this run.</summary>
    public static bool IsAvailable => initialized;

    /// <summary>The local player's Steam persona name, or "" when unavailable.</summary>
    public static string PersonaName => initialized ? SteamFriends.GetPersonaName() : "";

    /// <summary>
    /// Initializes the Steam API once and installs the runtime pump. Returns
    /// false (and never throws) when Steam is not running - the caller falls
    /// back to the UTP backend.
    /// </summary>
    public static bool EnsureInitialized()
    {
        if (initialized) return true;
        if (initAttempted) return false;
        initAttempted = true;

        try
        {
            if (!SteamAPI.Init())
            {
                Debug.LogWarning("SteamIntegration: SteamAPI.Init() failed; falling back to UTP.");
                return false;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("SteamIntegration: Steam unavailable (" + e.Message + "); falling back to UTP.");
            return false;
        }

        initialized = true;
        var go = new GameObject("SteamIntegration");
        DontDestroyOnLoad(go);
        go.AddComponent<SteamIntegration>();
        return true;
    }

    void Awake()
    {
        Instance = this;
        statsReceivedCallback = Callback<UserStatsReceived_t>.Create(OnUserStatsReceived);
        SteamUserStats.RequestCurrentStats();

        GameManager.OnClientStateChanged += HandleStateChanged;
        GameManager.OnClientResults += HandleResults;
        NoiseSystem.OnNoiseVisible += HandleNoiseVisible;
        MimicDisguise.OnLocalDisguiseChanged += HandleDisguiseChanged;

        SetPresence("Lurking in the lobby of SCREAMER");
    }

    void OnDestroy()
    {
        GameManager.OnClientStateChanged -= HandleStateChanged;
        GameManager.OnClientResults -= HandleResults;
        NoiseSystem.OnNoiseVisible -= HandleNoiseVisible;
        MimicDisguise.OnLocalDisguiseChanged -= HandleDisguiseChanged;
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        SteamAPI.RunCallbacks();
    }

    void OnApplicationQuit()
    {
        if (!initialized) return;
        SteamFriends.ClearRichPresence();
        SteamAPI.Shutdown();
        initialized = false;
    }

    void OnUserStatsReceived(UserStatsReceived_t data)
    {
        statsReady = true;
    }

    // ------------------------- Rich presence -------------------------

    void HandleStateChanged(GameManager.GameState state)
    {
        bool iAmMonster = GameManager.Instance != null && GameManager.Instance.IAmMonster;
        switch (state)
        {
            case GameManager.GameState.Lobby:
                SetPresence("Lurking in the lobby of SCREAMER");
                break;
            case GameManager.GameState.Countdown:
            case GameManager.GameState.Lockdown:
            case GameManager.GameState.Playing:
            case GameManager.GameState.Finale:
                SetPresence(iAmMonster ? "Eating friends in SCREAMER" : "Burning noodles in SCREAMER");
                break;
            case GameManager.GameState.Results:
                SetPresence("Reviewing the damage in SCREAMER");
                break;
        }
    }

    void HandleDisguiseChanged(bool disguised)
    {
        if (disguised)
            SetPresence("Being a chair in SCREAMER");
        else
            HandleStateChanged(GameManager.Instance != null
                ? GameManager.Instance.State.Value
                : GameManager.GameState.Lobby);
    }

    static void SetPresence(string status)
    {
        if (!initialized) return;
        SteamFriends.SetRichPresence("status", status);
        SteamFriends.SetRichPresence("steam_display", "#Status");
    }

    // ------------------------- Achievements -------------------------

    void HandleNoiseVisible(ulong sourceActorId, Vector3 position, float loudness, NoiseType type)
    {
        if (Unity.Netcode.NetworkManager.Singleton == null) return;
        if (sourceActorId != Unity.Netcode.NetworkManager.Singleton.LocalClientId) return;

        if (type == NoiseType.Scream)
            BumpCounter(PrefScreams, ScreamGoal, AchTherapyComplete);
        else if (type == NoiseType.Slap)
            BumpCounter(PrefSlaps, SlapGoal, AchJustAChair);
    }

    void HandleResults(PlayerRoundResult[] results)
    {
        if (Unity.Netcode.NetworkManager.Singleton == null || results == null) return;
        ulong localId = Unity.Netcode.NetworkManager.Singleton.LocalClientId;

        // Read the outcome mirrors carried by the Results RPC, never the
        // NetworkVariables: their deltas land a tick after this event on
        // remote clients, which would evaluate achievements on LAST round's
        // outcome (NATURE IS HEALING / FINAL GIRL would misfire or be missed).
        bool survivorsWon = GameManager.Instance != null && GameManager.Instance.ClientSurvivorsWon;
        bool monsterQuit = GameManager.Instance != null && GameManager.Instance.ClientMonsterForfeited;

        bool found = false;
        PlayerRoundResult mine = default;
        int escapeeCount = 0;
        foreach (PlayerRoundResult result in results)
        {
            if (result.escaped) escapeeCount++;
            if (result.actorId == localId)
            {
                mine = result;
                found = true;
            }
        }
        if (!found || mine.wasMonster) return;

        if (survivorsWon && monsterQuit)
            Unlock(AchNatureIsHealing);

        if (!survivorsWon || !mine.escaped) return;

        // Won without personally harassing the bird.
        if (mine.chickenChaseSeconds <= 0f)
            Unlock(AchPacifistChicken);

        if (escapeeCount == 1)
            BumpCounter(PrefSoleEscapes, SoleEscapeGoal, AchFinalGirl);
    }

    void BumpCounter(string prefKey, int goal, string achievementId)
    {
        int count = PlayerPrefs.GetInt(prefKey, 0) + 1;
        PlayerPrefs.SetInt(prefKey, count);
        if (count >= goal)
            Unlock(achievementId);
    }

    /// <summary>Sets and stores one achievement. Safe to call repeatedly.</summary>
    public void Unlock(string achievementId)
    {
        if (!initialized || !statsReady) return;
        if (SteamUserStats.GetAchievement(achievementId, out bool already) && already) return;
        SteamUserStats.SetAchievement(achievementId);
        SteamUserStats.StoreStats();
    }
}
#endif
