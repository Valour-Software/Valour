using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_invites")]
public class PlanetInvite : Item, ISharedPlanetInvite
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    [Column("planet_id")]
    public long PlanetId { get; set; }
    
    /// <summary>
    /// The invite code
    /// </summary>
    [Column("code")]
    public string Code { get; set; }

    /// <summary>
    /// The user that created the invite
    /// </summary>
    [Column("issuer_id")]
    public long IssuerId { get; set; }

    /// <summary>
    /// The time the invite was created
    /// </summary>
    [Column("time_created")]
    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// When the invite expires
    /// </summary>
    [Column("time_expires")]
    public DateTime? TimeExpires { get; set; }
}
