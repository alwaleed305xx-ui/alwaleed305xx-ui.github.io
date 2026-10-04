// Entire file is Steam-conditional. Without the SCREAMER_STEAM scripting
// define (and the Steamworks.NET SDK it implies), this file compiles to
// nothing and the project stays green.
#if SCREAMER_STEAM
using Steamworks;
using UnityEngine;

/// <summary>
/// Steam session backend: a friends-only Steam lobby carries discovery,
/// the room code and overlay invites, while the actual game traffic rides
/// the same UnityTransport pipe as the LAN path (the lobby stores the
/// host's address). One transport pipeline to debug, two front doors.
///
/// Lobby code = the Steam lobby id (a plain number friends can paste).
/// An "ip:port" string still joins directly, so mixed setups keep working.
/// </summary>
public class SteamBackend : INetworkBackend
{
    const int MaxLobbyMembers = 8;
    const string LobbyKeyHostAddress = "screamer_host_addr";
    const string LobbyKeyName = "name";

    readonly UtpBackend transport = new UtpBackend();

    CSteamID currentLobby = CSteamID.Nil;
    bool hosting;

    // Steamworks callback handles must stay referenced or they are collected.
    Callback<LobbyCreated_t> lobbyCreatedCallback;
    Callback<LobbyEnter_t> lobbyEnterCallback;
    Callback<GameLobbyJoinRequested_t> joinRequestedCallback;

    public SteamBackend()
    {
        lobbyCreatedCallback = Callback<LobbyCreated_t>.Create(OnLobbyCreated);
        lobbyEnterCallback = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
        joinRequestedCallback = Callback<GameLobbyJoinRequested_t>.Create(OnGameLobbyJoinRequested);
    }

    public string LobbyCode =>
        currentLobby.IsValid() ? currentLobby.m_SteamID.ToString() : transport.LobbyCode;

    public bool SupportsInvites => currentLobby.IsValid();

    public void OpenInviteOverlay()
    {
        if (currentLobby.IsValid())
            SteamFriends.ActivateGameOverlayInviteDialog(currentLobby);
    }

    public bool Host()
    {
        if (!SteamIntegration.IsAvailable) return transport.Host();
        if (!transport.Host()) return false;

        hosting = true;
        SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, MaxLobbyMembers);
        return true; // lobby id arrives asynchronously in OnLobbyCreated
    }

    public bool Join(string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        address = address.Trim();

        // A pure number is a Steam lobby id; anything else is a direct address.
        if (SteamIntegration.IsAvailable && ulong.TryParse(address, out ulong lobbyId))
        {
            hosting = false;
            SteamMatchmaking.JoinLobby(new CSteamID(lobbyId));
            return true; // connection continues in OnLobbyEntered
        }
        return transport.Join(address);
    }

    public void Shutdown()
    {
        LeaveLobby();
        hosting = false;
        transport.Shutdown();
    }

    // ------------------------- Steam callbacks -------------------------

    void OnLobbyCreated(LobbyCreated_t data)
    {
        if (!hosting) return;
        if (data.m_eResult != EResult.k_EResultOK)
        {
            Debug.LogWarning("SteamBackend: lobby creation failed (" + data.m_eResult + "). Direct ip:port still works.");
            return;
        }

        currentLobby = new CSteamID(data.m_ulSteamIDLobby);
        SteamMatchmaking.SetLobbyData(currentLobby, LobbyKeyHostAddress, transport.LobbyCode);
        SteamMatchmaking.SetLobbyData(currentLobby, LobbyKeyName, SteamFriends.GetPersonaName() + "'s haunted house");
    }

    void OnLobbyEntered(LobbyEnter_t data)
    {
        if (hosting) return; // the host created this lobby; nothing to join

        if (data.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
        {
            Debug.LogWarning("SteamBackend: could not enter lobby (response " + data.m_EChatRoomEnterResponse + ").");
            return;
        }

        currentLobby = new CSteamID(data.m_ulSteamIDLobby);
        string hostAddress = SteamMatchmaking.GetLobbyData(currentLobby, LobbyKeyHostAddress);
        if (string.IsNullOrEmpty(hostAddress))
        {
            Debug.LogWarning("SteamBackend: lobby has no host address yet; leaving.");
            LeaveLobby();
            return;
        }

        if (!transport.Join(hostAddress))
            LeaveLobby();
    }

    void OnGameLobbyJoinRequested(GameLobbyJoinRequested_t data)
    {
        // The player accepted a friend invite from the Steam overlay.
        Shutdown();
        hosting = false;
        SteamMatchmaking.JoinLobby(data.m_steamIDLobby);
    }

    // ------------------------- Internals -------------------------

    void LeaveLobby()
    {
        if (!currentLobby.IsValid()) return;
        SteamMatchmaking.LeaveLobby(currentLobby);
        currentLobby = CSteamID.Nil;
    }
}
#endif
