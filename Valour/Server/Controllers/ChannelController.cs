using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Messages;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using System.Text.RegularExpressions;
using System.Collections.Specialized;
using Valour.Server.Messages;
using Microsoft.EntityFrameworkCore;

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
        
        public async Task<TaskResult> SetDescription(string description, ulong id, ulong userid, string token)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            PlanetChatChannel channel = await Context.PlanetChatChannels.Where(x => x.Id == id).FirstOrDefaultAsync();

            channel.Description = description;

            await Context.SaveChangesAsync();

            return new TaskResult(true, "Successfully set description.");
        }

        public async Task<TaskResult> SetName(string name, ulong id, ulong userid, string token)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            PlanetChatChannel channel = await Context.PlanetChatChannels.Where(x => x.Id == id).FirstOrDefaultAsync();

            channel.Name = name;

            await Context.SaveChangesAsync();

            return new TaskResult(true, "Successfully set name.");
        }

        public async Task<TaskResult> Delete(ulong id, ulong userid, string token)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            PlanetChatChannel channel = await Context.PlanetChatChannels.Where(x => x.Id == id).FirstOrDefaultAsync();

            Context.PlanetChatChannels.Remove(channel);

            await Context.SaveChangesAsync();

            return new TaskResult(true, "Successfully deleted.");
        }

        public async Task<TaskResult> SetParentId(ulong id, ushort parentId, ulong userid, string token)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            PlanetChatChannel channel = await Context.PlanetChatChannels.Where(x => x.Id == id).FirstOrDefaultAsync();

            channel.Parent_Id = parentId;

            await Context.SaveChangesAsync();
            
            return new TaskResult(true, "Successfully set parentid.");
        }

        /// <summary>
        /// Creates a server and if successful returns a task result with the created
        /// planet's id
        /// </summary>
        public async Task<TaskResult<ulong>> CreateChannel(string name, ulong planet_id, ulong userid, ulong parentid, string token)
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

            // Creates the channel channel

            PlanetChatChannel channel = new PlanetChatChannel()
            {
                Name = name,
                Planet_Id = planet_id,
                Parent_Id = parentid,
                Message_Count = 0,
                Description = "A chat channel",
                Position = Convert.ToUInt16(Context.PlanetChatChannels.Count())
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
        
        public async Task<TaskResult<IEnumerable<ulong>>> GetChannelIdsAsync(ulong planet_id)
        {
            IEnumerable<ulong> channels = await Task.Run(() => Context.PlanetChatChannels.Where(c => c.Planet_Id == planet_id).Select(c => c.Id).ToList());

            return new TaskResult<IEnumerable<ulong>>(true, "Successfully retireved channels.", channels);;
        }

        [HttpGet]
        public async Task<TaskResult<IEnumerable<PlanetChatChannel>>> GetPlanetChannelsAsync(ulong planet_id)
        {
            IEnumerable<PlanetChatChannel> channels = await Task.Run(() => Context.PlanetChatChannels.Where(c => c.Planet_Id == planet_id).ToList());

            return new TaskResult<IEnumerable<PlanetChatChannel>>(true, "Successfully retireved channels.", channels);
        }

        [HttpGet]
        public async Task<IEnumerable<PlanetMessage>> GetMessages(ulong channel_id, ulong index = ulong.MaxValue, int count = 10)
        {
            // Prevent requesting a ridiculous amount of messages
            if (count > 64)
            {
                count = 64;
            }

            IEnumerable<PlanetMessage> messages = await Task.Run(() =>
            Context.PlanetMessages.Where(x => x.Channel_Id == channel_id && x.Message_Index < index)
                                  .OrderByDescending(x => x.Message_Index)
                                  .Take(count)
                                  .Reverse()
                                  .ToList());

            return messages;
        }

        [HttpGet]
        public async Task<IEnumerable<PlanetMessage>> GetLastMessages(ulong channel_id, int count = 10)
        {
            // Prevent requesting a ridiculous amount of messages
            if (count > 64)
            {
                count = 64;
            }

            IEnumerable<PlanetMessage> messages = await Task.Run(() => 
            Context.PlanetMessages.Where(x => x.Channel_Id == channel_id)
                                  .OrderByDescending(x => x.Message_Index)
                                  .Take(count)
                                  .Reverse()
                                  .ToList());

            return messages;
        }

        [HttpPost]
        public async Task<TaskResult<ulong>> PostMessage(PlanetMessage msg)
        {
            //ClientMessage msg = JsonConvert.DeserializeObject<ClientMessage>(json);

            if (msg == null)
            {
                return new TaskResult<ulong>(false, "Malformed message.", 0);
            }

            ulong channel_id = msg.Channel_Id;

            PlanetChatChannel channel = await Context.PlanetChatChannels.FindAsync(channel_id);

            // Get index for message
            ulong index = channel.Message_Count;

            // Update message count. May have to queue this in the future to prevent concurrency issues.
            channel.Message_Count += 1;
            await Context.SaveChangesAsync();

            msg.Message_Index = index;

            string json = JsonConvert.SerializeObject(msg);

            await MessageHub.Current.Clients.Group(channel_id.ToString()).SendAsync("Relay", json);

            await Context.PlanetMessages.AddAsync(msg);

            await Context.SaveChangesAsync();
            
            return new TaskResult<ulong>(true, $"Posted message {msg.Message_Index}.", index);
        }
    }
}
