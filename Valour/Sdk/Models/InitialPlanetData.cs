namespace Valour.Sdk.Models;

/// <summary>
/// Rather than using multiple API calls to get the initial data for a planet,
/// we can use this class to store all the data we need in one go.
/// </summary>
public class InitialPlanetData
{
    /// <summary>
    /// The planet this data was requested for
    /// </summary>
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The roles within the planet
    /// </summary>
    public List<PlanetRole> Roles { get; set; }
    
    /// <summary>
    /// The channels within the planet that the user has access to
    /// </summary>
    public List<Channel> Channels { get; set; }
    
    /// <summary>
    /// Initial member data. Will include most recently active members, but may not
    /// include all members
    /// </summary>
    public List<PlanetMemberData> MemberData { get; set; }
    
    /// <summary>
    /// The 
    /// </summary>
    public List<PermissionsNode> Permissions { get; set; }
}