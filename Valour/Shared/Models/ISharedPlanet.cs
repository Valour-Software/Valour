/*  Valour (TM) - A free and secure chat client
 *  Copyright (C) 2025 Valour Software LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

using Valour.Shared.Utilities;

namespace Valour.Shared.Models;

public interface ISharedPlanet : ISharedModel<long>
{
    const string BaseRoute = "api/planets";
    
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
    /// If the server requires express allowal to join a planet
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
    /// The version of the planet. Used for cache busting.
    /// </summary>
    public int Version { get; set; }
    
    private static readonly Dictionary<IconFormat, string> IconFormatMap = new()
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
    };
    
    private static readonly HashSet<IconFormat> AnimatedFormats = new()
    {
        IconFormat.Gif64,
        IconFormat.Gif128,
        IconFormat.Gif256,
        IconFormat.WebpAnimated64,
        IconFormat.WebpAnimated128,
        IconFormat.WebpAnimated256,
    };
    
    private static readonly Dictionary<IconFormat, IconFormat> AnimatedToStaticBackup = new()
    {
        { IconFormat.Gif64, IconFormat.Webp64 },
        { IconFormat.Gif128, IconFormat.Webp128 },
        { IconFormat.Gif256, IconFormat.Webp256 },
        { IconFormat.WebpAnimated64, IconFormat.Webp64 },
        { IconFormat.WebpAnimated128, IconFormat.Webp128 },
        { IconFormat.WebpAnimated256, IconFormat.Webp256 },
    };
    
    public static string GetIconUrl(ISharedPlanet planet, IconFormat format)
    {
        if (!planet.HasCustomIcon)
        {
            return PlanetIconSvgGenerator.GetPlanetIconColor(planet);                        
        }

        // If an animated icon is requested, but the planet doesn't have one, use the static version
        if (!planet.HasAnimatedIcon)
        {
            if (AnimatedFormats.Contains(format))
            {
                format = AnimatedToStaticBackup[format];
            }
        }
        
        string formatStr = IconFormatMap[format];
        return $"https://public-cdn.valour.gg/valour-public/planets/{planet.Id}/{formatStr}?v={planet.Version}";
    }
    
    public static string GetIconUrl(PlanetListInfo planet, IconFormat format)
    {
        if (!planet.HasCustomIcon)
        {
            return PlanetIconSvgGenerator.GetPlanetIconColor(planet.PlanetId);                        
        }

        // If an animated icon is requested, but the planet doesn't have one, use the static version
        if (!planet.HasAnimatedIcon)
        {
            if (AnimatedFormats.Contains(format))
            {
                format = AnimatedToStaticBackup[format];
            }
        }
        
        string formatStr = IconFormatMap[format];
        return $"https://public-cdn.valour.gg/valour-public/planets/{planet.PlanetId}/{formatStr}?v={planet.Version}";
    }
}

