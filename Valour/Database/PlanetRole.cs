using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("planet_roles")]
public class PlanetRole : ISharedPlanetRole
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("PlanetId")]
    public Planet Planet { get; set; }
    
    [InverseProperty("Role")]
    public virtual ICollection<PermissionsNode> PermissionNodes { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Key]
    [Column("id")]
    public long Id { get; set; }
    
    
    [Column("is_owner")]
    public bool IsOwner { get; set; }
    
    /// <summary>
    /// True if this is an admin role - meaning that it overrides all permissions
    /// </summary>
    [Column("is_admin")]
    public bool IsAdmin { get; set; }

    /// <summary>
    /// The id of the planet this belongs to
    /// </summary>
    [Column("planet_id")]
    public long PlanetId { get; set; }

    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    [Column("position")]
    public uint Position { get; set; }
    
    /// <summary>
    /// True if this is the default (everyone) role
    /// </summary>
    [Column("is_default")]
    public bool IsDefault { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    [Column("permissions")]
    public long Permissions { get; set; }

    /// <summary>
    /// The chat channel permissions for the role
    /// </summary>
    [Column("chat_perms")]
    public long ChatPermissions { get; set; }

    /// <summary>
    /// The category permissions for the role
    /// </summary>
    [Column("cat_perms")]
    public long CategoryPermissions { get; set; }

    /// <summary>
    /// The voice channel permissions for the role
    /// </summary>
    [Column("voice_perms")]
    public long VoicePermissions { get; set; }

    /// <summary>
    /// The hex color for the role
    /// </summary>
    [Column("color")]
    public string Color { get; set; }

    // Formatting options
    [Column("bold")]
    public bool Bold { get; set; }

    [Column("italics")]
    public bool Italics { get; set; }

    [Column("name")]
    public string Name { get; set; }
    
    [Column("anyone_can_mention")]
    public bool AnyoneCanMention { get; set; }

    public uint GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);
}