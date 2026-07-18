using Valour.Sdk.Models;
using Valour.Sdk.Models.Wiki;
using Valour.Shared.Hosting;

namespace Valour.Client.Utility;

/// <summary>
/// Builds public and in-app docs URLs, mirroring the server's routing: a
/// dedicated docs subdomain uses clean paths, while single-domain self-hosts
/// serve docs under the /docs path prefix.
/// </summary>
public static class WikiLinks
{
    private static bool WikiHostIsDistinct =>
        !ValourHosts.WikiHost.Equals(ValourHosts.AppHost, StringComparison.OrdinalIgnoreCase) &&
        !ValourHosts.WikiHost.Equals(ValourHosts.RootDomain, StringComparison.OrdinalIgnoreCase);

    public static string PublicWikiBase => WikiHostIsDistinct
        ? ClientHosts.WikiBaseUrl
        : $"{ClientHosts.WikiBaseUrl}/wiki";

    private static string GetPlanetSegment(Planet planet) =>
        string.IsNullOrWhiteSpace(planet.Vanity) ? planet.Id.ToString() : planet.Vanity;

    public static string GetPublicHomeUrl(Planet planet) =>
        $"{PublicWikiBase}/{GetPlanetSegment(planet)}";

    public static string GetPublicPageUrl(Planet planet, PlanetWikiPage doc) =>
        $"{PublicWikiBase}/{GetPlanetSegment(planet)}/{doc.Slug}";

    public static string GetAppHomeUrl(long planetId) =>
        $"{ClientHosts.AppBaseUrl}/planetwiki/{planetId}";

    public static string GetAppPageUrl(long planetId, long pageId) =>
        $"{ClientHosts.AppBaseUrl}/planetwiki/{planetId}/{pageId}";
}
