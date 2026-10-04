using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A hideable closet (Hide &amp; Shriek update, GDD 14.2). A survivor can step
/// inside and pull the doors shut; the doors are real geometry, so they hide
/// by honestly blocking line of sight - no invisibility tricks. The monster
/// (or a helpful friend) can open the closet from outside, which flushes the
/// occupant out with an involuntary yelp ping.
///
/// The closed-closet prompt is identical whether the closet is empty or
/// occupied - the same no-free-detector rule as the furniture slap prompt.
///
/// Built by HideSpotFactory as an in-scene placed NetworkObject; occupancy and
/// door state are server-authoritative. Entering, leaving and getting flushed
/// all make noise: a closet is a bet, not a bunker.
/// </summary>
public class HideSpot : NetworkBehaviour
{
    [Header("Noise (GDD 14.2)")]
    [Range(0f, 1f)] public float doorNoiseLoudness = 0.3f;
    [Range(0f, 1f)] public float flushYelpLoudness = 0.6f;

    [Header("Door swing")]
    public float doorOpenDegrees = 115f;
    public float doorSwingSeconds = 0.3f;

    [Header("Geometry (wired by HideSpotFactory)")]
    [Tooltip("Hinge pivots; each swings open in its own direction.")]
    public Transform leftHinge;
    public Transform rightHinge;
    [Tooltip("Where the occupant stands, in closet-local space.")]
    public Vector3 insideLocalPosition = Vector3.zero;
    [Tooltip("Where a leaver (or flushed victim) pops out, in closet-local space.")]
    public Vector3 exitLocalPosition = new Vector3(0f, 0f, 1.3f);

    const ulong NoOccupant = ulong.MaxValue;
    const float OccupantWatchInterval = 0.5f;
    const float InteractSlackRange = 5f; // lenient server range check (GDD 8.2 trust model)

    // Server-written, replicated so every client swings the same doors.
    readonly NetworkVariable<ulong> occupant = new NetworkVariable<ulong>(NoOccupant);
    readonly NetworkVariable<bool> doorsOpen = new NetworkVariable<bool>(false);

    float occupantWatchTimer;
    Coroutine swingRoutine;

    public bool IsOccupied => occupant.Value != NoOccupant;

    public Vector3 InsideWorldPos => transform.TransformPoint(insideLocalPosition);
    public Vector3 ExitWorldPos => transform.TransformPoint(exitLocalPosition);

    public override void OnNetworkSpawn()
    {
        doorsOpen.OnValueChanged += OnDoorsChanged;
        ApplyDoorPose(doorsOpen.Value, instant: true);

        if (IsServer && GameManager.Instance != null)
            GameManager.Instance.State.OnValueChanged += ServerOnStateChanged;
    }

    public override void OnNetworkDespawn()
    {
        doorsOpen.OnValueChanged -= OnDoorsChanged;
        if (IsServer && GameManager.Instance != null)
            GameManager.Instance.State.OnValueChanged -= ServerOnStateChanged;
    }

    void Update()
    {
        if (!IsSpawned || !IsServer || occupant.Value == NoOccupant) return;

        // Watchdog: a caught, escaped, or disconnected occupant frees the
        // closet without anyone touching the doors.
        occupantWatchTimer -= Time.deltaTime;
        if (occupantWatchTimer > 0f) return;
        occupantWatchTimer = OccupantWatchInterval;

        IVictim victim = FindVictim(occupant.Value);
        if (victim == null || !victim.IsCatchable)
            ServerReleaseOccupant(flush: false, popDoorsOpen: false);
    }

    // ------------------------- prompts -------------------------

    /// <summary>
    /// Context prompt for whoever is looking at the closet. Pass the local
    /// survivor pawn, or null for the monster. Deliberately identical for
    /// empty and occupied closed closets.
    /// </summary>
    public string PromptFor(PlayerController viewer)
    {
        if (viewer != null && viewer.CurrentHideSpot == this) return GameCopy.PromptLeaveCloset;
        if (doorsOpen.Value) return GameCopy.PromptCloseCloset;
        return GameCopy.PromptCloset;
    }

    // ------------------------- interaction -------------------------

    /// <summary>Any client's [E] on the closet routes here; the server resolves what it means.</summary>
    public void RequestInteract()
    {
        if (IsSpawned) InteractServerRpc();
    }

    // SECURITY: client-trusted interaction report; the server re-checks state,
    // occupancy, and a lenient range only (private-lobby trust model, GDD 8.2).
    [ServerRpc(RequireOwnership = false)]
    void InteractServerRpc(ServerRpcParams p = default)
    {
        if (!IsHuntState()) return;

        ulong sender = p.Receive.SenderClientId;

        // The occupant's own [E] is always "let me out".
        if (occupant.Value == sender)
        {
            ServerReleaseOccupant(flush: false, popDoorsOpen: false);
            return;
        }

        if (doorsOpen.Value)
        {
            doorsOpen.Value = false; // anyone can tidy up
            return;
        }

        if (occupant.Value != NoOccupant)
        {
            // Opened from outside with someone inside: flushed, loudly.
            ServerReleaseOccupant(flush: true, popDoorsOpen: true);
            return;
        }

        // Closed and empty: a living survivor steps in; the monster just
        // yanks it open and finds coats.
        IVictim senderVictim = FindVictim(sender);
        bool senderIsLivingSurvivor = senderVictim is PlayerController pc && pc.IsSpawned && !pc.IsGhost;
        if (senderIsLivingSurvivor)
        {
            Transform vt = senderVictim.VictimTransform;
            if (vt == null) return;
            if ((vt.position - transform.position).sqrMagnitude > InteractSlackRange * InteractSlackRange) return;

            occupant.Value = sender;
            occupantWatchTimer = OccupantWatchInterval;
            ServerDoorNoise(sender);
            HideClientRpc(InsideWorldPos, TargetOnly(sender));
        }
        else
        {
            doorsOpen.Value = true;
        }
    }

