using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The server-side mind of a survivor bot (GDD 10): fun-dumb, loud, and doomed
/// on purpose. State machine:
///
///   PickTask -> WalkToTask -> DoTask -> (Flee on a scare) -> Rejoin -> PickTask
///
/// Chores run through TaskBase.ServerBotWork at a human-ish cadence (bursts of
/// effort with breathing gaps) with the personality's chaos multiplier, so the
/// bots roll the same honest failure chances players face. Scares: the monster
/// sighted within 12 units (and moving - a perfectly still couch is just a
/// couch), or a loud ping landing nearby; the bot then sprints (real footstep
/// noise) for the farthest nearby node, with a 15% chance to orbit the couch
/// screaming instead. During the Finale every living bot gathers at the cellar
/// door - EscapeDoor lends their lungs to the shared meter - and runs out once
/// it blows open.
///
/// Sits permanently on every pool pawn (BotFactory); it only acts on the
/// server while BotManager has assigned a bot and a personality to the pawn.
/// Ping awareness comes from NoiseSystem.OnNoiseVisible, which module 1
/// guarantees also fires on the server/host process.
/// </summary>
[RequireComponent(typeof(BotPawn))]
public class SurvivorBotBrain : MonoBehaviour
{
    enum BotState { PickTask, WalkToTask, DoTask, Flee, Rejoin }

    [Header("Scares (GDD 10)")]
    [Tooltip("The bot flees when it sees the monster within this range.")]
    public float monsterSightRange = 12f;
    [Tooltip("A sighted monster only counts if it moved within the last this-many seconds (a disguised Mimic holds still).")]
    public float monsterMoveMemory = 1.5f;
    [Tooltip("Chance a panic turns into the couch-orbit scream routine.")]
    [Range(0f, 1f)] public float couchOrbitChance = 0.15f;

    [Header("Chores")]
    [Tooltip("How close the bot stands to a station while working (chicken included).")]
    public float workRange = 2.2f;

    [Header("Finale")]
    [Tooltip("The bot sidesteps a moving monster closer than this while holding the door line.")]
    public float finaleEvadeRange = 6f;

    BotPawn pawn;
    BotPersonality personality;

    BotState state = BotState.PickTask;
    TaskBase currentTask;
    Vector3 taskPickupPosition; // where the station stood when the bot chose it

    // Shadowing (Tiffany): when set, the "task" is a human being.
    bool following;
    ulong followClientId;
    float followUntil;

    // Path following.
    List<Vector3> path;
    int pathIndex;
    float repathTimer;
    Vector3 lastPathTarget;

    // Flee bookkeeping.
    float fleeUntil;
    float fleeLockUntil;
    float hideUntil;
    bool couchOrbit;
    float orbitAngle;
    Vector3 fleeTarget;

    // Work cadence.
    bool working;
    float workPulseTimer;

    // Monster tracking.
    float sightCheckTimer;
    Vector3 lastMonsterPos;
    float lastMonsterMoveTime = -999f;

    // Stuck recovery.
    Vector3 stuckAnchor;
    float stuckTimer;
    int stuckStrikes;

    // Lobby loitering.
    Vector3 lobbyAnchor;
    Vector3 lobbyTarget;
    float lobbyRepickTimer;

    // Finale gathering.
    bool gatherSeeded;
    Vector3 gatherOffset;
    float evadeUntil;
    Vector3 evadeDirection;

    // Rejoin breather.
    float rejoinUntil;

    bool Driving =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer &&
        pawn != null && pawn.IsSpawned && pawn.IsInUse && personality != null;

    void Awake()
    {
        pawn = GetComponent<BotPawn>();
    }

    void OnEnable() => NoiseSystem.OnNoiseVisible += HandleNoiseVisible;
    void OnDisable() => NoiseSystem.OnNoiseVisible -= HandleNoiseVisible;

    // ------------------------- BotManager hooks -------------------------

    /// <summary>Server: arms the brain for a freshly assigned pawn.</summary>
    public void Configure(BotPersonality assignedPersonality)
    {
        personality = assignedPersonality;
        state = BotState.PickTask;
        currentTask = null;
        following = false;
        couchOrbit = false;
        working = false;
        hideUntil = 0f;
        fleeLockUntil = 0f;
        evadeUntil = 0f;
        gatherSeeded = false;
        stuckStrikes = 0;
        lastMonsterMoveTime = -999f;
        ClearPath();

        lobbyAnchor = pawn != null ? pawn.transform.position : Vector3.zero;
        lobbyRepickTimer = 0f;
        lobbyTarget = lobbyAnchor;
        stuckAnchor = lobbyAnchor;
    }

