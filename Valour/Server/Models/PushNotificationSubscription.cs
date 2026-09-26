using System.Text.Json.Serialization;
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

    /// <summary>
    /// The session that registered the subscription. The server sets it from
    /// the request, never from the body.
    /// </summary>
    [JsonIgnore]
    public string AuthTokenId { get; set; }
}