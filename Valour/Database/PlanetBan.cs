using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Items.Planets.Members;

namespace Valour.Database;

[Table("planet_bans")]
public class PlanetBan : Item, ISharedPlanetBan
{
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }

    [Column("planet_id")]
    public long PlanetId { get; set; }

    /// <summary>
    /// The member that banned the user
    /// </summary>
    [Column("issuer_id")]
    public long IssuerId { get; set; }

    /// <summary>
    /// The userId of the target that was banned
    /// </summary>
    [Column("target_id")]
    public long TargetId { get; set; }

    /// <summary>
    /// The reason for the ban
    /// </summary>
    [Column("reason")]
    public string Reason { get; set; }

    /// <summary>
    /// The time the ban was placed
    /// </summary>
    [Column("time_created")]
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// The time the ban expires. Null for permanent.
    /// </summary>
    [Column("time_expires")]
    public DateTime? TimeExpires { get; set; }
    
}
