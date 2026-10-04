using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// The storm-cellar escape door: six oversized padlocks (one shatters per
/// finished chore), an unlock at 6/6 that flips the round into the Finale,
/// and the scream-powered hydraulics - every living survivor gathers within
/// 4 units and mash-screams a shared 3-second meter until the door blows
/// open. Escapes are timestamped and broadcast so photo-finish watchers can
/// trigger their slow-mo.
///
/// Built by <see cref="TaskFactory.BuildCellarDoor"/> as an in-scene placed
/// NetworkObject. All meter/lock state is server-authoritative.
/// </summary>
public class EscapeDoor : NetworkBehaviour
{
    public static EscapeDoor Instance { get; private set; }

    const int PadlockCount = 6;

    [Header("Finale balance (GDD 4.4)")]
    [Tooltip("Survivors must be within this many units of the door to scream into it.")]
    public float screamRange = 4f;
    [Tooltip("Seconds of combined group mashing needed to fill the meter.")]
    public float meterSeconds = 3f;
    [Tooltip("Mash presses per second EACH living survivor is assumed to land.")]
    public float assumedGroupScreamsPerSecond = 6f;

    [Header("Door open theatrics")]
    public float panelOpenDegrees = 120f;
    public float panelOpenSeconds = 0.4f;

    [Header("Props (wired by TaskFactory)")]
    [Tooltip("Six padlock props, left to right. Disabled one by one as chores finish.")]
    public GameObject[] padlocks = new GameObject[PadlockCount];
    [Tooltip("Hinge transforms; the panels swing on these when the door blows open.")]
    public Transform leftHinge;
    public Transform rightHinge;
    [Tooltip("World-space sign labels; fonts are re-acquired at runtime.")]
    public UnityEngine.UI.Text[] signLabels = new UnityEngine.UI.Text[0];

    // ------------------------- Synchronized state -------------------------

    /// <summary>Padlocks shattered so far, 0..6. Drives door props and HUD icons.</summary>
    public NetworkVariable<int> PadlocksBroken = new NetworkVariable<int>(0);

    /// <summary>True from 6/6 chores until the rematch reset; the finale gather phase.</summary>
    public NetworkVariable<bool> Unlocked = new NetworkVariable<bool>(false);

    /// <summary>The shared scream meter, 0..1.</summary>
    public NetworkVariable<float> FinaleMeter = new NetworkVariable<float>(0f);

    /// <summary>True once the meter filled and the door blew open.</summary>
    public NetworkVariable<bool> Open = new NetworkVariable<bool>(false);

    /// <summary>Every client, whenever the shared scream meter moves.</summary>
    public static event System.Action<float> OnFinaleMeterChanged;

    /// <summary>
    /// Every client, per escape: (actorId, seconds since the door blew open).
    /// Photo-finish watchers compare these times; within 2 s = slow-mo.
    /// </summary>
    public static event System.Action<ulong, float> OnClientEscape;

    // ------------------------- Internals -------------------------

    Quaternion leftHingeClosed;
    Quaternion rightHingeClosed;
    Coroutine openRoutine;
    float localScreamSfxCooldown;
    float botScreamTimer;
    bool announcedFinale;

    void Awake()
    {
        Instance = this;

        if (leftHinge != null) leftHingeClosed = leftHinge.localRotation;
        if (rightHinge != null) rightHingeClosed = rightHinge.localRotation;

        // World-space Text loses its runtime-created font across scene saves.
        if (signLabels != null)
            foreach (UnityEngine.UI.Text label in signLabels)
                if (label != null && label.font == null)
                    label.font = TaskFactory.RuntimeFont();
    }

    public override void OnDestroy()
    {
        if (Instance == this) Instance = null;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        PadlocksBroken.OnValueChanged += HandlePadlocksChanged;
        Unlocked.OnValueChanged += HandleUnlockedChanged;
        FinaleMeter.OnValueChanged += HandleMeterChanged;
        Open.OnValueChanged += HandleOpenChanged;

        // Late joiners sync their props to the current round state.
        ApplyPadlockVisuals(PadlocksBroken.Value);
        if (Open.Value) SnapPanels(open: true);

        if (IsServer && GameManager.Instance != null)
            GameManager.Instance.OnServerActorEscaped += ServerHandleActorEscaped;
    }

    public override void OnNetworkDespawn()
    {
        PadlocksBroken.OnValueChanged -= HandlePadlocksChanged;
        Unlocked.OnValueChanged -= HandleUnlockedChanged;
        FinaleMeter.OnValueChanged -= HandleMeterChanged;
        Open.OnValueChanged -= HandleOpenChanged;

        if (IsServer && GameManager.Instance != null)
            GameManager.Instance.OnServerActorEscaped -= ServerHandleActorEscaped;
    }

    // ------------------------- Server API -------------------------

    /// <summary>One chore done = one padlock gone. Called by TaskManager (server).</summary>
    public void ServerBreakPadlock()
    {
        if (!IsServer || Unlocked.Value) return;
        PadlocksBroken.Value = Mathf.Min(PadlockCount, PadlocksBroken.Value + 1);
    }

