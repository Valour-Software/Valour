using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetInvite : ServerModel<string>, ISharedPlanetInvite
{
    /// <summary>
    /// The id of the planet this belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    public long IssuerId { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// When the invite expires
    /// </summary>
    public DateTime? TimeExpires { get; set; }

    /// <summary>
    /// True if the invite is permanent
    /// </summary>
    public bool IsPermanent() => TimeExpires is null;
}