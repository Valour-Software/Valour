using Valour.Shared.Models;

namespace Valour.Server.Models;

/// <summary>
/// Service model for a planet member
/// </summary>
public class PlanetMember : ServerModel, ISharedPlanetMember
{
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
    public string MemberAvatar { get; set; }
    
    /// <summary>
    /// The key representing the roles the user has within the planet
    /// </summary>
    public long RoleHashKey { get; set; }
    
    /// <summary>
    /// True if the user is the owner of the planet
    /// </summary>
    public bool IsPlanetOwner { get; set; }
}