using Valour.Shared.Items;
using Valour.Shared.Items.Notifications;

namespace Valour.Api.Items.Notifications;

public class NotificationSubscription : ISharedNotificationSubscription
{
    public long Id {get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    public long UserId { get; set; }
    public string Endpoint { get; set; }
    public string Not_Key { get; set; }
    public string Auth { get; set; }
}

