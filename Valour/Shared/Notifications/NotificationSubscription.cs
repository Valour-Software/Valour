using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Shared.Notifications
{
    public class NotificationSubscription
    {
        /// <summary>
        /// The Id of this subscription
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// The Id of the user this subscription is for
        /// </summary>
        public ulong User_Id { get; set; }

        public string Endpoint { get; set; }

        public string Not_Key { get; set; }

        public string Auth { get; set; }
    }
}
