using Microsoft.AspNetCore.Components;
using Valour.Sdk.Models;
using Valour.Shared.Models.Threads;

namespace Valour.Client.Utility;

public static class ShareUtils
{
    /// <summary>
    /// Base URI usable in links shared outside the app. Inside the MAUI shell the
    /// navigation base is a local loopback host, so fall back to the public web app.
    /// </summary>
    public static string GetPublicBaseUri(NavigationManager nav)
    {
        var uri = new Uri(nav.BaseUri);
        if (uri.Host is "0.0.0.0" or "0.0.0.1")
            return "https://app.valour.gg/";

        return nav.BaseUri;
    }

    /// <summary>
    /// Builds the best link for a thread: the public server-rendered page when the
    /// planet exposes threads publicly, otherwise the in-app deep link.
    /// </summary>
    public static string GetThreadShareUrl(NavigationManager nav, Planet planet, ISharedPlanetThread thread)
    {
        var baseUri = GetPublicBaseUri(nav).TrimEnd('/');

        if (planet is not null && planet.EnableThreads && planet.PublicThreads)
            return $"{baseUri}/threads/{thread.PlanetId}/{thread.Id}";

        return $"{baseUri}/planetthreads/{thread.PlanetId}/{thread.Id}";
    }
}
