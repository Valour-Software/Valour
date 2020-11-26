using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Messaging;
using Valour.Shared;
using Valour.Shared.Messaging;

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Controls all routes for channels
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class ChannelController
    {

        public static List<ClientPlanetMessage> messageCache = new List<ClientPlanetMessage>();

        // TODO: Make this DB-based and not bad
        public static Dictionary<ulong, ulong> channelIndexes = new Dictionary<ulong, ulong>();

        [HttpGet]
        public IEnumerable<ClientPlanetMessage> GetMessages(ulong channel_id)
        {
            Console.WriteLine(channel_id);

            ulong channelId = 1;

            ClientPlanetMessage welcome = new ClientPlanetMessage()
            {
                ChannelId = channelId,
                Content = "Welcome back."
            };

            messageCache.Add(welcome);

            return messageCache.TakeLast(10).ToList();
        }

        [HttpPost]
        public async Task<MessagePostResponse> PostMessage(ClientPlanetMessage msg)
        {
            //ClientMessage msg = JsonConvert.DeserializeObject<ClientMessage>(json);

            if (msg == null)
            {
                return new MessagePostResponse(false, "Malformed message.", 0);
            }

            ulong channel_id = msg.ChannelId;

            if (channelIndexes.ContainsKey(channel_id))
            {
                channelIndexes[channel_id] += 1;
            }
            else
            {
                channelIndexes.Add(channel_id, 0);
            }

            // Get index for message
            ulong index = channelIndexes[channel_id];
            msg.Index = index;

            await MessageHub.Current.Clients.Group(channel_id.ToString()).SendAsync("Relay", msg.Content);

            return new MessagePostResponse(true, "Test", index);
        }
    }
}
