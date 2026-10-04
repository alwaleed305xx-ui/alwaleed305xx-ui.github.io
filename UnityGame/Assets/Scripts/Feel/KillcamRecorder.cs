using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The killcam's server half (GDD 12.2, plan of record): a lightweight ring
/// buffer - position and yaw of every living survivor plus the monster,
/// sampled at 10 Hz, 6 seconds deep. When the GameManager reports a kill, the
/// victim's recent loudness decides whether this becomes the round's starring
/// clip ("the loudest mistake"); at Results the best clip is broadcast once
/// and every client's <see cref="KillcamPlayer"/> can restage it on proxy
/// pawns in the real map. No frame capture, no risk, ships in v1.
///
/// Also infers the caption context (GDD 11.8) when the caller passes the
/// default: scream-therapy kills, bathroom kills, sprint kills and Mimic
/// rounds each get their own joke.
///
/// Lives on an in-scene NetworkObject created by FeelFactory.
/// </summary>
public class KillcamRecorder : NetworkBehaviour
{
    public static KillcamRecorder Instance { get; private set; }

    /// <summary>Every client, once per round, when the Results clip lands.</summary>
    public static event System.Action<KillcamClip> OnClientClipReady;

    /// <summary>Ring buffer shape: 10 Hz for 6 seconds (GDD 12.2).</summary>
    public const float SampleInterval = 0.1f;
    public const int BufferSamples = 60;

    [Header("Caption inference (used when the kill reports context 0)")]
    [Tooltip("A victim this close to the Screaming Closet station died mid-therapy.")]
    public float screamStationRadius = 3.5f;
    [Tooltip("A victim moving faster than this in its last sampled step was sprinting.")]
    public float sprintSpeedThreshold = 6f;

    class TrackBuffer
    {
        public readonly Vector3[] positions = new Vector3[BufferSamples];
        public readonly float[] yaws = new float[BufferSamples];
        public int head;  // next write slot
        public int count; // filled samples, up to BufferSamples

        public void Push(Vector3 position, float yaw)
        {
            positions[head] = position;
            yaws[head] = yaw;
            head = (head + 1) % BufferSamples;
            if (count < BufferSamples) count++;
        }

        /// <summary>Copies the newest <paramref name="samples"/> entries, oldest first.</summary>
        public void CopyTail(int samples, Vector3[] outPositions, float[] outYaws)
        {
            for (int i = 0; i < samples; i++)
            {
                int index = (head - samples + i + BufferSamples * 2) % BufferSamples;
                outPositions[i] = positions[index];
                outYaws[i] = yaws[index];
            }
        }
    }

    readonly Dictionary<ulong, TrackBuffer> victimTracks = new Dictionary<ulong, TrackBuffer>();
    readonly TrackBuffer monsterTrack = new TrackBuffer();

    float sampleTimer;
    KillcamClip bestClip;
    float bestLoudness = -1f;
    bool hasClip;

