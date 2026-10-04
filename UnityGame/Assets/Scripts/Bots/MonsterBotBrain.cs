using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The practice-mode monster's mind (GDD 10). Server-only, attached at runtime
/// by BotManager to the spawned monster body (a plain MonoBehaviour, because
/// NGO forbids adding NetworkBehaviours after spawn). It drives the shared
/// MonsterController through BotMove/BotTryAttack, so the bot obeys exactly
/// the same speed, freeze, cooldown, and raycast rules as a human monster.
///
/// Hunting doctrine:
/// - walks to the latest noise ping with loudness >= 0.5;
/// - with no ping for 12 seconds, patrols the waypoint graph;
/// - chases and bites any victim it can actually see up close;
/// - HARD RULE: never camps the yard - after 10 seconds inside it leaves and
///   refuses yard bait for a while (the finale door lives out there; a camping
///   bot would strangle solo rounds);
/// - never uses the Mimic disguise. Lying is for humans.
///
/// Noise awareness comes from NoiseSystem.OnNoiseVisible on the server, and
/// death screams are ignored on purpose: the silence-after-a-kill rule (GDD
/// 9.8) applies to bot monsters too.
/// </summary>
[RequireComponent(typeof(MonsterController))]
public class MonsterBotBrain : MonoBehaviour
{
    [Header("Hearing (GDD 10)")]
    [Tooltip("Pings quieter than this are beneath the monster's attention.")]
    [Range(0f, 1f)] public float minChaseLoudness = 0.5f;
    [Tooltip("Seconds of silence before the monster gives up and patrols.")]
    public float pingMemorySeconds = 12f;

    [Header("Yard discipline (GDD 10)")]
    [Tooltip("Maximum seconds spent in the backyard before the hard rule kicks in.")]
    public float yardCampLimitSeconds = 10f;
    [Tooltip("How long yard pings are ignored after the monster is sent back inside.")]
    public float yardBanSeconds = 12f;

    [Header("Hunting")]
    [Tooltip("Radius of the close-quarters victim scan.")]
    public float victimScanRadius = 10f;
    [Tooltip("The bot swings when a visible victim is within this range (attack range is 2.4).")]
    public float attackReach = 2.1f;

    MonsterController monster;
    ulong botId = ulong.MaxValue;

    // Latest interesting noise.
    Vector3 latestPing;
    float latestPingAt = -999f;
    bool pingConsumed = true;

    // Patrol.
    Vector3 patrolTarget;
    bool hasPatrolTarget;
    float dwellUntil;

    // Yard discipline.
    float yardTimer;
    float yardBanUntil;
    Rect yardBounds;
    bool yardBoundsCached;

    // Close-quarters hunting.
    float scanTimer;
    IVictim visibleVictim;

    // Path following.
    List<Vector3> path;
    int pathIndex;
    float repathTimer;
    Vector3 lastPathTarget;

    readonly Collider[] scanBuffer = new Collider[32];

    bool Driving =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer &&
        monster != null && monster.IsSpawned && monster.BotDriven;

    void Awake()
    {
        monster = GetComponent<MonsterController>();
    }

    void OnEnable() => NoiseSystem.OnNoiseVisible += HandleNoiseVisible;
    void OnDisable() => NoiseSystem.OnNoiseVisible -= HandleNoiseVisible;

    /// <summary>Server: BotManager stamps the driving bot's actor id right after spawn.</summary>
    public void ServerConfigure(ulong assignedBotId)
    {
        botId = assignedBotId;
    }

    // ------------------------- Hearing -------------------------

    void HandleNoiseVisible(ulong sourceActorId, Vector3 position, float loudness, NoiseType type)
    {
        if (!Driving) return;
        if (sourceActorId == botId) return;

        // Silence after a kill applies to bot monsters too: its own kills
        // never hand it the next lead for free.
        if (type == NoiseType.DeathScream) return;

        if (loudness < minChaseLoudness) return;
        if (YardBanActive && InYard(position)) return; // it refuses to be baited back

        latestPing = position;
        latestPingAt = Time.time;
        pingConsumed = false;
        repathTimer = 0f; // a fresh lead deserves a fresh route
    }

