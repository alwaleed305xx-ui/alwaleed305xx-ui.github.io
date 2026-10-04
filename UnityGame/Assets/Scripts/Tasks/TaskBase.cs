using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Base class for every chore in the house. A task:
/// - has a name and a deadpan flavor line shown on the task panel,
/// - takes time to finish (you are standing still - easy meat),
/// - emits noise on an interval, and the monster hears every bit of it.
///
/// Flow: the local player raycasts the station and calls <see cref="TryStart"/>.
/// Progress runs client-side on the runner's machine (the station merely hosts
/// the state); completion is reported to the server, which flips the
/// synchronized <c>done</c> flag, credits the stat, and lets
/// <see cref="OnAnyTaskCompleted"/> fire on every client. Bots advance the same
/// task honestly through <see cref="ServerBotWork"/>: same noise, same fail
/// rolls, same completion path.
///
/// Stations are in-scene placed NetworkObjects built by <see cref="TaskFactory"/>.
/// </summary>
public abstract class TaskBase : NetworkBehaviour
{
    [Header("Task identity")]
    public string taskName = "CHORE";
    [TextArea] public string flavorText = "";

    [Header("Balance (GDD 4.3)")]
    [Tooltip("Seconds of progress at 1x multiplier.")]
    public float duration = 6f;
    [Tooltip("Seconds between ambient noise pings while someone works. <= 0 disables the ambient ping.")]
    public float noiseInterval = 1.5f;
    [Range(0f, 1f)] public float noiseLoudness = 0.5f;
    public NoiseType noiseType = NoiseType.Music;
    [Tooltip("Label the monster reads for the ambient ping (GDD 4.2).")]
    public string noiseLabel = "";

    [Header("Optional station audio loop (plays in 3D while someone works)")]
    public AudioSource taskSound;

    [Header("World label (wired by TaskFactory)")]
    public UnityEngine.UI.Text worldLabel;

    // ------------------------- Synchronized state -------------------------

    // Server-written; late joiners catch up through the initial sync.
    // completedBy is declared BEFORE done on purpose: NetworkVariable deltas
    // apply in declaration order, so handlers reacting to done flipping true
    // always read the already-updated completer id.
    readonly NetworkVariable<ulong> completedBy = new NetworkVariable<ulong>(ulong.MaxValue);
    readonly NetworkVariable<bool> soundOn = new NetworkVariable<bool>(false);
    readonly NetworkVariable<bool> done = new NetworkVariable<bool>(false);

    /// <summary>True once anyone (human or bot) finished this chore this round.</summary>
    public bool IsDone => done.Value;

    /// <summary>Actor id of whoever finished the task; ulong.MaxValue while unfinished.</summary>
    public ulong CompletedByActorId => completedBy.Value;

    /// <summary>Fires on every client when a task's done flag flips true.</summary>
    public static event Action<TaskBase> OnAnyTaskCompleted;

    // ------------------------- Local-run state -------------------------

    protected PlayerController currentPlayer;
    float progress;
    float noiseTimer;
    bool running;

    // ------------------------- Server-side bot state -------------------------

    float serverBotProgress;
    float serverBotNoiseTimer;

    // Server only: which client's local run switched the station loop on.
    // Tracked so a disconnect can silence a loop its runner can never stop.
    ulong soundRunnerClientId = ulong.MaxValue;

    // ------------------------- Defaults -------------------------

    /// <summary>
    /// Writes this task's GDD 4.3 balance numbers and GameCopy strings into the
    /// serialized fields. Called by Unity's editor Reset() and by TaskFactory
    /// right after AddComponent, so stations are correctly tuned whether they
    /// are built in edit mode or at runtime.
    /// </summary>
    public virtual void ApplyBalanceDefaults() { }

    void Reset() => ApplyBalanceDefaults();

    // ------------------------- Lifecycle -------------------------

    protected virtual void Awake()
    {
        // Fonts created via Font.CreateDynamicFontFromOSFont cannot be saved
        // into the scene, so the label re-acquires one on every boot.
        if (worldLabel != null && worldLabel.font == null)
            worldLabel.font = TaskFactory.RuntimeFont();
    }

    public override void OnNetworkSpawn()
    {
        done.OnValueChanged += HandleDoneChanged;
        soundOn.OnValueChanged += HandleSoundChanged;
    }

    public override void OnNetworkDespawn()
    {
        done.OnValueChanged -= HandleDoneChanged;
        soundOn.OnValueChanged -= HandleSoundChanged;
    }

    void HandleDoneChanged(bool wasDone, bool isDone)
    {
        if (!isDone) return; // ServerReset flipping back to false is silent

        // Someone else beat the local runner to it - fold the panel politely.
        if (running) StopLocalRun(GameCopy.TaskComplete, notifyEnd: true);

        OnAnyTaskCompleted?.Invoke(this);
    }

