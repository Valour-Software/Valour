using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PushNotificationSubscription : ISharedPushNotificationSubscription
{
    public long Id { get; set; }
    public NotificationDeviceType DeviceType { get; set; }
    public DateTime ExpiresAt { get; set; }
    public long UserId { get; set; }
    public string Endpoint { get; set; }
    public string Key { get; set; }
    public string Auth { get; set; }
}