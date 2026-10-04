using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The one audio authority (GDD 13.2): owns the single clip table (synthesized
/// at boot by <see cref="ProceduralAudioBank"/>), a pooled set of one-shot
/// voices for 3D and UI sounds, and loop handles for station/ambience sources.
///
/// Buses: Master / SFX / Music / Ambience / Voice, read live from
/// <see cref="ScreamerSettings"/> (the "Scream Volume" slider genuinely drives
/// the Voice bus). The Ambience bus is additionally ducked 18 dB under SFX per
/// the spec. Every clip plays with a +/-10% random pitch so the procedural
/// bank never sounds like a sample pad.
///
/// Installed once by <see cref="AudioFactory.Install"/>; everything it needs
/// is built in Awake, so nothing non-serializable touches the scene file.
/// </summary>
public class AudioDirector : MonoBehaviour
{
    public static AudioDirector Instance { get; private set; }

    [Header("3D voice settings")]
    [Tooltip("World one-shots are full volume inside this distance.")]
    public float minDistance = 2f;
    [Tooltip("World one-shots fall silent past this distance (hearing radius tops out at 60).")]
    public float maxDistance = 45f;

    [Header("Pool")]
    [Tooltip("One-shot voices created up front; grows on demand up to twice this.")]
    public int poolSize = 12;

    /// <summary>-18 dB: how far the ambience bus ducks under SFX (GDD 13.2).</summary>
    const float AmbienceDuck = 0.1259f;

    /// <summary>+/-10% random pitch per play (GDD 13.2).</summary>
    const float PitchJitter = 0.1f;

    // One active voice per playing one-shot, so buses and the photo-finish
    // pitch drop can be re-applied to sounds already in flight.
    class ActiveVoice
    {
        public AudioSource source;
        public Sfx sfx;
        public float baseVolume;
        public float basePitch;
        public bool worldSpace;
    }

    Dictionary<Sfx, AudioClip> bank;
    readonly List<AudioSource> pool = new List<AudioSource>();
    readonly List<ActiveVoice> activeOneShots = new List<ActiveVoice>();
    readonly List<ActiveVoice> activeLoops = new List<ActiveVoice>();
    Transform poolRoot;
    float worldPitchScale = 1f;

    // ------------------------- Lifecycle -------------------------

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        bank = ProceduralAudioBank.Build();

        poolRoot = new GameObject("OneShotVoices").transform;
        poolRoot.SetParent(transform, false);
        for (int i = 0; i < poolSize; i++)
            pool.Add(CreateVoice("Voice_" + (i + 1)));
    }

    void OnEnable()
    {
        ScreamerSettings.OnChanged += ApplyBusVolumes;
    }

    void OnDisable()
    {
        ScreamerSettings.OnChanged -= ApplyBusVolumes;
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void Update()
    {
        // Retire finished one-shots so their sources return to the pool.
        for (int i = activeOneShots.Count - 1; i >= 0; i--)
        {
            ActiveVoice voice = activeOneShots[i];
            if (voice.source == null || !voice.source.isPlaying)
                activeOneShots.RemoveAt(i);
        }
    }

    // ------------------------- Public API (inter-module contract) -------------------------

    /// <summary>Plays a 3D one-shot at a world position. Pitch multiplies the +/-10% jitter.</summary>
    public void Play(Sfx sfx, Vector3 worldPos, float volume = 1f, float pitch = 1f)
    {
        AudioClip clip = Clip(sfx);
        if (clip == null) return;

        AudioSource source = TakeVoice();
        if (source == null) return;

        source.transform.position = worldPos;
        source.spatialBlend = 1f;
        StartVoice(source, sfx, clip, volume, pitch, worldSpace: true, loop: false);
    }

    /// <summary>Plays a flat 2D one-shot for menus and HUD chrome.</summary>
    public void PlayUI(Sfx sfx, float volume = 1f)
    {
        AudioClip clip = Clip(sfx);
        if (clip == null) return;

        AudioSource source = TakeVoice();
        if (source == null) return;

        source.spatialBlend = 0f;
        StartVoice(source, sfx, clip, volume, 1f, worldSpace: false, loop: false);
    }

    /// <summary>
    /// Starts a looping 3D source at a world position and returns the handle.
    /// The director keeps applying bus volumes to it until <see cref="StopLoop"/>.
    /// </summary>
    public AudioSource StartLoop(Sfx sfx, Vector3 worldPos, float volume = 1f)
    {
        AudioClip clip = Clip(sfx);
        if (clip == null) return null;

        var go = new GameObject("Loop_" + sfx);
        go.transform.SetParent(transform, false);
        go.transform.position = worldPos;

        AudioSource source = go.AddComponent<AudioSource>();
        ConfigureSource(source);
        source.spatialBlend = 1f;
        StartVoice(source, sfx, clip, volume, 1f, worldSpace: true, loop: true);
        return source;
    }

    /// <summary>Stops and disposes a loop started with <see cref="StartLoop"/>.</summary>
    public void StopLoop(AudioSource handle)
    {
        if (handle == null) return;

        for (int i = activeLoops.Count - 1; i >= 0; i--)
        {
            if (activeLoops[i].source != handle) continue;
            activeLoops.RemoveAt(i);
            handle.Stop();
            Destroy(handle.gameObject);
            return;
        }
        handle.Stop(); // not one of ours; stop it but never destroy foreign objects
    }

    /// <summary>The raw clip, for AudioSources owned by other modules (task station loops).</summary>
    public AudioClip Clip(Sfx sfx)
    {
        if (bank != null && bank.TryGetValue(sfx, out AudioClip clip)) return clip;
        return null;
    }

    // ------------------------- Module extras (not part of the shared contract) -------------------------

    /// <summary>
    /// Final gain multiplier for a sound of this kind right now:
    /// master x bus (x the -18 dB ambience duck). AmbienceController multiplies
    /// its own source volumes by this every frame so sliders apply live.
    /// </summary>
    public float BusGain(Sfx sfx)
    {
        ScreamerSettings settings = ScreamerSettings.Instance;
        float master = settings != null ? settings.MasterVolume : 1f;

        float bus;
        switch (sfx)
        {
            case Sfx.Scream:
            case Sfx.Breathing:
                bus = settings != null ? settings.VoiceVolume : 1f;
                break;
            case Sfx.BassKick:
            case Sfx.Chime:
            case Sfx.PipeOrganSting:
                bus = settings != null ? settings.MusicVolume : 1f;
                break;
            case Sfx.MonsterDrone:
            case Sfx.Heartbeat:
            case Sfx.DenAmbience:
            case Sfx.YardAmbience:
                bus = (settings != null ? settings.AmbienceVolume : 1f) * AmbienceDuck;
                break;
            default:
                bus = settings != null ? settings.SfxVolume : 1f;
                break;
        }
        return master * bus;
    }

    /// <summary>
    /// Scales the pitch of every world-space sound (photo-finish audio drop).
    /// UI sounds stay crisp. 1 restores normal speed.
    /// </summary>
    public void SetWorldPitchScale(float scale)
    {
        worldPitchScale = Mathf.Clamp(scale, 0.25f, 2f);
        ApplyBusVolumes();
    }

    // ------------------------- Internals -------------------------

    void StartVoice(AudioSource source, Sfx sfx, AudioClip clip, float volume, float pitch, bool worldSpace, bool loop)
    {
        var voice = new ActiveVoice
        {
            source = source,
            sfx = sfx,
            baseVolume = Mathf.Clamp01(volume),
            basePitch = Mathf.Max(0.05f, pitch) * Random.Range(1f - PitchJitter, 1f + PitchJitter),
            worldSpace = worldSpace
        };

        source.clip = clip;
        source.loop = loop;
        source.volume = voice.baseVolume * BusGain(sfx);
        source.pitch = voice.basePitch * (worldSpace ? worldPitchScale : 1f);
        source.Play();

        (loop ? activeLoops : activeOneShots).Add(voice);
    }

    void ApplyBusVolumes()
    {
        ApplyTo(activeOneShots);
        ApplyTo(activeLoops);
    }

    void ApplyTo(List<ActiveVoice> voices)
    {
        for (int i = voices.Count - 1; i >= 0; i--)
        {
            ActiveVoice voice = voices[i];
            if (voice.source == null)
            {
                voices.RemoveAt(i);
                continue;
            }
            voice.source.volume = voice.baseVolume * BusGain(voice.sfx);
            voice.source.pitch = voice.basePitch * (voice.worldSpace ? worldPitchScale : 1f);
        }
    }

    AudioSource TakeVoice()
    {
        // A free pooled source first.
        for (int i = 0; i < pool.Count; i++)
            if (pool[i] != null && !pool[i].isPlaying)
                return pool[i];

        // Grow up to a hard cap, then steal the pool's first voice - a lost
        // footstep beats an unbounded source pile.
        if (pool.Count < poolSize * 2)
        {
            AudioSource grown = CreateVoice("Voice_" + (pool.Count + 1));
            pool.Add(grown);
            return grown;
        }
        AudioSource stolen = pool[0];
        if (stolen != null) stolen.Stop();
        return stolen;
    }

    AudioSource CreateVoice(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(poolRoot, false);
        AudioSource source = go.AddComponent<AudioSource>();
        ConfigureSource(source);
        return source;
    }

    void ConfigureSource(AudioSource source)
    {
        source.playOnAwake = false;
        source.rolloffMode = AudioRolloffMode.Linear; // predictable audibility edge
        source.minDistance = minDistance;
        source.maxDistance = maxDistance;
        source.dopplerLevel = 0f;
        source.spread = 40f; // soften hard panning when sounds pass the ear
    }
}
