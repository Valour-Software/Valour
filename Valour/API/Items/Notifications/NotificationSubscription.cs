using Valour.Shared.Items;
using Valour.Shared.Items.Notifications;

namespace Valour.Api.Items.Notifications;

public class NotificationSubscription : Item<NotificationSubscription>, ISharedNotificationSubscription
{
    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    public ulong User_Id { get; set; }

    public string Endpoint { get; set; }

    public string Not_Key { get; set; }

    public string Auth { get; set; }

    public override ItemType ItemType => ItemType.NotificationSubscription;
}

