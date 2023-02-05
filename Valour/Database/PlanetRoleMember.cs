using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_role_members")]
public class PlanetRoleMember : Item, ISharedPlanetRoleMember
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("PlanetId")]
    public virtual Planet Planet { get; set; }
    
    [ForeignKey("MemberId")]
    public virtual PlanetMember Member { get; set; }
    
    [ForeignKey("RoleId")]
    public virtual PlanetRole Role { get; set; }

    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    [Column("planet_id")]
    public long PlanetId { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }

    [Column("role_id")]
    public long RoleId { get; set; }

    [Column("member_id")]
    public long MemberId { get; set; }
}

