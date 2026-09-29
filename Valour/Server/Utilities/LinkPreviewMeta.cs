using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Valour.Client.Utility;
using Valour.Shared.Hosting;
using Valour.Shared.Models;

namespace Valour.Server.Utilities;

/// <summary>
/// What a Valour page tells other apps (Discord, X, Slack, iMessage and
/// others) to show when someone shares its link: the Open Graph and Twitter
/// card tags, the page title, and a theme color.
/// </summary>
public sealed record LinkPreviewMeta
{
    public const string DefaultThemeColor = "#0a0d12";

    public required string Title { get; init; }

    /// <summary>
    /// The browser tab title, when it should say more than Title.
    /// </summary>
    public string DocumentTitle { get; init; }
    public required string Description { get; init; }

    /// <summary>
    /// The canonical address of the shared page.
    /// </summary>
    public required string Url { get; init; }

    public string SiteName { get; init; } = "Valour";
    public string Type { get; init; } = "website";

    /// <summary>
    /// An absolute https image address that does not expire.
    /// </summary>
    public string Image { get; init; } = DefaultImage;
    public int ImageWidth { get; init; } = DefaultImageSize;
    public int ImageHeight { get; init; } = DefaultImageSize;
    public string ImageAlt { get; init; } = "The Valour logo";

    /// <summary>
    /// Shows the image large across the preview instead of as a thumbnail.
    /// </summary>
    public bool LargeImage { get; init; }

    /// <summary>
    /// The accent other apps may use for the preview, such as the edge
    /// of a Discord embed.
    /// </summary>
    public string ThemeColor { get; init; } = DefaultThemeColor;

    /// <summary>
    /// Keeps search engines from listing the page, for links that are only
    /// meant to be followed, such as invites.
    /// </summary>
    public bool NoIndex { get; init; }

    public static string DefaultImage => $"{ValourHosts.AppBaseUrl}/_content/Valour.Client/media/logo/logo-square-512.png";
    private const int DefaultImageSize = 512;

    /// <summary>
    /// Uses the planet's icon as the image, unless it has none or is
    /// age-restricted, and the planet's light as the theme color.
    /// </summary>
    public LinkPreviewMeta WithPlanet(ISharedPlanet planet) =>
        WithPlanet(PlanetIconUrl(planet), planet.Name, NebulaPalettes.PlanetLightHex(planet.Id, planet.WorldVariant));

    public LinkPreviewMeta WithPlanet(ISharedPlanetListInfo planet) =>
        WithPlanet(PlanetIconUrl(planet), planet.Name, NebulaPalettes.PlanetLightHex(planet.PlanetId, planet.WorldVariant));

    /// <summary>
    /// An image address for a planet that other apps can load: its custom
    /// icon, or the Valour logo. Planets without a custom icon draw theirs in
    /// the browser, and the icon of an age-restricted planet is not shared.
    /// </summary>
    public static string PlanetImageUrl(ISharedPlanet planet) => PlanetIconUrl(planet) ?? DefaultImage;

    public static string PlanetImageUrl(ISharedPlanetListInfo planet) => PlanetIconUrl(planet) ?? DefaultImage;

    private static string PlanetIconUrl(ISharedPlanet planet) =>
        planet.HasCustomIcon && !planet.Nsfw ? ISharedPlanet.GetIconUrl(planet, IconFormat.Jpeg256) : null;

    private static string PlanetIconUrl(ISharedPlanetListInfo planet) =>
        planet.HasCustomIcon && !planet.Nsfw ? ISharedPlanet.GetIconUrl(planet, IconFormat.Jpeg256) : null;

    private LinkPreviewMeta WithPlanet(string iconUrl, string planetName, string themeColor) => iconUrl is null
        ? this with { ThemeColor = themeColor }
        : this with
        {
            Image = iconUrl,
            ImageWidth = PlanetIconSize,
            ImageHeight = PlanetIconSize,
            ImageAlt = $"The icon of {planetName}",
            ThemeColor = themeColor,
        };

    private const int PlanetIconSize = 256;

    // The app's host page (Client/wwwroot/index.html) marks its generic
    // preview tags with these comments so a host can swap in a page's own.
    public const string HostPageStartMarker = "<!-- link-preview -->";
    public const string HostPageEndMarker = "<!-- /link-preview -->";

    /// <summary>
    /// Replaces the generic preview tags in the app's host page with these.
    /// Returns the page unchanged when it has no markers.
    /// </summary>
    public string ApplyToHostPage(string hostPage)
    {
        var start = hostPage.IndexOf(HostPageStartMarker, StringComparison.Ordinal);
        var end = start < 0 ? -1 : hostPage.IndexOf(HostPageEndMarker, start, StringComparison.Ordinal);
        if (start < 0 || end < 0)
            return hostPage;

        return string.Concat(
            hostPage.AsSpan(0, start + HostPageStartMarker.Length),
            "\n",
            RenderHead(),
            hostPage.AsSpan(end));
    }

    /// <summary>
    /// Writes the tags for a page head. Every value is HTML encoded.
    /// </summary>
    public string RenderHead()
    {
        var html = new StringBuilder();
        // Readable text in any script; markup characters are still encoded.
        var encoder = HtmlEncoder.Create(UnicodeRanges.All);

        void Meta(string attribute, string key, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            html.Append("<meta ").Append(attribute).Append("=\"").Append(encoder.Encode(key))
                .Append("\" content=\"").Append(encoder.Encode(value)).Append("\">\n");
        }

        html.Append("<title>").Append(encoder.Encode(DocumentTitle ?? Title)).Append("</title>\n");
        Meta("name", "description", Description);
        if (NoIndex)
            Meta("name", "robots", "noindex");

        Meta("property", "og:site_name", SiteName);
        Meta("property", "og:type", Type);
        Meta("property", "og:url", Url);
        Meta("property", "og:title", Title);
        Meta("property", "og:description", Description);
        Meta("property", "og:image", Image);
        if (ImageWidth > 0 && ImageHeight > 0)
        {
            Meta("property", "og:image:width", ImageWidth.ToString());
            Meta("property", "og:image:height", ImageHeight.ToString());
        }
        Meta("property", "og:image:alt", ImageAlt);

        Meta("name", "twitter:card", LargeImage ? "summary_large_image" : "summary");
        Meta("name", "twitter:site", "@valourapp");
        Meta("name", "twitter:title", Title);
        Meta("name", "twitter:description", Description);
        Meta("name", "twitter:image", Image);
        Meta("name", "twitter:image:alt", ImageAlt);

        Meta("name", "theme-color", ThemeColor);
        html.Append("<link rel=\"canonical\" href=\"").Append(encoder.Encode(Url)).Append("\">\n");
        return html.ToString();
    }
}
