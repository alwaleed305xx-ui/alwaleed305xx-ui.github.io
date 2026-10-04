using UnityEngine;

/// <summary>
/// The living soundscape around the local ear (always the main camera, since
/// the AudioListener rides it):
///
/// - Den / yard room-tone loops, crossfaded as the camera crosses the back
///   door line.
/// - The monster-proximity dread layer: a beating drone fades in inside the
///   15-unit dread radius (GDD 4.4) and a heartbeat joins underneath when the
///   thing gets properly close. The monster itself is spared its own theme.
/// - Local body foley: walk/sprint footsteps and a sprint breathing loop for
///   the local survivor pawn (GDD 12.1), inferred from its CharacterController
///   so no other module needs a new API.
/// - Ghost acoustics: a gentle echo filter on the listener while the local
///   player is dead (GDD 12.1 "reverb added to all audio").
///
/// All volumes are multiplied by <see cref="AudioDirector.BusGain"/> every
/// frame, so the settings sliders apply live to running loops.
/// </summary>
public class AmbienceController : MonoBehaviour
{
    [Header("Dread radius (GDD 4.4)")]
    [Tooltip("Drone starts fading in when the monster is this close to the camera.")]
    public float dreadRadius = 15f;
    [Tooltip("Heartbeat starts underneath the drone at this distance.")]
    public float heartbeatRadius = 10f;

    [Header("Map line between den tone and yard tone")]
    [Tooltip("Camera Z beyond this plays the yard loop (the back door line).")]
    public float yardBoundaryZ = 12.5f;

    [Header("Mix (pre-bus volumes)")]
    public float roomToneVolume = 0.85f;
    public float droneVolume = 0.95f;
    public float heartbeatVolume = 0.9f;
    public float breathingVolume = 0.6f;
    public float footstepVolume = 0.3f;

    [Header("Fade speeds (volume per second)")]
    public float crossfadeSpeed = 1.2f;
    public float dreadFadeSpeed = 0.8f;

    AudioSource denSource;
    AudioSource yardSource;
    AudioSource droneSource;
    AudioSource heartbeatSource;
    AudioSource breathingSource;

    // Current logical levels (0..1), eased toward their targets.
    float denLevel = 1f;
    float yardLevel;
    float droneLevel;
    float heartbeatLevel;
    float breathingLevel;

    float footstepTimer;
    AudioEchoFilter ghostEcho;
    GameObject echoHost;

    void Start()
    {
        // Built in Start: AudioDirector.Awake (same install, order undefined)
        // must have baked the bank first.
        denSource = CreateLoopSource("Ambience_Den", Sfx.DenAmbience);
        yardSource = CreateLoopSource("Ambience_Yard", Sfx.YardAmbience);
        droneSource = CreateLoopSource("Ambience_MonsterDrone", Sfx.MonsterDrone);
        heartbeatSource = CreateLoopSource("Ambience_Heartbeat", Sfx.Heartbeat);
        breathingSource = CreateLoopSource("Ambience_Breathing", Sfx.Breathing);
    }

    void OnDestroy()
    {
        if (ghostEcho != null) ghostEcho.enabled = false;
    }

    void Update()
    {
        Camera ear = Camera.main;
        if (ear == null || AudioDirector.Instance == null) return;

        float dt = Time.unscaledDeltaTime;

        UpdateRoomTone(ear.transform.position, dt);
        UpdateDread(ear.transform.position, dt);
        UpdateLocalBody(dt);
        UpdateGhostEcho(ear.gameObject);
        ApplyVolumes();
    }

    // ------------------------- Room tone -------------------------

    void UpdateRoomTone(Vector3 earPosition, float dt)
    {
        bool inYard = earPosition.z > yardBoundaryZ;
        denLevel = Mathf.MoveTowards(denLevel, inYard ? 0f : 1f, crossfadeSpeed * dt);
        yardLevel = Mathf.MoveTowards(yardLevel, inYard ? 1f : 0f, crossfadeSpeed * dt);
    }

    // ------------------------- Dread layer -------------------------

    void UpdateDread(Vector3 earPosition, float dt)
    {
        float droneTarget = 0f;
        float heartbeatTarget = 0f;

        MonsterController monster = MonsterController.ActiveMonster;
        bool inRound = IsHuntState();
        bool iAmMonster = GameManager.Instance != null && GameManager.Instance.IAmMonster;

        if (monster != null && inRound && !iAmMonster)
        {
            float distance = Vector3.Distance(earPosition, monster.transform.position);

            // Drone: silent at the dread radius, full a few steps from the teeth.
            droneTarget = Mathf.Clamp01(1f - (distance - 3f) / Mathf.Max(1f, dreadRadius - 3f));

            // Heartbeat: joins closer in, and speeds up as it closes.
            heartbeatTarget = Mathf.Clamp01(1f - (distance - 2f) / Mathf.Max(1f, heartbeatRadius - 2f));
            if (heartbeatSource != null)
                heartbeatSource.pitch = Mathf.Lerp(1f, 1.35f, heartbeatTarget);
        }

        droneLevel = Mathf.MoveTowards(droneLevel, droneTarget, dreadFadeSpeed * dt);
        heartbeatLevel = Mathf.MoveTowards(heartbeatLevel, heartbeatTarget, dreadFadeSpeed * dt);
    }

