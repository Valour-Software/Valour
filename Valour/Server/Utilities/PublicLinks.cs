using System.Net;
using Valour.Config.Configs;
using Valour.Shared.Hosting;

namespace Valour.Server.Utilities;

/// <summary>
/// Builds absolute links that leave the server, such as the ones in emails.
/// They come from the configured hosts (the Hosting settings), never from the
/// request's Host header, which the client controls. A forged Host would
/// otherwise send a password reset or verification code to the attacker's site.
/// </summary>
public static class PublicLinks
{
    /// <summary>
    /// Base URL of the web app, without a trailing slash.
    /// </summary>
    public static string GetAppBaseUrl(HttpRequest request = null) =>
        Resolve(HostingConfig.Current?.AppBaseUrl ?? ValourHosts.AppBaseUrl, request);

    /// <summary>
    /// Base URL of the backend API, without a trailing slash.
    /// </summary>
    public static string GetApiBaseUrl(HttpRequest request = null) =>
        Resolve(HostingConfig.Current?.ApiBaseUrl ?? $"https://api.{ValourHosts.RootDomain}", request);

    private static string Resolve(string configured, HttpRequest request)
    {
#if DEBUG
        // A local development server usually runs without Hosting settings.
        // Loopback requests can only come from this machine, so their host is
        // safe to reuse there. Release builds always use the configuration.
        if (request is not null && IsLoopbackRequest(request))
            return $"{request.Scheme}://{request.Host.ToUriComponent()}";
#endif

        return configured.TrimEnd('/');
    }

#if DEBUG
    private static bool IsLoopbackRequest(HttpRequest request)
    {
        var remote = request.HttpContext.Connection.RemoteIpAddress;
        if (remote is null || !IPAddress.IsLoopback(remote))
            return false;

        var host = request.Host.Host;
        return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
               (IPAddress.TryParse(host, out var hostAddress) && IPAddress.IsLoopback(hostAddress));
    }
#endif
}
