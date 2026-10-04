using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// A server-driven survivor body. Seven of these live in the scene as a hidden
/// pool (built by BotFactory): the project spawns pawns with no Unity editor in
/// the loop, so pre-placed in-scene NetworkObjects - the same mechanism the
/// GameManager and task stations already rely on - replace a prefab asset.
/// BotManager assigns a pool pawn to a bot id when the bot needs a body and
/// parks it again when the bot is eaten, escapes, or the round resets.
///
/// Movement is server-owned CharacterController motion replicated through a
/// plain server-authoritative NetworkTransform (never ClientNetworkTransform).
/// Sprinting emits the real loudness-0.3 footstep ping every 2.5 s, exactly
/// like a human survivor (GDD 4.2) - bots snitch on themselves honestly.
///
/// Implements IVictim so the monster's attack raycast catches bots and humans
/// through one interface.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class BotPawn : NetworkBehaviour, IVictim
{
    /// <summary>Every pool pawn on this client, parked ones included (their BotId is ulong.MaxValue).</summary>
    public static readonly List<BotPawn> All = new List<BotPawn>();

    [Header("Movement (GDD 4.1)")]
    public float walkSpeed = 4.5f;
    public float runSpeed = 7.5f;
    public float gravity = -18f;
    public float turnSpeed = 10f;

    [Header("Sprinting is loud (GDD 4.2)")]
    public float sprintNoiseInterval = 2.5f;
    [Range(0f, 1f)] public float sprintNoiseLoudness = 0.3f;

    [Header("Rig references (wired by BotFactory)")]
    public GameObject visualRoot;
    public Renderer capRenderer;
    public Light rimLight;
    public UnityEngine.UI.Text nameLabel;
    public Transform nameTagRoot;

    [Header("Pooling")]
    [Tooltip("Where this pawn waits, hidden under the map, while no bot is using it.")]
    public Vector3 parkPosition = new Vector3(0f, -25f, 0f);

    // ------------------------- Synchronized state -------------------------

    // Server-written. ulong.MaxValue = parked (no bot inside this body).
    readonly NetworkVariable<ulong> botId = new NetworkVariable<ulong>(ulong.MaxValue);

    // Index into ScreamerPalette survivor colors; mirrors the roster row.
    readonly NetworkVariable<int> colorIndex = new NetworkVariable<int>(0);

    /// <summary>The synthetic actor id driving this body; ulong.MaxValue while parked.</summary>
    public ulong BotId => botId.Value;

    /// <summary>True while a bot is assigned to this pool pawn.</summary>
    public bool IsInUse => botId.Value != ulong.MaxValue;

    // ------------------------- IVictim -------------------------

    public ulong ActorId => BotId;
    public Transform VictimTransform => transform;

    public bool IsCatchable => IsInUse && IsSpawned && IsHuntState();

    public void RequestCaught()
    {
        if (IsSpawned && IsInUse) GetCaughtServerRpc();
    }

    static bool IsHuntState()
    {
        if (GameManager.Instance == null) return true; // isolated module test scene
        GameManager.GameState s = GameManager.Instance.State.Value;
        return s == GameManager.GameState.Playing || s == GameManager.GameState.Finale;
    }

    [ServerRpc(RequireOwnership = false)]
    void GetCaughtServerRpc()
    {
        // SECURITY: client-trusted catch reporting (friends/invite-first scope,
        // GDD 8.2) - any client could claim this bot was caught. Fine for
        // private lobbies; GameManager's guards still reject invalid states.
        // Same server-side hunt-state gate as PlayerController.GetCaughtServerRpc:
        // during Lockdown the monster is frozen and no catch may land.
        if (!IsInUse || !IsHuntState()) return;
        GameManager.Instance?.ServerActorCaught(BotId);
        // BotManager listens to GameManager.OnServerActorCaught and parks the
        // body, so a rejected catch (wrong state, double report) changes nothing.
    }

    // ------------------------- Internals -------------------------

    CharacterController cc;
    NetworkTransform netTransform;
    float verticalVelocity;
    float sprintNoiseTimer;
    float nameRefreshTimer;

    void Awake()
    {
        cc = GetComponent<CharacterController>();
        netTransform = GetComponent<NetworkTransform>();

        // Runtime-created fonts cannot be saved into the scene; re-acquire.
        if (nameLabel != null && nameLabel.font == null)
            nameLabel.font = Font.CreateDynamicFontFromOSFont("Arial", 36);
    }

    public override void OnNetworkSpawn()
    {
        if (!All.Contains(this)) All.Add(this);

        botId.OnValueChanged += HandleBotIdChanged;
        colorIndex.OnValueChanged += HandleColorChanged;
        ScreamerSettings.OnChanged += HandleSettingsChanged;

        ApplyInUseState(IsInUse);
        ApplyColor(colorIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        botId.OnValueChanged -= HandleBotIdChanged;
        colorIndex.OnValueChanged -= HandleColorChanged;
        ScreamerSettings.OnChanged -= HandleSettingsChanged;
        All.Remove(this);
    }

    public override void OnDestroy()
    {
        All.Remove(this);
        base.OnDestroy();
    }

    void Update()
    {
        if (!IsInUse) return;

        // Keep the floating name in sync with the roster (which may land a few
        // frames after the pawn's own variables on a fresh client).
        nameRefreshTimer -= Time.deltaTime;
        if (nameRefreshTimer <= 0f)
        {
            nameRefreshTimer = 1f;
            if (nameLabel != null && GameManager.Instance != null)
                nameLabel.text = GameManager.Instance.ActorName(BotId);
        }
    }

    void LateUpdate()
    {
        if (!IsInUse || nameTagRoot == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;
        nameTagRoot.rotation = Quaternion.LookRotation(nameTagRoot.position - cam.transform.position);
    }

    // ------------------------- Server pool API (BotManager) -------------------------

    /// <summary>Server: gives this body to a bot and teleports it to its spawn point.</summary>
    public void ServerAssign(ulong newBotId, int newColorIndex, Vector3 position)
    {
        if (!IsServerProcess()) return;

        verticalVelocity = 0f;
        sprintNoiseTimer = 0f;
        TeleportTo(position, Quaternion.identity);

        colorIndex.Value = newColorIndex;
        botId.Value = newBotId; // flips visuals + collider on all clients
    }

    /// <summary>Server: the bot is gone (eaten, escaped, removed, rematch) - hide and park the body.</summary>
    public void ServerRelease()
    {
        if (!IsServerProcess() || !IsInUse) return;

        botId.Value = ulong.MaxValue; // hides visuals + collider everywhere
        verticalVelocity = 0f;
        sprintNoiseTimer = 0f;
        TeleportTo(parkPosition, Quaternion.identity);
    }

    void TeleportTo(Vector3 position, Quaternion rotation)
    {
        // CharacterController fights direct transform writes; park it first.
        bool hadCc = cc != null && cc.enabled;
        if (hadCc) cc.enabled = false;

        transform.SetPositionAndRotation(position, rotation);
        if (netTransform != null && IsSpawned)
            netTransform.Teleport(position, rotation, transform.localScale);

        if (hadCc) cc.enabled = true;
    }

    // ------------------------- Server movement API (SurvivorBotBrain) -------------------------

    /// <summary>
    /// Server: one frame of steering toward a world target. Applies rotation,
    /// speed, and gravity, and emits the real sprint ping while sprinting.
    /// Returns true once the pawn is horizontally within <paramref name="arriveRadius"/>.
    /// </summary>
    public bool ServerMoveTowards(Vector3 target, bool sprint, float arriveRadius = 0.6f)
    {
        if (!IsServerProcess() || !IsInUse || cc == null || !cc.enabled) return false;

        Vector3 flat = target - transform.position;
        flat.y = 0f;
        bool arrived = flat.magnitude <= arriveRadius;

        Vector3 direction = Vector3.zero;
        if (!arrived)
        {
            direction = flat.normalized;
            transform.rotation = Quaternion.Slerp(
                transform.rotation, Quaternion.LookRotation(direction), turnSpeed * Time.deltaTime);
        }

        ApplyMotion(direction * (sprint ? runSpeed : walkSpeed));
        TickSprintNoise(sprint && !arrived);
        return arrived;
    }

    /// <summary>Server: stand in place (gravity still applies; the sprint timer rests).</summary>
    public void ServerStandStill()
    {
        if (!IsServerProcess() || !IsInUse || cc == null || !cc.enabled) return;
        ApplyMotion(Vector3.zero);
        TickSprintNoise(false);
    }

    /// <summary>Server: face a point of interest without moving (task stations, the door).</summary>
    public void ServerFaceTowards(Vector3 point)
    {
        if (!IsServerProcess() || !IsInUse) return;
        Vector3 flat = point - transform.position;
        flat.y = 0f;
        if (flat.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(
            transform.rotation, Quaternion.LookRotation(flat.normalized), turnSpeed * Time.deltaTime);
    }

    void ApplyMotion(Vector3 horizontalVelocity)
    {
        if (cc.isGrounded) verticalVelocity = -1f;
        verticalVelocity += gravity * Time.deltaTime;

        Vector3 motion = horizontalVelocity;
        motion.y = verticalVelocity;
        cc.Move(motion * Time.deltaTime);
    }

    void TickSprintNoise(bool sprinting)
    {
        // Mirrors PlayerController: a loudness-0.3 ping every 2.5 s of grounded
        // sprinting, attributed to the bot - the monster hears bots for real.
        if (!sprinting || !cc.isGrounded)
        {
            sprintNoiseTimer = 0f;
            return;
        }

        sprintNoiseTimer += Time.deltaTime;
        if (sprintNoiseTimer < sprintNoiseInterval) return;
        sprintNoiseTimer = 0f;

        NoiseSystem.Instance?.ServerMakeNoise(
            BotId, transform.position, sprintNoiseLoudness, NoiseType.Footsteps, GameCopy.NoiseFootsteps);
    }

    // ------------------------- Visuals -------------------------

    void HandleBotIdChanged(ulong oldId, ulong newId)
    {
        ApplyInUseState(newId != ulong.MaxValue);
        nameRefreshTimer = 0f; // re-read the roster name right away
    }

    void HandleColorChanged(int oldIndex, int newIndex) => ApplyColor(newIndex);

    // The colorblind palette applies live (GDD 7.5); re-resolve the identity color.
    void HandleSettingsChanged() => ApplyColor(colorIndex.Value);

    void ApplyInUseState(bool inUse)
    {
        if (visualRoot != null) visualRoot.SetActive(inUse);
        if (rimLight != null) rimLight.enabled = inUse;
        if (nameTagRoot != null) nameTagRoot.gameObject.SetActive(inUse);

        // The CharacterController doubles as the attack-raycast collider on
        // every client, so it is enabled wherever the pawn is visible.
        if (cc != null) cc.enabled = inUse;
    }

    void ApplyColor(int index)
    {
        Color color = ScreamerPalette.Survivor(index);
        if (capRenderer != null) capRenderer.material.color = color;
        if (rimLight != null) rimLight.color = color;
        if (nameLabel != null) nameLabel.color = color;
    }

    static bool IsServerProcess() =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
}