    void HandleSoundChanged(bool wasOn, bool isOn)
    {
        if (taskSound == null) return;
        if (isOn) taskSound.Play();
        else taskSound.Stop();
    }

    // ------------------------- Human interaction -------------------------

    /// <summary>
    /// Entry point for the local player's [E] interaction. Starts the chore,
    /// opens the task panel, and (for stationary chores) locks movement.
    /// </summary>
    public void TryStart(PlayerController player)
    {
        if (player == null || running || IsDone) return;
        if (player.IsGhost || player.InputLocked) return;
        if (!RoundAllowsTasks()) return;

        currentPlayer = player;
        running = true;
        progress = 0f;
        noiseTimer = 0f;

        if (LocksPlayerInput)
            player.InputLocked = true; // standing still, doing chores - easy meat

        if (GameUI.Instance != null)
        {
            GameUI.Instance.ShowTaskPanel(taskName, flavorText);
            GameUI.Instance.UpdateTaskProgress(0f);
        }

        OnTaskStart();
        if (IsSpawned) SetSoundServerRpc(true);
    }

    /// <summary>Chores that require chasing (the chicken) override this to false.</summary>
    protected virtual bool LocksPlayerInput => true;

    bool RoundAllowsTasks()
    {
        GameManager gm = GameManager.Instance;
        if (gm == null) return true; // factory test scenes without a round flow
        GameManager.GameState state = gm.State.Value;
        return state == GameManager.GameState.Lockdown || state == GameManager.GameState.Playing;
    }

    void Update()
    {
        ClientCosmeticTick();

        if (!running) return;

        // The runner died, left, or the round moved on: stop without ceremony.
        if (currentPlayer == null || currentPlayer.IsGhost || !RoundAllowsTasks())
        {
            StopLocalRun("", notifyEnd: true);
            return;
        }

        // [Q] abandons the chore and releases the player (GDD 4.3).
        if (Input.GetKeyDown(KeyCode.Q))
        {
            StopLocalRun(GameCopy.TaskCancelled, notifyEnd: true);
            return;
        }

        progress = Mathf.Max(0f, progress + Time.deltaTime * ProgressMultiplier());

        if (noiseInterval > 0f)
        {
            noiseTimer += Time.deltaTime;
            if (noiseTimer >= noiseInterval)
            {
                noiseTimer = 0f;
                EmitTaskNoise(noiseLoudness, noiseType, noiseLabel);
            }
        }

        if (GameUI.Instance != null)
            GameUI.Instance.UpdateTaskProgress(duration > 0f ? progress / duration : 1f);

        if (progress >= duration)
            CompleteLocally();
    }

    /// <summary>
    /// Runs every frame on every client, even while idle. Cosmetic-only hooks
    /// (the dance pad's beat cycle) live here so subclasses never shadow
    /// the base Update.
    /// </summary>
    protected virtual void ClientCosmeticTick() { }

    /// <summary>
    /// Interactive tasks shape their progress speed here (and read input here,
    /// since it runs once per frame while the local player works). May return
    /// negative values for visible rollback (the noodles).
    /// </summary>
    protected virtual float ProgressMultiplier() => 1f;

    protected virtual void OnTaskStart() { }
    protected virtual void OnTaskEnd() { }

    /// <summary>Per-task completion juice for the local runner (confetti, popups).</summary>
    protected virtual void OnLocalCompleted() { }

    /// <summary>Subclasses may push progress directly (mash-driven chores).</summary>
    protected void AddProgress(float seconds)
    {
        if (!running) return;
        progress = Mathf.Max(0f, progress + seconds);
    }

    void CompleteLocally()
    {
        OnLocalCompleted();
        StopLocalRun(GameCopy.TaskComplete, notifyEnd: true);
        if (IsSpawned) CompleteServerRpc();
    }

    void StopLocalRun(string closingLine, bool notifyEnd)
    {
        if (!running) return;
        running = false;

        if (currentPlayer != null && LocksPlayerInput)
            currentPlayer.InputLocked = false;
        currentPlayer = null;

        if (GameUI.Instance != null)
            GameUI.Instance.HideTaskPanel(closingLine);

        if (IsSpawned) SetSoundServerRpc(false);
        if (notifyEnd) OnTaskEnd();
    }

    // ------------------------- Noise plumbing -------------------------

