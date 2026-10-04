using UnityEngine;

/// <summary>
/// One tunable per bot archetype (GDD 10): a doomed film crew, each member
/// wrong in exactly one way. The class carries all four dials, but every
/// shipped archetype moves only its own:
///
///   The Rusher    "[BOT] Chad"     panicThreshold 0.9  - barely flees, sprints everywhere
///   The Coward    "[BOT] Brenda"   panicThreshold 0.2  - flees distant pings, hides in the bathroom
///   The Liability "[BOT] Dale"     chaosRoll x3        - triple fail rates on noodles/karaoke/toilet
///   The Shadow    "[BOT] Tiffany"  followBias 0.8      - shadows the nearest human, uselessly
///
/// Plain serializable data, never a ScriptableObject asset: BotManager hands
/// an instance to the brain that drives each pawn.
/// </summary>
[System.Serializable]
public class BotPersonality
{
    [Tooltip("Roster/name-tag name, e.g. \"[BOT] Chad\".")]
    public string displayName = "[BOT] Extra";

    [Tooltip("Ping loudness needed to spook this bot. High = fearless (and reckless), low = jumpy.")]
    [Range(0f, 1f)] public float panicThreshold = 0.8f;

    [Tooltip("Multiplier on the honest per-task failure rolls (TaskBase.ServerBotWork).")]
    [Min(0f)] public float chaosRoll = 1f;

    [Tooltip("Chance per task pick to shadow the nearest human player instead of doing chores.")]
    [Range(0f, 1f)] public float followBias = 0f;

    // ------------------------- Derived behavior -------------------------
    // One parameter per archetype stays honest: the secondary quirks fall out
    // of the same dial instead of hiding extra knobs.

    /// <summary>Fearless bots never learned to walk. Constant sprint = constant noise (Chad).</summary>
    public bool SprintsEverywhere => panicThreshold >= 0.85f;

    /// <summary>Jumpy bots bolt for the bathroom and hide there for a while (Brenda).</summary>
    public bool HidesInBathroom => panicThreshold <= 0.3f;

    /// <summary>Seconds a bathroom-hider spends behind the shower curtain (GDD 10).</summary>
    public const float BathroomHideSeconds = 8f;

    /// <summary>
    /// How far away a scary ping still registers. The baseline rule is a
    /// loudness &gt;= 0.8 ping landing within 10 units; jumpier bots hear
    /// danger from farther, fearless ones only when it is practically on them.
    /// </summary>
    public float PanicPingRadius =>
        Mathf.Clamp(10f * (0.8f / Mathf.Max(0.05f, panicThreshold)), 4f, 40f);

    // ------------------------- The archetypes -------------------------

    public static BotPersonality Chad() => new BotPersonality
    {
        displayName = "[BOT] Chad",
        panicThreshold = 0.9f,
        chaosRoll = 1f,
        followBias = 0f
    };

    public static BotPersonality Brenda() => new BotPersonality
    {
        displayName = "[BOT] Brenda",
        panicThreshold = 0.2f,
        chaosRoll = 1f,
        followBias = 0f
    };

    public static BotPersonality Dale() => new BotPersonality
    {
        displayName = "[BOT] Dale",
        panicThreshold = 0.8f,
        chaosRoll = 3f,
        followBias = 0f
    };

    public static BotPersonality Tiffany() => new BotPersonality
    {
        displayName = "[BOT] Tiffany",
        panicThreshold = 0.8f,
        chaosRoll = 1f,
        followBias = 0.8f
    };

    /// <summary>
    /// Archetype for the Nth bot added this session. The four leads cycle;
    /// a second lap books their lookalike cousins ("[BOT] Chad II") so a full
    /// seven-bot lobby still reads as distinct people.
    /// </summary>
    public static BotPersonality Archetype(int serial)
    {
        if (serial < 0) serial = 0;

        BotPersonality p;
        switch (serial % 4)
        {
            case 0: p = Chad(); break;
            case 1: p = Brenda(); break;
            case 2: p = Dale(); break;
            default: p = Tiffany(); break;
        }

        int lap = serial / 4;
        if (lap > 0) p.displayName += " " + Roman(lap + 1);
        return p;
    }

    static string Roman(int value)
    {
        switch (value)
        {
            case 2: return "II";
            case 3: return "III";
            case 4: return "IV";
            case 5: return "V";
            default: return value.ToString();
        }
    }
}
