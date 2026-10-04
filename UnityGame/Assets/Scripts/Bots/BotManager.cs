using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-driven bot roster (GDD 10): the lobby auto-fills to four actors
/// (GameManager calls <see cref="ServerFillTo"/> after 10 seconds), the host
/// adds/removes bots from the lobby rail, a late-joining human replaces one,
/// and the rematch reset parks every bot body without touching the roster so
/// the same crew shuffles back into the next round.
///
/// Bodies come from a pool of seven pre-placed BotPawn scene objects (built by
/// BotFactory): the project has no editor in the loop to author a prefab
/// asset, and in-scene NetworkObjects are the same replication mechanism the
/// GameManager and task stations already use. The practice-mode monster is
/// different - it reuses the registered monster prefab, spawned server-owned
/// with BotDriven set before Spawn, plus a MonsterBotBrain.
///
/// Bot deaths and escapes resolve through the regular GameManager events, so
/// stats, the killcam, and the award polaroids treat bots exactly like people.
/// Lives on an in-scene NetworkObject created by BotFactory.Install().
/// </summary>
public class BotManager : NetworkBehaviour
{
    public static BotManager Instance { get; private set; }

    /// <summary>Hard cap on lobby seats; 1 human + up to 7 bots, or any mix (GDD 4.4).</summary>
    public const int MaxRosterSeats = 8;

    [Header("Body pool (wired by BotFactory)")]
    [Tooltip("Pre-placed survivor bodies handed out to bots; parked under the map when idle.")]
    public BotPawn[] pawnPool = new BotPawn[0];

    /// <summary>One hired extra: identity plus whichever body it currently wears.</summary>
    class BotRecord
    {
        public ulong botId;
        public BotPersonality personality;
        public BotPawn pawn; // null while the bot has no body (lobby pending, eaten, reset)
    }

    readonly List<BotRecord> bots = new List<BotRecord>();
    int personalitySerial; // which archetype the next hire gets
    ulong nextBotSerial;   // ids are never recycled within a session

    // The practice-mode monster body (server-owned instance of the monster prefab).
    NetworkObject botMonster;
    ulong botMonsterId = ulong.MaxValue;

    /// <summary>Bots currently seated, derived from the synchronized roster so it is right on every client.</summary>
    public int BotCount
    {
        get
        {
            GameManager gm = GameManager.Instance;
            if (gm == null || gm.Roster == null) return bots.Count;

            int count = 0;
            for (int i = 0; i < gm.Roster.Count; i++)
                if (gm.Roster[i].isBot)
                    count++;
            return count;
        }
    }