    /// <summary>
    /// Local-run noise: routes through the client noise path (attributed to the
    /// local player) and spikes the task panel's VU meter.
    /// </summary>
    protected void EmitTaskNoise(float loudness, NoiseType type, string label)
    {
        ulong actorId = NetworkManager.Singleton != null
            ? NetworkManager.Singleton.LocalClientId
            : ulong.MaxValue;

        if (NoiseSystem.Instance != null)
            NoiseSystem.Instance.MakeNoise(actorId, transform.position, loudness, type, label);

        if (GameUI.Instance != null)
            GameUI.Instance.NotifyTaskNoise();
    }

    /// <summary>Server-side noise attributed to a bot (or the station itself).</summary>
    protected void ServerEmitTaskNoise(ulong actorId, float loudness, NoiseType type, string label)
    {
        if (NoiseSystem.Instance != null)
            NoiseSystem.Instance.ServerMakeNoise(actorId, transform.position, loudness, type, label);
    }

    // ------------------------- Server API -------------------------

    /// <summary>Rematch reset: unfinished, silent, progress wiped. Server only.</summary>
    public void ServerReset()
    {
        if (!IsServer) return;
        done.Value = false;
        soundOn.Value = false;
        soundRunnerClientId = ulong.MaxValue;
        completedBy.Value = ulong.MaxValue;
        serverBotProgress = 0f;
        serverBotNoiseTimer = 0f;
        OnServerReset();
    }

    /// <summary>
    /// Server: a client disconnected. If that client's local run left this
    /// station's audio loop on, switch it off - the departed client was the
    /// only sender of the matching "sound off" RPC, so nothing else ever would.
    /// </summary>
    public void ServerClearSoundForClient(ulong clientId)
    {
        if (!IsServer || clientId == ulong.MaxValue) return;
        if (soundRunnerClientId != clientId) return;
        soundRunnerClientId = ulong.MaxValue;
        if (soundOn.Value) soundOn.Value = false;
    }

    /// <summary>Per-task server-side reset hook (the chicken walks home).</summary>
    protected virtual void OnServerReset() { }

    /// <summary>
    /// The honest bot channel: a bot brain calls this every server frame while
    /// its bot stands at the station. Progress advances at a human-ish pace,
    /// ambient noise pings fire for real, and each task rolls its own real
    /// failure chances (scaled by the bot's chaos personality) - bots feed the
    /// comedy systems exactly like players do.
    /// </summary>
    public void ServerBotWork(ulong botId, float deltaTime, float chaosMultiplier)
    {
        if (!IsServer || done.Value || deltaTime <= 0f) return;

        GameManager gm = GameManager.Instance;
        if (gm != null &&
            gm.State.Value != GameManager.GameState.Lockdown &&
            gm.State.Value != GameManager.GameState.Playing) return;

        if (chaosMultiplier <= 0f) chaosMultiplier = 1f;

        if (noiseInterval > 0f)
        {
            serverBotNoiseTimer += deltaTime;
            if (serverBotNoiseTimer >= noiseInterval)
            {
                serverBotNoiseTimer = 0f;
                ServerEmitTaskNoise(botId, noiseLoudness, noiseType, noiseLabel);
            }
        }

        serverBotProgress = Mathf.Max(0f, serverBotProgress + ServerBotWorkStep(botId, deltaTime, chaosMultiplier));

        if (serverBotProgress >= duration)
            ServerComplete(botId);
    }

    /// <summary>
    /// Progress delta for one slice of bot work. Default: steady 1x progress.
    /// Subclasses simulate keypresses and roll their honest failure chances.
    /// </summary>
    protected virtual float ServerBotWorkStep(ulong botId, float deltaTime, float chaosMultiplier)
        => deltaTime;

    /// <summary>Server-side completion shared by the human RPC and the bot channel.</summary>
    protected void ServerComplete(ulong actorId)
    {
        if (!IsServer || done.Value) return;
        completedBy.Value = actorId;
        soundOn.Value = false;
        soundRunnerClientId = ulong.MaxValue;
        done.Value = true;

        if (GameManager.Instance != null)
            GameManager.Instance.ServerAddStat(actorId, StatKind.TasksCompleted, 1f);
    }

    // ------------------------- RPCs -------------------------

    [ServerRpc(RequireOwnership = false)]
    void CompleteServerRpc(ServerRpcParams p = default)
    {
        // SECURITY: client-trusted task completion (friends/invite-first scope,
        // GDD 8.2). A client claims it finished the chore; fine for private
        // lobbies, validate before public matchmaking.
        if (done.Value) return;
        ServerComplete(p.Receive.SenderClientId);
    }

    [ServerRpc(RequireOwnership = false)]
    void SetSoundServerRpc(bool on, ServerRpcParams p = default)
    {
        if (done.Value) return;
        soundOn.Value = on;
        soundRunnerClientId = on ? p.Receive.SenderClientId : ulong.MaxValue;
    }
}
