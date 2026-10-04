/// <summary>
/// Picks the live network backend exactly once:
/// SteamBackend when the SCREAMER_STEAM define is set AND the Steam API
/// initializes (Steam running, app id known); UtpBackend in every other
/// case. Nothing above this class may branch on the platform.
/// </summary>
public static class BackendSelector
{
    static INetworkBackend active;

    public static INetworkBackend Active
    {
        get
        {
            if (active == null)
                active = Create();
            return active;
        }
    }

    static INetworkBackend Create()
    {
#if SCREAMER_STEAM
        if (SteamIntegration.EnsureInitialized())
            return new SteamBackend();
#endif
        return new UtpBackend();
    }
}