    void Awake()
    {
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer || GameManager.Instance == null) return;
        GameManager.Instance.OnServerActorCaught += ServerHandleActorResolved;
        GameManager.Instance.OnServerActorEscaped += ServerHandleActorResolved;
    }

    public override void OnNetworkDespawn()
    {
        if (!IsServer || GameManager.Instance == null) return;
        GameManager.Instance.OnServerActorCaught -= ServerHandleActorResolved;
        GameManager.Instance.OnServerActorEscaped -= ServerHandleActorResolved;
    }

    // ------------------------- Roster API (server) -------------------------

    /// <summary>
    /// Server: hires the next archetype, registers it with the GameManager
    /// (which seats it in the roster and queues its lobby body), and returns
    /// its synthetic actor id - or ulong.MaxValue when the house is full.
    /// </summary>
    public ulong ServerAddBot()
    {
        if (!IsServerProcess() || GameManager.Instance == null) return ulong.MaxValue;
        if (GameManager.Instance.Roster.Count >= MaxRosterSeats) return ulong.MaxValue;
        if (bots.Count >= pawnPool.Length && pawnPool.Length > 0) return ulong.MaxValue;

        var record = new BotRecord
        {
            botId = GameManager.BotIdBase + nextBotSerial++,
            personality = BotPersonality.Archetype(personalitySerial++)
        };
        bots.Add(record);

        GameManager.Instance.ServerRegisterBot(record.botId, record.personality.displayName);
        return record.botId;
    }

    /// <summary>
    /// Server: fires a bot - host removal, or a late-joining human taking its
    /// seat. Parks the body and clears the roster row.
    /// </summary>
    public void ServerRemoveBot(ulong botId)
    {
        if (!IsServerProcess()) return;

        BotRecord record = FindRecord(botId);
        if (record != null)
        {
            ServerReleaseBody(record);
            bots.Remove(record);
        }
        if (botMonsterId == botId) ServerDespawnBotMonster();

        GameManager.Instance?.ServerUnregisterBot(botId);
    }

    /// <summary>Server: tops the roster up to the fill minimum (GameManager calls this with 4).</summary>
    public void ServerFillTo(int minTotalPlayers)
    {
        if (!IsServerProcess() || GameManager.Instance == null) return;

        int safety = MaxRosterSeats; // the roster grows synchronously per add
        while (GameManager.Instance.Roster.Count < minTotalPlayers && safety-- > 0)
            if (ServerAddBot() == ulong.MaxValue)
                break;
    }

    public bool TryGetBotName(ulong botId, out string name)
    {
        BotRecord record = FindRecord(botId);
        if (record != null)
        {
            name = record.personality.displayName;
            return true;
        }
        name = "";
        return false;
    }

    // ------------------------- Bodies (server) -------------------------

    /// <summary>
    /// Server: gives a bot its survivor body at a spawn point. GameManager
    /// calls this for every seated bot when the lobby forms and after each
    /// rematch reset. Idempotent: an already-embodied bot is just teleported.
    /// </summary>
    public void ServerSpawnSurvivorBot(ulong botId, Vector3 position)
    {
        if (!IsServerProcess()) return;

        BotRecord record = FindRecord(botId);
        if (record == null) return;

        if (record.pawn == null)
        {
            record.pawn = ClaimFreePawn();
            if (record.pawn == null)
            {
                Debug.LogWarning("BotManager: no free pawn in the pool for bot " + botId + ".");
                return;
            }
        }

        record.pawn.ServerAssign(botId, RosterColorIndex(botId), position);
        record.pawn.GetComponent<SurvivorBotBrain>()?.Configure(record.personality);
    }

    /// <summary>
    /// Server: practice mode - the chosen bot morphs into the monster. Its
    /// survivor body vanishes in the smoke; the registered monster prefab is
    /// spawned server-owned with BotDriven set BEFORE Spawn (so it never grabs
    /// a camera), and a MonsterBotBrain starts hunting.
    /// </summary>
    public void ServerSpawnMonsterBot(ulong botId, Vector3 position)
    {
        if (!IsServerProcess() || GameManager.Instance == null) return;

        BotRecord record = FindRecord(botId);
        if (record == null) return;

        ServerReleaseBody(record); // the survivor pawn morphed away
        ServerDespawnBotMonster(); // never two monster bodies

        GameObject prefab = GameManager.Instance.monsterPrefab;
        if (prefab == null)
        {
            Debug.LogWarning("BotManager: GameManager has no monster prefab; practice monster skipped.");
            return;
        }

        GameObject body = Object.Instantiate(prefab, position, Quaternion.identity);

        MonsterController controller = body.GetComponent<MonsterController>();
        if (controller != null) controller.BotDriven = true;

        NetworkObject networkObject = body.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogWarning("BotManager: monster prefab has no NetworkObject; practice monster skipped.");
            Object.Destroy(body);
            return;
        }
        networkObject.Spawn(true);

        // A plain MonoBehaviour may be added post-spawn (NetworkBehaviours may not).
        body.AddComponent<MonsterBotBrain>().ServerConfigure(botId);

        botMonster = networkObject;
        botMonsterId = botId;
    }

    /// <summary>
    /// Server: the rematch reset - every bot body goes back in the pool and
    /// the practice monster despawns. Roster rows survive on purpose; the
    /// GameManager re-queues lobby bodies for every seated bot.
    /// </summary>
    public void ServerDespawnAllPawns()
    {
        if (!IsServerProcess()) return;

        foreach (BotRecord record in bots)
            ServerReleaseBody(record);

        // Defensive sweep: park strays even if their record is already gone.
        foreach (BotPawn pawn in pawnPool)
            if (pawn != null && pawn.IsInUse)
                ReleasePawn(pawn);

        ServerDespawnBotMonster();
    }

    /// <summary>
    /// Server: the hard new-session reset. BotManager is an in-scene
    /// NetworkObject, so a re-host in the same scene inherits the previous
    /// session's hire list and the pool pawns' stale ids; GameManager calls
    /// this once per session start. The rematch path never does - the crew
    /// stays hired between rounds on purpose.
    /// </summary>
    public void ServerResetSession()
    {
        if (!IsServerProcess()) return;
        ServerDespawnAllPawns();
        bots.Clear();
    }

    // ------------------------- Internals (server) -------------------------

    void ServerHandleActorResolved(ulong actorId)
    {
        // Eaten or out the door: either way the body leaves the stage. Stats,
        // killcam, and the death scream were already handled by GameManager.
        if (!GameManager.IsBotId(actorId)) return;

        BotRecord record = FindRecord(actorId);
        if (record != null) ServerReleaseBody(record);
    }

    void ServerReleaseBody(BotRecord record)
    {
        if (record.pawn == null) return;
        ReleasePawn(record.pawn);
        record.pawn = null;
    }

    void ReleasePawn(BotPawn pawn)
    {
        pawn.GetComponent<SurvivorBotBrain>()?.Release();
        pawn.ServerRelease();
    }

    void ServerDespawnBotMonster()
    {
        if (botMonster != null && botMonster.IsSpawned)
            botMonster.Despawn(true);
        botMonster = null;
        botMonsterId = ulong.MaxValue;
    }

    BotPawn ClaimFreePawn()
    {
        foreach (BotPawn pawn in pawnPool)
        {
            if (pawn == null || pawn.IsInUse) continue;
            if (IsClaimed(pawn)) continue;
            return pawn;
        }
        return null;
    }

    bool IsClaimed(BotPawn pawn)
    {
        foreach (BotRecord record in bots)
            if (record.pawn == pawn)
                return true;
        return false;
    }

    BotRecord FindRecord(ulong botId)
    {
        foreach (BotRecord record in bots)
            if (record.botId == botId)
                return record;
        return null;
    }

    int RosterColorIndex(ulong actorId)
    {
        GameManager gm = GameManager.Instance;
        if (gm == null || gm.Roster == null) return 0;
        for (int i = 0; i < gm.Roster.Count; i++)
            if (gm.Roster[i].actorId == actorId)
                return gm.Roster[i].colorIndex;
        return 0;
    }

    static bool IsServerProcess() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
}
