using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Models;

namespace Valour.Database;

[Table("channels")]
public class Channel : Item, ISharedChannel
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("PlanetId")]
    public virtual Planet Planet { get; set; }
    
    [ForeignKey("ParentId")]
    public virtual Channel Parent { get; set; }
    
    [InverseProperty("Channel")]
    public virtual List<ChannelMember> Members { get; set; }
    
    [InverseProperty("Target")]
    public virtual List<PermissionsNode> Permissions { get; set; }
    
    [InverseProperty("Channel")]
    public virtual List<Message> Messages { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    /// <summary>
    /// The name of the channel
    /// </summary>
    [Column("name")]
    public string Name { get; set; }
    
    /// <summary>
    /// The description of the channel
    /// </summary>
    [Column("description")]
    public string Description { get; set; }
    
    /// <summary>
    /// The type of this channel
    /// </summary>
    [Column("channel_type")]
    public ChannelTypeEnum ChannelType { get; set; }
    
    /// <summary>
    /// The last time a message was sent (or event occured) in this channel
    /// </summary>
    [Column("last_update_time")]
    public DateTime LastUpdateTime { get; set; }
    
    /// <summary>
    /// Soft-delete flag
    /// </summary>
    [Column("is_deleted")]
    public bool IsDeleted { get; set; }
    
    /////////////////////////////
    // Only on planet channels //
    /////////////////////////////
    
    /// <summary>
    /// The id of the parent of the channel, if any
    /// </summary>
    [Column("planet_id")]
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The id of the parent of the channel, if any
    /// </summary>
    [Column("parent_id")]
    public long? ParentId { get; set; }

    /// <summary>
    /// The position of the channel in the channel list
    /// </summary>
    [Column("position")]
    public int? Position { get; set; }

    /// <summary>
    /// If this channel inherits permissions from its parent
    /// </summary>
    [Column("inherits_perms")]
    public bool? InheritsPerms { get; set; }
    
    /// <summary>
    /// True if this is the default chat channel
    /// </summary>
    [Column("is_default")]
    public bool? IsDefault { get; set; }
}
