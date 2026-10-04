using Unity.Collections;
using Unity.Netcode;

/// <summary>
/// One row of the lobby roster, synchronized to every client through
/// <c>GameManager.Roster</c> (a NetworkList). Humans and bots share the
/// same row shape; the lobby UI renders color chip, name, ping and the
/// READY check straight from this data.
/// During the Results state the <see cref="ready"/> flag doubles as this
/// actor's rematch vote, so the "x/8 want a sequel" counter can be read
/// from the roster alone.
/// </summary>
[System.Serializable]
public struct RosterEntry : INetworkSerializable, System.IEquatable<RosterEntry>
{
    /// <summary>NGO client id for humans; GameManager.BotIdBase + n for bots.</summary>
    public ulong actorId;

    /// <summary>Display name: Steam persona, "Victim N", or "[BOT] Chad".</summary>
    public FixedString64Bytes name;

    /// <summary>Ready in the Lobby; rematch vote in Results.</summary>
    public bool ready;

    public bool isBot;

    /// <summary>Index into ScreamerPalette.SurvivorColors, assigned in join order.</summary>
    public int colorIndex;

    /// <summary>Round-trip time in milliseconds. 0 for the host and for bots.</summary>
    public float pingMs;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref actorId);
        serializer.SerializeValue(ref name);
        serializer.SerializeValue(ref ready);
        serializer.SerializeValue(ref isBot);
        serializer.SerializeValue(ref colorIndex);
        serializer.SerializeValue(ref pingMs);
    }

    public bool Equals(RosterEntry other)
    {
        return actorId == other.actorId
            && name.Equals(other.name)
            && ready == other.ready
            && isBot == other.isBot
            && colorIndex == other.colorIndex
            && pingMs.Equals(other.pingMs);
    }

    public override int GetHashCode() => actorId.GetHashCode();
}
