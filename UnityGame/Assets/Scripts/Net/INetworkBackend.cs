/// <summary>
/// The seam between the game and whatever carries its packets. Exactly two
/// implementations exist: <c>UtpBackend</c> (UnityTransport over IP - always
/// compiled, the first-class CI and LAN path) and <c>SteamBackend</c>
/// (Steam lobbies and invites, entirely behind #if SCREAMER_STEAM).
/// UI code talks only to <c>BackendSelector.Active</c>; nothing above this
/// interface may know which backend is running.
/// </summary>
public interface INetworkBackend
{
    /// <summary>Starts hosting a session. Returns false if the session could not start.</summary>
    bool Host();

    /// <summary>
    /// Joins a session. The address is whatever <see cref="LobbyCode"/>
    /// produced on the host's machine: "ip:port" for UTP, a Steam lobby id
    /// for Steam.
    /// </summary>
    bool Join(string address);

    /// <summary>Leaves/stops the session and releases backend resources.</summary>
    void Shutdown();

    /// <summary>Human-shareable session code: Steam lobby id, or "ip:port".</summary>
    string LobbyCode { get; }

    /// <summary>True when the backend can summon a native invite UI.</summary>
    bool SupportsInvites { get; }

    /// <summary>Opens the platform invite overlay. No-op on UTP.</summary>
    void OpenInviteOverlay();

    // Player-list changes are read from GameManager.Roster (the synchronized
    // source of truth); the seam deliberately carries no membership event.
}
