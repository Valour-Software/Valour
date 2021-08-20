using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Web;
using Valour.Client.Messages;
using Valour.Client.Planets;
using Valour.Shared;
using Valour.Shared.Items;
using Valour.Shared.Roles;

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
    public class ClientPlanetChatChannel : IPlanetChatChannel, IClientNamedItem, IClientPlanetListItem
    {
        /// <summary>
        /// The id of the channel
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// The name of the channel
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The amount of messages ever sent in the channel
        /// </summary>
        public ulong Message_Count { get; set; }

        /// <summary>
        /// If true, this channel will inherit the permission nodes
        /// from the category it belongs to
        /// </summary>
        public bool Inherits_Perms { get; set; }

        /// <summary>
        /// The position of the channel
        /// </summary>
        public ushort Position { get; set; }

        /// <summary>
        /// The id of the parent category
        /// </summary>
        public ulong? Parent_Id { get; set; }

        /// <summary>
        /// The id of the planet
        /// </summary>
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The description of the channel
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The type of this item
        /// </summary>
        public ItemType ItemType => ItemType.Channel;

        public static async Task<ClientPlanetChatChannel> GetAsync(ulong id)
        {
            var response = await ClientUserManager.Http.GetAsync($"api/channel/{id}");
            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to get channel {id}");
                Console.WriteLine(message);
                return null;
            }

            ClientPlanetChatChannel channel = JsonConvert.DeserializeObject<ClientPlanetChatChannel>(message);

            return channel;
        }

        /// <summary>
        /// Attempts to delete this channel
        /// </summary>
        public async Task<TaskResult> TryDeleteAsync()
        {
            var response = await ClientUserManager.Http.DeleteAsync($"api/channel/{Id}");

            return new TaskResult(
                response.IsSuccessStatusCode,
                await response.Content.ReadAsStringAsync()
            );
        }

        public async Task<TaskResult> DeleteAsync()
        {
            var response = await ClientUserManager.Http.DeleteAsync($"api/channel/{Id}");
            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to delete channel {Id}");
                Console.WriteLine(message);
            }

            return new TaskResult(response.IsSuccessStatusCode, message);
        }

        /// <summary>
        /// Returns the planet this chat channel belongs to
        /// </summary>
        public async Task<ClientPlanet> GetPlanetAsync()
        {
            return await ClientPlanetManager.Current.GetPlanetAsync(Planet_Id);
        }

        /// <summary>
        /// Attempts to set the description of the channel and returns the result
        /// </summary>
        public async Task<TaskResult> TrySetDescriptionAsync(string desc)
        {
            string encodedDesc = HttpUtility.UrlEncode(desc);

            JsonContent content = JsonContent.Create(encodedDesc);
            var response = await ClientUserManager.Http.PutAsync($"api/channel/{Id}/description", content);

            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to set description");
                Console.WriteLine(message);
            }
            else
            {
                this.Description = desc;
            }

            return new TaskResult(response.IsSuccessStatusCode, message);
        }
        public string GetItemTypeName()
        {
            return "Chat Channel";
        }

        /// <summary>
        /// Sets whether or not the permissions should be inherited from the category
        /// </summary>
        public async Task<TaskResult> SetPermissionInheritModeAsync(bool value)
        {
            JsonContent content = JsonContent.Create(value);
            var response = await ClientUserManager.Http.PutAsync($"api/channel/{Id}/inherits_perms", content);

            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to set inheritance");
                Console.WriteLine(message);
            }
            else
            {
                this.Inherits_Perms = value;
            }

            return new TaskResult(response.IsSuccessStatusCode, message);
        }

        /// <summary>
        /// Sets the parent id of this channel
        /// </summary>
        public async Task<TaskResult> TrySetParentIdAsync(ulong parent_id)
        {
            JsonContent content = JsonContent.Create(parent_id);
            var response = await ClientUserManager.Http.PutAsync($"api/channel/{Id}/parent_id", content);

            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to set parent_id for channel {Id}");
                Console.WriteLine(message);
            }
            else
            {
                this.Parent_Id = parent_id;
            }

            return new TaskResult(response.IsSuccessStatusCode, message);
        }

        public async Task<PermissionsNode> GetPermissionsNodeAsync(PlanetRole role)
        {
            return await GetChatChannelPermissionsNodeAsync(role);
        }

        public async Task<ChatChannelPermissionsNode> GetChatChannelPermissionsNodeAsync(PlanetRole role)
        {

            string json = await ClientUserManager.Http.GetStringAsync($"Permissions/GetChatChannelNode?channel_id={Id}" +
                                                                                                    $"&role_id={role.Id}" +
                                                                                                    $"&token={ClientUserManager.UserSecretToken}");
            
            TaskResult<ChatChannelPermissionsNode> result = JsonConvert.DeserializeObject<TaskResult<ChatChannelPermissionsNode>>(json);

            if (result == null)
            {
                Console.WriteLine("Failed to deserialize result from GetPermissionsNode in channel");
                return null;
            }

            if (!result.Success)
            {
                Console.WriteLine("Permissions/GetChatChannelNode failed.");
                Console.WriteLine(result.Message);
                return null;
            }

            // Return the node - it may be null, but that's ok
            return result.Data;
        }

        /// <summary>
        /// Returns messages from the channel
        /// </summary>
        /// <param name="index">The starting index for the messages</param>
        /// <param name="count">The amount of messages to return</param>
        /// <returns>An enumerable list of planet messages</returns>
        public async Task<List<ClientPlanetMessage>> GetMessagesAsync(ulong index = ulong.MaxValue, int count = 10)
        {
            string json = await ClientUserManager.Http.GetStringAsync($"api/channel/{Id}/messages?index={index}&count={count}");

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
            string json = await ClientUserManager.Http.GetStringAsync($"api/channel/{Id}/messages?count={count}");

            List<ClientPlanetMessage> messages = JsonConvert.DeserializeObject<List<ClientPlanetMessage>>(json);

            if (messages == null)
            {
                Console.WriteLine("Failed to deserialize messages from GetLastMessages");
            }

            return messages;
        }

        public Task<TaskResult> TrySetName(string name)
        {
            throw new NotImplementedException();
        }
    }
}
