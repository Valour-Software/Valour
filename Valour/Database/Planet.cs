using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items.Planets;

namespace Valour.Database;

[Table("planets")]
public class Planet : Item, ISharedPlanet
{
    [InverseProperty("Planet")]
    public virtual ICollection<PlanetRole> Roles { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetMember> Members { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetChatChannel> ChatChannels { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetCategory> Categories { get; set; }

    [InverseProperty("Planet")]
    public virtual ICollection<PlanetInvite> Invites { get; set; }

    [ForeignKey("DefaultRoleId")]
    public virtual PlanetRole DefaultRole { get; set; }

    [ForeignKey("PrimaryChannelId")]
    public virtual PlanetChatChannel PrimaryChannel { get; set; }

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
    /// The image url for the planet 
    /// </summary>
    [Column("icon_url")]
    public string IconUrl { get; set; }

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
    /// The default role for the planet
    /// </summary>
    [Column("default_role_id")]
    public long DefaultRoleId { get; set; }

    /// <summary>
    /// The id of the main channel of the planet
    /// </summary>
    [Column("primary_channel_id")]
    public long PrimaryChannelId { get; set; }
    
    /// <summary>
    /// Soft-delete flag
    /// </summary>
    [Column("is_deleted")]
    public bool IsDeleted { get; set; }
}