    void Awake()
    {
        Instance = this;
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    // ------------------------- Recording (server) -------------------------

    void Update()
    {
        if (!IsSpawned || !IsServer) return;
        if (!IsHuntState()) return;

        sampleTimer += Time.deltaTime;
        if (sampleTimer < SampleInterval) return;
        sampleTimer = 0f;
        SampleEveryone();
    }

    static bool IsHuntState()
    {
        if (GameManager.Instance == null) return true; // isolated module test scene
        GameManager.GameState s = GameManager.Instance.State.Value;
        return s == GameManager.GameState.Lockdown
            || s == GameManager.GameState.Playing
            || s == GameManager.GameState.Finale;
    }

    void SampleEveryone()
    {
        // Humans: every living survivor pawn. Dead pawns stop updating, which
        // freezes their buffer on the pre-death seconds - exactly what the
        // replay wants.
        foreach (PlayerController pawn in PlayerController.All)
        {
            if (pawn == null || pawn.IsGhost) continue;
            Track(pawn.OwnerClientId).Push(pawn.transform.position, pawn.transform.eulerAngles.y);
        }

        // Bots: bodies in use only; a parked body belongs to nobody.
        foreach (BotPawn pawn in BotPawn.All)
        {
            if (pawn == null || !pawn.IsInUse) continue;
            Track(pawn.BotId).Push(pawn.transform.position, pawn.transform.eulerAngles.y);
        }

        MonsterController monster = MonsterController.ActiveMonster;
        if (monster != null)
            monsterTrack.Push(monster.transform.position, monster.transform.eulerAngles.y);
    }

    TrackBuffer Track(ulong actorId)
    {
        if (!victimTracks.TryGetValue(actorId, out TrackBuffer track))
        {
            track = new TrackBuffer();
            victimTracks[actorId] = track;
        }
        return track;
    }

    // ------------------------- Public server API (inter-module contract) -------------------------

    /// <summary>
    /// GameManager reports every kill here. The loudest victim of the round
    /// (their last five seconds of noise) stars in the Results killcam.
    /// Context 0 asks the recorder to infer the caption from the scene.
    /// </summary>
    public void ServerNotifyKill(ulong victimId, float victimRecentLoudness, byte captionContextId)
    {
        if (!IsServer) return;
        if (hasClip && victimRecentLoudness <= bestLoudness) return; // the reigning clip is louder

        if (!victimTracks.TryGetValue(victimId, out TrackBuffer victimTrack) || victimTrack.count < 2)
            return; // caught before two samples existed; nothing worth replaying

        int samples = Mathf.Min(victimTrack.count, BufferSamples);

        var clip = new KillcamClip
        {
            victimId = victimId,
            victimPos = new Vector3[samples],
            victimYaw = new float[samples],
            monsterPos = new Vector3[samples],
            monsterYaw = new float[samples]
        };
        victimTrack.CopyTail(samples, clip.victimPos, clip.victimYaw);

        // The monster may have fewer samples (it spawns at lockdown); pad the
        // missing past with its oldest known pose so the arrays stay aligned.
        int monsterSamples = Mathf.Min(monsterTrack.count, samples);
        if (monsterSamples > 0)
        {
            var tailPositions = new Vector3[monsterSamples];
            var tailYaws = new float[monsterSamples];
            monsterTrack.CopyTail(monsterSamples, tailPositions, tailYaws);

            int pad = samples - monsterSamples;
            for (int i = 0; i < samples; i++)
            {
                int source = Mathf.Max(0, i - pad);
                clip.monsterPos[i] = tailPositions[source];
                clip.monsterYaw[i] = tailYaws[source];
            }
        }
        else
        {
            // No monster ever sampled (module test scene): shadow the victim.
            for (int i = 0; i < samples; i++)
            {
                clip.monsterPos[i] = clip.victimPos[i] - Vector3.forward * 2f;
                clip.monsterYaw[i] = clip.victimYaw[i];
            }
        }

        clip.captionContextId = captionContextId != GameCopy.KillcamContextDefault
            ? captionContextId
            : InferCaptionContext(clip);

        bestClip = clip;
        bestLoudness = victimRecentLoudness;
        hasClip = true;
    }

    /// <summary>Results entry: sends the round's loudest kill to every client, once.</summary>
    public void ServerBroadcastBestClip()
    {
        if (!IsServer || !hasClip) return;
        BestClipClientRpc(bestClip);
    }

    /// <summary>Rematch reset: wipes every buffer and forgets the clip.</summary>
    public void ServerReset()
    {
        if (!IsServer) return;
        victimTracks.Clear();
        monsterTrack.head = 0;
        monsterTrack.count = 0;
        sampleTimer = 0f;
        hasClip = false;
        bestLoudness = -1f;
        bestClip = default;
    }

    // ------------------------- Caption inference -------------------------

    /// <summary>
    /// Picks the GDD 11.8 joke from the scene when the caller had no richer
    /// context: dying mid-therapy beats dying in the bathroom beats dying at a
    /// sprint beats dying to the Mimic's round, and everyone else simply never
    /// saw it coming.
    /// </summary>
    byte InferCaptionContext(KillcamClip clip)
    {
        int last = clip.SampleCount - 1;
        Vector3 deathPosition = clip.victimPos[last];

        if (Vector3.Distance(deathPosition, HouseLayout.ScreamStation) <= screamStationRadius)
            return GameCopy.KillcamContextScreamTherapy;

        foreach (HouseLayout.RoomDef room in HouseLayout.Rooms)
        {
            if (room.name != "Bathroom") continue;
            if (room.boundsXZ.Contains(new Vector2(deathPosition.x, deathPosition.z)))
                return GameCopy.KillcamContextBathroom;
            break;
        }

        float lastStepSpeed = Vector3.Distance(clip.victimPos[last], clip.victimPos[last - 1]) / SampleInterval;
        if (lastStepSpeed >= sprintSpeedThreshold)
            return GameCopy.KillcamContextSprinting;

        MonsterController monster = MonsterController.ActiveMonster;
        if (monster != null && monster.SkinIndex == 2) // 2 = Mimic (contract: 0 zombie, 1 mutant, 2 mimic)
            return GameCopy.KillcamContextMimic;

        return GameCopy.KillcamContextDefault;
    }

    // ------------------------- Broadcast -------------------------

    [ClientRpc]
    void BestClipClientRpc(KillcamClip clip)
    {
        OnClientClipReady?.Invoke(clip);
    }
}