    /// <summary>Server: the pawn went back to the pool; the mind goes with it.</summary>
    public void Release()
    {
        personality = null;
        currentTask = null;
        ClearPath();
    }

    // ------------------------- Main loop -------------------------

    void Update()
    {
        if (!Driving) return;

        GameManager gm = GameManager.Instance;
        if (gm == null)
        {
            pawn.ServerStandStill(); // isolated module test scene
            return;
        }

        switch (gm.State.Value)
        {
            case GameManager.GameState.Lobby:
                gatherSeeded = false;
                LobbyTick();
                break;

            case GameManager.GameState.Countdown:
                pawn.ServerStandStill(); // watching the morph like everyone else
                break;

            case GameManager.GameState.Lockdown:
            case GameManager.GameState.Playing:
                gatherSeeded = false;
                RoundTick();
                break;

            case GameManager.GameState.Finale:
                FinaleTick(gm);
                break;

            default: // Results
                pawn.ServerStandStill();
                break;
        }
    }

    void RoundTick()
    {
        SightTick();

        switch (state)
        {
            case BotState.PickTask: PickSomethingToDo(); break;
            case BotState.WalkToTask: WalkTick(); break;
            case BotState.DoTask: WorkTick(); break;
            case BotState.Flee: FleeTick(); break;
            case BotState.Rejoin: RejoinTick(); break;
        }
    }

    // ------------------------- Lobby -------------------------

    void LobbyTick()
    {
        // Mill around the den like somebody waiting for pizza.
        lobbyRepickTimer -= Time.deltaTime;
        if (lobbyRepickTimer <= 0f)
        {
            lobbyRepickTimer = Random.Range(3f, 7f);
            Vector2 drift = Random.insideUnitCircle * 2.5f;
            lobbyTarget = lobbyAnchor + new Vector3(drift.x, 0f, drift.y);
        }
        pawn.ServerMoveTowards(lobbyTarget, sprint: false, arriveRadius: 0.4f);
        state = BotState.PickTask; // a fresh round always starts with a plan
    }

    // ------------------------- Picking work -------------------------

    void PickSomethingToDo()
    {
        // Tiffany clause: sometimes the plan is just "stand near a human".
        if (Random.value < personality.followBias && TryPickHumanToShadow(out followClientId))
        {
            following = true;
            currentTask = null;
            followUntil = Time.time + Random.Range(6f, 10f);
            state = BotState.WalkToTask;
            ClearPath();
            return;
        }

        following = false;
        currentTask = PickRandomUnfinishedTask();
        if (currentTask == null)
        {
            // Nothing left to do and the finale has not flipped yet; breathe.
            BeginRejoin();
            return;
        }

        taskPickupPosition = currentTask.transform.position;
        state = BotState.WalkToTask;
        ClearPath();
    }

    TaskBase PickRandomUnfinishedTask()
    {
        TaskManager manager = TaskManager.Instance;
        if (manager == null || manager.allTasks == null) return null;

        var open = new List<TaskBase>();
        foreach (TaskBase task in manager.allTasks)
            if (task != null && !task.IsDone)
                open.Add(task);

        return open.Count > 0 ? open[Random.Range(0, open.Count)] : null;
    }

    bool TryPickHumanToShadow(out ulong clientId)
    {
        clientId = 0;
        NetworkManager net = NetworkManager.Singleton;
        GameManager gm = GameManager.Instance;
        if (net == null || gm == null) return false;

        float best = float.MaxValue;
        bool found = false;
        foreach (ulong candidate in net.ConnectedClientsIds)
        {
            if (candidate == gm.MonsterClientId.Value) continue; // not THAT human
            if (!net.ConnectedClients.TryGetValue(candidate, out NetworkClient client) ||
                client.PlayerObject == null) continue;

            // Ghosts are not filtered out on purpose: shadowing a dead friend's
            // floating spirit, uselessly, is peak Tiffany.
            float dist = (client.PlayerObject.transform.position - pawn.transform.position).sqrMagnitude;
            if (dist < best)
            {
                best = dist;
                clientId = candidate;
                found = true;
            }
        }
        return found;
    }

    // ------------------------- Walking -------------------------

