using Microsoft.AspNetCore.SignalR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Messages
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
