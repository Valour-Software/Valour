using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Client.Notifications
{
    public class NotificationSubscription
    {
        public string url { get; set; }
        public string p256dh { get; set; }
        public string auth { get; set; }
    }
}