    void WalkTick()
    {
        if (!TryGetMoveTarget(out Vector3 target))
        {
            state = BotState.PickTask;
            return;
        }
        if (!following && currentTask != null && currentTask.IsDone)
        {
            state = BotState.PickTask; // someone beat us to the chore
            return;
        }
        if (following && Time.time > followUntil)
        {
            state = BotState.PickTask;
            return;
        }

        // A station that wanders off is being chased (the chicken is the only
        // chore that runs); only that justifies cardio for a non-Chad.
        bool chasingRunner = !following && currentTask != null &&
            HorizontalDistance(currentTask.transform.position, taskPickupPosition) > 1.5f;

        float arrive = following ? 2.5f : workRange;
        bool sprint = personality.SprintsEverywhere ||
                      (chasingRunner && HorizontalDistance(pawn.transform.position, target) > 5f);

        if (MoveAlong(target, sprint, arrive))
        {
            state = BotState.DoTask;
            working = false;
            workPulseTimer = Random.Range(0.1f, 0.4f);
        }
    }

    bool TryGetMoveTarget(out Vector3 target)
    {
        if (following)
        {
            NetworkManager net = NetworkManager.Singleton;
            if (net != null &&
                net.ConnectedClients.TryGetValue(followClientId, out NetworkClient client) &&
                client.PlayerObject != null)
            {
                target = client.PlayerObject.transform.position;
                return true;
            }
            target = Vector3.zero;
            return false;
        }

        if (currentTask != null)
        {
            target = currentTask.transform.position;
            return true;
        }
        target = Vector3.zero;
        return false;
    }

    // ------------------------- Working -------------------------

    void WorkTick()
    {
        if (following)
        {
            LoiterNearHuman();
            return;
        }

        if (currentTask == null || currentTask.IsDone)
        {
            state = BotState.PickTask;
            return;
        }

        Vector3 station = currentTask.transform.position;
        if (HorizontalDistance(pawn.transform.position, station) > workRange + 0.8f)
        {
            state = BotState.WalkToTask; // the chicken ran; chase resumes
            return;
        }

        pawn.ServerStandStill();
        pawn.ServerFaceTowards(station);

        // Human-ish cadence: bursts of effort with breathing gaps, never a
        // metronome. The task itself rolls the honest failure chances, scaled
        // by this bot's chaos (Dale pays triple).
        workPulseTimer -= Time.deltaTime;
        if (working)
        {
            currentTask.ServerBotWork(pawn.BotId, Time.deltaTime, personality.chaosRoll);
            if (workPulseTimer <= 0f)
            {
                working = false;
                workPulseTimer = Random.Range(0.15f, 0.5f);
            }
        }
        else if (workPulseTimer <= 0f)
        {
            working = true;
            workPulseTimer = Random.Range(0.5f, 1.4f);
        }
    }

    void LoiterNearHuman()
    {
        if (Time.time > followUntil || !TryGetMoveTarget(out Vector3 target))
        {
            state = BotState.PickTask;
            return;
        }

        if (HorizontalDistance(pawn.transform.position, target) > 3.5f)
        {
            state = BotState.WalkToTask;
            return;
        }

        pawn.ServerStandStill();
        pawn.ServerFaceTowards(target); // just... watching. Helpfully.
    }

    // ------------------------- Scares -------------------------

    void HandleNoiseVisible(ulong sourceActorId, Vector3 position, float loudness, NoiseType type)
    {
        if (!Driving) return;
        if (sourceActorId == pawn.BotId) return; // its own racket is fine, apparently

        GameManager gm = GameManager.Instance;
        if (gm == null) return;
        GameManager.GameState s = gm.State.Value;
        if (s != GameManager.GameState.Lockdown && s != GameManager.GameState.Playing) return;

        // The personality dial: Chad shrugs off anything under a full scream,
        // Brenda panics at sprint footsteps from across the house.
        if (loudness < personality.panicThreshold) return;
        if (HorizontalDistance(pawn.transform.position, position) > personality.PanicPingRadius) return;

        StartFlee(position);
    }

    void SightTick()
    {
        sightCheckTimer -= Time.deltaTime;
        if (sightCheckTimer > 0f) return;
        sightCheckTimer = 0.25f;

        MonsterController monster = MonsterController.ActiveMonster;
        if (monster == null) return;

        TrackMonsterMovement(monster);
        if (state == BotState.Flee) return;

        float distance = Vector3.Distance(monster.transform.position, pawn.transform.position);
        if (distance > monsterSightRange) return;
        if (Time.time - lastMonsterMoveTime > monsterMoveMemory) return; // still = furniture
        if (!HasLineOfSight(monster, distance)) return;

        StartFlee(monster.transform.position);
    }

    void TrackMonsterMovement(MonsterController monster)
    {
        Vector3 pos = monster.transform.position;
        if ((pos - lastMonsterPos).sqrMagnitude > 0.01f)
        {
            lastMonsterPos = pos;
            lastMonsterMoveTime = Time.time;
        }
    }

