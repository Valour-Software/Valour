namespace Valour.Shared.Models;

public enum NotificationDeviceType
{
    WebPush,
}

public interface ISharedPushNotificationSubscription
{
    /// <summary>
    /// The Id of the subscription
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// The type of device this subscription is for
    /// </summary>
    public NotificationDeviceType DeviceType { get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    public long UserId { get; set; }
    
    /// <summary>
    /// The Id of the planet this subscription is for
    /// </summary>
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The Id of the member this subscription is for, if a planet subscription.
    /// </summary>
    public long? MemberId { get; set; }
    
    /// <summary>
    /// The RoleHashKey the planet member is subscribed to. Should only be used for planet subscriptions.
    /// </summary>
    public long? RoleHashKey { get; set; }
    
    /// <summary>
    /// The endpoint the notification is sent to
    /// </summary>
    public string Endpoint { get; set; }
    
    public string Key { get; set; }
    public string Auth { get; set; }
    
    /// <summary>
    /// When this subscription expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

}
