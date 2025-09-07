using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetListInfo : ServerModel<long>, ISharedPlanetListInfo
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
    /// List of full tag objects associated with the planet
    /// </summary>
    public List<PlanetTag> Tags { get; set; } = new();
    
    public List<ISharedPlanetTag> GetTagsGeneric() => Tags.Cast<ISharedPlanetTag>().ToList();
}
