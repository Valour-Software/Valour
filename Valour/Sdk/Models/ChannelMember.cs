using Valour.Sdk.Client;
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

    public bool IsAdmin { get; set; }
    
    private ChannelMember() {}
    public ChannelMember(ValourClient client) : base(client) { }

    protected override ModelStore<ChannelMember, long> CacheStore => Client.Cache.ChannelMembers;
}
