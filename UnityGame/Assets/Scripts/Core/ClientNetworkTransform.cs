using Unity.Netcode.Components;

/// <summary>
/// Owner-authoritative NetworkTransform: each player moves their own pawn
/// locally and everyone else sees the result. The stock NetworkTransform is
/// server-authoritative, which would add a full round trip to every step.
///
/// SECURITY: owner authority means a modified client can teleport itself.
/// Acceptable for the friends/invite-first scope (GDD 8.2); flagged for the
/// public-matchmaking pass. Bot pawns use the server-authoritative default
/// instead - never this component.
/// </summary>
public class ClientNetworkTransform : NetworkTransform
{
    protected override bool OnIsServerAuthoritative() => false;
}