    /// <summary>
    /// 6/6 chores: the door unlocks and the round enters the Finale (mega-ping
    /// and monster speed bonus are GameManager's side of that call).
    /// </summary>
    public void ServerUnlock()
    {
        if (!IsServer || Unlocked.Value) return;
        PadlocksBroken.Value = PadlockCount;
        Unlocked.Value = true;

        if (GameManager.Instance != null)
            GameManager.Instance.ServerEnterFinale();
    }

    /// <summary>Rematch reset: locked, six padlocks restored, meter drained, door shut.</summary>
    public void ServerReset()
    {
        if (!IsServer) return;
        PadlocksBroken.Value = 0;
        Unlocked.Value = false;
        FinaleMeter.Value = 0f;
        Open.Value = false;
        botScreamTimer = 0f;
    }

    void ServerHandleActorEscaped(ulong actorId)
    {
        // Timestamp relative to the door blowing open - the epoch every
        // photo-finish comparison shares.
        double openedAt = GameManager.Instance != null ? GameManager.Instance.DoorOpenedServerTime : 0;
        float sinceOpen = 0f;
        if (openedAt > 0 && NetworkManager != null)
            sinceOpen = Mathf.Max(0f, (float)(NetworkManager.ServerTime.Time - openedAt));

        EscapeClientRpc(actorId, sinceOpen);
    }

    // ------------------------- Finale scream meter -------------------------

    /// <summary>
    /// One mash of the scream key at the door. Server validates that the
    /// sender is a living survivor standing within range.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    public void FinaleScreamServerRpc(ServerRpcParams p = default)
    {
        if (!Unlocked.Value || Open.Value) return;

        GameManager gm = GameManager.Instance;
        if (gm == null || gm.State.Value != GameManager.GameState.Finale) return;

        ulong sender = p.Receive.SenderClientId;
        if (sender == gm.MonsterClientId.Value) return; // the monster screams plenty already

        // Alive + within 4.0 of the door, checked against the server's copy
        // of the sender's pawn - clients are not trusted with either.
        if (NetworkManager == null ||
            !NetworkManager.ConnectedClients.TryGetValue(sender, out NetworkClient client) ||
            client.PlayerObject == null) return;

        PlayerController pawn = client.PlayerObject.GetComponent<PlayerController>();
        if (pawn == null || pawn.IsGhost) return;
        if (Vector3.Distance(pawn.transform.position, transform.position) > screamRange) return;

        ServerAddScreamContribution(sender);
    }

    void ServerAddScreamContribution(ulong actorId)
    {
        if (!IsServer || Open.Value) return;

        // GDD 4.4: the 3-second meter is tuned for the WHOLE living group
        // mashing together, so one press is worth
        //   1 / (seconds * per-survivor cadence * living survivors).
        // A full lobby fills it in ~3 s of group screaming, a lone survivor
        // takes proportionally longer, and a crowd (humans plus bots) can no
        // longer blow the door open in under a second.
        int livingSurvivors = GameManager.Instance != null
            ? Mathf.Max(1, GameManager.Instance.LivingSurvivorCount)
            : 1;
        float perPress = 1f / Mathf.Max(1f, meterSeconds * assumedGroupScreamsPerSecond * livingSurvivors);
        FinaleMeter.Value = Mathf.Min(1f, FinaleMeter.Value + perPress);

        if (GameManager.Instance != null)
            GameManager.Instance.ServerAddStat(actorId, StatKind.FinaleScreamContribution, perPress);

        if (FinaleMeter.Value >= 1f)
            ServerBlowOpen();
    }

    void ServerBlowOpen()
    {
        if (!IsServer || Open.Value) return;
        FinaleMeter.Value = 1f;
        Open.Value = true;

        if (GameManager.Instance != null)
            GameManager.Instance.ServerDoorOpened();
    }

    // ------------------------- Per-frame -------------------------

    void Update()
    {
        if (IsServer) ServerBotScreamTick();

        localScreamSfxCooldown -= Time.deltaTime;
        ClientFinaleScreamInput();
    }

    void ServerBotScreamTick()
    {
        // Living bot survivors who happen to stand at the door lend their
        // lungs too, at a believable mash cadence - a solo human with bot
        // teammates still gets a group finale instead of a stuck meter.
        if (!Unlocked.Value || Open.Value) return;

        GameManager gm = GameManager.Instance;
        if (gm == null || gm.State.Value != GameManager.GameState.Finale) return;

        botScreamTimer += Time.deltaTime;
        if (botScreamTimer < 0.25f) return;
        botScreamTimer = 0f;

        foreach (BotPawn pawn in BotPawn.All)
        {
            if (pawn == null) continue;
            IVictim victim = pawn;
            if (!victim.IsCatchable) continue;
            if (Vector3.Distance(pawn.transform.position, transform.position) > screamRange) continue;
            ServerAddScreamContribution(pawn.BotId);
            if (Open.Value) return;
        }
    }