    bool YardBanActive => Time.time < yardBanUntil;

    // ------------------------- Main loop -------------------------

    void Update()
    {
        if (!Driving) return;

        GameManager gm = GameManager.Instance;
        if (gm != null)
        {
            GameManager.GameState s = gm.State.Value;
            if (s != GameManager.GameState.Lockdown &&
                s != GameManager.GameState.Playing &&
                s != GameManager.GameState.Finale)
                return;
        }

        YardTick();
        ScanForVictims();

        // The hard rule outranks everything, meals included: banned and still
        // in the yard means one destination - indoors.
        if (YardBanActive && InYard(monster.transform.position))
        {
            MoveAlong(RetreatTarget(), 1.2f);
            return;
        }

        if (visibleVictim != null)
        {
            HuntVisibleVictim();
            return;
        }

        bool pingIsFresh = !pingConsumed && Time.time - latestPingAt <= pingMemorySeconds;
        if (pingIsFresh)
        {
            if (MoveAlong(latestPing, 1.6f))
            {
                // Arrived, nobody here: sniff around this spot, then move on.
                pingConsumed = true;
                patrolTarget = PointNear(latestPing, 4f);
                hasPatrolTarget = true;
                dwellUntil = Time.time + Random.Range(0.5f, 1.2f);
            }
            return;
        }

        PatrolTick();
    }

    // ------------------------- Hunting -------------------------

