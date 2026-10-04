using System.Collections.Generic;

/// <summary>
/// Computes the three post-round yearbook polaroids from the results snapshot
/// (GDD 9.6): fixed priority order, one award per player, fill from the top.
/// USELESS is always available as filler, so a full lobby always deals three
/// cards; tiny or degenerate rounds may deal fewer rather than lie.
/// </summary>
public static class SuperlativeEngine
{
    /// <summary>One polaroid: title, caption, and who is being publicly honored.</summary>
    public struct Award
    {
        public string title;
        public string caption;
        public ulong actorId;
    }

    const int MaxAwards = 3;

    delegate bool Filter(PlayerRoundResult r);
    delegate float Score(PlayerRoundResult r);

    /// <summary>
    /// GDD 9.6 priority order with dedupe:
    /// LOUDEST HUMAN, NOODLE ARSONIST, THAT WAS A J, CHICKEN'S NEMESIS,
    /// FURNITURE ABUSER, CLUTCH MOUTH, FINAL GIRL, USELESS, THE VEGETARIAN.
    /// </summary>
    public static Award[] Pick(PlayerRoundResult[] results)
    {
        var awards = new List<Award>(MaxAwards);
        if (results == null || results.Length == 0) return awards.ToArray();

        var taken = new HashSet<ulong>();

        TryAdd(awards, taken, results, GameCopy.AwardLoudestHuman, GameCopy.AwardLoudestHumanCaption,
            MaxBy(results, taken, r => !r.wasMonster && r.noiseEmitted > 0f, r => r.noiseEmitted));

        TryAdd(awards, taken, results, GameCopy.AwardNoodleArsonist, GameCopy.AwardNoodleArsonistCaption,
            MaxBy(results, taken, r => !r.wasMonster && r.noodleBurns >= 1f, r => r.noodleBurns));

        TryAdd(awards, taken, results, GameCopy.AwardThatWasAJ, GameCopy.AwardThatWasAJCaption,
            MaxBy(results, taken, r => !r.wasMonster && r.wrongNotes >= 2f, r => r.wrongNotes));

        TryAdd(awards, taken, results, GameCopy.AwardChickensNemesis, GameCopy.AwardChickensNemesisCaption,
            MaxBy(results, taken, r => !r.wasMonster && r.chickenChaseSeconds >= 20f, r => r.chickenChaseSeconds));

        TryAdd(awards, taken, results, GameCopy.AwardFurnitureAbuser, GameCopy.AwardFurnitureAbuserCaption,
            MaxBy(results, taken, r => !r.wasMonster && r.furnitureSlaps >= 3f, r => r.furnitureSlaps));

        TryAdd(awards, taken, results, GameCopy.AwardClutchMouth, GameCopy.AwardClutchMouthCaption,
            MaxBy(results, taken, r => !r.wasMonster && r.finaleScreamContribution > 0f, r => r.finaleScreamContribution));

        TryAdd(awards, taken, results, GameCopy.AwardFinalGirl, GameCopy.AwardFinalGirlCaption,
            FindFinalSurvivor(results, taken));

        // Always-available filler: the survivor who contributed the least.
        TryAdd(awards, taken, results, GameCopy.AwardUseless, GameCopy.AwardUselessCaption,
            MinBy(results, taken, r => !r.wasMonster, r => r.tasksCompleted));

        TryAdd(awards, taken, results, GameCopy.AwardVegetarian, GameCopy.AwardVegetarianCaption,
            FindVegetarianMonster(results, taken));

        return awards.ToArray();
    }

    static void TryAdd(List<Award> awards, HashSet<ulong> taken, PlayerRoundResult[] results,
        string title, string caption, int index)
    {
        if (awards.Count >= MaxAwards || index < 0) return;
        ulong id = results[index].actorId;
        awards.Add(new Award { title = title, caption = caption, actorId = id });
        taken.Add(id);
    }

    static int MaxBy(PlayerRoundResult[] results, HashSet<ulong> taken, Filter filter, Score score)
    {
        int best = -1;
        float bestScore = float.MinValue;
        for (int i = 0; i < results.Length; i++)
        {
            if (taken.Contains(results[i].actorId) || !filter(results[i])) continue;
            float s = score(results[i]);
            if (s > bestScore)
            {
                bestScore = s;
                best = i;
            }
        }
        return best;
    }

    static int MinBy(PlayerRoundResult[] results, HashSet<ulong> taken, Filter filter, Score score)
    {
        int best = -1;
        float bestScore = float.MaxValue;
        for (int i = 0; i < results.Length; i++)
        {
            if (taken.Contains(results[i].actorId) || !filter(results[i])) continue;
            float s = score(results[i]);
            if (s < bestScore)
            {
                bestScore = s;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// FINAL GIRL: the only survivor still breathing when nobody else was.
    /// Requires at least two survivors, so a duo round cannot hand it out
    /// for simply existing.
    /// </summary>
    static int FindFinalSurvivor(PlayerRoundResult[] results, HashSet<ulong> taken)
    {
        int survivors = 0, alive = 0, aliveIndex = -1;
        for (int i = 0; i < results.Length; i++)
        {
            if (results[i].wasMonster) continue;
            survivors++;
            if (results[i].deathTime < 0f)
            {
                alive++;
                aliveIndex = i;
            }
        }
        if (survivors < 2 || alive != 1) return -1;
        if (taken.Contains(results[aliveIndex].actorId)) return -1;
        return aliveIndex;
    }

    /// <summary>THE VEGETARIAN: the monster, if nobody got eaten all round.</summary>
    static int FindVegetarianMonster(PlayerRoundResult[] results, HashSet<ulong> taken)
    {
        int monsterIndex = -1;
        for (int i = 0; i < results.Length; i++)
        {
            if (!results[i].wasMonster && results[i].deathTime >= 0f) return -1; // somebody died
            if (results[i].wasMonster) monsterIndex = i;
        }
        if (monsterIndex < 0 || taken.Contains(results[monsterIndex].actorId)) return -1;
        return monsterIndex;
    }
}
