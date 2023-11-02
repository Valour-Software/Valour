using System.ComponentModel.DataAnnotations.Schema;

namespace Valour.Database;

/// <summary>
/// Channel members represent members of a channel that is not a planet channel
/// In direct message channels there will only be two members, but in group channels there can be more
/// </summary>
[Table("channel_members")]
public class ChannelMember
{
    ///////////////////////////
    // Relational Properties //
    ///////////////////////////
    
    [ForeignKey("ChannelId")]
    public virtual Channel Channel { get; set; }
    
    [ForeignKey("UserId")]
    public virtual User User { get; set; }
    
    ///////////////////////
    // Entity Properties //
    ///////////////////////
    
    /// <summary>
    /// Id of the member
    /// </summary>
    [Column("id")]
    public long Id { get; set; }
    
    /// <summary>
    /// Id of the channel this member belongs to
    /// </summary>
    [Column("channel_id")]
    public long ChannelId { get; set; }
    
    /// <summary>
    /// Id of the user that has this membership
    /// </summary>
    [Column("user_id")]
    public long UserId { get; set; }
}