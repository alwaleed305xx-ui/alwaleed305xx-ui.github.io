using UnityEngine;

/// <summary>
/// Anything the monster can catch: human survivor pawns and server-driven bot pawns.
/// The monster's attack raycast resolves this interface, so new victim types plug in
/// without the monster ever knowing their concrete class.
/// </summary>
public interface IVictim
{
    /// <summary>NGO client id for humans; synthetic id (>= GameManager.BotIdBase) for bots.</summary>
    ulong ActorId { get; }

    /// <summary>True while the victim is alive, in play, and allowed to be caught.</summary>
    bool IsCatchable { get; }

    /// <summary>World transform used for range checks, line of sight, and killcam sampling.</summary>
    Transform VictimTransform { get; }

    /// <summary>
    /// Routes the catch to the server. Safe to call from the monster owner's client:
    /// implementations forward through a RequireOwnership = false ServerRpc.
    /// </summary>
    void RequestCaught();
}
