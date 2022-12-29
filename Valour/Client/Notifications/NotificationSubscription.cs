using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Shared.Models;

namespace Valour.Client.Notifications;

public class NotificationSubscription : ISharedNotificationSubscription
{
    public long Id { get; set; }

    /// <summary>
    /// The Id of the user this subscription is for
    /// </summary>
    public long UserId { get; set; }
    public string Endpoint { get; set; }
    public string Key { get; set; }
    public string Auth { get; set; }
}