    static bool IsHuntState()
    {
        if (GameManager.Instance == null) return true; // isolated module test scene
        GameManager.GameState s = GameManager.Instance.State.Value;
        return s == GameManager.GameState.Lockdown
            || s == GameManager.GameState.Playing
            || s == GameManager.GameState.Finale;
    }

    // ------------------------- Local body foley -------------------------

    void UpdateLocalBody(float dt)
    {
        PlayerController local = PlayerController.Local;
        CharacterController cc = local != null && !local.IsGhost
            ? local.GetComponent<CharacterController>() : null;

        float speed = 0f;
        bool grounded = false;
        if (cc != null && cc.enabled)
        {
            Vector3 velocity = cc.velocity;
            velocity.y = 0f;
            speed = velocity.magnitude;
            grounded = cc.isGrounded;
        }

        // Footsteps: cadence and weight scale with speed. Sprint steps are the
        // loud ones the monster also "hears" through the real noise system.
        if (grounded && speed > 1f)
        {
            footstepTimer -= dt;
            if (footstepTimer <= 0f)
            {
                float sprintBlend = Mathf.InverseLerp(4.5f, 7.5f, speed);
                footstepTimer = Mathf.Lerp(0.52f, 0.33f, sprintBlend);
                AudioDirector.Instance.Play(Sfx.Footstep, local.transform.position,
                    footstepVolume * Mathf.Lerp(0.7f, 1.3f, sprintBlend));
            }
        }
        else
        {
            footstepTimer = 0.05f;
        }

        // Breathing loop while sprinting (speed sits between walk 4.5 and run 7.5).
        bool sprinting = grounded && speed > 6.2f;
        breathingLevel = Mathf.MoveTowards(breathingLevel, sprinting ? 1f : 0f, 1.5f * dt);
    }

    // ------------------------- Ghost acoustics -------------------------

    void UpdateGhostEcho(GameObject listenerHost)
    {
        bool ghost = PlayerController.Local != null && PlayerController.Local.IsGhost;

        if (ghost)
        {
            if (ghostEcho == null || echoHost != listenerHost)
            {
                // The camera can move between pawns; keep the filter on
                // whichever object currently carries the listener.
                if (ghostEcho != null) ghostEcho.enabled = false;
                echoHost = listenerHost;
                ghostEcho = listenerHost.GetComponent<AudioEchoFilter>();
                if (ghostEcho == null) ghostEcho = listenerHost.AddComponent<AudioEchoFilter>();
                ghostEcho.delay = 160f;
                ghostEcho.decayRatio = 0.3f;
                ghostEcho.wetMix = 0.55f;
                ghostEcho.dryMix = 0.9f;
            }
            ghostEcho.enabled = true;
        }
        else if (ghostEcho != null && ghostEcho.enabled)
        {
            ghostEcho.enabled = false;
        }
    }

    // ------------------------- Mixing -------------------------

    void ApplyVolumes()
    {
        AudioDirector director = AudioDirector.Instance;
        Apply(denSource, Sfx.DenAmbience, denLevel * roomToneVolume, director);
        Apply(yardSource, Sfx.YardAmbience, yardLevel * roomToneVolume, director);
        Apply(droneSource, Sfx.MonsterDrone, droneLevel * droneVolume, director);
        Apply(heartbeatSource, Sfx.Heartbeat, heartbeatLevel * heartbeatVolume, director);
        Apply(breathingSource, Sfx.Breathing, breathingLevel * breathingVolume, director);
    }

    static void Apply(AudioSource source, Sfx sfx, float level, AudioDirector director)
    {
        if (source == null) return;
        source.volume = Mathf.Clamp01(level) * director.BusGain(sfx);
    }

    AudioSource CreateLoopSource(string name, Sfx sfx)
    {
        AudioClip clip = AudioDirector.Instance != null ? AudioDirector.Instance.Clip(sfx) : null;
        if (clip == null) return null;

        var go = new GameObject(name);
        go.transform.SetParent(transform, false);

        AudioSource source = go.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.playOnAwake = false;
        source.spatialBlend = 0f; // listener-local: the soundscape follows the ear
        source.volume = 0f;
        source.Play();
        return source;
    }
}
