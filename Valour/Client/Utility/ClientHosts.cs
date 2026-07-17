using Valour.Shared.Hosting;

namespace Valour.Client.Utility;

/// <summary>
/// Public Valour origins the client builds external links against. Defaults
/// follow the shared ValourHosts (set from the instance manifest at startup);
/// an explicit set (e.g. from host-page runtime config) takes precedence.
/// </summary>
public static class ClientHosts
{
    private static string _appBaseUrl;
    private static string _threadsBaseUrl;

    public static string AppBaseUrl
    {
        get => _appBaseUrl ?? ValourHosts.AppBaseUrl;
        set => _appBaseUrl = value;
    }

    public static string ThreadsBaseUrl
    {
        get => _threadsBaseUrl ?? ValourHosts.ThreadsBaseUrl;
        set => _threadsBaseUrl = value;
    }
}
