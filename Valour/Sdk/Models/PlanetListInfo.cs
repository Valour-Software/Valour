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
            Version = planet.Version,
            TagIds = new List<long>() // Tags are not included in this model
        };
    }
}
