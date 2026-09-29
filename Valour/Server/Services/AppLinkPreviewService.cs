using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Caching.Memory;
using Valour.Server.Cdn;
using Valour.Server.Utilities;
using Valour.Shared.Hosting;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Builds the link preview for a shared app address, such as an invite, so
/// that other apps show the planet instead of the generic Valour card. It
/// shows only what the app already shows a signed-out visitor at that
/// address. Addresses it does not recognize return null and keep the
/// generic card.
/// </summary>
public class AppLinkPreviewService
{
    private const int MaxDescriptionLength = 200;

    private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);

    // Invite codes and vanity names; see PlanetInviteService.
    private static readonly Regex InvitePath = new(@"^/i/([A-Za-z0-9_-]{1,64})/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex PlanetPath = new(@"^/(?:d|planet)/(\d{1,20})/?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private readonly IMemoryCache _cache;
    private readonly PlanetInviteService _inviteService;
    private readonly PlanetService _planetService;

    public AppLinkPreviewService(IMemoryCache cache, PlanetInviteService inviteService, PlanetService planetService)
    {
        _cache = cache;
        _inviteService = inviteService;
        _planetService = planetService;
    }

    /// <summary>
    /// True when the path is one this service describes. Callers use it to
    /// skip the lookup for every other address.
    /// </summary>
    public static bool Handles(string path) =>
        !string.IsNullOrEmpty(path) && (InvitePath.IsMatch(path) || PlanetPath.IsMatch(path));

    public async Task<LinkPreviewMeta> GetAsync(string path)
    {
        if (!Handles(path))
            return null;

        var key = "app-link-preview:" + path.ToLowerInvariant().TrimEnd('/');
        if (_cache.TryGetValue(key, out LinkPreviewMeta cached))
            return cached;

        var meta = await BuildAsync(path);

        // Unknown codes are cached too, so guessing codes stays cheap.
        _cache.Set(key, meta, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheLifetime,
            Size = 1,
        });

        return meta;
    }

    private async Task<LinkPreviewMeta> BuildAsync(string path)
    {
        var url = ValourHosts.AppBaseUrl + path.TrimEnd('/');

        var invite = InvitePath.Match(path);
        if (invite.Success)
        {
            var result = await _inviteService.GetPlanetInfoByInviteCode(invite.Groups[1].Value);
            if (!result.Success || result.Data is null)
                return null;

            return ForPlanet(result.Data, url,
                title: $"Join {result.Data.Name} on Valour",
                noIndex: true);
        }

        var planet = PlanetPath.Match(path);
        if (planet.Success && long.TryParse(planet.Groups[1].Value, out var planetId))
        {
            // Public planets only, as on the planet's own public page.
            var info = await _planetService.GetPlanetInfoAsync(planetId);
            if (info is null)
                return null;

            // Other apps already name the site as Valour next to the title.
            return ForPlanet(info, url, title: info.Name, noIndex: false);
        }

        return null;
    }

    /// <summary>
    /// A planet's preview, with its description and member count. The
    /// planet's public page uses it too.
    /// </summary>
    public static LinkPreviewMeta ForPlanet(ISharedPlanetListInfo planet, string url, string title, bool noIndex)
    {
        var members = planet.MemberCount == 1
            ? "1 member"
            : $"{planet.MemberCount.ToString("N0", CultureInfo.InvariantCulture)} members";

        // Age-restricted planets keep their name, which the invite screen
        // shows anyone, but not their description or icon.
        var description = planet.Nsfw
            ? $"An age-restricted community on Valour · {members}"
            : SitePreviewFetcher.Clean(planet.Description, MaxDescriptionLength) is { } text
                ? $"{text} · {members}"
                : $"A community on Valour · {members}";

        return new LinkPreviewMeta
        {
            Title = SitePreviewFetcher.Clean(title, SitePreviewFetcher.MaxTitleLength) ?? "Valour",
            Description = description,
            Url = url,
            NoIndex = noIndex,
        }.WithPlanet(planet);
    }
}