    void ScanForVictims()
    {
        scanTimer -= Time.deltaTime;
        if (scanTimer > 0f)
        {
            // Keep a stale reference honest between scans. Every IVictim in the
            // game is a MonoBehaviour, so Unity's destroyed-object null check
            // applies before any member access can throw.
            var behaviour = visibleVictim as MonoBehaviour;
            if (behaviour == null || !visibleVictim.IsCatchable) visibleVictim = null;
            return;
        }
        scanTimer = 0.2f;
        visibleVictim = null;

        int hits = Physics.OverlapSphereNonAlloc(
            monster.transform.position, victimScanRadius, scanBuffer,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

        float best = float.MaxValue;
        for (int i = 0; i < hits; i++)
        {
            IVictim victim = scanBuffer[i].GetComponentInParent<IVictim>();
            if (victim == null || !victim.IsCatchable) continue;
            if (victim.ActorId == botId) continue;
            if (victim.VictimTransform == null) continue;

            float dist = Vector3.Distance(victim.VictimTransform.position, monster.transform.position);
            if (dist >= best) continue;
            if (!HasLineOfSight(victim, dist)) continue;

            best = dist;
            visibleVictim = victim;
        }
    }

    bool HasLineOfSight(IVictim victim, float distance)
    {
        Vector3 eye = monster.transform.position + Vector3.up * 1.7f;
        Vector3 aim = victim.VictimTransform.position + Vector3.up * 1f - eye;

        if (Physics.Raycast(eye, aim.normalized, out RaycastHit hit, distance + 1.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<IVictim>() == victim;

        return true;
    }

    void HuntVisibleVictim()
    {
        Vector3 target = visibleVictim.VictimTransform.position;
        Vector3 toVictim = target - monster.transform.position;
        Vector3 flat = toVictim;
        flat.y = 0f;
        float distance = flat.magnitude;

        if (distance > 6f)
        {
            // Still a chase through the house: respect walls, use the graph.
            MoveAlong(target, 1f);
        }
        else if (distance > 0.05f)
        {
            // Close quarters: charge straight at them.
            monster.BotMove(flat.normalized);
        }

        // Bite when in reach and actually facing dinner - BotTryAttack burns
        // the shared 1.2 s cooldown even on a miss, so no wild swings.
        if (distance <= attackReach &&
            Vector3.Dot(monster.transform.forward, flat.normalized) > 0.9f)
            monster.BotTryAttack();
    }

    // ------------------------- Patrol -------------------------

    void PatrolTick()
    {
        if (Time.time < dwellUntil)
        {
            monster.BotMove(Vector3.zero); // keeps gravity honest while it lurks
            return;
        }

        if (!hasPatrolTarget)
        {
            patrolTarget = PickPatrolNode();
            hasPatrolTarget = true;
        }

        if (MoveAlong(patrolTarget, 1.2f))
        {
            hasPatrolTarget = false;
            dwellUntil = Time.time + Random.Range(0.6f, 1.6f);
        }
    }

    Vector3 PickPatrolNode()
    {
        WaypointGraph graph = WaypointGraph.Instance;
        if (graph == null || graph.NodeCount == 0)
            return PointNear(monster.transform.position, 8f);

        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3 node = graph.NodePosition(Random.Range(0, graph.NodeCount));
            if (YardBanActive && InYard(node)) continue;
            Vector3 flat = node - monster.transform.position;
            flat.y = 0f;
            if (flat.magnitude < 3f) continue; // actually go somewhere
            return node;
        }
        return PointNear(monster.transform.position, 8f);
    }

    Vector3 PointNear(Vector3 center, float radius)
    {
        Vector2 ring = Random.insideUnitCircle * radius;
        return center + new Vector3(ring.x, 0f, ring.y);
    }

    // ------------------------- Yard discipline -------------------------

    void YardTick()
    {
        if (InYard(monster.transform.position))
        {
            yardTimer += Time.deltaTime;
            if (yardTimer >= yardCampLimitSeconds)
            {
                // Keep refreshing while it dawdles: the ban clock starts for
                // real once it actually leaves.
                yardBanUntil = Time.time + yardBanSeconds;
                hasPatrolTarget = false;
                pingConsumed = true; // drop any yard lead it was holding
            }
        }
        else
        {
            yardTimer = 0f;
        }
    }

    Vector3 RetreatTarget()
    {
        WaypointGraph graph = WaypointGraph.Instance;
        if (graph != null && graph.NodeCount > 0)
        {
            Vector3 best = Vector3.zero;
            float bestDist = float.MaxValue;
            bool found = false;
            for (int i = 0; i < graph.NodeCount; i++)
            {
                Vector3 node = graph.NodePosition(i);
                if (InYard(node)) continue;
                float dist = (node - monster.transform.position).sqrMagnitude;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = node;
                    found = true;
                }
            }
            if (found) return best;
        }
        return new Vector3(0f, 0f, 2f); // the den; everything indoors routes through it
    }

    bool InYard(Vector3 position)
    {
        if (!yardBoundsCached)
        {
            yardBoundsCached = true;
            yardBounds = new Rect(-14f, 12f, 28f, 16f); // GDD 3.1 backyard footprint
            var rooms = HouseLayout.Rooms;
            if (rooms != null)
            {
                foreach (HouseLayout.RoomDef room in rooms)
                {
                    if (room.name != null && room.name.ToUpperInvariant().Contains("YARD"))
                    {
                        yardBounds = room.boundsXZ;
                        break;
                    }
                }
            }
        }
        return yardBounds.Contains(new Vector2(position.x, position.z));
    }

    // ------------------------- Path plumbing -------------------------

    bool MoveAlong(Vector3 target, float arriveRadius)
    {
        repathTimer -= Time.deltaTime;
        bool targetMoved = (target - lastPathTarget).sqrMagnitude > 4f;
        if (path == null || path.Count == 0 || targetMoved || repathTimer <= 0f)
        {
            repathTimer = 1.5f;
            lastPathTarget = target;
            pathIndex = 0;
            path = WaypointGraph.Instance != null
                ? WaypointGraph.Instance.FindPath(monster.transform.position, target)
                : new List<Vector3> { target };
            if (path.Count == 0) path.Add(target);
        }

        bool finalLeg = pathIndex >= path.Count - 1;
        Vector3 waypoint = path[Mathf.Min(pathIndex, path.Count - 1)];
        float radius = finalLeg ? arriveRadius : 0.8f;

        Vector3 flat = waypoint - monster.transform.position;
        flat.y = 0f;
        if (flat.magnitude <= radius)
        {
            if (finalLeg)
            {
                monster.BotMove(Vector3.zero);
                return true;
            }
            pathIndex++;
            return false;
        }

        monster.BotMove(flat.normalized);
        return false;
    }
}
