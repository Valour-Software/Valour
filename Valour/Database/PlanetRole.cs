using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Database;

public class PlanetRole : ISharedPlanetRole
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////

    public Planet Planet { get; set; }
    public virtual ICollection<PermissionsNode> PermissionNodes { get; set; }
    
    [JsonIgnore]
    [Obsolete("Use new RoleMembership!")]
    public virtual ICollection<OldPlanetRoleMember> OldRoleMembers { get; set; }

    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    /// <summary>
    /// The Id of the role
    /// </summary>
    public long Id { get; set; }
    
    /// <summary>
    /// The index of the role in the membership flags.
    /// Ex: 5 would be the 5th bit in the membership flags
    /// </summary>
    public int FlagBitIndex { get; set; }

    /// <summary>
    /// True if this is an admin role - meaning that it overrides all permissions
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// The id of the planet this belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The position of the role: Lower has more authority
    /// </summary>
    public int Position { get; set; }

    /// <summary>
    /// True if this is the default (everyone) role
    /// </summary>
    public bool IsDefault { get; set; }

    /// <summary>
    /// The planet permissions for the role
    /// </summary>
    public long Permissions { get; set; }

    /// <summary>
    /// The chat channel permissions for the role
    /// </summary>
    public long ChatPermissions { get; set; }

    /// <summary>
    /// The category permissions for the role
    /// </summary>
    public long CategoryPermissions { get; set; }

    /// <summary>
    /// The voice channel permissions for the role
    /// </summary>
    public long VoicePermissions { get; set; }

    /// <summary>
    /// The hex color for the role
    /// </summary>
    public string Color { get; set; }

    // Formatting options
    public bool Bold { get; set; }

    public bool Italics { get; set; }

    public string Name { get; set; }

    public bool AnyoneCanMention { get; set; }
    
    // Used for migrations
    public int Version { get; set; }

    public uint GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);

    public static void SetupDbModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlanetRole>(e =>
        {
            e.ToTable("planet_roles");

            // Key
            e.HasKey(x => x.Id);

            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.FlagBitIndex)
                .HasColumnName("local_index");

            e.Property(x => x.IsAdmin)
                .HasColumnName("is_admin");

            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            e.Property(x => x.Position)
                .HasColumnName("position");

            e.Property(x => x.IsDefault)
                .HasColumnName("is_default");

            e.Property(x => x.Permissions)
                .HasColumnName("permissions");

            e.Property(x => x.ChatPermissions)
                .HasColumnName("chat_perms");

            e.Property(x => x.CategoryPermissions)
                .HasColumnName("cat_perms");

            e.Property(x => x.VoicePermissions)
                .HasColumnName("voice_perms");

            e.Property(x => x.Color)
                .HasColumnName("color");

            e.Property(x => x.Bold)
                .HasColumnName("bold");

            e.Property(x => x.Italics)
                .HasColumnName("italics");

            e.Property(x => x.Name)
                .HasColumnName("name");

            e.Property(x => x.AnyoneCanMention)
                .HasColumnName("anyone_can_mention");
            
            e.Property(x => x.Version)
                .HasColumnName("version");

            // Relationships
            e.HasOne(x => x.Planet)
                .WithMany(x => x.Roles)
                .HasForeignKey(x => x.PlanetId);

            e.HasMany(x => x.PermissionNodes)
                .WithOne(x => x.Role)
                .HasForeignKey(x => x.RoleId);
            
            // Indices
            e.HasIndex(x => x.PlanetId);
            
            e.HasIndex(x => new { x.PlanetId, x.Id })
                .IsUnique();
        });
    }
}