    void ServerReleaseOccupant(bool flush, bool popDoorsOpen)
    {
        ulong leaver = occupant.Value;
        if (leaver == NoOccupant) return;
        occupant.Value = NoOccupant;

        if (popDoorsOpen) doorsOpen.Value = true;

        // A vanished occupant (caught/disconnected) needs no teleport or noise.
        IVictim victim = FindVictim(leaver);
        if (victim == null || !victim.IsCatchable) return;

        UnhideClientRpc(ExitWorldPos, flush, TargetOnly(leaver));

        if (flush)
        {
            if (NoiseSystem.Instance != null)
                NoiseSystem.Instance.ServerMakeNoise(leaver, transform.position,
                    flushYelpLoudness, NoiseType.Scream, GameCopy.NoiseYelp);
            string name = GameManager.Instance != null ? GameManager.Instance.ActorName(leaver) : "Someone";
            FlushedClientRpc(name);
        }
        else
        {
            ServerDoorNoise(leaver);
        }
    }

    void ServerDoorNoise(ulong actorId)
    {
        if (NoiseSystem.Instance != null)
            NoiseSystem.Instance.ServerMakeNoise(actorId, transform.position,
                doorNoiseLoudness, NoiseType.Slap, GameCopy.NoiseClosetDoor);
    }

    void ServerOnStateChanged(GameManager.GameState previous, GameManager.GameState next)
    {
        // Rematch hygiene: the round reset empties and shuts every closet.
        if (next != GameManager.GameState.Lobby && next != GameManager.GameState.Results) return;
        ServerReleaseOccupant(flush: false, popDoorsOpen: false);
        if (doorsOpen.Value) doorsOpen.Value = false;
    }

    // ------------------------- occupant client hops -------------------------

    [ClientRpc]
    void HideClientRpc(Vector3 insideWorldPos, ClientRpcParams target = default)
    {
        if (PlayerController.Local != null)
            PlayerController.Local.EnterHideSpot(this, insideWorldPos);
        if (AudioDirector.Instance != null)
            AudioDirector.Instance.PlayUI(Sfx.Breathing, 0.5f);
    }

    [ClientRpc]
    void UnhideClientRpc(Vector3 exitWorldPos, bool flushed, ClientRpcParams target = default)
    {
        if (PlayerController.Local != null)
            PlayerController.Local.ExitHideSpot(exitWorldPos);
        if (flushed && GagFeedback.Instance != null)
            GagFeedback.Instance.Popup(GameCopy.PopupPeekaboo, ScreamerPalette.MonsterRed);
        if (flushed && ScreamerCam.Instance != null)
            ScreamerCam.Instance.Shake(0.3f, 0.25f);
    }

    [ClientRpc]
    void FlushedClientRpc(string occupantName)
    {
        if (GameUI.Instance != null)
            GameUI.Instance.ShowEvent(GameCopy.EventFlushed(occupantName));
    }

    static ClientRpcParams TargetOnly(ulong clientId) => new ClientRpcParams
    {
        Send = new ClientRpcSendParams { TargetClientIds = new[] { clientId } }
    };

    // ------------------------- doors -------------------------

    void OnDoorsChanged(bool previous, bool next) => ApplyDoorPose(next, instant: false);

    void ApplyDoorPose(bool open, bool instant)
    {
        if (swingRoutine != null) { StopCoroutine(swingRoutine); swingRoutine = null; }

        if (instant || !gameObject.activeInHierarchy)
        {
            SetHingeAngle(open ? doorOpenDegrees : 0f);
            return;
        }

        swingRoutine = StartCoroutine(SwingDoors(open));
        if (AudioDirector.Instance != null)
            AudioDirector.Instance.Play(Sfx.StampThunk, transform.position, 0.7f, open ? 1.1f : 0.9f);
    }

    IEnumerator SwingDoors(bool open)
    {
        float from = CurrentHingeAngle();
        float to = open ? doorOpenDegrees : 0f;
        float t = 0f;
        while (t < doorSwingSeconds)
        {
            t += Time.deltaTime;
            float n = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / doorSwingSeconds));
            SetHingeAngle(Mathf.Lerp(from, to, n));
            yield return null;
        }
        SetHingeAngle(to);
        swingRoutine = null;
    }

    float CurrentHingeAngle()
    {
        if (leftHinge == null) return 0f;
        float y = leftHinge.localEulerAngles.y;
        return y > 180f ? 360f - y : y;
    }

    void SetHingeAngle(float degrees)
    {
        if (leftHinge != null) leftHinge.localRotation = Quaternion.Euler(0f, -degrees, 0f);
        if (rightHinge != null) rightHinge.localRotation = Quaternion.Euler(0f, degrees, 0f);
    }

    // ------------------------- lookups -------------------------

    static IVictim FindVictim(ulong actorId)
    {
        foreach (PlayerController pc in PlayerController.All)
            if (pc != null && pc.OwnerClientId == actorId) return pc;
        foreach (NetworkBehaviour nb in FindObjectsOfType<NetworkBehaviour>())
            if (nb is IVictim v && !(nb is PlayerController) && v.ActorId == actorId) return v;
        return null;
    }

    static bool IsHuntState()
    {
        if (GameManager.Instance == null) return true; // isolated module test scene
        GameManager.GameState s = GameManager.Instance.State.Value;
        return s == GameManager.GameState.Playing || s == GameManager.GameState.Finale;
    }
}
