using Valour.Sdk.ModelLogic;

namespace Valour.Sdk.Models;

/// <summary>
/// Channel members represent members of a channel that is not a planet channel
/// In direct message channels there will only be two members, but in group channels there can be more
/// </summary>
public class ChannelMember : ClientModel<ChannelMember, long>
{
    /// <summary>
    /// Id of the channel this member belongs to
    /// </summary>
    public long ChannelId { get; set; }
    
    /// <summary>
    /// Id of the user that has this membership
    /// </summary>
    public long UserId { get; set; }

    public override ChannelMember AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return Client.Cache.ChannelMembers.Put(this, flags);
    }

    public override ChannelMember RemoveFromCache(bool skipEvents = false)
    {
        return Client.Cache.ChannelMembers.Remove(this, skipEvents);
    }
}