namespace Valour.Database;

/// <summary>
/// MemberChannelAccess denotes a planet member's access to a channel. If a member does not have access to a channel,
/// it will not be sent in the member's channel list. This only applies to planet channels.
/// </summary>
public class MemberChannelAccess
{
    // Pkey should be composite of MemberId and ChannelId
    
    public virtual PlanetMember Member { get; set; }
    public virtual Channel Channel { get; set; }
    public virtual Planet Planet { get; set; }

    /// <summary>
    /// ID of the member who has access to the channel
    /// </summary>
    public long MemberId { get; set; }
    
    /// <summary>
    /// ID of the channel the member has access to
    /// </summary>
    public long ChannelId { get; set; }
    
    /// <summary>
    /// The ID of the planet the target channel belongs to
    /// </summary>
    public long PlanetId { get; set; }
}