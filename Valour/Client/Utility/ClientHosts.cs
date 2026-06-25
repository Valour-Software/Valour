namespace Valour.Client.Utility;

/// <summary>
/// Public Valour origins the client builds external links against. Defaults to
/// production; the web client overrides these from runtime config at startup.
/// </summary>
public static class ClientHosts
{
    public static string AppBaseUrl { get; set; } = "https://app.valour.gg";
    public static string ThreadsBaseUrl { get; set; } = "https://threads.valour.gg";
}
