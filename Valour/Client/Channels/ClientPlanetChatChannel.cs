using AutoMapper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Web;
using Valour.Client.Messages;
using Valour.Client.Planets;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Oauth;

namespace Valour.Client.Channels
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// The clientside planet cache reduces the need to repeatedly ask the server
    /// for planet resources
    /// </summary>
    public class ClientPlanetChatChannel : PlanetChatChannel, IClientPlanetListItem
    {
        /// <summary>
        /// Converts to a client version of planet chat channel
        /// </summary>
        public static ClientPlanetChatChannel FromBase(PlanetChatChannel channel, IMapper mapper)
        {
            return mapper.Map<ClientPlanetChatChannel>(channel);
        }

        /// <summary>
        /// Returns the planet this chat channel belongs to
        /// </summary>
        public async Task<ClientPlanet> GetPlanetAsync()
        {
            return await ClientPlanetManager.Current.GetPlanetAsync(Planet_Id);
        }

        /// <summary>
        /// Returns messages from the channel
        /// </summary>
        /// <param name="index">The starting index for the messages</param>
        /// <param name="count">The amount of messages to return</param>
        /// <returns>An enumerable list of planet messages</returns>
        public async Task<List<ClientPlanetMessage>> GetMessagesAsync(ulong index = ulong.MaxValue, int count = 10)
        {
            string json = await ClientUserManager.Http.GetStringAsync($"Channel/GetMessages?channel_id={Id}" +
                                                                                           $"&index={index}" +
                                                                                           $"&count={count}");

            List<ClientPlanetMessage> messages = JsonConvert.DeserializeObject<List<ClientPlanetMessage>>(json);

            if (messages == null)
            {
                Console.WriteLine("Failed to deserialize messages from GetMessages");
            }

            return messages;
        }

        /// <summary>
        /// Returns messages from the channel
        /// </summary>
        /// <param name="index">The starting index for the messages</param>
        /// <param name="count">The amount of messages to return</param>
        /// <returns>An enumerable list of planet messages</returns>
        public async Task<List<ClientPlanetMessage>> GetLastMessagesAsync(int count = 10)
        {
            string json = await ClientUserManager.Http.GetStringAsync($"Channel/GetLastMessages?channel_id={Id}" +
                                                                                             $"&count={count}");

            List<ClientPlanetMessage> messages = JsonConvert.DeserializeObject<List<ClientPlanetMessage>>(json);

            if (messages == null)
            {
                Console.WriteLine("Failed to deserialize messages from GetLastMessages");
            }

            return messages;
        }

        /// <summary>
        /// Attempts to set the name of the channel and returns the result
        /// </summary>
        public async Task<TaskResult> SetNameAsync(string name)
        {
            string encodedName = HttpUtility.UrlEncode(name);

            string json = await ClientUserManager.Http.GetStringAsync($"Channel/SetName?channel_id={Id}" +
                                                                                     $"&name={encodedName}" +
                                                                                     $"&token={ClientUserManager.UserSecretToken}");

            TaskResult result = JsonConvert.DeserializeObject<TaskResult>(json);

            if (result == null)
            {
                Console.WriteLine("Failed to deserialize result from SetName in channel");
            }

            if (result.Success)
            {
                this.Name = name;
            }

            return result;
        }

        /// <summary>
        /// Attempts to set the description of the channel and returns the result
        /// </summary>
        public async Task<TaskResult> SetDescriptionAsync(string desc)
        {
            string encodedDesc = HttpUtility.UrlEncode(desc);

            string json = await ClientUserManager.Http.GetStringAsync($"Channel/SetDescription?channel_id={Id}" +
                                                                                            $"&description={encodedDesc}" +
                                                                                            $"&token={ClientUserManager.UserSecretToken}");

            TaskResult result = JsonConvert.DeserializeObject<TaskResult>(json);

            if (result == null)
            {
                Console.WriteLine("Failed to deserialize result from SetDescription in channel");
            }

            if (result.Success)
            {
                this.Description = desc;
            }

            return result;
        }
        public string GetItemTypeName()
        {
            return "Chat Channel";
        }

        /// <summary>
        /// Sets whether or not the permissions should be inherited from the category
        /// </summary>
        public async Task<TaskResult> SetPermissionInheritMode(bool value)
        {
            string json = await ClientUserManager.Http.GetStringAsync($"Channel/SetPermissionInheritMode?channel_id={Id}" +
                                                                                                      $"&value={value}" +
                                                                                                      $"&token={ClientUserManager.UserSecretToken}");

            TaskResult result = JsonConvert.DeserializeObject<TaskResult>(json);

            if (result == null)
            {
                Console.WriteLine("Failed to deserialize result from SetPermissionInheritMode in channel");
            }

            if (result.Success)
            {
                this.Inherits_Perms = value;
            }

            return result;
        }
    }
}