    bool HasLineOfSight(MonsterController monster, float distance)
    {
        Vector3 eye = pawn.transform.position + Vector3.up * 1.5f;
        Vector3 aim = monster.transform.position + Vector3.up * 1.2f - eye;

        if (Physics.Raycast(eye, aim.normalized, out RaycastHit hit, distance + 1.5f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return hit.collider.GetComponentInParent<MonsterController>() == monster;

        return true; // nothing in the way at all
    }

    void StartFlee(Vector3 threatPosition)
    {
        if (state == BotState.Flee && Time.time < fleeLockUntil) return; // one plan at a time

        state = BotState.Flee;
        working = false;
        hideUntil = 0f;
        fleeLockUntil = Time.time + 1.5f;
        ClearPath();

        couchOrbit = Random.value < couchOrbitChance;
        if (couchOrbit)
        {
            // The signature move: orbit the couch at a dead sprint, screaming.
            // One honest loudness-1.0 ping - the monster is cordially invited.
            NoiseSystem.Instance?.ServerMakeNoise(
                pawn.BotId, pawn.transform.position, 1f, NoiseType.Scream, GameCopy.NoiseScream);

            fleeTarget = HouseLayout.Couch;
            Vector3 fromCouch = pawn.transform.position - fleeTarget;
            orbitAngle = Mathf.Atan2(fromCouch.z, fromCouch.x);
            fleeUntil = Time.time + Random.Range(4f, 6f);
            return;
        }

        if (personality.HidesInBathroom && TryRoomCenter("BATH", out Vector3 bathroom))
        {
            // Brenda's plan: the bathroom. The hide timer starts on arrival.
            fleeTarget = bathroom;
            fleeUntil = Time.time + 20f; // generous travel cap
            return;
        }

        fleeTarget = FarthestNearbyNode(threatPosition);
        fleeUntil = Time.time + Random.Range(3.5f, 5.5f);
    }

    void FleeTick()
    {
        if (couchOrbit)
        {
            OrbitCouch();
            if (Time.time > fleeUntil) BeginRejoin();
            return;
        }

        bool arrived = MoveAlong(fleeTarget, sprint: true, arriveRadius: 1.2f);

        if (personality.HidesInBathroom && arrived)
        {
            // MoveAlong already parked the pawn this frame; just run the clock.
            if (hideUntil <= 0f) hideUntil = Time.time + BotPersonality.BathroomHideSeconds;
            if (Time.time >= hideUntil) BeginRejoin();
            return;
        }

        if (arrived || Time.time > fleeUntil) BeginRejoin();
    }

    void OrbitCouch()
    {
        const float orbitRadius = 2.6f;
        orbitAngle += (pawn.runSpeed / orbitRadius) * Time.deltaTime;
        Vector3 spot = HouseLayout.Couch +
            new Vector3(Mathf.Cos(orbitAngle), 0f, Mathf.Sin(orbitAngle)) * orbitRadius;
        pawn.ServerMoveTowards(spot, sprint: true, arriveRadius: 0.1f);
    }

    Vector3 FarthestNearbyNode(Vector3 threatPosition)
    {
        // "The farthest adjacent room node": among reachable nearby graph
        // nodes, pick the one that puts the most floor between bot and threat.
        Vector3 fallback = pawn.transform.position + AwayFrom(threatPosition) * 8f;

        WaypointGraph graph = WaypointGraph.Instance;
        if (graph == null || graph.NodeCount == 0) return fallback;

        Vector3 best = fallback;
        float bestScore = -1f;
        for (int i = 0; i < graph.NodeCount; i++)
        {
            Vector3 node = graph.NodePosition(i);
            float fromBot = HorizontalDistance(node, pawn.transform.position);
            if (fromBot < 2f || fromBot > 20f) continue; // actually go somewhere, nearby

            float score = HorizontalDistance(node, threatPosition);
            if (score > bestScore)
            {
                bestScore = score;
                best = node;
            }
        }
        return best;
    }

    Vector3 AwayFrom(Vector3 threatPosition)
    {
        Vector3 away = pawn.transform.position - threatPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
        {
            Vector2 any = Random.insideUnitCircle.normalized;
            away = new Vector3(any.x, 0f, any.y);
        }
        return away.normalized;
    }

    void BeginRejoin()
    {
        state = BotState.Rejoin;
        couchOrbit = false;
        hideUntil = 0f;
        rejoinUntil = Time.time + Random.Range(0.6f, 1.6f);
        ClearPath();
    }

    void RejoinTick()
    {
        pawn.ServerStandStill(); // catching its breath, reconsidering its life
        if (Time.time >= rejoinUntil) state = BotState.PickTask;
    }

    // ------------------------- Finale -------------------------

    void FinaleTick(GameManager gm)
    {
        Vector3 door = HouseLayout.CellarDoor;

        if (!gatherSeeded)
        {
            gatherSeeded = true;
            Vector2 ring = Random.insideUnitCircle.normalized * Random.Range(1.2f, 2.8f);
            gatherOffset = new Vector3(ring.x, 0f, ring.y); // inside the 4-unit scream range
        }

        // Sidestep a monster working the door line - but never abandon the
        // finale, or a camping monster could stall the round forever.
        MonsterController monster = MonsterController.ActiveMonster;
        if (monster != null)
        {
            TrackMonsterMovement(monster);
            if (Time.time < evadeUntil)
            {
                pawn.ServerMoveTowards(pawn.transform.position + evadeDirection * 4f, sprint: true, arriveRadius: 0.2f);
                return;
            }
            if (Vector3.Distance(monster.transform.position, pawn.transform.position) < finaleEvadeRange &&
                Time.time - lastMonsterMoveTime < monsterMoveMemory)
            {
                evadeDirection = AwayFrom(monster.transform.position);
                evadeUntil = Time.time + 1.2f;
                return;
            }
        }

        if (gm.DoorOpenedServerTime > 0)
        {
            // The door is open: run. BotManager parks the body off the escape event.
            if (MoveAlong(door, sprint: true, arriveRadius: 2f))
                gm.ServerActorEscaped(pawn.BotId);
            return;
        }

        // Gather and hold: EscapeDoor's server tick lends every living bot
        // within range to the shared scream meter. MoveAlong keeps the pawn
        // parked once it has arrived; facing the door is pure theater.
        Vector3 spot = door + gatherOffset;
        if (MoveAlong(spot, personality.SprintsEverywhere, arriveRadius: 0.8f))
            pawn.ServerFaceTowards(door);
    }

    // ------------------------- Path plumbing -------------------------

    bool MoveAlong(Vector3 target, bool sprint, float arriveRadius)
    {
        repathTimer -= Time.deltaTime;
        bool targetMoved = (target - lastPathTarget).sqrMagnitude > 4f;
        if (path == null || path.Count == 0 || targetMoved || repathTimer <= 0f)
        {
            repathTimer = 1.5f;
            lastPathTarget = target;
            pathIndex = 0;
            path = WaypointGraph.Instance != null
                ? WaypointGraph.Instance.FindPath(pawn.transform.position, target)
                : new List<Vector3> { target };
            if (path.Count == 0) path.Add(target);
        }

        bool finalLeg = pathIndex >= path.Count - 1;
        Vector3 waypoint = path[Mathf.Min(pathIndex, path.Count - 1)];
        float radius = finalLeg ? arriveRadius : 0.7f;

        if (pawn.ServerMoveTowards(waypoint, sprint, radius))
        {
            if (finalLeg)
            {
                stuckTimer = 0f;
                stuckStrikes = 0;
                return true;
            }
            pathIndex++;
        }

        StuckTick();
        return false;
    }

    void StuckTick()
    {
        // Doorframes happen to everyone. Re-path after a couple of stalled
        // seconds; after three strikes, give up on the whole plan.
        if ((pawn.transform.position - stuckAnchor).sqrMagnitude > 0.36f)
        {
            stuckAnchor = pawn.transform.position;
            stuckTimer = 0f;
            return;
        }

        stuckTimer += Time.deltaTime;
        if (stuckTimer < 2.5f) return;

        stuckTimer = 0f;
        stuckAnchor = pawn.transform.position;
        repathTimer = 0f;
        stuckStrikes++;
        if (stuckStrikes >= 3)
        {
            stuckStrikes = 0;
            if (state == BotState.WalkToTask) state = BotState.PickTask;
            else if (state == BotState.Flee) BeginRejoin();
        }
    }

    void ClearPath()
    {
        path = null;
        pathIndex = 0;
        repathTimer = 0f;
        if (pawn != null) stuckAnchor = pawn.transform.position;
        stuckTimer = 0f;
    }

    // ------------------------- Layout helpers -------------------------

    static bool TryRoomCenter(string nameFragment, out Vector3 center)
    {
        center = Vector3.zero;
        var rooms = HouseLayout.Rooms;
        if (rooms == null) return false;

        foreach (HouseLayout.RoomDef room in rooms)
        {
            if (room.name == null) continue;
            if (!room.name.ToUpperInvariant().Contains(nameFragment)) continue;
            center = room.center;
            return true;
        }
        return false;
    }

    static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
