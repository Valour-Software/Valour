using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Messaging;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Messaging;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Controls all routes for channels
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class ChannelController
    {

        public static List<PlanetMessage> messageCache = new List<PlanetMessage>();

        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;

        // Dependency injection
        public ChannelController(ValourDB context)
        {
            this.Context = context;
        }

        [HttpGet]
        public IEnumerable<PlanetMessage> GetMessages(ulong channel_id)
        {
            Console.WriteLine(channel_id);

            ulong channelId = 1;

            PlanetMessage welcome = new PlanetMessage()
            {
                ChannelId = channelId,
                Content = "Welcome back.",
                TimeSent = DateTime.UtcNow
            };

            messageCache.Add(welcome);

            return messageCache.TakeLast(10).ToList();
        }

        [HttpPost]
        public async Task<TaskResult<ulong>> PostMessage(PlanetMessage msg)
        {
            //ClientMessage msg = JsonConvert.DeserializeObject<ClientMessage>(json);

            if (msg == null)
            {
                return new TaskResult<ulong>(false, "Malformed message.", 0);
            }

            ulong channel_id = msg.ChannelId;

            PlanetChatChannel channel = await Context.PlanetChatChannels.FindAsync(channel_id);

            // Get index for message
            ulong index = channel.Message_Count;

            // Update message count. May have to queue this in the future to prevent concurrency issues.
            channel.Message_Count += 1;
            await Context.SaveChangesAsync();

            msg.Index = index;

            string json = JsonConvert.SerializeObject(msg);

            await MessageHub.Current.Clients.Group(channel_id.ToString()).SendAsync("Relay", json);

            return new TaskResult<ulong>(true, $"Posted message {msg.Index}.", index);
        }
    }
}
