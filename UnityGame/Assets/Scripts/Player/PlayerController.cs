using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The survivor pawn: walk/sprint/jump movement, sprint noise, context-sensitive
/// interaction prompts, the furniture slap, the diegetic lobby ready pose, and the
/// caught/escape flow that turns the pawn into a flying ghost spectator with one
/// Boo per round.
///
/// Prefab layout (CharacterFactory builds it): CharacterController + NetworkObject +
/// ClientNetworkTransform (owner authority) + this script, with a CameraHolder child
/// at head height, googly eyes, a color cap, a rim light, and an ArmR child for the
/// ready pose. GhostController is added at runtime after death -- it is a plain
/// MonoBehaviour because NGO forbids adding NetworkBehaviours at runtime, which is
/// why the Boo ServerRpc lives here.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : NetworkBehaviour, IVictim
{
    [Header("Movement (GDD 4.1)")]
    public float walkSpeed = 4.5f;
    public float runSpeed = 7.5f;
    public float jumpForce = 6f;
    public float gravity = -18f;

    [Header("Sprinting is loud (GDD 4.2)")]
    public float sprintNoiseInterval = 2.5f;
    [Range(0f, 1f)] public float sprintNoiseLoudness = 0.3f;

    [Header("Interaction")]
    public float interactRange = 3f;

    [Header("Furniture slap (GDD 9.4)")]
    public float slapRange = 2f;
    public float slapCooldown = 0.8f;
    [Range(0f, 1f)] public float slapLoudness = 0.45f;

    [Header("Rubber chicken decoy (GDD 14.3)")]
    public float decoyThrowRange = 12f;

    [Header("Rig references (wired by CharacterFactory)")]
    public Transform cameraHolder;
    public Transform readyArm;
    public Renderer capRenderer;
    public Light rimLight;

    // Fixed GDD 11.5 / 12.1 lines rendered by this pawn. Lines with a GameCopy
    // accessor come from the shared catalog; the rest live here (plan rule 13).
    const string BannerEscaped = "YOU MADE IT OUT. EVERYONE ELSE IS STILL DOING CHORES.";
    const string PopupInnocentSlap = "IT'S JUST A CHAIR.";
    const string PromptEscape = "[E] ESCAPE";
    const string PromptSlap = "[F] SLAP THE FURNITURE";

    /// <summary>The local player's pawn; null when none exists (menu, monster client mid-round).</summary>
    public static PlayerController Local { get; private set; }

    /// <summary>Every spawned survivor pawn on this client, ghosts included.</summary>
    public static readonly List<PlayerController> All = new List<PlayerController>();

    /// <summary>Index into ScreamerPalette.SurvivorColors; set by the server at spawn.</summary>
    public NetworkVariable<int> ColorIndex = new NetworkVariable<int>(0);

    /// <summary>Diegetic lobby ready state: raised hand, frozen in a goofy pose. Owner-written ([R]).</summary>
    public NetworkVariable<bool> ReadyPose = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    /// <summary>One Boo per ghost per round; the server flips this when it is spent.</summary>
    public NetworkVariable<bool> BooSpent = new NetworkVariable<bool>(false);

    /// <summary>One rubber-chicken decoy per survivor per round ([G], GDD 14.3).</summary>
    public NetworkVariable<bool> DecoySpent = new NetworkVariable<bool>(false);

    // Server-written: true once caught OR escaped. Replicated so late joiners see ghosts correctly.
    readonly NetworkVariable<bool> ghost = new NetworkVariable<bool>(false);

    CharacterController cc;
    float verticalVelocity;
    float cameraPitch;
    float sprintNoiseTimer;
    float slapTimer;
    bool wasSprinting;
    string shownPrompt;

    /// <summary>True after being caught or escaping; the pawn is a spectator from then on.</summary>
    public bool IsGhost => ghost.Value;

    /// <summary>Tasks lock movement while the player works (and panics).</summary>
    public bool InputLocked { get; set; }

    /// <summary>The closet this pawn is hiding in; null in the open (GDD 14.2).</summary>
    public HideSpot CurrentHideSpot { get; private set; }

    // ------------------------- IVictim -------------------------

    public ulong ActorId => OwnerClientId;
    public bool IsCatchable => !IsGhost && IsSpawned && IsHuntState();
    public Transform VictimTransform => transform;
    public void RequestCaught()
    {
        if (IsSpawned) GetCaughtServerRpc();
    }

    static bool IsHuntState()
    {
        if (GameManager.Instance == null) return true; // isolated module test scene
        GameManager.GameState s = GameManager.Instance.State.Value;
        return s == GameManager.GameState.Playing || s == GameManager.GameState.Finale;
    }

    // ------------------------- lifecycle -------------------------

    public override void OnNetworkSpawn()
    {
        cc = GetComponent<CharacterController>();
        if (!All.Contains(this)) All.Add(this);

        ColorIndex.OnValueChanged += OnColorChanged;
        ReadyPose.OnValueChanged += OnReadyPoseChanged;
        BooSpent.OnValueChanged += OnBooSpentChanged;
        ghost.OnValueChanged += OnGhostChanged;
        ScreamerSettings.OnChanged += OnSettingsChanged;

        ApplyColor(ColorIndex.Value);
        ApplyReadyPose(ReadyPose.Value);

        if (IsOwner)
        {
            Local = this;
            AttachMainCamera();
            Cursor.lockState = CursorLockMode.Locked;
        }

        if (ghost.Value) ApplyGhostState(); // late joiners see existing ghosts correctly
        RefreshGhostVisibility();
    }

    public override void OnNetworkDespawn()
    {
        ColorIndex.OnValueChanged -= OnColorChanged;
        ReadyPose.OnValueChanged -= OnReadyPoseChanged;
        BooSpent.OnValueChanged -= OnBooSpentChanged;
        ghost.OnValueChanged -= OnGhostChanged;
        ScreamerSettings.OnChanged -= OnSettingsChanged;

        All.Remove(this);
        if (Local == this)
        {
            ClearPrompt();
            ReleaseMainCamera();
            Local = null;
        }
    }

    // ------------------------- update loop (owner only) -------------------------

    void Update()
    {
        if (!IsSpawned || !IsOwner || IsGhost) return;

        slapTimer -= Time.deltaTime;

        GameManager.GameState state = GameManager.Instance != null
            ? GameManager.Instance.State.Value
            : GameManager.GameState.Playing; // isolated module test scene

        if (state != GameManager.GameState.Lobby && ReadyPose.Value)
            ReadyPose.Value = false; // the pose is lobby-only

        switch (state)
        {
            case GameManager.GameState.Lobby:
                Look();
                HandleReadyInput();
                if (!InputLocked && !ReadyPose.Value) Move(emitNoise: false);
                ClearPrompt();
                break;

            case GameManager.GameState.Countdown:
                Look(); // frozen for the big numbers; eyes free to panic
                ClearPrompt();
                break;

            case GameManager.GameState.Lockdown:
                Look();
                if (!InputLocked) Move(emitNoise: true);
                ClearPrompt();
                break;

            case GameManager.GameState.Playing:
            case GameManager.GameState.Finale:
                Look();
                if (CurrentHideSpot != null)
                {
                    HandleHiddenInput(); // doors shut, nerves fraying, [E] to leave
                }
                else if (!InputLocked)
                {
                    Move(emitNoise: true);
                    UpdatePrompt();
                    HandleInteractInput();
                    HandleSlapInput();
                    HandleDecoyInput();
                }
                else ClearPrompt();
                break;

            default: // Results
                ClearPrompt();
                break;
        }
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

    void Move(bool emitNoise)
    {
        Vector3 input = new Vector3(Input.GetAxisRaw("Horizontal"), 0f, Input.GetAxisRaw("Vertical"));
        bool moving = input.sqrMagnitude > 0.01f;
        bool sprinting = moving && Input.GetKey(KeyCode.LeftShift);
        float speed = sprinting ? runSpeed : walkSpeed;

        Vector3 move = moving ? transform.TransformDirection(input.normalized) * speed : Vector3.zero;

        if (cc.isGrounded)
        {
            verticalVelocity = -1f;
            if (Input.GetButtonDown("Jump")) verticalVelocity = jumpForce;
        }
        verticalVelocity += gravity * Time.deltaTime;
        move.y = verticalVelocity;
        cc.Move(move * Time.deltaTime);

        UpdateSprintFeel(sprinting);

        // Sprinting is never free: a loudness 0.3 ping every 2.5 s while on the ground.
        if (emitNoise && sprinting && cc.isGrounded)
        {
            sprintNoiseTimer += Time.deltaTime;
            if (sprintNoiseTimer >= sprintNoiseInterval)
            {
                sprintNoiseTimer = 0f;
                EmitNoise(sprintNoiseLoudness, NoiseType.Footsteps);
            }
        }
        else sprintNoiseTimer = 0f;
    }

    void UpdateSprintFeel(bool sprinting)
    {
        if (sprinting == wasSprinting) return;
        wasSprinting = sprinting;
        if (ScreamerCam.Instance != null)
            ScreamerCam.Instance.FovKick(BaseFov() + (sprinting ? 6f : 0f), 0.3f);
    }

    void EmitNoise(float loudness, NoiseType type)
    {
        if (NoiseSystem.Instance != null)
            NoiseSystem.Instance.MakeNoise(OwnerClientId, transform.position, loudness, type, GameCopy.NoiseLabel(type));
        if (GameUI.Instance != null)
            GameUI.Instance.PulseSelfNoise(loudness); // you always know you just snitched on yourself
    }

    void HandleReadyInput()
    {
        if (!Input.GetKeyDown(KeyCode.R)) return;

        // [R] is a full ready-up (GDD 5.1), not just the pose: the raised arm
        // is cosmetic, while the roster's ready flag is what gates the host's
        // START ROUND. Both flip together so the two never contradict.
        bool ready = !ReadyPose.Value;
        ReadyPose.Value = ready;
        GameManager.Instance?.SetReadyServerRpc(ready);
    }

    // ------------------------- prompts & interaction -------------------------

    void UpdatePrompt()
    {
        string prompt = null;
        if (cameraHolder != null && Physics.Raycast(cameraHolder.position, cameraHolder.forward,
                out RaycastHit hit, interactRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
        {
            TaskBase task = hit.collider.GetComponentInParent<TaskBase>();
            if (task != null && !task.IsDone)
            {
                string label = string.IsNullOrEmpty(task.taskName) ? "WORK" : task.taskName.ToUpperInvariant();
                prompt = "[E] " + label;
            }
            else if (hit.collider.GetComponentInParent<EscapeDoor>() != null)
            {
                prompt = PromptEscape;
            }
            else if (hit.collider.GetComponentInParent<HideSpot>() is HideSpot spot)
            {
                prompt = spot.PromptFor(this);
            }
            else if (hit.distance <= slapRange && IsSlappable(hit))
            {
                // Identical prompt for real furniture and the disguised Mimic --
                // the prompt must never work as a free Mimic detector.
                prompt = PromptSlap;
            }
        }
        SetPrompt(prompt);
    }

    static bool IsSlappable(RaycastHit hit)
    {
        if (hit.collider.GetComponentInParent<SlappableFurniture>() != null) return true;
        MimicDisguise mimic = hit.collider.GetComponentInParent<MimicDisguise>();
        return mimic != null && mimic.IsDisguised;
    }

    void SetPrompt(string prompt)
    {
        if (prompt == shownPrompt) return;
        shownPrompt = prompt;
        if (GameUI.Instance != null) GameUI.Instance.ShowPrompt(prompt);
    }

    void ClearPrompt() => SetPrompt(null);

    void HandleInteractInput()
    {
        if (!Input.GetKeyDown(KeyCode.E) || cameraHolder == null) return;
        if (!Physics.Raycast(cameraHolder.position, cameraHolder.forward, out RaycastHit hit,
                interactRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide)) return;

        TaskBase task = hit.collider.GetComponentInParent<TaskBase>();
        if (task != null) { task.TryStart(this); return; }

        EscapeDoor door = hit.collider.GetComponentInParent<EscapeDoor>();
        if (door != null) { door.TryEscape(this); return; }

        HideSpot spot = hit.collider.GetComponentInParent<HideSpot>();
        if (spot != null) spot.RequestInteract();
    }

    // ------------------------- hide & shriek: closets (GDD 14.2) -------------------------

    void HandleHiddenInput()
    {
        SetPrompt(GameCopy.PromptLeaveCloset);
        if (Input.GetKeyDown(KeyCode.E) && CurrentHideSpot != null)
            CurrentHideSpot.RequestInteract();
    }

    /// <summary>Occupant's owner client only: HideSpot snaps the pawn inside.</summary>
    public void EnterHideSpot(HideSpot spot, Vector3 insideWorldPos)
    {
        if (!IsOwner || IsGhost || spot == null) return;
        CurrentHideSpot = spot;
        if (cc != null) cc.enabled = false; // the closet shell is the collider now
        transform.position = insideWorldPos;
        UpdateSprintFeel(false);
        ClearPrompt();
    }

    /// <summary>Occupant's owner client only: leave (or get flushed out of) the closet.</summary>
    public void ExitHideSpot(Vector3 exitWorldPos)
    {
        if (!IsOwner || CurrentHideSpot == null) return;
        CurrentHideSpot = null;
        transform.position = exitWorldPos;
        if (cc != null && !IsGhost) cc.enabled = true;
        ClearPrompt();
    }

    // ------------------------- hide & shriek: the decoy (GDD 14.3) -------------------------

    void HandleDecoyInput()
    {
        if (!Input.GetKeyDown(KeyCode.G) || DecoySpent.Value || cameraHolder == null) return;

        // Aim where the crosshair looks; walls catch the bird honestly.
        Vector3 origin = cameraHolder.position;
        Vector3 dir = cameraHolder.forward;
        Vector3 target = Physics.Raycast(origin, dir, out RaycastHit hit, decoyThrowRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
            ? hit.point - dir * 0.4f
            : origin + dir * decoyThrowRange;
        target.y = Mathf.Max(0.25f, target.y);

        ThrowDecoyServerRpc(target);
    }

    /// <summary>
    /// The decoy's noise is ENVIRONMENTAL (actor id ulong.MaxValue) and wears the
    /// real chicken's label, so the monster cannot tell the liar from the task
    /// chicken - and nobody farms LOUDEST HUMAN with a toy.
    /// </summary>
    // SECURITY: client-picked landing spot (private-lobby trust model, GDD 8.2).
    [ServerRpc]
    void ThrowDecoyServerRpc(Vector3 target)
    {
        if (ghost.Value || DecoySpent.Value || !IsHuntState()) return;
        float maxSq = decoyThrowRange * decoyThrowRange * 4f; // lenient: camera offset + lag
        if ((target - transform.position).sqrMagnitude > maxSq) return;

        DecoySpent.Value = true;
        StartCoroutine(ServerDecoySqueaks(target));
        DecoyClientRpc(transform.position + Vector3.up * 1.2f, target);
    }

    IEnumerator ServerDecoySqueaks(Vector3 at)
    {
        yield return new WaitForSeconds(DecoyChicken.FlightSeconds);
        for (int i = 0; i < DecoyChicken.SqueakCount; i++)
        {
            if (!IsHuntState()) yield break; // the round ended; the bird can stop lying
            if (NoiseSystem.Instance != null)
                NoiseSystem.Instance.ServerMakeNoise(ulong.MaxValue, at, DecoyChicken.SqueakLoudness,
                    NoiseType.Chicken, GameCopy.NoiseChicken);
            yield return new WaitForSeconds(DecoyChicken.SqueakInterval);
        }
    }

    [ClientRpc]
    void DecoyClientRpc(Vector3 from, Vector3 to)
    {
        DecoyChicken.Spawn(from, to);
        if (IsOwner && GagFeedback.Instance != null)
            GagFeedback.Instance.Popup(GameCopy.PopupDecoyThrown, ScreamerPalette.ScreamYellow);
    }

    void HandleSlapInput()
    {
        if (!Input.GetKeyDown(KeyCode.F) || slapTimer > 0f || cameraHolder == null) return;
        slapTimer = slapCooldown; // whiffs cost the cooldown too

        if (!Physics.Raycast(cameraHolder.position, cameraHolder.forward, out RaycastHit hit,
                slapRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return;

        MimicDisguise mimic = hit.collider.GetComponentInParent<MimicDisguise>();
        if (mimic != null && mimic.IsDisguised)
        {
            // The "furniture" slaps back: trigger the Mimic at point blank. No
            // innocent ping -- the unfolding couch is its own announcement.
            mimic.NotifySlapped();
            PlaySlapFeel(hit.point, innocent: false);
            return;
        }

        if (hit.collider.GetComponentInParent<SlappableFurniture>() != null)
        {
            SlapServerRpc(); // stat + loudness 0.45 ping, server side
            if (GameUI.Instance != null) GameUI.Instance.PulseSelfNoise(slapLoudness);
            PlaySlapFeel(hit.point, innocent: true);
        }
    }

    void PlaySlapFeel(Vector3 at, bool innocent)
    {
        if (AudioDirector.Instance != null) AudioDirector.Instance.Play(Sfx.PatPat, at);
        if (ScreamerCam.Instance != null) ScreamerCam.Instance.Shake(0.08f, 0.1f);
        if (innocent && GagFeedback.Instance != null)
            GagFeedback.Instance.Popup(PopupInnocentSlap, ScreamerPalette.ScreamYellow);
    }

    // ------------------------- catch / escape / boo (RPCs) -------------------------

    /// <summary>Server-side catch entry. RequireOwnership = false so the monster owner's
    /// client can report the catch through IVictim.RequestCaught().</summary>
    // SECURITY: client-trusted catch report (private-lobby trust model, GDD 8.2).
    [ServerRpc(RequireOwnership = false)]
    public void GetCaughtServerRpc()
    {
        if (ghost.Value || !IsHuntState()) return;
        ghost.Value = true;
        if (GameManager.Instance != null) GameManager.Instance.ServerActorCaught(OwnerClientId);
        GhostBannerClientRpc(true);
    }

    /// <summary>Owner-only: EscapeDoor routes a valid escape here.</summary>
    [ServerRpc]
    public void EscapeServerRpc()
    {
        if (ghost.Value || !IsHuntState()) return;
        ghost.Value = true;
        if (GameManager.Instance != null) GameManager.Instance.ServerActorEscaped(OwnerClientId);
        GhostBannerClientRpc(false);
    }

    /// <summary>Ghost Boo: flashes the target and fires a real loudness 0.7 ping at its position.</summary>
    // SECURITY: the ghost's client picks the target via look-raycast; the server checks
    // only spend-once, ghost-ness, and target liveness (GDD 8.2).
    [ServerRpc]
    public void BooServerRpc(ulong targetActorId)
    {
        if (!ghost.Value || BooSpent.Value || !IsHuntState()) return;

        IVictim target = FindVictim(targetActorId);
        if (target == null || !target.IsCatchable || target.VictimTransform == null) return;

        BooSpent.Value = true;
        Vector3 at = target.VictimTransform.position;

        if (GameManager.Instance != null)
            GameManager.Instance.ServerAddStat(OwnerClientId, StatKind.BoosUsed, 1f);
        if (NoiseSystem.Instance != null)
            NoiseSystem.Instance.ServerMakeNoise(targetActorId, at, 0.7f, NoiseType.Boo, GameCopy.NoiseLabel(NoiseType.Boo));

        string ghostName = GameManager.Instance != null ? GameManager.Instance.ActorName(OwnerClientId) : "A ghost";
        string targetName = GameManager.Instance != null ? GameManager.Instance.ActorName(targetActorId) : "someone";
        BooDeliveredClientRpc(targetActorId, ghostName, targetName);
    }

    /// <summary>Innocent furniture slap: counts the stat and emits the loudness 0.45 ping.</summary>
    // SECURITY: range and cooldown are enforced only on the slapping client (GDD 8.2).
    [ServerRpc(RequireOwnership = false)]
    public void SlapServerRpc()
    {
        if (ghost.Value || !IsHuntState()) return;
        if (GameManager.Instance != null)
            GameManager.Instance.ServerAddStat(OwnerClientId, StatKind.FurnitureSlaps, 1f);
        if (NoiseSystem.Instance != null)
            NoiseSystem.Instance.ServerMakeNoise(OwnerClientId, transform.position, slapLoudness,
                NoiseType.Slap, GameCopy.NoiseLabel(NoiseType.Slap));
    }

    [ClientRpc]
    void GhostBannerClientRpc(bool wasCaught)
    {
        if (!IsOwner) return;

        if (wasCaught && ScreamerCam.Instance != null)
        {
            // The bite lands with the contract's 0.15 s catch freeze (GDD 12.1).
            ScreamerCam.Instance.Hitstop(0.15f);
            ScreamerCam.Instance.Shake(0.4f, 0.3f);
        }

        if (GameUI.Instance != null)
            GameUI.Instance.ShowCenterCard(wasCaught ? GameCopy.BannerCaught : BannerEscaped,
                ScreamerPalette.GhostMint, 4f);
    }

    [ClientRpc]
    void BooDeliveredClientRpc(ulong targetActorId, string ghostName, string targetName)
    {
        if (GameUI.Instance != null) GameUI.Instance.ShowEvent(GameCopy.EventBoo(ghostName, targetName));

        if (Local == null || Local.OwnerClientId != targetActorId || Local.IsGhost) return;
        if (ScreenFxOverlay.Instance != null) ScreenFxOverlay.Instance.Flash(ScreamerPalette.GhostMint, 0.2f);
        if (AudioDirector.Instance != null) AudioDirector.Instance.PlayUI(Sfx.BooSting);
        if (ScreamerCam.Instance != null) ScreamerCam.Instance.Shake(0.35f, 0.25f);
    }

    static IVictim FindVictim(ulong actorId)
    {
        foreach (PlayerController pc in All)
            if (pc != null && pc.OwnerClientId == actorId) return pc;

        // Bots and any future victim types, without referencing their concrete class.
        foreach (NetworkBehaviour nb in FindObjectsOfType<NetworkBehaviour>())
            if (nb is IVictim v && v.ActorId == actorId) return v;

        return null;
    }

    // ------------------------- ghost state -------------------------

    void OnGhostChanged(bool previous, bool next)
    {
        if (next) ApplyGhostState();
    }

    void ApplyGhostState()
    {
        // Structure first: no collisions, no further catches.
        if (cc != null) cc.enabled = false;
        foreach (Collider col in GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        if (IsOwner)
        {
            InputLocked = false;
            ClearPrompt();
            UpdateSprintFeel(false);
            if (GetComponent<GhostController>() == null) gameObject.AddComponent<GhostController>();
            if (GameUI.Instance != null) GameUI.Instance.SetGhostHud(true, !BooSpent.Value);
            if (ScreenFxOverlay.Instance != null) ScreenFxOverlay.Instance.SetDesaturation(0.3f);
        }

        RefreshGhostVisibility();
    }

    /// <summary>
    /// Ghosts are invisible to the living (and to the monster) but visible, mint-tinted,
    /// to other ghosts. Re-evaluated whenever anyone's ghost state changes.
    /// </summary>
    static void RefreshGhostVisibility()
    {
        bool viewerIsGhost = Local != null && Local.IsGhost;
        foreach (PlayerController pc in All)
        {
            if (pc == null || !pc.IsGhost) continue;

            bool visible = viewerIsGhost && pc != Local; // never render your own floating body
            foreach (Renderer r in pc.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = visible;
                if (visible) r.material.color = ScreamerPalette.GhostMint;
            }
            if (pc.rimLight != null)
            {
                pc.rimLight.enabled = visible;
                if (visible) pc.rimLight.color = ScreamerPalette.GhostMint;
            }
        }
    }

    void OnBooSpentChanged(bool previous, bool next)
    {
        if (IsOwner && IsGhost && GameUI.Instance != null)
            GameUI.Instance.SetGhostHud(true, !next);
    }

    // ------------------------- cosmetics -------------------------

    void OnColorChanged(int previous, int next) => ApplyColor(next);

    void OnSettingsChanged()
    {
        // The colorblind palette applies live (GDD 7.5). Ghosts keep their
        // mint tint; everyone else re-resolves their identity color.
        if (!IsGhost) ApplyColor(ColorIndex.Value);
    }

    void ApplyColor(int colorIndex)
    {
        Color c = ScreamerPalette.Survivor(colorIndex);
        if (capRenderer != null) capRenderer.material.color = c;
        if (rimLight != null) rimLight.color = c;
    }

    void OnReadyPoseChanged(bool previous, bool next) => ApplyReadyPose(next);

    void ApplyReadyPose(bool ready)
    {
        if (readyArm != null)
            readyArm.localRotation = ready
                ? Quaternion.Euler(0f, 0f, 160f)   // hand up, frozen, committed
                : Quaternion.Euler(0f, 0f, -15f);
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
