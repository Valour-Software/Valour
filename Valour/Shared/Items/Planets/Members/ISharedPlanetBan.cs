using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Valour.Shared.Items.Planets.Members;

public interface ISharedPlanetBan
{
    /// <summary>
    /// The user that was banned
    /// </summary>
    ulong TargetId { get; set; }

    /// <summary>
    /// The planet the user was within
    /// </summary>
    ulong PlanetId { get; set; }

    /// <summary>
    /// The user that banned the user
    /// </summary>
    ulong IssuerId { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    DateTime Time { get; set; }

    /// <summary>
    /// The time the ban expires. Null for permanent.
    /// </summary>
    DateTime? Expires { get; set; }

    /// <summary>
    /// True if the ban never expires
    /// </summary>
    public bool Permanent => Expires == null;
}

