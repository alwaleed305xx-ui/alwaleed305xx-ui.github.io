/// <summary>
/// Everything the server tallies per actor during a round. These feed the
/// results screen, the superlative awards, and the killcam star selection.
/// Most kinds accumulate via <see cref="RoundStats.Add"/>; DeathTime and
/// KillLoudness are written once, when the actor is caught.
/// </summary>
public enum StatKind : byte
{
    /// <summary>Sum of loudness across every noise event the actor caused.</summary>
    NoiseEmitted,

    /// <summary>Instant-noodle pots burned (missed stir windows).</summary>
    NoodleBurns,

    /// <summary>Karaoke keys that were definitely not the note.</summary>
    WrongNotes,

    /// <summary>Seconds spent chasing the chicken around the yard.</summary>
    ChickenChaseSeconds,

    /// <summary>Innocent furniture slapped. The ottoman did nothing wrong.</summary>
    FurnitureSlaps,

    /// <summary>Ghost Boo charges spent from beyond the grave.</summary>
    BoosUsed,

    /// <summary>Share of the scream-powered finale meter this actor filled.</summary>
    FinaleScreamContribution,

    /// <summary>Tasks this actor completed.</summary>
    TasksCompleted,

    /// <summary>Round time (seconds) at which the actor was caught. Unset if they survived.</summary>
    DeathTime,

    /// <summary>Loudness the victim emitted in the 5 seconds before dying. Picks the killcam star.</summary>
    KillLoudness
}
