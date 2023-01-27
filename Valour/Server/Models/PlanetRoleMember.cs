using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetRoleMember : Item, ISharedPlanetRoleMember
{
    /// <summary>
    /// The planet id this role member belongs to
    /// </summary>
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The user id this role member applies to
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// The role id this member has
    /// </summary>
    public long RoleId { get; set; }

    /// <summary>
    /// The member id the role applies to
    /// </summary>
    public long MemberId { get; set; }
}