    void ClientFinaleScreamInput()
    {
        if (!Unlocked.Value || Open.Value) return;

        GameManager gm = GameManager.Instance;
        if (gm == null || gm.State.Value != GameManager.GameState.Finale) return;
        if (gm.IAmMonster) return;

        PlayerController local = PlayerController.Local;
        if (local == null || local.IsGhost) return;
        if (Vector3.Distance(local.transform.position, transform.position) > screamRange) return;

        if (!Input.GetKeyDown(KeyCode.Space)) return;

        FinaleScreamServerRpc();

        // Per-mash juice: a tiny punch and a raw throat, right now, locally.
        if (ScreamerCam.Instance != null)
            ScreamerCam.Instance.Shake(0.06f, 0.06f);
        if (localScreamSfxCooldown <= 0f && AudioDirector.Instance != null)
        {
            localScreamSfxCooldown = 0.12f;
            AudioDirector.Instance.Play(Sfx.Scream, local.transform.position, 0.9f, Random.Range(0.9f, 1.15f));
        }
    }

    // ------------------------- Escape -------------------------

    /// <summary>
    /// [E] on the door. Locked = a dry note; unlocked-but-shut = scream harder;
    /// open = the owner reports the escape to the server.
    /// </summary>
    public void TryEscape(PlayerController player)
    {
        if (player == null || player.IsGhost || !player.IsOwner) return;

        if (!Open.Value)
        {
            if (GameUI.Instance != null)
                GameUI.Instance.ShowEvent(Unlocked.Value ? GameCopy.FinalePrompt : GameCopy.DoorLocked);
            if (AudioDirector.Instance != null)
                AudioDirector.Instance.PlayUI(Sfx.UiThunk, 0.8f);
            return;
        }

        player.EscapeServerRpc();
    }

    // ------------------------- Client reactions -------------------------

    void HandlePadlocksChanged(int oldCount, int newCount)
    {
        ApplyPadlockVisuals(newCount);

        if (newCount > oldCount)
        {
            // A padlock shatters in the yard whether or not anyone is watching.
            if (AudioDirector.Instance != null)
                AudioDirector.Instance.Play(Sfx.GlassBreak, transform.position);
            if (ScreamerCam.Instance != null)
                ScreamerCam.Instance.Shake(0.1f, 0.15f);
        }
    }

    void ApplyPadlockVisuals(int brokenCount)
    {
        if (padlocks == null) return;
        for (int i = 0; i < padlocks.Length; i++)
            if (padlocks[i] != null)
                padlocks[i].SetActive(i >= brokenCount);
    }

    void HandleUnlockedChanged(bool wasUnlocked, bool isUnlocked)
    {
        if (!isUnlocked)
        {
            announcedFinale = false;
            return;
        }
        if (announcedFinale) return;
        announcedFinale = true;

        if (GameUI.Instance != null)
            GameUI.Instance.ShowEvent(GameCopy.FinalePrompt);
        if (AudioDirector.Instance != null)
            AudioDirector.Instance.Play(Sfx.PipeOrganSting, transform.position);
    }

    void HandleMeterChanged(float oldValue, float newValue)
    {
        OnFinaleMeterChanged?.Invoke(newValue);
    }

    void HandleOpenChanged(bool wasOpen, bool isOpen)
    {
        if (isOpen)
        {
            if (openRoutine != null) StopCoroutine(openRoutine);
            openRoutine = StartCoroutine(OpenPanelsRoutine());

            if (AudioDirector.Instance != null)
                AudioDirector.Instance.Play(Sfx.DoorExplosion, transform.position);
            if (ScreamerCam.Instance != null)
                ScreamerCam.Instance.Shake(0.6f, 0.5f);
        }
        else
        {
            // Rematch reset: the door quietly heals itself shut.
            if (openRoutine != null) { StopCoroutine(openRoutine); openRoutine = null; }
            SnapPanels(open: false);
        }
    }

    IEnumerator OpenPanelsRoutine()
    {
        float t = 0f;
        float seconds = Mathf.Max(0.05f, panelOpenSeconds);
        while (t < 1f)
        {
            t += Time.deltaTime / seconds;
            float eased = 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t)); // ease-out
            ApplyPanelSwing(eased);
            yield return null;
        }
        ApplyPanelSwing(1f);
        openRoutine = null;
    }

    void SnapPanels(bool open) => ApplyPanelSwing(open ? 1f : 0f);

    void ApplyPanelSwing(float t)
    {
        float angle = panelOpenDegrees * t;
        if (leftHinge != null)
            leftHinge.localRotation = leftHingeClosed * Quaternion.Euler(0f, 0f, angle);
        if (rightHinge != null)
            rightHinge.localRotation = rightHingeClosed * Quaternion.Euler(0f, 0f, -angle);
    }

    [ClientRpc]
    void EscapeClientRpc(ulong actorId, float secondsSinceDoorOpened)
    {
        OnClientEscape?.Invoke(actorId, secondsSinceDoorOpened);
    }
}
