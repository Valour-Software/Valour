using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PlanetBan : ClientModel, ISharedPlanetBan
{
    #region IPlanetModel implementation

    public override string BaseRoute => "api/bans";

    #endregion

    /// <summary>
    /// The user that banned the user
    /// </summary>
    public long IssuerId { get; set; }

    /// <summary>
    /// The planet this ban belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The userId of the target that was banned
    /// </summary>
    public long TargetId { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time the ban expires. Null for permanent.
    /// </summary>
    public DateTime? TimeExpires { get; set; }

    /// <summary>
    /// True if the ban never expires
    /// </summary>
    public bool Permanent => TimeExpires == null;
}
