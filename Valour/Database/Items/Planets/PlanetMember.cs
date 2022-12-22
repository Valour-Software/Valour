using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Server.Database.Items;
using Valour.Server.Database.Items.Users;
using Valour.Shared.Items.Planets.Members;

namespace Valour.Database.Items.Planets;

/// <summary>
/// Database model for a planet member
/// </summary>
[Table("planet_members")]
public class PlanetMember : Item, ISharedPlanetMember
{
    ///////////////////////////
    // Relational properties //
    ///////////////////////////
    
    [JsonIgnore]
    [ForeignKey("PlanetId")] 
    public Planet Planet { get; set; }

    // Relational DB stuff
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
    public string MemberPfp { get; set; }

    [Column("is_deleted")]
    public bool IsDeleted { get; set; }
}

