using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Models;

namespace Valour.Database;

public class Channel : ISharedChannel
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    public virtual Planet Planet { get; set; }
    public virtual Channel Parent { get; set; }
    public virtual List<ChannelMember> Members { get; set; }
    public virtual List<PermissionsNode> Permissions { get; set; }
    public virtual List<Message> Messages { get; set; }
    public virtual ICollection<Channel> Children { get; set; }
    public virtual ICollection<UserChannelState> UserChannelStates { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    public long Id { get; set; }
    
    /// <summary>
    /// The name of the channel
    /// </summary>
    public string Name { get; set; }
    
    /// <summary>
    /// The description of the channel
    /// </summary>
    public string Description { get; set; }
    
    /// <summary>
    /// The type of this channel
    /// </summary>
    public ChannelTypeEnum ChannelType { get; set; }
    
    /// <summary>
    /// The last time a message was sent (or event occured) in this channel
    /// </summary>
    public DateTime LastUpdateTime { get; set; }
    
    /// <summary>
    /// Soft-delete flag
    /// </summary>
    public bool IsDeleted { get; set; }
    
    /////////////////////////////
    // Only on planet channels //
    /////////////////////////////
    
    /// <summary>
    /// The id of the parent of the channel, if any
    /// </summary>
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The id of the parent of the channel, if any
    /// </summary>
    public long? ParentId { get; set; }

    /// <summary>
    /// The position of the channel in the channel list
    /// </summary>
    public uint RawPosition { get; set; }

    /// <summary>
    /// If this channel inherits permissions from its parent
    /// </summary>
    public bool InheritsPerms { get; set; }
    
    /// <summary>
    /// True if this is the default chat channel
    /// </summary>
    public bool IsDefault { get; set; }
    
    // Used for migrations
    public int Version { get; set; }

    public static void SetupDbModel(ModelBuilder builder)
    {
        builder.Entity<Channel>(e =>
        {
            // Table
            e.ToTable("channels");

            // Keys
            e.HasKey(x => x.Id);

            // Properties
            e.Property(x => x.Id)
                .HasColumnName("id");
            
            e.Property(x => x.Name)
                .HasColumnName("name");
            
            e.Property(x => x.Description)
                .HasColumnName("description");
            
            e.Property(x => x.ChannelType)
                .HasColumnName("channel_type");
            
            e.Property(x => x.LastUpdateTime)
                .HasColumnName("last_update_time")
                .HasConversion(
                    x => x,
                    x => new DateTime(x.Ticks, DateTimeKind.Utc)
                );
            
            e.Property(x => x.IsDeleted)
                .HasColumnName("is_deleted");
            
            e.Property(x => x.PlanetId)
                .HasColumnName("planet_id");
            
            e.Property(x => x.ParentId)
                .HasColumnName("parent_id");
            
            e.Property(x => x.RawPosition)
                .HasColumnName("position");
            
            e.Property(x => x.InheritsPerms)
                .HasColumnName("inherits_perms");
            
            e.Property(x => x.IsDefault)
                .HasColumnName("is_default");
            
            e.Property(x => x.Version)
                .HasColumnName("version");
            
            // Relationships
            e.HasOne(x => x.Planet)
                .WithMany(x => x.Channels)
                .HasForeignKey(x => x.PlanetId);
            
            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId);
            
            e.HasMany(x => x.Messages)
                .WithOne(x => x.Channel)
                .HasForeignKey(x => x.ChannelId);
            
            e.HasMany(x => x.Permissions)
                .WithOne(x => x.Target)
                .HasForeignKey(x => x.TargetId);

            e.HasMany(x => x.Members)
                .WithOne(x => x.Channel)
                .HasForeignKey(x => x.ChannelId);
            
            e.HasMany(x => x.UserChannelStates)
                .WithOne(x => x.Channel)
                .HasForeignKey(x => x.ChannelId);

            // Indices
            e.HasIndex(x => x.PlanetId);
            e.HasIndex(x => x.ParentId);
            e.HasIndex(x => x.RawPosition);
            e.HasIndex(x => x.IsDeleted);
        });
    }
}
