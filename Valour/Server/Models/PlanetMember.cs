using Valour.Shared.Items.Planets.Members;

namespace Valour.Server.Models;

/// <summary>
/// Service model for a planet member
/// </summary>
public class PlanetMember : ISharedPlanetMember
{
    /// <summary>
    /// The id of the member
    /// </summary>
    public long Id { get; set; }
    
    /// <summary>
    /// The name of the node that returned this object
    /// </summary>
    public string NodeName { get; set; }
    
    /// <summary>
    /// The user id of the member
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// The planet id of the member
    /// </summary>
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The in-planet nickname of the member
    /// </summary>
    public string Nickname { get; set; }

    /// <summary>
    /// The in-planet profile picture of the member
    /// </summary>
    public string MemberPfp { get; set; }
}