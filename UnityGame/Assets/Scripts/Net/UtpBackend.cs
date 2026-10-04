using System.Net;
using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

/// <summary>
/// The always-compiled IP backend: UnityTransport, direct host/join by
/// address. This is the LAN fallback, the CI path, and the reason the
/// project runs green with the Steam SDK absent. "TRACKING: MANUAL."
/// </summary>
public class UtpBackend : INetworkBackend
{
    string lobbyCode = "";
    bool callbacksHooked;

    public string LobbyCode => lobbyCode;

    public bool SupportsInvites => false;

    /// <summary>
    /// When true, the NEXT Host() binds to loopback only: the session is
    /// invisible to the LAN and the room code reads 127.0.0.1. Used by
    /// PRACTICE WITH BOTS (GDD 7.1, "offline host"). One-shot - it clears
    /// itself after Host() so a later HOST GAME listens normally again.
    /// </summary>
    public bool HostLocalOnly { get; set; }

    public void OpenInviteOverlay()
    {
        // No overlay to open - players share the ip:port code by hand.
    }

    public bool Host()
    {
        bool localOnly = HostLocalOnly;
        HostLocalOnly = false;

        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.IsListening) return false;

        UnityTransport transport = FindTransport(nm);
        ushort port = transport != null ? transport.ConnectionData.Port : RoundFlowFactory.DefaultPort;
        // Normal hosting listens on all interfaces; practice stays on loopback.
        transport?.SetConnectionData("127.0.0.1", port, localOnly ? "127.0.0.1" : "0.0.0.0");

        if (!nm.StartHost()) return false;

        lobbyCode = (localOnly ? "127.0.0.1" : LocalIPv4()) + ":" + port;
        HookCallbacks(nm);
        return true;
    }

    public bool Join(string address)
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || nm.IsListening) return false;

        if (!TryParseAddress(address, out string ip, out ushort port)) return false;

        UnityTransport transport = FindTransport(nm);
        transport?.SetConnectionData(ip, port);

        if (!nm.StartClient()) return false;

        lobbyCode = ip + ":" + port;
        HookCallbacks(nm);
        return true;
    }

    public void Shutdown()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null) return;
        UnhookCallbacks(nm);
        if (nm.IsListening || nm.IsClient) nm.Shutdown();
        lobbyCode = "";
    }

    // ------------------------- Internals -------------------------

    void HookCallbacks(NetworkManager nm)
    {
        if (callbacksHooked) return;
        nm.OnClientDisconnectCallback += HandleClientDisconnected;
        callbacksHooked = true;
    }

    void UnhookCallbacks(NetworkManager nm)
    {
        if (!callbacksHooked) return;
        nm.OnClientDisconnectCallback -= HandleClientDisconnected;
        callbacksHooked = false;
    }

    void HandleClientDisconnected(ulong clientId)
    {
        // Host-quit teardown: no host migration in v1. When this client loses
        // its own connection (the host unplugged the house, or we were
        // kicked), shut the session down so the UI can fall back to the menu.
        NetworkManager nm = NetworkManager.Singleton;
        if (nm != null && !nm.IsServer && clientId == nm.LocalClientId)
        {
            UnhookCallbacks(nm);
            if (nm.IsListening || nm.IsClient) nm.Shutdown();
            lobbyCode = "";
        }
    }

    static UnityTransport FindTransport(NetworkManager nm)
    {
        if (nm.NetworkConfig != null && nm.NetworkConfig.NetworkTransport is UnityTransport fromConfig)
            return fromConfig;
        return nm.GetComponent<UnityTransport>();
    }

    /// <summary>Parses "ip", "ip:port" or "hostname:port". Empty = localhost.</summary>
    static bool TryParseAddress(string address, out string ip, out ushort port)
    {
        ip = "127.0.0.1";
        port = RoundFlowFactory.DefaultPort;
        if (string.IsNullOrWhiteSpace(address)) return true;

        address = address.Trim();
        int colon = address.LastIndexOf(':');
        if (colon < 0)
        {
            ip = address;
            return true;
        }

        string host = address.Substring(0, colon);
        string portText = address.Substring(colon + 1);
        if (string.IsNullOrEmpty(host)) return false;
        if (!ushort.TryParse(portText, out ushort parsedPort)) return false;

        ip = host;
        port = parsedPort;
        return true;
    }

    /// <summary>
    /// Best-effort LAN address for the room code. Opens a UDP socket toward a
    /// public IP (no traffic is sent) just to learn which interface routes out.
    /// </summary>
    static string LocalIPv4()
    {
        try
        {
            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                if (socket.LocalEndPoint is IPEndPoint endPoint)
                    return endPoint.Address.ToString();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("UtpBackend: could not determine LAN address, falling back to loopback. " + e.Message);
        }
        return "127.0.0.1";
    }
}
