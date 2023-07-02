namespace Valour.Shared.Models;

public enum NotificationSource
{
    // Chat
    Platform =               0x01, // Reserved for platform-wide notifications
    DirectMention =          0x02,
    DirectReply =            0x04,
    PlanetMemberMention =    0x08,
    PlanetMemberReply =      0x10,
    PlanetRoleMention =      0x20,
    PlanetHereMention =      0x40,
    PlanetEveryoneMention =  0x80,
    FriendRequest =          0x100,
    FriendRequestAccepted =  0x200,
    
    // Economy
    TransactionReceived =    0x400,
    TradeProposed =          0x800,
    TradeAccepted =          0x1000,
    TradeDeclined =          0x2000,
}

public interface ISharedNotification
{
    /// <summary>
    /// The id of the notification
    /// </summary>
    long Id { get; set; }
    
    /// <summary>
    /// The user the notification was sent to
    /// </summary>
    long UserId { get; set; }
    
    /// <summary>
    /// The planet (if any) the notification came from
    /// </summary>
    long? PlanetId { get; set; }
    
    /// <summary>
    /// The channel (if any) the notification came from
    /// </summary>
    long? ChannelId { get; set; }
    
    /// <summary>
    /// The message (if any) the notification came from
    /// </summary>
    long? SourceId { get; set; }
    
    /// <summary>
    /// The source of the notification
    /// </summary>
    NotificationSource Source { get; set; }
}