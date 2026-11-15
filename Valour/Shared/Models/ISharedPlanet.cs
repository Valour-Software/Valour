#nullable enable

/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

using System.Collections.Frozen;
using System.Text;
using Valour.Shared.Utilities;

namespace Valour.Shared.Models;

public interface ISharedPlanet : ISharedModel<long>
{
    const string BaseRoute = "api/planets";

    public const int MaxVanityLength = 32;

    public static string GetVanityRoute(long planetId) => $"{BaseRoute}/{planetId}/vanity";
    public static string GetVanityCheckRoute(long planetId) => $"{GetVanityRoute(planetId)}/check";

    /// <summary>
    /// Resolves a claimed vanity name to its planet id
    /// </summary>
    public static string GetVanityResolveRoute(string name) => $"{BaseRoute}/vanity/{name}";
    
    /// <summary>
    /// The Id of Valour Central, used for some platform-wide features
    /// </summary>
    const long ValourCentralId = 12215159187308544;

    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    long OwnerId { get; set; }
    
    /// <summary>
    /// The node this planet belongs to
    /// </summary>
    string NodeName { get; set; } 

    /// <summary>
    /// The name of this planet
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// True if the planet has a custom icon
    /// </summary>
    bool HasCustomIcon { get; set; }
    
    /// <summary>
    /// True if the planet has an animated icon
    /// </summary>
    bool HasAnimatedIcon { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    string Description { get; set; }

    /// <summary>
    /// True when anyone can join the planet and read its messages. A private
    /// planet is joined with an invite link, and its signed membership log
    /// decides who receives its keys.
    /// </summary>
    bool Public { get; set; }

    /// <summary>
    /// If this and public are true, a planet will appear on the discovery tab
    /// </summary>
    bool Discoverable { get; set; }
    
    /// <summary>
    /// True if you probably shouldn't be on this server at work owo
    /// </summary>
    bool Nsfw { get; set; }

    /// <summary>
    /// True when this planet stores media on its own infrastructure
    /// (bring-your-own-storage) rather than Valour's CDN
    /// </summary>
    bool SelfHostedMedia { get; set; }

    /// <summary>
    /// True when this planet runs voice/video calls on its own LiveKit SFU
    /// (bring-your-own-voice) rather than Valour's voice backend
    /// </summary>
    bool SelfHostedVoice { get; set; }
    
    /// <summary>
    /// The version of the planet. Used for cache busting.
    /// </summary>
    public int Version { get; set; }
    
    /// <summary>
    /// True if the planet has a custom background
    /// </summary>
    public bool HasCustomBackground { get; set; }

    /// <summary>
    /// True if the threads feed is enabled for this planet
    /// </summary>
    bool EnableThreads { get; set; }

    /// <summary>
    /// Who may receive the planet's channel keys. Every message is end-to-end
    /// encrypted either way.
    /// </summary>
    PlanetEncryptionMode EncryptionMode { get; set; }

    /// <summary>
    /// Whether new members of an encrypted planet can read earlier messages
    /// </summary>
    bool EncryptionSharesHistory { get; set; }

    /// <summary>
    /// True if this planet's threads can be browsed publicly without an account
    /// </summary>
    bool PublicThreads { get; set; }

    /// <summary>
    /// The id of the single thread pinned to the top of this planet's feed, if any.
    /// Members can dismiss it individually via <see cref="ISharedPlanetMember.DismissedPinThreadId"/>.
    /// </summary>
    long? PinnedThreadId { get; set; }

    /// <summary>
    /// True if the docs/wiki is enabled for this planet
    /// </summary>
    bool EnableWiki { get; set; }

    /// <summary>
    /// True if the village is enabled for this planet
    /// </summary>
    bool EnableVillage { get; set; }

    /// <summary>
    /// True if this planet's docs can be read publicly without an account
    /// </summary>
    bool PublicWiki { get; set; }

    /// <summary>
    /// True if the community calendar is enabled for this planet
    /// </summary>
    bool EnableCalendar { get; set; }

    /// <summary>
    /// The planet's claimed vanity name, if any — identifies the planet in
    /// public URLs (wiki pages, the public planet page, and future surfaces
    /// like invites). Lowercase, unique across planets, never all digits.
    /// Set only via the vanity endpoint.
    /// </summary>
    string? Vanity { get; set; }

    /// <summary>
    /// True when the planet's vanity name also works as a permanent invite
    /// link (/i/{vanity}), letting a memorable name double as a share link
    /// instead of a random invite code. Only takes effect while Public and Vanity are both set.
    /// </summary>
    bool VanityInviteEnabled { get; set; }

    /// <summary>
    /// Owner-chosen default cadence for channel activity notifications.
    /// Applies to members who have not set their own cooldown preference.
    /// </summary>
    ChannelActivityCadence ActivityNotificationCadence { get; set; }
    
    /// <summary>
    /// Selects which generated world (sky, globe, and planet light) the planet
    /// shows. 0 is the world derived from the id alone; regenerating the planet
    /// in its settings picks another value.
    /// </summary>
    byte WorldVariant { get; set; }

    private static readonly FrozenDictionary<IconFormat, string> IconFormatMap = new Dictionary<IconFormat, string>()
    {
        { IconFormat.Webp64, "64.webp" },
        { IconFormat.Webp128, "128.webp" },
        { IconFormat.Webp256, "256.webp" },
        
        { IconFormat.Jpeg64, "64.jpg" },
        { IconFormat.Jpeg128, "128.jpg" },
        { IconFormat.Jpeg256, "256.jpg" },
        
        { IconFormat.WebpAnimated64, "anim-64.webp" },
        { IconFormat.WebpAnimated128, "anim-128.webp" },
        { IconFormat.WebpAnimated256, "anim-256.webp" },
        
        { IconFormat.Gif64, "anim-64.gif" },
        { IconFormat.Gif128, "anim-128.gif" },
        { IconFormat.Gif256, "anim-256.gif" },
    }.ToFrozenDictionary();
    
    private static readonly FrozenDictionary<PlanetBackgroundFormat, string> BackgroundFormatMap = new Dictionary<PlanetBackgroundFormat, string>()
    {
        { PlanetBackgroundFormat.Webp, "300x400.webp" },
        { PlanetBackgroundFormat.Jpeg, "300x400.jpg" },
    }.ToFrozenDictionary();
    
    private static readonly FrozenSet<IconFormat> AnimatedFormats = new HashSet<IconFormat>()
    {
        IconFormat.Gif64,
        IconFormat.Gif128,
        IconFormat.Gif256,
        IconFormat.WebpAnimated64,
        IconFormat.WebpAnimated128,
        IconFormat.WebpAnimated256,
    }.ToFrozenSet();
    
    private static readonly FrozenDictionary<IconFormat, IconFormat> AnimatedToStaticBackup = new Dictionary<IconFormat, IconFormat>()
    {
        { IconFormat.Gif64, IconFormat.Webp64 },
        { IconFormat.Gif128, IconFormat.Webp128 },
        { IconFormat.Gif256, IconFormat.Webp256 },
        { IconFormat.WebpAnimated64, IconFormat.Webp64 },
        { IconFormat.WebpAnimated128, IconFormat.Webp128 },
        { IconFormat.WebpAnimated256, IconFormat.Webp256 },
    }.ToFrozenDictionary();
    
    public static string? GetBackgroundUrl(bool hasCustom, long planetId, int version, PlanetBackgroundFormat format)
    {
        if (!hasCustom)
        {
            return null;
        }
        
        var formatStr = BackgroundFormatMap[format];
        return $"{ValourHosts.PublicCdnBaseUrl}/valour-public/planetbgs/{planetId}/{formatStr}?v={version}";
    }

    /// <summary>
    /// The icon URL in the requested format. Planets without a custom icon get
    /// the generated default icon, and animated formats fall back to the static
    /// image when the planet has no animated icon.
    /// </summary>
    public static string GetIconUrl(long planetId, int version, bool hasCustomIcon, bool hasAnimatedIcon, IconFormat format)
    {
        if (!hasCustomIcon)
            return PlanetIconSvgGenerator.GetPlanetIconColor(planetId);

        if (!hasAnimatedIcon && AnimatedFormats.Contains(format))
            format = AnimatedToStaticBackup[format];

        return $"{ValourHosts.PublicCdnBaseUrl}/valour-public/planets/{planetId}/{IconFormatMap[format]}?v={version}";
    }

    public static string GetCommunityShortCode(string name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        if (name.Length < 5) return name.ToUpperInvariant();
        var sb = new StringBuilder();
        sb.Append(char.ToUpperInvariant(name[0]));
        for (int i = 1; i < name.Length && sb.Length < 4; i++)
        {
            var c = name[i];
            if (name[i - 1] == ' ' || (char.IsUpper(c) && char.IsLower(name[i - 1])))
                sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}

/// <summary>
/// Members shared by every planet model, and by the smaller planet list entries.
/// </summary>
public static class SharedPlanetExtensions
{
    extension(ISharedPlanet planet)
    {
        public string GetIconUrl(IconFormat format = IconFormat.Webp256) =>
            ISharedPlanet.GetIconUrl(planet.Id, planet.Version, planet.HasCustomIcon, planet.HasAnimatedIcon, format);

        public string? GetBackgroundUrl(PlanetBackgroundFormat format) =>
            ISharedPlanet.GetBackgroundUrl(planet.HasCustomBackground, planet.Id, planet.Version, format);

        /// <summary>
        /// The short text shown in place of an icon for planets without one.
        /// </summary>
        public string CommunityShortCode => ISharedPlanet.GetCommunityShortCode(planet.Name);
    }

    extension(ISharedPlanetListInfo planet)
    {
        public string GetIconUrl(IconFormat format = IconFormat.Webp256) =>
            ISharedPlanet.GetIconUrl(planet.PlanetId, planet.Version, planet.HasCustomIcon, planet.HasAnimatedIcon, format);

        public string? GetBackgroundUrl(PlanetBackgroundFormat format) =>
            ISharedPlanet.GetBackgroundUrl(planet.HasCustomBackground, planet.PlanetId, planet.Version, format);

        /// <summary>
        /// The short text shown in place of an icon for planets without one.
        /// </summary>
        public string CommunityShortCode => ISharedPlanet.GetCommunityShortCode(planet.Name);
    }
}
