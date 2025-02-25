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
    public uint Position { get; set; }

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

    public uint GetAuthority() =>
        ISharedPlanetRole.GetAuthority(this);

    public bool HasPermission(PlanetPermission perm) =>
        ISharedPlanetRole.HasPermission(this, perm);

    public static void SetupDbModel(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlanetRole>(entity =>
        {
            entity.ToTable("planet_roles");

            // Key
            entity.HasKey(x => x.Id);

            // Properties
            entity.Property(x => x.Id)
                .HasColumnName("id");
            
            entity.Property(x => x.FlagBitIndex)
                .HasColumnName("local_index");

            entity.Property(x => x.IsAdmin)
                .HasColumnName("is_admin");

            entity.Property(x => x.PlanetId)
                .HasColumnName("planet_id");

            entity.Property(x => x.Position)
                .HasColumnName("position");

            entity.Property(x => x.IsDefault)
                .HasColumnName("is_default");

            entity.Property(x => x.Permissions)
                .HasColumnName("permissions");

            entity.Property(x => x.ChatPermissions)
                .HasColumnName("chat_perms");

            entity.Property(x => x.CategoryPermissions)
                .HasColumnName("cat_perms");

            entity.Property(x => x.VoicePermissions)
                .HasColumnName("voice_perms");

            entity.Property(x => x.Color)
                .HasColumnName("color");

            entity.Property(x => x.Bold)
                .HasColumnName("bold");

            entity.Property(x => x.Italics)
                .HasColumnName("italics");

            entity.Property(x => x.Name)
                .HasColumnName("name");

            entity.Property(x => x.AnyoneCanMention)
                .HasColumnName("anyone_can_mention");

            // Relationships
            entity.HasOne(x => x.Planet)
                .WithMany(x => x.Roles)
                .HasForeignKey(x => x.PlanetId);

            entity.HasMany(x => x.PermissionNodes)
                .WithOne(x => x.Role)
                .HasForeignKey(x => x.RoleId);
            
            // Indices
            entity.HasIndex(x => x.PlanetId);
            
            entity.HasIndex(x => new { x.PlanetId, x.Id })
                .IsUnique();
        });
    }
}
