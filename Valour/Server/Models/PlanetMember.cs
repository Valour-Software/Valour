using Valour.Shared.Models;

namespace Valour.Server.Models;

/// <summary>
/// Service model for a planet member
/// </summary>
public class PlanetMember : ServerModel<long>, ISharedPlanetMember
{
    /// <summary>
    /// The user of the member
    /// </summary>
    public User User { get; set; }
    
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
    /// Flags representing the roles of the member
    /// </summary>
    public PlanetRoleMembership RoleMembership { get; set; }
}