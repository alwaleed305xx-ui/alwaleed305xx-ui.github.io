using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The server-authoritative heart of SCREAMER. Owns the round state machine
///
///     Lobby -> Countdown -> Lockdown -> Playing -> Finale -> Results -> Lobby
///
/// including role selection, lobby pawns, ready/rematch voting, the roster,
/// join-in-progress spectators, win conditions (with the monster-disconnect
/// coward clause), server-side round stats, the results broadcast, and the
/// mandatory rematch-without-restart reset - no scene reload, ever.
///
/// Setup (done automatically by RoundFlowFactory / the editor wizard):
/// one GameObject carrying GameManager + NoiseSystem + NetworkObject, with
/// the survivor and monster prefabs registered in the NetworkManager.
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState : byte { Lobby, Countdown, Lockdown, Playing, Finale, Results }

    /// <summary>First synthetic actor id handed to bots. Humans use NGO client ids.</summary>
    public const ulong BotIdBase = 9000;

    /// <summary>Bots fill the lobby up to this many total actors.</summary>
    public const int MinFillPlayers = 4;

    [Header("Prefabs (must be registered in NetworkManager -> Network Prefabs)")]
    public GameObject survivorPrefab;
    public GameObject monsterPrefab;

    [Header("Spawn points")]
    public Vector3[] survivorSpawnPoints = new Vector3[0];
    public Vector3 monsterSpawnPoint;

    [Header("Round timing (seconds)")]
    public float countdownSeconds = 5f;
    public float lockdownSeconds = 10f;
    public float resultsSeconds = 15f;

    [Header("Lobby")]
    [Tooltip("Bots fill the lobby to MinFillPlayers after this long.")]
    public float botFillDelaySeconds = 10f;

    [Header("Finale")]
    public float finaleSpeedMultiplier = 1.25f;
    public float finaleSpeedBonusSeconds = 8f;

    [Header("Drama")]
    [Tooltip("How many seconds before the public morph the monster privately learns 'IT'S YOU.'")]
    public float monsterRevealLeadSeconds = 1f;
    [Tooltip("Survivors within this many units of a kill hear the 3D death scream.")]
    public float deathScreamRadius = 25f;

    // ------------------------- Synchronized state -------------------------

    /// <summary>
    /// Actor id of the monster. ulong.MaxValue = none chosen yet. Set at the
    /// public morph (lockdown entry), never earlier - the countdown's private
    /// reveal travels as a targeted ClientRpc so the chosen identity is never
    /// on the wire for the other clients before the morph.
    /// Declared BEFORE State on purpose: NetworkVariable deltas apply in
    /// declaration order, so handlers reacting to the Lockdown state change
    /// always read the already-updated monster id.
    /// </summary>
    public NetworkVariable<ulong> MonsterClientId = new NetworkVariable<ulong>(ulong.MaxValue);

    /// <summary>Server-written round state. Clients react via OnClientStateChanged.</summary>
    public NetworkVariable<GameState> State = new NetworkVariable<GameState>(GameState.Lobby);

    /// <summary>Who won. Only meaningful during Results.</summary>
    public NetworkVariable<bool> SurvivorsWon = new NetworkVariable<bool>(false);

    /// <summary>
    /// True when the current Results screen exists because the monster
    /// rage-quit (the coward clause). Nature is healing.
    /// </summary>
    public NetworkVariable<bool> MonsterForfeited = new NetworkVariable<bool>(false);

    /// <summary>
    /// Server time at which the current timed state ends. 0 = no timer.
    /// Drives the countdown numbers, the lockdown HUD and the rematch ring.
    /// </summary>
    public NetworkVariable<double> StateEndsAtServerTime = new NetworkVariable<double>(0);

    /// <summary>One row per actor (humans and bots), synchronized everywhere.</summary>
    public NetworkList<RosterEntry> Roster;

    public bool IAmMonster =>
        NetworkManager.Singleton != null &&
        NetworkManager.Singleton.LocalClientId == MonsterClientId.Value;

    /// <summary>Server-side scoreboard for the current round.</summary>
    public RoundStats Stats { get; } = new RoundStats();

    /// <summary>
    /// Client-side copy of the round outcome, carried inside the Results
    /// broadcast itself. NetworkVariable deltas land a tick after RPCs in NGO,
    /// so anything reacting to OnClientResults must read these two mirrors,
    /// not SurvivorsWon/MonsterForfeited, or it sees last round's values.
    /// </summary>
    public bool ClientSurvivorsWon { get; private set; }

    /// <summary>See <see cref="ClientSurvivorsWon"/>; true when the monster rage-quit.</summary>
    public bool ClientMonsterForfeited { get; private set; }

    // ------------------------- Events -------------------------

    /// <summary>Every client, on every state change (and once on spawn).</summary>
    public static event System.Action<GameState> OnClientStateChanged;

    /// <summary>Every client, once per round, when the Results broadcast lands.</summary>
    public static event System.Action<PlayerRoundResult[]> OnClientResults;

    /// <summary>
    /// Every client, during the countdown: where the public morph will erupt.
    /// A position only - it carries no identity, so the morph FX can stage its
    /// smoke without the monster id ever replicating early.
    /// </summary>
    public static event System.Action<Vector3> OnClientMorphPosition;

    public event System.Action<ulong> OnServerActorCaught;
    public event System.Action<ulong> OnServerActorEscaped;

    // ------------------------- Server-only bookkeeping -------------------------

    readonly HashSet<ulong> caught = new HashSet<ulong>();
    readonly HashSet<ulong> escaped = new HashSet<ulong>();
    readonly HashSet<ulong> roundParticipants = new HashSet<ulong>();
    readonly HashSet<ulong> rematchVotes = new HashSet<ulong>();
    readonly List<ulong> pendingBotPawns = new List<ulong>();
    readonly List<ulong> pendingHumanPawns = new List<ulong>();

    // Names captured at round start, so results can still name actors whose
    // roster row is gone (e.g. the coward-clause monster).
    readonly Dictionary<ulong, string> participantNames = new Dictionary<ulong, string>();

    ulong chosenMonsterActorId = ulong.MaxValue;
    bool monsterRevealed;
    bool pendingMonsterFreeze;
    bool pendingWorldReset;
    int survivorsTotal;
    double roundStartServerTime;
    double lobbyEnteredServerTime;
    float pingRefreshTimer;
    float botFillRetryTimer;
    int victimNameCounter;

    /// <summary>
    /// Server: the server time at which the cellar door blew open this round,
    /// or 0. Photo-finish logic in other systems may compare escapes to this.
    /// </summary>
    public double DoorOpenedServerTime { get; private set; }

    /// <summary>
    /// Server: survivors still unresolved this round (not caught, not escaped,
    /// not disconnected). The finale meter scales by this headcount.
    /// </summary>
    public int LivingSurvivorCount => Mathf.Max(0, survivorsTotal - caught.Count - escaped.Count);

    NetworkVariable<GameState>.OnValueChangedDelegate stateChangedHandler;

    // ------------------------- Lifecycle -------------------------

    void Awake()
    {
        Instance = this;
        Roster = new NetworkList<RosterEntry>();
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        stateChangedHandler = (_, next) => OnClientStateChanged?.Invoke(next);
        State.OnValueChanged += stateChangedHandler;

        if (IsServer)
        {
            // GameManager is an in-scene NetworkObject: NetworkManager.Shutdown
            // neither destroys it nor resets its NetworkVariables, so a re-host
            // in the same scene would inherit the previous session's mid-round
            // state and seat everyone as spectators forever. Every new session
            // therefore starts from a hard lobby reset.
            ServerResetSessionState();

            NetworkManager.OnClientConnectedCallback += ServerOnClientConnected;
            NetworkManager.OnClientDisconnectCallback += ServerOnClientDisconnected;
            lobbyEnteredServerTime = NetworkManager.ServerTime.Time;

            // The host (and anyone who connected before this object spawned)
            // is already in the building - seat them. Roster rows are added
            // immediately; pawn spawning is deferred to Update so no
            // NetworkObject spawns while the scene itself is still spawning.
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
                ServerOnClientConnected(clientId);
        }

#if SCREAMER_STEAM
        // Tell the server what Steam calls us, so the roster shows personas
        // instead of "Victim N". Truncated locally so a long persona can
        // never overflow the fixed-size wire string.
        if (IsClient && SteamIntegration.IsAvailable)
            SubmitDisplayNameServerRpc(new FixedString64Bytes(TruncateName(SteamIntegration.PersonaName)));
#endif

        // Late joiners (and the host) sync their UI to the current state.
        OnClientStateChanged?.Invoke(State.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (stateChangedHandler != null)
        {
            State.OnValueChanged -= stateChangedHandler;
            stateChangedHandler = null;
        }
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= ServerOnClientConnected;
            NetworkManager.OnClientDisconnectCallback -= ServerOnClientDisconnected;
        }
    }

    /// <summary>
    /// Server: wipes everything a previous session in this scene may have left
    /// behind (roster rows for clients that no longer exist, a mid-round State,
    /// stale win flags and bookkeeping) so the new session opens on a clean
    /// lobby. World objects (tasks/door/killcam) reset on the first Update
    /// tick, once all in-scene NetworkObjects have finished spawning.
    /// </summary>
    void ServerResetSessionState()
    {
        caught.Clear();
        escaped.Clear();
        roundParticipants.Clear();
        rematchVotes.Clear();
        pendingHumanPawns.Clear();
        pendingBotPawns.Clear();
        participantNames.Clear();
        Roster.Clear();
        Stats.Reset();

        chosenMonsterActorId = ulong.MaxValue;
        monsterRevealed = false;
        pendingMonsterFreeze = false;
        survivorsTotal = 0;
        roundStartServerTime = 0;
        DoorOpenedServerTime = 0;
        victimNameCounter = 0;
        ClientSurvivorsWon = false;
        ClientMonsterForfeited = false;

        MonsterClientId.Value = ulong.MaxValue;
        SurvivorsWon.Value = false;
        MonsterForfeited.Value = false;
        StateEndsAtServerTime.Value = 0;
        ServerSetState(GameState.Lobby);

        pendingWorldReset = true;
    }

    void Update()
    {
        if (!IsSpawned || !IsServer) return;

        if (pendingWorldReset)
        {
            // Deferred from ServerResetSessionState: by the first Update every
            // in-scene NetworkObject has spawned, so their server resets stick.
            pendingWorldReset = false;
            NoiseSystem.Instance?.ServerResetRound();
            TaskManager.Instance?.ServerResetAll();
            EscapeDoor.Instance?.ServerReset();
            KillcamRecorder.Instance?.ServerReset();
            BotManager.Instance?.ServerResetSession();
        }

        double now = NetworkManager.ServerTime.Time;

        switch (State.Value)
        {
            case GameState.Lobby:
                ServerLobbyTick(now);
                break;

            case GameState.Countdown:
                if (!monsterRevealed && now >= StateEndsAtServerTime.Value - monsterRevealLeadSeconds)
                {
                    // The monster privately learns "IT'S YOU." one second
                    // before the public morph - via a ClientRpc targeted at
                    // the chosen client only. The public MonsterClientId is
                    // written at lockdown entry, so no other client (modified
                    // or sniffing) can read the identity before the morph.
                    monsterRevealed = true;
                    ServerSendEarlyReveal();
                }
                if (now >= StateEndsAtServerTime.Value)
                    ServerEnterLockdown();
                break;

            case GameState.Lockdown:
                ServerApplyDeferredMonsterFreeze(now);
                if (now >= StateEndsAtServerTime.Value)
                    ServerEnterPlaying();
                break;

            case GameState.Results:
                if (now >= StateEndsAtServerTime.Value || ServerRematchMajorityReached())
                    ServerResetToLobby();
                break;
        }

        ServerProcessPendingHumanPawns();
        ServerProcessPendingBotPawns();
        ServerRefreshPings();
    }

    void ServerSendEarlyReveal()
    {
        if (chosenMonsterActorId == ulong.MaxValue || IsBotId(chosenMonsterActorId)) return;

        var target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { chosenMonsterActorId } }
        };
        MonsterEarlyRevealClientRpc(target);
    }

    // ------------------------- Identity helpers -------------------------

    public static bool IsBotId(ulong actorId) => actorId >= BotIdBase && actorId != ulong.MaxValue;

    /// <summary>
    /// Display name for any actor. Works on every client - names live in the
    /// synchronized roster.
    /// </summary>
    public string ActorName(ulong actorId)
    {
        if (actorId == ulong.MaxValue) return "The House";

        int index = FindRosterIndex(actorId);
        if (index >= 0) return Roster[index].name.ToString();

        if (IsBotId(actorId))
        {
            if (IsServer && BotManager.Instance != null &&
                BotManager.Instance.TryGetBotName(actorId, out string botName))
                return botName;
            return "[BOT] " + (actorId - BotIdBase + 1);
        }
        return "Victim " + actorId;
    }

    int FindRosterIndex(ulong actorId)
    {
        for (int i = 0; i < Roster.Count; i++)
            if (Roster[i].actorId == actorId)
                return i;
        return -1;
    }

    int CountHumans()
    {
        int n = 0;
        for (int i = 0; i < Roster.Count; i++)
            if (!Roster[i].isBot) n++;
        return n;
    }

    int NextFreeColorIndex()
    {
        const int paletteSize = 7; // survivor colors in ScreamerPalette, join order
        var used = new bool[paletteSize];
        for (int i = 0; i < Roster.Count; i++)
        {
            int c = Roster[i].colorIndex;
            if (c >= 0 && c < paletteSize) used[c] = true;
        }
        for (int c = 0; c < paletteSize; c++)
            if (!used[c]) return c;
        return Roster.Count % paletteSize;
    }

    Vector3 SpawnPointFor(ulong actorId)
    {
        if (survivorSpawnPoints == null || survivorSpawnPoints.Length == 0)
            return Vector3.zero;
        int index = FindRosterIndex(actorId);
        if (index < 0) index = (int)(actorId % (ulong)survivorSpawnPoints.Length);
        return survivorSpawnPoints[index % survivorSpawnPoints.Length];
    }

    // ------------------------- Join / leave (server) -------------------------

    void ServerOnClientConnected(ulong clientId)
    {
        if (!IsServer) return;
        if (FindRosterIndex(clientId) >= 0) return;

        victimNameCounter++;
        Roster.Add(new RosterEntry
        {
            actorId = clientId,
            name = new FixedString64Bytes("Victim " + victimNameCounter),
            ready = false,
            isBot = false,
            colorIndex = NextFreeColorIndex(),
            pingMs = 0f
        });

        if (State.Value == GameState.Lobby)
        {
            pendingHumanPawns.Add(clientId); // spawned next Update tick
            AnnounceClientRpc(GameCopy.EventJoin(ActorName(clientId)));
            ServerReplaceBotWithHuman();
        }
        else
        {
            // Join-in-progress: the newcomer gets a roster row and an announce,
            // but no pawn and no round role - they watch from the fixed scene
            // camera until the round ends. ServerResetToLobby seats every
            // CONNECTED client (not just round participants), so the next
            // lobby picks them up automatically.
            AnnounceClientRpc(GameCopy.EventSpectating(ActorName(clientId)));
        }
    }

    void ServerOnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;

        bool wasMonster = clientId == chosenMonsterActorId;
        bool activeRound = State.Value == GameState.Countdown || State.Value == GameState.Lockdown ||
                           State.Value == GameState.Playing || State.Value == GameState.Finale;

        string name = ActorName(clientId);

        int index = FindRosterIndex(clientId);
        if (index >= 0) Roster.RemoveAt(index);
        rematchVotes.Remove(clientId);

        // If the leaver was mid-chore, their client can no longer send the
        // "sound off" RPC - silence any station loop they left running.
        TaskManager.Instance?.ServerClearSoundForClient(clientId);

        if (wasMonster && activeRound)
        {
            // The coward clause: the monster unplugged. Survivors win.
            MonsterForfeited.Value = true;
            AnnounceClientRpc(GameCopy.MonsterQuit);
            ServerEnterResults(true);
            return;
        }

        AnnounceClientRpc(GameCopy.EventLeave(name));

        if (activeRound && roundParticipants.Remove(clientId) && !caught.Contains(clientId) && !escaped.Contains(clientId))
        {
            // An unresolved survivor walked out; the remaining headcount shrinks.
            survivorsTotal--;
            ServerCheckWinConditions();
        }
    }

    void ServerReplaceBotWithHuman()
    {
        // Late lobby join replaces a bot if one exists and the room is
        // already at (or past) the fill minimum.
        if (BotManager.Instance == null) return;
        if (Roster.Count <= MinFillPlayers) return;

        for (int i = Roster.Count - 1; i >= 0; i--)
        {
            if (!Roster[i].isBot) continue;
            ulong botId = Roster[i].actorId;
            string botName = Roster[i].name.ToString();
            BotManager.Instance.ServerRemoveBot(botId);
            AnnounceClientRpc(GameCopy.EventBotLeft(botName));
            return;
        }
    }

    // ------------------------- Bot roster API -------------------------

    /// <summary>Server: BotManager registers each bot here to get a roster row.</summary>
    public void ServerRegisterBot(ulong botId, string botName)
    {
        if (!IsServer || FindRosterIndex(botId) >= 0) return;

        Roster.Add(new RosterEntry
        {
            actorId = botId,
            name = new FixedString64Bytes(TruncateName(botName)),
            ready = true, // bots are always up for it
            isBot = true,
            colorIndex = NextFreeColorIndex(),
            pingMs = 0f
        });

        // Give the bot a walkable lobby pawn. Deferred one frame so we never
        // re-enter BotManager while it is still mid-registration.
        if (State.Value == GameState.Lobby || State.Value == GameState.Countdown)
            pendingBotPawns.Add(botId);
    }

    /// <summary>Server: removes a bot's roster row (pawn cleanup is BotManager's job).</summary>
    public void ServerUnregisterBot(ulong botId)
    {
        if (!IsServer) return;
        int index = FindRosterIndex(botId);
        if (index >= 0) Roster.RemoveAt(index);
        pendingBotPawns.Remove(botId);
        if (roundParticipants.Remove(botId) && !caught.Contains(botId) && !escaped.Contains(botId))
        {
            survivorsTotal--;
            ServerCheckWinConditions();
        }
    }

    void ServerProcessPendingHumanPawns()
    {
        if (pendingHumanPawns.Count == 0) return;
        if (State.Value != GameState.Lobby && State.Value != GameState.Countdown)
        {
            pendingHumanPawns.Clear(); // round already running; they spectate
            return;
        }

        for (int i = 0; i < pendingHumanPawns.Count; i++)
        {
            ulong clientId = pendingHumanPawns[i];
            if (FindRosterIndex(clientId) < 0) continue; // left before seating
            ServerSpawnLobbyPawn(clientId);
        }
        pendingHumanPawns.Clear();
    }

    void ServerProcessPendingBotPawns()
    {
        if (pendingBotPawns.Count == 0 || BotManager.Instance == null) return;
        if (State.Value != GameState.Lobby && State.Value != GameState.Countdown) return;

        for (int i = 0; i < pendingBotPawns.Count; i++)
        {
            ulong botId = pendingBotPawns[i];
            if (botId == chosenMonsterActorId) continue; // morphs at countdown zero instead
            if (FindRosterIndex(botId) < 0) continue;    // removed before it got a body
            BotManager.Instance.ServerSpawnSurvivorBot(botId, SpawnPointFor(botId));
        }
        pendingBotPawns.Clear();
    }

    // ------------------------- Lobby (server) -------------------------

    void ServerLobbyTick(double now)
    {
        if (now - lobbyEnteredServerTime < botFillDelaySeconds) return;
        if (Roster.Count >= MinFillPlayers) return;

        botFillRetryTimer -= Time.deltaTime;
        if (botFillRetryTimer > 0f) return;
        botFillRetryTimer = 1f;

        BotManager.Instance?.ServerFillTo(MinFillPlayers);
    }

    void ServerSpawnLobbyPawn(ulong clientId)
    {
        if (survivorPrefab == null) return;

        if (NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
            client.PlayerObject != null)
            client.PlayerObject.Despawn(true);

        GameObject pawn = Instantiate(survivorPrefab, SpawnPointFor(clientId), Quaternion.identity);
        pawn.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId);

        // Stamp the roster's join-order identity color onto the pawn (cap,
        // rim light); the NetworkVariable replicates it to every client.
        PlayerController controller = pawn.GetComponent<PlayerController>();
        if (controller != null)
        {
            int index = FindRosterIndex(clientId);
            controller.ColorIndex.Value = index >= 0 ? Roster[index].colorIndex : 0;
        }
    }

    // NetworkTransport.GetCurrentRtt wants transport-level connection ids and
    // NGO client ids are NOT those (the mapping is private to NetworkManager),
    // so the roster ping is measured with a tiny RPC echo instead: every two
    // seconds the server stamps its clock into a broadcast, each client echoes
    // the stamp straight back, and the round trip lands in that client's
    // roster row. Transport-agnostic, so it also works on the Steam backend.
    void ServerRefreshPings()
    {
        pingRefreshTimer += Time.deltaTime;
        if (pingRefreshTimer < 2f) return;
        pingRefreshTimer = 0f;

        ServerSetRosterPing(NetworkManager.LocalClientId, 0f); // the host IS the server
        PingProbeClientRpc(Time.realtimeSinceStartupAsDouble);
    }

    void ServerSetRosterPing(ulong clientId, float pingMs)
    {
        int index = FindRosterIndex(clientId);
        if (index < 0) return;

        RosterEntry entry = Roster[index];
        if (entry.isBot) return;
        if ((int)entry.pingMs == (int)pingMs) return; // only dirty the list on a whole-ms change

        entry.pingMs = pingMs;
        Roster[index] = entry;
    }

    [ClientRpc]
    void PingProbeClientRpc(double serverRealtime)
    {
        // The stamp is opaque to the client; it just bounces it back.
        if (!IsServer) PingEchoServerRpc(serverRealtime);
    }

    [ServerRpc(RequireOwnership = false)]
    void PingEchoServerRpc(double serverRealtime, ServerRpcParams p = default)
    {
        float rttMs = (float)((Time.realtimeSinceStartupAsDouble - serverRealtime) * 1000.0);
        ServerSetRosterPing(p.Receive.SenderClientId, Mathf.Clamp(rttMs, 0f, 999f));
    }

    // ------------------------- Client -> server RPCs -------------------------

    [ServerRpc(RequireOwnership = false)]
    public void SetReadyServerRpc(bool ready, ServerRpcParams p = default)
    {
        if (State.Value != GameState.Lobby) return;
        int index = FindRosterIndex(p.Receive.SenderClientId);
        if (index < 0) return;

        RosterEntry entry = Roster[index];
        entry.ready = ready;
        Roster[index] = entry;
    }

    [ServerRpc(RequireOwnership = false)]
    public void VoteRematchServerRpc(ServerRpcParams p = default)
    {
        if (State.Value != GameState.Results) return;

        ulong sender = p.Receive.SenderClientId;
        if (!rematchVotes.Add(sender)) return;

        // Mirror the vote into the roster's ready flag so every client can
        // render "x/8 want a sequel" straight from the roster.
        int index = FindRosterIndex(sender);
        if (index >= 0)
        {
            RosterEntry entry = Roster[index];
            entry.ready = true;
            Roster[index] = entry;
        }

        if (ServerRematchMajorityReached())
            ServerResetToLobby();
    }

    [ServerRpc(RequireOwnership = false)]
    public void ReportStatServerRpc(StatKind kind, float amount, ServerRpcParams p = default)
    {
        // SECURITY: client-trusted stat reporting (friends/invite-first scope,
        // GDD 8.2). Fine for private lobbies; validate before matchmaking.
        Stats.Add(p.Receive.SenderClientId, kind, amount);
        ServerAnnounceStatGag(p.Receive.SenderClientId, kind);
    }

    [ServerRpc(RequireOwnership = false)]
    void SubmitDisplayNameServerRpc(FixedString64Bytes displayName, ServerRpcParams p = default)
    {
        string name = TruncateName(displayName.ToString());
        if (string.IsNullOrWhiteSpace(name)) return;

        int index = FindRosterIndex(p.Receive.SenderClientId);
        if (index < 0) return;

        RosterEntry entry = Roster[index];
        entry.name = new FixedString64Bytes(name);
        Roster[index] = entry;
    }

    static string TruncateName(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        s = s.Trim();
        if (s.Length > 24) s = s.Substring(0, 24);
        // Keep the UTF-8 form inside a FixedString64Bytes payload (61 bytes).
        while (s.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(s) > 60)
            s = s.Substring(0, s.Length - 1);
        return s;
    }

    bool ServerRematchMajorityReached()
    {
        int humans = CountHumans();
        return humans > 0 && rematchVotes.Count * 2 > humans;
    }

    // ------------------------- Round flow (server) -------------------------

    /// <summary>
    /// Host presses START ROUND. Lobby -> Countdown. One human is enough -
    /// bots fill the rest (the old 2-player gate is gone on purpose).
    /// </summary>
    public void StartRound()
    {
        if (!IsServer || State.Value != GameState.Lobby) return;
        if (NetworkManager.ConnectedClientsIds.Count < 1) return;

        // Guarantee a full house before roles are dealt.
        if (Roster.Count < MinFillPlayers)
            BotManager.Instance?.ServerFillTo(MinFillPlayers);

        // A hunt needs a monster AND at least one survivor. With no BotManager
        // in the scene the fill above is a no-op, and a lone host would start
        // a round with survivorsTotal == 0 that no catch/escape/disconnect
        // event could ever finish. Refuse instead of soft-locking.
        if (Roster.Count < 2) return;

        // Snapshot who is actually playing this round.
        roundParticipants.Clear();
        participantNames.Clear();
        caught.Clear();
        escaped.Clear();
        for (int i = 0; i < Roster.Count; i++)
        {
            roundParticipants.Add(Roster[i].actorId);
            participantNames[Roster[i].actorId] = Roster[i].name.ToString();
        }

        chosenMonsterActorId = ServerPickMonster();
        monsterRevealed = false;
        pendingMonsterFreeze = false;
        survivorsTotal = roundParticipants.Count - 1;

        Stats.Reset();
        NoiseSystem.Instance?.ServerResetRound();
        SurvivorsWon.Value = false;
        MonsterForfeited.Value = false;

        ServerSetState(GameState.Countdown);
        StateEndsAtServerTime.Value = NetworkManager.ServerTime.Time + countdownSeconds;
    }

    ulong ServerPickMonster()
    {
        // Random among humans when at least two are present; otherwise any
        // actor may be it (practice mode: bot monster allowed).
        var humans = new List<ulong>();
        var everyone = new List<ulong>();
        foreach (ulong actorId in roundParticipants)
        {
            everyone.Add(actorId);
            if (!IsBotId(actorId)) humans.Add(actorId);
        }

        List<ulong> pool = humans.Count >= 2 ? humans : everyone;
        return pool.Count > 0 ? pool[Random.Range(0, pool.Count)] : ulong.MaxValue;
    }

    void ServerEnterLockdown()
    {
        double now = NetworkManager.ServerTime.Time;
        roundStartServerTime = now;

        // The public reveal: the monster id replicates now, with the state
        // change (declaration order guarantees the id applies first).
        MonsterClientId.Value = chosenMonsterActorId;

        // The morph FX wants the chosen pawn's last position; the RPC is sent
        // before the despawn below so the spot outruns the vanish. Pawns are
        // frozen through the countdown, so this is where everyone saw them.
        if (TryGetActorPosition(chosenMonsterActorId, out Vector3 morphAt))
            MorphPositionClientRpc(morphAt);

        // Countdown zero: the chosen pawn is despawned in full public view
        // and the monster body appears in the garage, frozen for the head
        // start. The red-smoke morph itself is client-side theater.
        if (chosenMonsterActorId != ulong.MaxValue)
        {
            if (!IsBotId(chosenMonsterActorId))
            {
                if (NetworkManager.ConnectedClients.TryGetValue(chosenMonsterActorId, out NetworkClient client) &&
                    client.PlayerObject != null)
                    client.PlayerObject.Despawn(true);

                if (monsterPrefab != null)
                {
                    GameObject monster = Instantiate(monsterPrefab, monsterSpawnPoint, Quaternion.identity);
                    monster.GetComponent<NetworkObject>().SpawnAsPlayerObject(chosenMonsterActorId);
                    monster.GetComponent<MonsterController>()?.ServerFreeze(lockdownSeconds);
                }
            }
            else
            {
                BotManager.Instance?.ServerSpawnMonsterBot(chosenMonsterActorId, monsterSpawnPoint);
                pendingMonsterFreeze = true; // frozen as soon as the pawn reports in
            }
        }

        ServerSetState(GameState.Lockdown);
        StateEndsAtServerTime.Value = now + lockdownSeconds;

        // A survivor who disconnected during the countdown shrank the
        // headcount while ServerCheckWinConditions still refused to run
        // (state was Countdown). Re-check now that the hunt is on, so an
        // already-empty round resolves instead of running forever.
        ServerCheckWinConditions();
    }

    void ServerApplyDeferredMonsterFreeze(double now)
    {
        if (!pendingMonsterFreeze) return;
        MonsterController monster = MonsterController.ActiveMonster;
        if (monster == null) return;

        pendingMonsterFreeze = false;
        float remaining = (float)(StateEndsAtServerTime.Value - now);
        if (remaining > 0f) monster.ServerFreeze(remaining);
    }

    void ServerEnterPlaying()
    {
        ServerSetState(GameState.Playing);
        StateEndsAtServerTime.Value = 0;
    }

    /// <summary>
    /// Called by EscapeDoor when the sixth padlock shatters. Map-wide mega
    /// ping, +25% monster speed for 8 seconds, and the door gather begins.
    /// </summary>
    public void ServerEnterFinale()
    {
        if (!IsServer || State.Value != GameState.Playing) return;

        ServerSetState(GameState.Finale);
        StateEndsAtServerTime.Value = 0;

        // The house itself screams the location. Loudness 1.0 = 60 units =
        // the whole map hears it, monster included.
        NoiseSystem.Instance?.ServerMakeNoise(
            ulong.MaxValue, HouseLayout.CellarDoor, 1f, NoiseType.Alarm,
            GameCopy.NoiseFinale);

        MonsterController.ActiveMonster?.ServerApplySpeedBonus(finaleSpeedMultiplier, finaleSpeedBonusSeconds);
    }

    /// <summary>Called by EscapeDoor when the shared scream meter fills and the door blows open.</summary>
    public void ServerDoorOpened()
    {
        if (!IsServer || State.Value != GameState.Finale) return;
        DoorOpenedServerTime = NetworkManager.ServerTime.Time;
    }

    // ------------------------- Catch / escape (server) -------------------------

    public void ServerActorCaught(ulong actorId)
    {
        if (!IsServer) return;
        if (State.Value != GameState.Lockdown && State.Value != GameState.Playing && State.Value != GameState.Finale) return;
        if (!roundParticipants.Contains(actorId) || actorId == chosenMonsterActorId) return;
        if (escaped.Contains(actorId) || !caught.Add(actorId)) return;

        float roundTime = (float)(NetworkManager.ServerTime.Time - roundStartServerTime);
        float recentLoudness = NoiseSystem.Instance != null ? NoiseSystem.Instance.ServerRecentLoudness(actorId) : 0f;

        Stats.Add(actorId, StatKind.DeathTime, roundTime);
        Stats.Add(actorId, StatKind.KillLoudness, recentLoudness);

        // Caption context 0 = "infer it": the recorder derives the GDD 11.8
        // joke (scream station, bathroom, sprinting, Mimic round) from the
        // clip itself whenever no richer context id is passed. Nothing in the
        // project currently passes one.
        KillcamRecorder.Instance?.ServerNotifyKill(actorId, recentLoudness, 0);

        if (TryGetActorPosition(actorId, out Vector3 position))
        {
            // The death scream: real, loud, 3D for anyone nearby - and
            // deliberately absent from the monster's HUD (NoiseSystem
            // suppresses DeathScream pings).
            NoiseSystem.Instance?.ServerMakeNoise(actorId, position, 1f, NoiseType.DeathScream, "");
            DeathScreamClientRpc(position);
        }

        AnnounceClientRpc(GameCopy.EventCaught(ActorName(actorId)));
        KillTallyClientRpc();
        OnServerActorCaught?.Invoke(actorId);
        ServerCheckWinConditions();
    }

    public void ServerActorEscaped(ulong actorId)
    {
        if (!IsServer) return;
        if (State.Value != GameState.Playing && State.Value != GameState.Finale) return;
        if (!roundParticipants.Contains(actorId) || actorId == chosenMonsterActorId) return;
        if (caught.Contains(actorId) || !escaped.Add(actorId)) return;

        // GDD 11.5: the one survivor who gets out while everyone else died
        // earns the "final girl behavior" line instead of the generic brag.
        bool soleEscape = escaped.Count == 1 && survivorsTotal > 0 &&
                          caught.Count + escaped.Count >= survivorsTotal;
        AnnounceClientRpc(soleEscape
            ? GameCopy.EventSoleEscape(ActorName(actorId))
            : GameCopy.EventEscaped(ActorName(actorId)));
        OnServerActorEscaped?.Invoke(actorId);
        ServerCheckWinConditions();
    }

    public void ServerAddStat(ulong actorId, StatKind kind, float amount)
    {
        if (!IsServer) return;
        Stats.Add(actorId, kind, amount);
        ServerAnnounceStatGag(actorId, kind);
    }

    /// <summary>
    /// The public-shaming feed (GDD 11.5): burns, wrong notes and innocent
    /// slaps go on every client's event feed the moment the stat lands, for
    /// humans and bots alike. All other stat kinds stay silent.
    /// </summary>
    void ServerAnnounceStatGag(ulong actorId, StatKind kind)
    {
        switch (kind)
        {
            case StatKind.NoodleBurns:
                AnnounceClientRpc(GameCopy.EventNoodleBurn(ActorName(actorId)));
                break;
            case StatKind.WrongNotes:
                AnnounceClientRpc(GameCopy.EventWrongNote(ActorName(actorId)));
                break;
            case StatKind.FurnitureSlaps:
                AnnounceClientRpc(GameCopy.EventInnocentSlap(ActorName(actorId)));
                break;
        }
    }

    /// <summary>Server: drops one line on every client's event feed.</summary>
    public void ServerAnnounce(string line)
    {
        if (IsServer && !string.IsNullOrEmpty(line)) AnnounceClientRpc(line);
    }

    void ServerCheckWinConditions()
    {
        if (State.Value != GameState.Lockdown && State.Value != GameState.Playing && State.Value != GameState.Finale) return;

        if (survivorsTotal <= 0)
        {
            // Everyone else vanished mid-round; whoever got out decides it.
            ServerEnterResults(escaped.Count > 0);
            return;
        }

        if (escaped.Count > 0 && escaped.Count + caught.Count >= survivorsTotal)
        {
            ServerEnterResults(true);  // at least one got out, all resolved
        }
        else if (caught.Count >= survivorsTotal)
        {
            ServerEnterResults(false); // everybody died :)
        }
    }

    bool TryGetActorPosition(ulong actorId, out Vector3 position)
    {
        if (!IsBotId(actorId))
        {
            if (NetworkManager.ConnectedClients.TryGetValue(actorId, out NetworkClient client) &&
                client.PlayerObject != null)
            {
                position = client.PlayerObject.transform.position;
                return true;
            }
        }
        else
        {
            foreach (BotPawn pawn in BotPawn.All)
            {
                if (pawn != null && pawn.BotId == actorId)
                {
                    position = pawn.transform.position;
                    return true;
                }
            }
        }
        position = Vector3.zero;
        return false;
    }

    // ------------------------- Results (server) -------------------------

    void ServerEnterResults(bool survivorsWin)
    {
        if (!IsServer || State.Value == GameState.Results) return;

        SurvivorsWon.Value = survivorsWin;

        // Stamp identity and outcome for every participant, then bake the report.
        foreach (ulong actorId in roundParticipants)
        {
            string name = FindRosterIndex(actorId) >= 0 ? ActorName(actorId)
                : participantNames.TryGetValue(actorId, out string captured) ? captured
                : ActorName(actorId);
            Stats.SetIdentity(actorId, name, IsBotId(actorId));
            bool wasMonster = actorId == chosenMonsterActorId;
            float deathTime = caught.Contains(actorId) ? Stats.Get(actorId, StatKind.DeathTime) : -1f;
            Stats.SetOutcome(actorId, wasMonster, escaped.Contains(actorId), deathTime);
        }
        PlayerRoundResult[] results = Stats.BuildResults();

        rematchVotes.Clear();
        ServerSetAllReadyFlags(humansReady: false, botsReady: false); // ready = rematch vote here

        ServerSetState(GameState.Results);
        StateEndsAtServerTime.Value = NetworkManager.ServerTime.Time + resultsSeconds;

        KillcamRecorder.Instance?.ServerBroadcastBestClip();

        // The outcome rides INSIDE the RPC. NetworkVariable deltas are sent on
        // the next tick while RPCs queue immediately, so a client handling
        // this RPC would otherwise still read LAST round's SurvivorsWon.
        ResultsClientRpc(survivorsWin, MonsterForfeited.Value, results);
    }

    // ------------------------- Rematch reset (server) -------------------------

    /// <summary>
    /// The mandatory rematch-without-restart reset: despawn every pawn, clear
    /// all round state, reset tasks / door / killcam / stats, and respawn
    /// lobby pawns. No scene reload, no app restart.
    /// </summary>
    void ServerResetToLobby()
    {
        if (!IsServer) return;

        // 1. Every player-owned NetworkObject goes away (survivor pawns,
        //    the monster body, ghosts riding the pawn objects).
        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
        {
            if (NetworkManager.ConnectedClients.TryGetValue(clientId, out NetworkClient client) &&
                client.PlayerObject != null)
                client.PlayerObject.Despawn(true);
        }
        BotManager.Instance?.ServerDespawnAllPawns();

        // 2. Round bookkeeping back to zero.
        caught.Clear();
        escaped.Clear();
        roundParticipants.Clear();
        rematchVotes.Clear();
        pendingHumanPawns.Clear();
        pendingBotPawns.Clear();
        chosenMonsterActorId = ulong.MaxValue;
        MonsterClientId.Value = ulong.MaxValue;
        monsterRevealed = false;
        pendingMonsterFreeze = false;
        survivorsTotal = 0;
        DoorOpenedServerTime = 0;
        SurvivorsWon.Value = false;
        MonsterForfeited.Value = false;
        Stats.Reset();
        NoiseSystem.Instance?.ServerResetRound();

        // 3. The world resets in place.
        TaskManager.Instance?.ServerResetAll();
        EscapeDoor.Instance?.ServerReset();
        KillcamRecorder.Instance?.ServerReset();

        // 4. Back to the den.
        ServerSetAllReadyFlags(humansReady: false, botsReady: true);
        ServerSetState(GameState.Lobby);
        StateEndsAtServerTime.Value = 0;
        lobbyEnteredServerTime = NetworkManager.ServerTime.Time;

        foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            ServerSpawnLobbyPawn(clientId);

        for (int i = 0; i < Roster.Count; i++)
            if (Roster[i].isBot)
                pendingBotPawns.Add(Roster[i].actorId);
    }

    void ServerSetAllReadyFlags(bool humansReady, bool botsReady)
    {
        for (int i = 0; i < Roster.Count; i++)
        {
            RosterEntry entry = Roster[i];
            bool target = entry.isBot ? botsReady : humansReady;
            if (entry.ready == target) continue;
            entry.ready = target;
            Roster[i] = entry;
        }
    }

    void ServerSetState(GameState next)
    {
        if (State.Value == next) return;
        State.Value = next;
    }

    // ------------------------- Client RPCs -------------------------

    [ClientRpc]
    void AnnounceClientRpc(string line)
    {
        GameUI.Instance?.ShowEvent(line);
    }

    [ClientRpc]
    void ResultsClientRpc(bool survivorsWin, bool monsterForfeited, PlayerRoundResult[] results)
    {
        ClientSurvivorsWon = survivorsWin;
        ClientMonsterForfeited = monsterForfeited;
        OnClientResults?.Invoke(results);
    }

    [ClientRpc]
    void MonsterEarlyRevealClientRpc(ClientRpcParams rpcParams = default)
    {
        // Targeted at the chosen client only: nobody else ever receives it.
        GameUI.Instance?.ShowRoleReveal(true, -1);
    }

    [ClientRpc]
    void MorphPositionClientRpc(Vector3 at)
    {
        OnClientMorphPosition?.Invoke(at);
    }

    [ClientRpc]
    void KillTallyClientRpc()
    {
        // One more stroke on the monster's HUD tally. Everyone receives the
        // RPC; only the monster's own screen keeps score.
        if (IAmMonster && GameUI.Instance != null)
            GameUI.Instance.AddKillTally();
    }

    [ClientRpc]
    void DeathScreamClientRpc(Vector3 position)
    {
        // Anyone close enough hears the real 3D scream. The audio listener
        // rides the camera, so the camera is the ear.
        Camera cam = Camera.main;
        if (cam == null) return;
        if (Vector3.Distance(cam.transform.position, position) > deathScreamRadius) return;
        AudioDirector.Instance?.Play(Sfx.Scream, position);
    }
}
