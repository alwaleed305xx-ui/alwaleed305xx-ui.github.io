using Unity.Netcode;
using UnityEngine;

/// <summary>
/// CATCH THE CHICKEN - the one chore that lets you move, because the chore is
/// running away from you. The bird flees at speed 5.0 whenever a living
/// survivor comes within 6 units, never strays more than 11 from its roost,
/// and squawks a loudness-0.8 ping every 1.2 s while fleeing - attributed to
/// whoever is harassing it. Stay within arm's reach (2.0) to bank the 4.0 s
/// of catch progress. GDD 4.3 / 4.2.
///
/// The chicken's body moves on the server and syncs to everyone through a
/// server-authoritative NetworkTransform (added by TaskFactory).
/// </summary>
public class TaskChicken : TaskBase
{
    [Header("Chicken (GDD 4.3)")]
    public float fleeSpeed = 5f;
    [Tooltip("The bird bolts when a living survivor is within this range.")]
    public float fleeRadius = 6f;
    [Tooltip("Maximum wander distance from the roost (round-start position).")]
    public float wanderLeash = 11f;
    [Tooltip("You must be within this range of the bird for catch progress.")]
    public float catchRadius = 2f;
    public float squawkInterval = 1.2f;
    [Tooltip("Idle waddle speed between panics.")]
    public float wanderSpeed = 1.2f;

    Vector3 roostPosition;
    float roostY;
    float squawkTimer;

    Vector3 wanderTarget;
    float wanderRepickTimer;

    float chaseStatBuffer;
    float botSquawkTimer;

    public override void ApplyBalanceDefaults()
    {
        taskName = "CATCH THE CHICKEN";
        flavorText = GameCopy.FlavorChicken;
        duration = 4f;        // short - the chase is the difficulty
        noiseInterval = 0f;   // the squawks below are the noise, chase or not
        noiseLoudness = 0.8f;
        noiseType = NoiseType.Chicken;
        noiseLabel = GameCopy.NoiseChicken;
    }

    /// <summary>The chase is the chore: the player keeps full movement.</summary>
    protected override bool LocksPlayerInput => false;

