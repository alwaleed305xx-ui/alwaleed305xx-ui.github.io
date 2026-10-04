using System.Collections.Generic;
using Unity.Collections;

/// <summary>
/// Server-side scoreboard for one round. Plain C# - it never travels over
/// the network itself; <see cref="BuildResults"/> bakes it into
/// <see cref="PlayerRoundResult"/> structs that the GameManager broadcasts
/// once when Results begins.
///
/// Gameplay systems only ever call <see cref="Add"/> (through
/// <c>GameManager.ServerAddStat</c> / <c>ReportStatServerRpc</c>). The
/// GameManager itself stamps identity and outcome just before building
/// the final report.
/// </summary>
public class RoundStats
{
    static readonly int KindCount = System.Enum.GetValues(typeof(StatKind)).Length;

    class ActorRecord
    {
        public readonly float[] values = new float[KindCount];
        public string name = "";
        public bool isBot;
        public bool wasMonster;
        public bool escaped;
        public bool died;

        /// <summary>
        /// Only actors stamped via SetIdentity appear in BuildResults.
        /// Stats may exist for actors who left mid-round; they are kept for
        /// bookkeeping but excluded from the report.
        /// </summary>
        public bool included;
    }

    readonly Dictionary<ulong, ActorRecord> records = new Dictionary<ulong, ActorRecord>();

    ActorRecord Record(ulong actorId)
    {
        if (!records.TryGetValue(actorId, out ActorRecord rec))
        {
            rec = new ActorRecord();
            records[actorId] = rec;
        }
        return rec;
    }

    /// <summary>Accumulates <paramref name="amount"/> onto an actor's tally.</summary>
    public void Add(ulong actorId, StatKind kind, float amount = 1f)
    {
        Record(actorId).values[(int)kind] += amount;
    }

    /// <summary>Current tally for one actor and kind. 0 if never recorded.</summary>
    public float Get(ulong actorId, StatKind kind)
    {
        return records.TryGetValue(actorId, out ActorRecord rec) ? rec.values[(int)kind] : 0f;
    }

    /// <summary>
    /// Stamps who an actor is. Called by the GameManager for every round
    /// participant before <see cref="BuildResults"/>.
    /// </summary>
    public void SetIdentity(ulong actorId, string name, bool isBot)
    {
        ActorRecord rec = Record(actorId);
        rec.name = name ?? "";
        rec.isBot = isBot;
        rec.included = true;
    }

    /// <summary>
    /// Stamps how an actor's round ended. A negative <paramref name="deathTime"/>
    /// means they were never caught.
    /// </summary>
    public void SetOutcome(ulong actorId, bool wasMonster, bool escaped, float deathTime)
    {
        ActorRecord rec = Record(actorId);
        rec.wasMonster = wasMonster;
        rec.escaped = escaped;
        rec.died = deathTime >= 0f;
        if (rec.died)
            rec.values[(int)StatKind.DeathTime] = deathTime;
    }

    /// <summary>
    /// Bakes every stamped actor into the wire-format result array,
    /// ordered by actor id (humans first, bots after - bot ids start high).
    /// </summary>
    public PlayerRoundResult[] BuildResults()
    {
        var ids = new List<ulong>();
        foreach (KeyValuePair<ulong, ActorRecord> pair in records)
            if (pair.Value.included)
                ids.Add(pair.Key);
        ids.Sort();

        var results = new PlayerRoundResult[ids.Count];
        for (int i = 0; i < ids.Count; i++)
        {
            ActorRecord rec = records[ids[i]];
            results[i] = new PlayerRoundResult
            {
                actorId = ids[i],
                name = new FixedString64Bytes(Truncate(rec.name, 60)),
                isBot = rec.isBot,
                wasMonster = rec.wasMonster,
                escaped = rec.escaped,
                deathTime = rec.died ? rec.values[(int)StatKind.DeathTime] : -1f,
                noiseEmitted = rec.values[(int)StatKind.NoiseEmitted],
                noodleBurns = rec.values[(int)StatKind.NoodleBurns],
                wrongNotes = rec.values[(int)StatKind.WrongNotes],
                chickenChaseSeconds = rec.values[(int)StatKind.ChickenChaseSeconds],
                furnitureSlaps = rec.values[(int)StatKind.FurnitureSlaps],
                boosUsed = rec.values[(int)StatKind.BoosUsed],
                finaleScreamContribution = rec.values[(int)StatKind.FinaleScreamContribution],
                tasksCompleted = rec.values[(int)StatKind.TasksCompleted],
                killLoudness = rec.values[(int)StatKind.KillLoudness]
            };
        }
        return results;
    }

    /// <summary>Wipes everything. Part of the rematch reset path.</summary>
    public void Reset()
    {
        records.Clear();
    }

    static string Truncate(string s, int maxChars)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Length > maxChars) s = s.Substring(0, maxChars);
        // Keep the UTF-8 form inside a FixedString64Bytes payload (61 bytes).
        while (s.Length > 0 && System.Text.Encoding.UTF8.GetByteCount(s) > 60)
            s = s.Substring(0, s.Length - 1);
        return s;
    }
}
