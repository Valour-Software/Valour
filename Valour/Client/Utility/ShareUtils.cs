using Microsoft.AspNetCore.Components;
using Valour.Sdk.Models;
using Valour.Sdk.Models.Wiki;
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
            return ClientHosts.AppBaseUrl;

        return nav.BaseUri;
    }

    /// <summary>
    /// Builds the best link for a thread: the public server-rendered page on the
    /// threads subdomain when the planet exposes threads publicly, otherwise the
    /// in-app deep link.
    /// </summary>
    public static string GetThreadShareUrl(NavigationManager nav, Planet planet, ISharedPlanetThread thread)
    {
        if (planet is not null && planet.EnableThreads && planet.PublicThreads)
            return $"{ClientHosts.ThreadsBaseUrl.TrimEnd('/')}/{thread.PlanetId}/{thread.Id}";

        var baseUri = GetPublicBaseUri(nav).TrimEnd('/');
        return $"{baseUri}/planetthreads/{thread.PlanetId}/{thread.Id}";
    }

    /// <summary>
    /// Builds the best link for a docs page: the public docs site when the
    /// planet exposes docs publicly and the page is published, otherwise the
    /// in-app deep link on the current origin.
    /// </summary>
    public static string GetWikiPageShareUrl(NavigationManager nav, Planet planet, PlanetWikiPage doc)
    {
        if (planet is not null && planet.EnableWiki && planet.PublicWiki &&
            doc is not null && !doc.IsFolder && doc.IsPublished && doc.Slug is not null)
        {
            return WikiLinks.GetPublicPageUrl(planet, doc);
        }

        var baseUri = GetPublicBaseUri(nav).TrimEnd('/');
        return doc is null
            ? $"{baseUri}/planetwiki/{planet!.Id}"
            : $"{baseUri}/planetwiki/{doc.PlanetId}/{doc.Id}";
    }
}
