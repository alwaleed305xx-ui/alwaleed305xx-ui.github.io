using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The soul of the game: every chore, footstep and scream funnels through
/// here, and the monster hears whatever the server decides it should.
///
/// Server rules (all enforced here, never on clients):
/// - Hearing radius = 8 + loudness * 52 units. Pings outside the radius are
///   culled on the server and never sent to the monster client.
/// - <see cref="NoiseType.DeathScream"/> is NEVER relayed to the monster.
///   The loudest sound in the house, and the hunter's screen stays quiet -
///   silence after a scream means it's eating (GDD 9.8).
/// - Every noise fires <see cref="OnNoiseVisible"/> on all clients AND on
///   the server/host process. World rings, the self-noise meter and bot
///   sensing all hang off that one event.
///
/// Lives on the same GameObject as the GameManager (see RoundFlowFactory).
/// </summary>
public class NoiseSystem : NetworkBehaviour
{
    public static NoiseSystem Instance { get; private set; }

    /// <summary>Window (seconds) of a victim's noise that counts as its "kill loudness".</summary>
    const float RecentLoudnessWindow = 5f;

    /// <summary>
    /// Monster client only, already radius-culled by the server, DeathScream
    /// suppressed. (sourceActorId, worldPos, loudness, type, label).
    /// </summary>
    public static event System.Action<ulong, Vector3, float, NoiseType, string> OnMonsterHeardNoise;

    /// <summary>
    /// Every client and the server/host process. (sourceActorId, worldPos,
    /// loudness, type). sourceActorId == ulong.MaxValue for house events.
    /// </summary>
    public static event System.Action<ulong, Vector3, float, NoiseType> OnNoiseVisible;

    // Server only: per-actor rolling record of recent noise, for KillLoudness.
    readonly Dictionary<ulong, Queue<RecentNoise>> recentNoise = new Dictionary<ulong, Queue<RecentNoise>>();

    struct RecentNoise
    {
        public double time;
        public float loudness;
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

    /// <summary>Hearing radius in world units for a given loudness (0..1).</summary>
    public static float HearingRadius(float loudness)
    {
        return 8f + Mathf.Clamp01(loudness) * 52f;
    }

    /// <summary>
    /// Client entry point: report a noise you just made. The server decides
    /// who hears it.
    /// </summary>
    public void MakeNoise(ulong sourceActorId, Vector3 position, float loudness, NoiseType type, string label)
    {
        // SECURITY: client-trusted path (friends/invite-first scope, GDD 8.2).
        // Clients can claim any actor id / loudness; acceptable for private
        // lobbies, revisit before public matchmaking.
        MakeNoiseServerRpc(sourceActorId, position, loudness, type, label ?? "");
    }

    [ServerRpc(RequireOwnership = false)]
    void MakeNoiseServerRpc(ulong sourceActorId, Vector3 position, float loudness, NoiseType type, string label)
    {
        ServerMakeNoise(sourceActorId, position, loudness, type, label);
    }

    /// <summary>
    /// Server entry point: bots, house events (TV blare, smoke alarm), and
    /// any server-authored noise. Pass ulong.MaxValue as the actor id for
    /// environmental noise that belongs to nobody.
    /// </summary>
    public void ServerMakeNoise(ulong sourceActorId, Vector3 position, float loudness, NoiseType type, string label)
    {
        if (!IsServer) return;

        loudness = Mathf.Clamp01(loudness);
        label ??= "";

        bool isActor = sourceActorId != ulong.MaxValue;
        if (isActor)
        {
            // Tally toward LOUDEST HUMAN and friends.
            if (GameManager.Instance != null)
                GameManager.Instance.ServerAddStat(sourceActorId, StatKind.NoiseEmitted, loudness);
            ServerRecordRecentNoise(sourceActorId, loudness);
        }

        // Visible to everyone: fire directly for the server process (bots and
        // host-side systems sense through this), then tell remote clients.
        // The ClientRpc skips re-invoking on a host, which already fired here.
        OnNoiseVisible?.Invoke(sourceActorId, position, loudness, type);
        NoiseVisibleClientRpc(sourceActorId, position, loudness, type);

        ServerRelayToMonster(sourceActorId, position, loudness, type, label);
    }

    void ServerRelayToMonster(ulong sourceActorId, Vector3 position, float loudness, NoiseType type, string label)
    {
        // Silence after a kill: the death scream never pings the monster.
        if (type == NoiseType.DeathScream) return;

        GameManager gm = GameManager.Instance;
        if (gm == null) return;

        ulong monsterId = gm.MonsterClientId.Value;
        if (monsterId == ulong.MaxValue) return;
        if (GameManager.IsBotId(monsterId)) return; // bot monsters sense via OnNoiseVisible
        if (monsterId == sourceActorId) return;     // the monster needs no ping for its own racket

        MonsterController monster = MonsterController.ActiveMonster;
        if (monster == null) return;

        // The actual culling: outside the hearing radius, the ping simply
        // does not exist. Clients are never trusted with this decision.
        float distance = Vector3.Distance(monster.transform.position, position);
        if (distance > HearingRadius(loudness)) return;

        var target = new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { monsterId } }
        };
        MonsterHeardClientRpc(sourceActorId, position, loudness, type, label, target);
    }

    [ClientRpc]
    void NoiseVisibleClientRpc(ulong sourceActorId, Vector3 position, float loudness, NoiseType type)
    {
        if (IsServer) return; // host already fired the event directly in ServerMakeNoise
        OnNoiseVisible?.Invoke(sourceActorId, position, loudness, type);
    }

    [ClientRpc]
    void MonsterHeardClientRpc(ulong sourceActorId, Vector3 position, float loudness, NoiseType type, string label, ClientRpcParams rpcParams = default)
    {
        OnMonsterHeardNoise?.Invoke(sourceActorId, position, loudness, type, label);
    }

    // ------------------------- Server bookkeeping -------------------------

    void ServerRecordRecentNoise(ulong actorId, float loudness)
    {
        if (!recentNoise.TryGetValue(actorId, out Queue<RecentNoise> queue))
        {
            queue = new Queue<RecentNoise>();
            recentNoise[actorId] = queue;
        }
        queue.Enqueue(new RecentNoise { time = NetworkManager.ServerTime.Time, loudness = loudness });
        TrimQueue(queue);
    }

    /// <summary>
    /// Server only: total loudness this actor emitted in the last 5 seconds.
    /// The GameManager reads this when the actor dies - it picks the killcam
    /// star and fills the KillLoudness stat.
    /// </summary>
    public float ServerRecentLoudness(ulong actorId)
    {
        if (!IsServer) return 0f;
        if (!recentNoise.TryGetValue(actorId, out Queue<RecentNoise> queue)) return 0f;
        TrimQueue(queue);

        float total = 0f;
        foreach (RecentNoise entry in queue)
            total += entry.loudness;
        return total;
    }

    void TrimQueue(Queue<RecentNoise> queue)
    {
        double cutoff = NetworkManager.ServerTime.Time - RecentLoudnessWindow;
        while (queue.Count > 0 && queue.Peek().time < cutoff)
            queue.Dequeue();
    }

    /// <summary>Server only: wipes recent-noise history. Part of the rematch reset.</summary>
    public void ServerResetRound()
    {
        if (!IsServer) return;
        recentNoise.Clear();
    }
}
