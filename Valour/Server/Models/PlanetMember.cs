using Valour.Shared.Models;

namespace Valour.Server.Models;

/// <summary>
/// Service model for a planet member
/// </summary>
public class PlanetMember : Item, ISharedPlanetMember
{
    /// <summary>
    /// The user id of the member
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// The node this item belongs to
    /// </summary>
    public string NodeName { get; set; }
    
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