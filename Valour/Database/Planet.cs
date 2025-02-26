using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planets")]
public class Planet : ISharedPlanet
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [InverseProperty("Planet")]
    public virtual ICollection<PlanetRole> Roles { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetMember> Members { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<Channel> Channels { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetInvite> Invites { get; set; }
    
    public virtual ICollection<Message> Messages { get; set; }
    
    public virtual ICollection<UserChannelState> UserChannelStates { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////

    [Key]
    [Column("id")]
    public long Id { get; set; }
    
    /// <summary>
    /// The Id of the owner of this planet
    /// </summary>
    [Column("owner_id")]
    public long OwnerId { get; set; }

    /// <summary>
    /// The name of this planet
    /// </summary>
    [Column("name")]
    public string Name { get; set; }

    /// <summary>
    /// True if the planet has a custom icon
    /// </summary>
    [Column("custom_icon")]
    public bool HasCustomIcon { get; set; }
    
    /// <summary>
    /// True if the planet has an animated icon
    /// </summary>
    [Column("animated_icon")]
    public bool HasAnimatedIcon { get; set; }
    
    /// <summary>
    /// The old icon url. Do not use.
    /// </summary>
    [Column("icon_url")]
    public string OldIconUrl { get; set; }

    /// <summary>
    /// The description of the planet
    /// </summary>
    [Column("description")]
    public string Description { get; set; }

    /// <summary>
    /// If the server requires express allowal to join a planet
    /// </summary>
    [Column("public")]
    public bool Public { get; set; }

    /// <summary>
    /// If the server should show up on the discovery tab
    /// </summary>
    [Column("discoverable")]
    public bool Discoverable { get; set; }

    /// <summary>
    /// Soft-delete flag
    /// </summary>
    [Column("is_deleted")]
    public bool IsDeleted { get; set; }
    
    /// <summary>
    /// True if you probably shouldn't be on this server at work owo
    /// </summary>
    [Column("nsfw")]
    public bool Nsfw { get; set; }
    
    [Column("version")]
    public int Version { get; set; }

    // Only to fulfill contract
    [NotMapped]
    public new string NodeName { get; set; }
}
