/// <summary>
/// Every sound effect SCREAMER ships. All 24 clips are synthesized at boot by
/// <see cref="ProceduralAudioBank"/> (GDD 13.2) and played through
/// <see cref="AudioDirector"/>'s single clip table, so real recorded assets
/// can later replace the procedural bank in exactly one place.
///
/// Bus routing (applied by AudioDirector):
/// - Voice: Scream, Breathing - the "Scream Volume" gag slider genuinely
///   maps to these (you will be heard anyway).
/// - Music: BassKick, Chime, PipeOrganSting - the diegetic musical gags.
/// - Ambience: MonsterDrone, Heartbeat, DenAmbience, YardAmbience - ducked
///   18 dB under SFX per the audio spec.
/// - SFX: everything else.
/// </summary>
public enum Sfx : byte
{
    Scream,
    ChickenSquawk,
    FeedbackScreech,
    SmokeAlarm,
    KettleWhine,
    SlideWhistle,
    BassKick,
    Chime,
    Glorp,
    GeyserBurst,
    GlassBreak,
    UiThunk,
    StampThunk,
    DoorExplosion,
    PipeOrganSting,
    MonsterDrone,
    ShuffleThump,
    Heartbeat,
    DenAmbience,
    YardAmbience,
    Footstep,
    Breathing,
    PatPat,
    BooSting
}
