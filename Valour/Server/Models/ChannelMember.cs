namespace Valour.Server.Models;

/// <summary>
/// Channel members represent members of a channel that is not a planet channel
/// In direct message channels there will only be two members, but in group channels there can be more
/// </summary>
public class ChannelMember : ServerModel<long>
{
    /// <summary>
    /// Id of the channel this member belongs to
    /// </summary>
    public long ChannelId { get; set; }
    
    /// <summary>
    /// Id of the user that has this membership
    /// </summary>
    public long UserId { get; set; }
}