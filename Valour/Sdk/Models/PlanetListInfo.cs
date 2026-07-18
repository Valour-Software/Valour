using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PlanetListInfo : ClientModel<PlanetListInfo, long>, ISharedPlanetListInfo
{
    /// <summary>
    /// The ID of the planet this info represents
    /// </summary>
    public new long Id { get; set; }
    
    /// <summary>
    /// The ID of the planet this info represents
    /// </summary>
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The name of the planet
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The description of the planet
    /// </summary>
    public string Description { get; set; }
    
    /// <summary>
    /// True if the planet has a custom icon
    /// </summary>
    public bool HasCustomIcon { get; set; }
    
    /// <summary>
    /// True if the planet has an animated icon
    /// </summary>
    public bool HasAnimatedIcon { get; set; }
    
    /// <summary>
    /// True if the planet has a custom background
    /// </summary>
    public bool HasCustomBackground { get; set; }
    
    /// <summary>
    /// True if the planet is marked as NSFW
    /// </summary>
    public bool Nsfw { get; set; }

    /// <summary>
    /// True when this planet stores media on its own infrastructure
    /// </summary>
    public bool SelfHostedMedia { get; set; }

    /// <summary>
    /// True when this planet runs voice/video on its own LiveKit SFU
    /// </summary>
    public bool SelfHostedVoice { get; set; }

    /// <summary>
    /// Community node domain hosting this planet, or null when official.
    /// </summary>
    public string NodeDomain { get; set; }

    /// <summary>
    /// True if the planet is discoverable (shows up in planet discovery)
    /// </summary>
    public bool Discoverable { get; set; }
    
    /// <summary>
    /// The number of members in the planet
    /// </summary>
    public int MemberCount { get; set; }
    
    /// <summary>
    /// The version of the planet
    /// </summary>
    public int Version { get; set; }
    
    /// <summary>
    /// List of tag IDs associated with the planet
    /// </summary>
    public List<long> TagIds { get; set; } = new();
    
    /// <summary>
    /// List of full tag objects associated with the planet (for SSR)
    /// </summary>
    public List<PlanetTag> Tags { get; set; } = new();

    public override PlanetListInfo AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }
    
    public override PlanetListInfo RemoveFromCache(bool skipEvents = false)
    {
        return this;
    }

    public static PlanetListInfo FromPlanet(ISharedPlanet planet)
    {
        return new PlanetListInfo
        {
            Id = planet.Id,
            PlanetId = planet.Id,
            Name = planet.Name,
            Description = planet.Description,
            HasCustomIcon = planet.HasCustomIcon,
            HasAnimatedIcon = planet.HasAnimatedIcon,
            HasCustomBackground = planet.HasCustomBackground,
            Nsfw = planet.Nsfw,
            SelfHostedMedia = planet.SelfHostedMedia,
            SelfHostedVoice = planet.SelfHostedVoice,
            Discoverable = planet.Discoverable,
            Version = planet.Version,
            TagIds = new List<long>() // Tags are not included in this model
        };
    }
    
    public List<ISharedPlanetTag> GetTagsGeneric()
    {
        return Tags.Cast<ISharedPlanetTag>().ToList();
    }
}
