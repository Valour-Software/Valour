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
using Valour.Shared.Messages;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using System.Text.RegularExpressions;

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
        
        /// <summary>
        /// Creates a server and if successful returns a task result with the created
        /// planet's id
        /// </summary>
        public async Task<TaskResult<ulong>> CreateChannel(string name, ulong userid, string token, string planetid)
        {
            TaskResult nameValid = ValidateName(name);

            if (!nameValid.Success)
            {
                return new TaskResult<ulong>(false, nameValid.Message, 0);
            }

            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult<ulong>(false, "Failed to authorize user.", 0);
            }

            // User is verified and given channel info is valid by this point

            // Converts planetid into a ulong
            
            ulong planetId = Convert.ToUInt64(planetid);

            // Creates the channel channel

            PlanetChatChannel channel = new PlanetChatChannel()
            {
                Name = name,
                Planet_Id = planetId,
                Message_Count = 0
            };

            // Add channel to database
            await Context.PlanetChatChannels.AddAsync(channel);

            // Save changes to DB
            await Context.SaveChangesAsync();

            // Return success
            return new TaskResult<ulong>(true, "Successfully created channel.", channel.Id);
        }

        public Regex planetRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

        /// <summary>
        /// Validates that a given name is allowable for a server
        /// </summary>
        public TaskResult ValidateName(string name)
        {
            if (name.Length > 32)
            {
                return new TaskResult(false, "Planet names must be 32 characters or less.");
            }

            if (!planetRegex.IsMatch(name))
            {
                return new TaskResult(false, "Planet names may only include letters, numbers, dashes, and underscores.");
            }

            return new TaskResult(true, "The given name is valid.");
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