    protected override void Awake()
    {
        base.Awake();
        roostPosition = transform.position;
        roostY = transform.position.y;
        wanderTarget = roostPosition;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            roostPosition = transform.position;
            roostY = transform.position.y;
            wanderTarget = roostPosition;
        }
    }

    // ------------------------- Server-side bird brain -------------------------

    void LateUpdate()
    {
        if (!IsSpawned || !IsServer || IsDone) return;

        PlayerController threat = FindNearestThreat(out float threatDistance);

        if (threat != null && threatDistance < fleeRadius)
        {
            FleeFrom(threat.transform.position);
            ServerSquawkTick(threat.OwnerClientId);
        }
        else
        {
            WanderCalmly();
            squawkTimer = 0f;
        }
    }

    PlayerController FindNearestThreat(out float nearestDistance)
    {
        PlayerController nearest = null;
        nearestDistance = float.MaxValue;

        foreach (PlayerController player in PlayerController.All)
        {
            if (player == null || player.IsGhost) continue;
            float distance = Vector3.Distance(transform.position, player.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = player;
            }
        }
        return nearest;
    }

    void FleeFrom(Vector3 threatPosition)
    {
        Vector3 away = transform.position - threatPosition;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
            away = Random.insideUnitSphere; // pinned against the player: pick any exit
        away.y = 0f;

        Vector3 target = transform.position + away.normalized * fleeSpeed * Time.deltaTime;

        // The leash: a cornered bird skims along its circle instead of leaving it.
        Vector3 fromRoost = target - roostPosition;
        fromRoost.y = 0f;
        if (fromRoost.magnitude > wanderLeash)
            target = roostPosition + fromRoost.normalized * wanderLeash;

        target.y = roostY;
        MoveAndFace(target);
    }

    void WanderCalmly()
    {
        wanderRepickTimer -= Time.deltaTime;
        if (wanderRepickTimer <= 0f || Vector3.Distance(transform.position, wanderTarget) < 0.3f)
        {
            wanderRepickTimer = Random.Range(2.5f, 5f);
            Vector2 spot = Random.insideUnitCircle * (wanderLeash * 0.5f);
            wanderTarget = roostPosition + new Vector3(spot.x, 0f, spot.y);
            wanderTarget.y = roostY;
        }

        Vector3 target = Vector3.MoveTowards(transform.position, wanderTarget, wanderSpeed * Time.deltaTime);
        target.y = roostY;
        MoveAndFace(target);
    }

    void MoveAndFace(Vector3 target)
    {
        Vector3 heading = target - transform.position;
        heading.y = 0f;
        if (heading.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(heading);
        transform.position = target;
    }

    void ServerSquawkTick(ulong harasserActorId)
    {
        squawkTimer += Time.deltaTime;
        if (squawkTimer < squawkInterval) return;
        squawkTimer = 0f;

        // The ping is pinned on whoever is chasing it. Their fault, really.
        ServerEmitTaskNoise(harasserActorId, noiseLoudness, noiseType, noiseLabel);
        SquawkClientRpc();
    }

    [ClientRpc]
    void SquawkClientRpc()
    {
        if (AudioDirector.Instance != null)
            AudioDirector.Instance.Play(Sfx.ChickenSquawk, transform.position, 1f, Random.Range(0.8f, 1.3f));
    }

    // ------------------------- Local chase -------------------------

    protected override void OnTaskStart()
    {
        chaseStatBuffer = 0f;
        if (GameUI.Instance != null)
            GameUI.Instance.ShowTaskHint("Stay within arm's reach. It knows what you did.");
    }

    protected override float ProgressMultiplier()
    {
        if (currentPlayer == null) return 0f;

        // CHICKEN'S NEMESIS bookkeeping, reported in one-second chunks so the
        // chase does not spam the server with tiny stat RPCs.
        chaseStatBuffer += Time.deltaTime;
        if (chaseStatBuffer >= 1f && GameManager.Instance != null)
        {
            GameManager.Instance.ReportStatServerRpc(StatKind.ChickenChaseSeconds, chaseStatBuffer);
            chaseStatBuffer = 0f;
        }

        float distance = Vector3.Distance(currentPlayer.transform.position, transform.position);

        // A whiffed grab deserves its own little humiliation.
        if (Input.GetKeyDown(KeyCode.E) && distance > catchRadius)
        {
            if (ScreamerCam.Instance != null)
                ScreamerCam.Instance.Tilt(10f, 0.3f);
            if (GagFeedback.Instance != null)
                GagFeedback.Instance.Popup("MISSED.", ScreamerPalette.ScreamYellow);
        }

        return distance <= catchRadius ? 1f : 0f;
    }

    protected override void OnTaskEnd()
    {
        FlushChaseStat();
    }

    void FlushChaseStat()
    {
        if (chaseStatBuffer > 0f && GameManager.Instance != null)
        {
            GameManager.Instance.ReportStatServerRpc(StatKind.ChickenChaseSeconds, chaseStatBuffer);
            chaseStatBuffer = 0f;
        }
    }

    protected override void OnLocalCompleted()
    {
        if (ScreamerCam.Instance != null)
            ScreamerCam.Instance.Hitstop(0.15f);
        if (GagFeedback.Instance != null)
            GagFeedback.Instance.Gag("CHICKEN ACQUIRED. THE CHICKEN DISAGREES.",
                transform.position, Sfx.ChickenSquawk, ScreamerPalette.NoodleCream, 0.2f);
    }

    // ------------------------- Bot chase -------------------------

    protected override float ServerBotWorkStep(ulong botId, float deltaTime, float chaosMultiplier)
    {
        // Bots are slower at cornering poultry, and the bird complains for real.
        botSquawkTimer += deltaTime;
        if (botSquawkTimer >= squawkInterval)
        {
            botSquawkTimer = 0f;
            ServerEmitTaskNoise(botId, noiseLoudness, noiseType, noiseLabel);
            SquawkClientRpc();
        }

        if (GameManager.Instance != null)
            GameManager.Instance.ServerAddStat(botId, StatKind.ChickenChaseSeconds, deltaTime);

        return deltaTime * 0.6f;
    }

    protected override void OnServerReset()
    {
        // The bird walks home and pretends none of that happened.
        squawkTimer = 0f;
        botSquawkTimer = 0f;
        wanderRepickTimer = 0f;
        wanderTarget = roostPosition;
        transform.position = roostPosition;
    }
}
