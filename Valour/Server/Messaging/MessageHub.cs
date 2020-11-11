using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Valour.Server.Messaging
{
    public class MessageHub : Hub
    {
        public const string HubUrl = "/messagehub";

        //public async Task JoinChannel()

        public static IHubContext<MessageHub> Current;

        public async Task JoinChannel(ulong channel_id)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, channel_id.ToString());
        }

        public async Task LeaveChannel(ulong channel_id)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, channel_id.ToString());
        }
    }
}
