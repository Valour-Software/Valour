namespace Valour.Shared.Models;

/// <summary>
/// This information is used to give the client a summary of a planet that has
/// not yet been loaded.
/// </summary>
public interface ISharedPlanetListInfo : ISharedModel<long>
{
    /// <summary>
    /// The ID of the planet this info represents
    /// </summary>
    long PlanetId { get; set; }
    
    /// <summary>
    /// The name of the planet
    /// </summary>
    string Name { get; set; }
    
    /// <summary>
    /// The description of the planet
    /// </summary>
    string Description { get; set; }
    
    /// <summary>
    /// True if the planet has a custom icon
    /// </summary>
    bool HasCustomIcon { get; set; }
    
    /// <summary>
    /// True if the planet has an animated icon
    /// </summary>
    bool HasAnimatedIcon { get; set; }
    
    /// <summary>
    /// True if the planet has a custom background
    /// </summary>
    bool HasCustomBackground { get; set; }
    
    /// <summary>
    /// True if the planet is marked as NSFW
    /// </summary>
    bool Nsfw { get; set; }
    
    /// <summary>
    /// The number of members in the planet
    /// </summary>
    int MemberCount { get; set; }
    
    /// <summary>
    /// The version of the planet
    /// </summary>
    int Version { get; set; }
    
    /// <summary>
    /// List of tag IDs associated with the planet
    /// </summary>
    List<long> TagIds { get; set; }
}
