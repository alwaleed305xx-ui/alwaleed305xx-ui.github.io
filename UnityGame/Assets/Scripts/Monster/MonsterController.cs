using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The monster: slightly faster than the survivors (per-skin speeds), catches
/// anything implementing IVictim with a camera-forward raycast, gets frozen by the
/// server during the round-start lockdown, and takes the +25% finale speed bonus.
/// Also exposes the server-side drive hooks the practice-mode bot brain uses.
///
/// Prefab layout (CharacterFactory builds it): CharacterController + NetworkObject +
/// ClientNetworkTransform (owner authority) + this script + MonsterSkinSelector +
/// MimicDisguise, a CameraHolder child at eye height, and the three skin roots
/// under "Skins". For a bot monster the spawner must set BotDriven = true BEFORE
/// NetworkObject.Spawn so the pawn never grabs the host's camera.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class MonsterController : NetworkBehaviour
{
    [Header("Movement -- per-skin speeds (GDD 4.1)")]
    public float moveSpeed = 8.2f; // overwritten from the synced skin on spawn
    public float zombieSpeed = 7.8f;
    public float mutantSpeed = 8.6f;
    public float mimicSpeed = 8.2f;
    public float gravity = -18f;

    [Header("Attack (GDD 4.1)")]
    public float attackRange = 2.4f;
    public float attackCooldown = 1.2f;
    [Tooltip("Extra delay when attacking out of a Mimic disguise -- point-blank reveals stay survivable.")]
    public float mimicAttackWindupSeconds = 0.3f;

    [Header("Rig references (wired by CharacterFactory)")]
    public Transform cameraHolder;

    /// <summary>The live monster pawn on every client; null while despawned.</summary>
    public static MonsterController ActiveMonster { get; private set; }

    // Server-written; synced so every client agrees on freeze and bonus windows.
    readonly NetworkVariable<double> frozenUntilServerTime = new NetworkVariable<double>(0);
    readonly NetworkVariable<float> speedBonusMultiplier = new NetworkVariable<float>(1f);
    readonly NetworkVariable<double> speedBonusEndsServerTime = new NetworkVariable<double>(0);

    CharacterController cc;
    MonsterSkinSelector skinSelector;
    MimicDisguise mimic;
    float verticalVelocity;
    float cameraPitch;
    float cooldownTimer;
    float windupTimer = -1f;
    bool wiredSkinEvent;

    /// <summary>True while the server-timed lockdown (or any later freeze) is active.</summary>
    public bool IsFrozen => NetworkManager != null && NetworkManager.ServerTime.Time < frozenUntilServerTime.Value;

    /// <summary>0 zombie, 1 mutant, 2 mimic; -1 until the server has chosen.</summary>
    public int SkinIndex => skinSelector != null ? skinSelector.CurrentSkin : -1;

    /// <summary>Server-side bot drive. Disables local input reading entirely.</summary>
    public bool BotDriven { get; set; }

    float EffectiveMoveSpeed =>
        moveSpeed * (NetworkManager != null && NetworkManager.ServerTime.Time < speedBonusEndsServerTime.Value
            ? speedBonusMultiplier.Value : 1f);

    public override void OnNetworkSpawn()
    {
        cc = GetComponent<CharacterController>();
        skinSelector = GetComponent<MonsterSkinSelector>();
        mimic = GetComponent<MimicDisguise>();
        ActiveMonster = this;

        if (skinSelector != null)
        {
            skinSelector.OnSkinApplied += ApplySkinSpeed;
            wiredSkinEvent = true;
            if (skinSelector.CurrentSkin >= 0) ApplySkinSpeed(skinSelector.CurrentSkin);
        }

        if (IsOwner && !BotDriven)
        {
            AttachMainCamera();
            Cursor.lockState = CursorLockMode.Locked;
            if (GameUI.Instance != null) GameUI.Instance.SetMonsterHud(true);
        }
    }

    public override void OnNetworkDespawn()
    {
        if (wiredSkinEvent && skinSelector != null) skinSelector.OnSkinApplied -= ApplySkinSpeed;
        if (ActiveMonster == this) ActiveMonster = null;

        if (IsOwner && !BotDriven)
        {
            ReleaseMainCamera();
            if (GameUI.Instance != null) GameUI.Instance.SetMonsterHud(false);
        }
    }

    void ApplySkinSpeed(int skin)
    {
        switch (skin)
        {
            case MonsterSkinSelector.SkinZombie: moveSpeed = zombieSpeed; break;
            case MonsterSkinSelector.SkinMutant: moveSpeed = mutantSpeed; break;
            case MonsterSkinSelector.SkinMimic: moveSpeed = mimicSpeed; break;
        }
    }

    // ------------------------- server API -------------------------

    /// <summary>Server-timed freeze (round-start lockdown). Synced via server time so every client agrees.</summary>
    public void ServerFreeze(float seconds)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        frozenUntilServerTime.Value = NetworkManager.Singleton.ServerTime.Time + Mathf.Max(0f, seconds);
    }

    /// <summary>Timed speed multiplier; the finale calls this with (1.25f, 8f).</summary>
    public void ServerApplySpeedBonus(float multiplier, float seconds)
    {
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;
        speedBonusMultiplier.Value = Mathf.Max(0.1f, multiplier);
        speedBonusEndsServerTime.Value = NetworkManager.Singleton.ServerTime.Time + Mathf.Max(0f, seconds);
    }

    // ------------------------- update loop -------------------------

    void Update()
    {
        if (!IsSpawned) return;

        // Shared timers tick everywhere so the server-driven bot shares the cooldown.
        cooldownTimer -= Time.deltaTime;
        TickWindup();

        if (!IsOwner || BotDriven) return;
        if (!InHuntState()) return;

        Look();

        bool busyDisguised = mimic != null && mimic.IsBusy;
        if (!IsFrozen && !busyDisguised) Move();

        HandleDisguiseBreakInput();
        HandleAttackInput();
    }

    bool InHuntState()
    {
        if (GameManager.Instance == null) return true; // isolated module test scene
        GameManager.GameState s = GameManager.Instance.State.Value;
        return s == GameManager.GameState.Lockdown
            || s == GameManager.GameState.Playing
            || s == GameManager.GameState.Finale;
    }

    void Look()
    {
        if (cameraHolder == null) return;
        float sens = Sensitivity();
        float mx = Input.GetAxis("Mouse X") * sens;
        float my = Input.GetAxis("Mouse Y") * sens;
        transform.Rotate(Vector3.up * mx);
        cameraPitch = Mathf.Clamp(cameraPitch + (InvertLook() ? my : -my), -80f, 80f);
        cameraHolder.localEulerAngles = new Vector3(cameraPitch, 0f, 0f);
    }

    void Move()
    {
        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        Vector3 move = transform.TransformDirection(input.normalized) * EffectiveMoveSpeed;

        if (cc.isGrounded) verticalVelocity = -1f;
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;
        cc.Move(move * Time.deltaTime);
    }

    /// <summary>Movement input while disguised is the break trigger -- the couch stands up.</summary>
    void HandleDisguiseBreakInput()
    {
        if (mimic == null || !mimic.IsDisguised || IsFrozen) return;

        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        if (input.sqrMagnitude > 0.1f || Input.GetButtonDown("Jump"))
            mimic.RequestBreak();
    }

    void HandleAttackInput()
    {
        if (!Input.GetMouseButtonDown(0) || cooldownTimer > 0f || IsFrozen) return;

        if (mimic != null && (mimic.IsDisguised || mimic.IsUnfolding))
        {
            // Attack out of disguise: break first, land the raycast after the windup.
            if (mimic.IsDisguised) mimic.RequestBreak();
            if (windupTimer < 0f)
            {
                windupTimer = mimicAttackWindupSeconds;
                cooldownTimer = attackCooldown;
            }
            return;
        }

        cooldownTimer = attackCooldown;
        PerformAttack();
    }

    void TickWindup()
    {
        if (windupTimer < 0f) return;
        windupTimer -= Time.deltaTime;
        if (windupTimer <= 0f)
        {
            windupTimer = -1f;
            PerformAttack();
        }
    }

    void PerformAttack()
    {
        Transform eye = cameraHolder != null ? cameraHolder : transform;
        if (IsOwner && !BotDriven && ScreamerCam.Instance != null)
            ScreamerCam.Instance.Shake(0.15f, 0.12f);

        // Trigger volumes (task zones) must never shield a victim from the bite.
        if (!Physics.Raycast(eye.position, eye.forward, out RaycastHit hit, attackRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            return;

        IVictim victim = hit.collider.GetComponentInParent<IVictim>();
        if (victim != null && victim.IsCatchable)
            victim.RequestCaught(); // the server resolves the catch; feel and feed hang off GameManager events
    }

    // ------------------------- bot drive (server only) -------------------------

    /// <summary>Server-side bot steering; applies rotation, speed, and gravity itself.</summary>
    public void BotMove(Vector3 worldDirection)
    {
        if (!IsSpawned || !IsServer || !BotDriven) return;
        if (IsFrozen || (mimic != null && mimic.IsBusy)) return;

        Vector3 flat = worldDirection;
        flat.y = 0f;
        if (flat.sqrMagnitude > 0.0001f)
        {
            flat.Normalize();
            transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flat), 10f * Time.deltaTime);
        }

        if (cc == null || !cc.enabled) return;
        if (cc.isGrounded) verticalVelocity = -1f;
        verticalVelocity += gravity * Time.deltaTime;
        Vector3 motion = flat * EffectiveMoveSpeed;
        motion.y = verticalVelocity;
        cc.Move(motion * Time.deltaTime);
    }

    /// <summary>Server-side bot attack: same raycast, same cooldown as a human monster.</summary>
    public void BotTryAttack()
    {
        if (!IsSpawned || !IsServer || !BotDriven) return;
        if (cooldownTimer > 0f || IsFrozen) return;
        if (mimic != null && mimic.IsBusy) return; // bots never disguise, but stay safe

        cooldownTimer = attackCooldown;
        PerformAttack();
    }

    // ------------------------- camera / settings -------------------------

    void AttachMainCamera()
    {
        Camera cam = Camera.main;
        if (cam == null || cameraHolder == null) return;
        cam.transform.SetParent(cameraHolder, false);
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;
        cam.fieldOfView = BaseFov();
    }

    void ReleaseMainCamera()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.transform.IsChildOf(transform))
            cam.transform.SetParent(null, true);
    }

    static float Sensitivity() => ScreamerSettings.Instance != null ? ScreamerSettings.Instance.MouseSensitivity : 2.2f;
    static bool InvertLook() => ScreamerSettings.Instance != null && ScreamerSettings.Instance.InvertY;
    static float BaseFov() => ScreamerSettings.Instance != null ? ScreamerSettings.Instance.Fov : 70f;
}
