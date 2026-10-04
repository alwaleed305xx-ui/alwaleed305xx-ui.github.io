using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// One actor's complete round report card, broadcast from the server to every
/// client once when the Results state begins. The results screen, the
/// superlative engine, and the Steam layer all read from this snapshot -
/// nobody queries the server again after the broadcast.
/// </summary>
[System.Serializable]
public struct PlayerRoundResult : INetworkSerializable
{
    public ulong actorId;
    public FixedString64Bytes name;
    public bool isBot;
    public bool wasMonster;
    public bool escaped;

    /// <summary>Round time in seconds when this actor was caught. Negative = survived.</summary>
    public float deathTime;

    public float noiseEmitted;
    public float noodleBurns;
    public float wrongNotes;
    public float chickenChaseSeconds;
    public float furnitureSlaps;
    public float boosUsed;
    public float finaleScreamContribution;
    public float tasksCompleted;
    public float killLoudness;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref actorId);
        serializer.SerializeValue(ref name);
        serializer.SerializeValue(ref isBot);
        serializer.SerializeValue(ref wasMonster);
        serializer.SerializeValue(ref escaped);
        serializer.SerializeValue(ref deathTime);
        serializer.SerializeValue(ref noiseEmitted);
        serializer.SerializeValue(ref noodleBurns);
        serializer.SerializeValue(ref wrongNotes);
        serializer.SerializeValue(ref chickenChaseSeconds);
        serializer.SerializeValue(ref furnitureSlaps);
        serializer.SerializeValue(ref boosUsed);
        serializer.SerializeValue(ref finaleScreamContribution);
        serializer.SerializeValue(ref tasksCompleted);
        serializer.SerializeValue(ref killLoudness);
    }
}
