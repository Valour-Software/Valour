using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Models;

namespace Valour.Database;

/// <summary>
/// Database model for a planet member
/// </summary>
[Table("planet_members")]
public class PlanetMember : Item, ISharedPlanetMember
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [JsonIgnore]
    [ForeignKey("PlanetId")] 
    public Planet Planet { get; set; }
    
    [ForeignKey("UserId")]
    [JsonIgnore]
    public virtual User User { get; set; }

    [InverseProperty("Member")]
    [JsonIgnore]
    public virtual ICollection<PlanetRoleMember> RoleMembership { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Column("user_id")]
    public long UserId { get; set; }
    
    [Column("planet_id")]
    public long PlanetId { get; set; }
    
    [Column("nickname")]
    public string Nickname { get; set; }

    [Column("member_pfp")]
    public string MemberAvatar { get; set; }

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }
}

