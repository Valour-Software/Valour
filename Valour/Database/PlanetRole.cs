using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
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
    
    [InverseProperty("Role")]
    public virtual ICollection<PlanetRoleMember> Roles { get; set; }

    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    [Key]
    [Column("id")]
    public long Id { get; set; }
    
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


    public static void SetUpDbModel(ModelBuilder builder)
    {
        builder.Entity<PlanetRole>(e =>
        {
            // ToTable
            e.ToTable("planet_roles");
            
            // Key
            
            e.HasKey(x => x.Id);
            
            // Properties

            e.Property(x => x.Id)
                .HasColumnName("id");
            
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
                .HasColumnName("category_perms");
            
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
            
            // Relationships

            e.HasOne(x => x.Planet)
                .WithMany(x => x.Roles)
                .HasForeignKey(x => x.PlanetId);
            
            // Indices

            e.HasIndex(x => x.Position);
            
            e.HasIndex(x => x.IsDefault);
            
            e.HasIndex(x => x.Permissions);
            
            e.HasIndex(x => x.ChatPermissions);
            
            e.HasIndex(x => x.CategoryPermissions);
            
            e.HasIndex(x => x.VoicePermissions);
            
            e.HasIndex(x => x.Color);
            
            e.HasIndex(x => x.Bold);
            
            e.HasIndex(x => x.Italics);
            
            e.HasIndex(x => x.PlanetId);
            



        });
    }
}