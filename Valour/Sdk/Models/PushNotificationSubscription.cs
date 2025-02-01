using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PushNotificationSubscription : ISharedPushNotificationSubscription
{
    public long Id { get; set; }

    public NotificationDeviceType DeviceType { get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    public long UserId { get; set; }
    public long? PlanetId { get; set; }
    public long? MemberId { get; set; }
    public long? RoleHashKey { get; set; }
    public string Endpoint { get; set; }
    public string Key { get; set; }
    public string Auth { get; set; }
    public DateTime ExpiresAt { get; set; }
}

