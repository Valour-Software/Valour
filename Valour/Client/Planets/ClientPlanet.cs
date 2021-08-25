using System;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using Valour.Client.Channels;
using Valour.Shared;
using Valour.Shared.Planets;
using Valour.Shared.Roles;
using Valour.Client.Categories;
using System.Linq;
using System.Text.Json;
using System.Net.Http.Json;

namespace Valour.Client.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */
    public class ClientPlanet : Planet 
    {
        // Cached values
        private List<ulong> _channel_ids = null;
        private List<ulong> _category_ids = null;

        public async Task NotifyUpdateChannel(ClientPlanetChatChannel channel)
        {
            if (_channel_ids == null)
            {
                await LoadChannelsAsync();
            }

            // Set in cache
            ClientCache.Channels[channel.Id] = channel;

            // Re-order channels
            List<ClientPlanetChatChannel> channels = new();

            foreach (var id in _channel_ids)
            {
                channels.Add(ClientCache.Channels[id]);
            }

            _channel_ids = channels.OrderBy(x => x.Position).Select(x => x.Id).ToList();
        }

        public void NotifyDeleteChannel(ClientPlanetChatChannel channel)
        {
            _channel_ids.Remove(channel.Id);

            ClientCache.Channels.Remove(channel.Id, out _);
        }

        public async Task NotifyUpdateCategory(ClientPlanetCategory category)
        {
            if (_category_ids == null)
            {
                await LoadCategoriesAsync();
            }

            // Set in cache
            ClientCache.Categories[category.Id] = category;
            
            // Reo-order categories
            List<ClientPlanetCategory> categories = new();
            
            foreach (var id in _category_ids)
            {
                categories.Add(ClientCache.Categories[id]);
            }

            _category_ids = categories.OrderBy(x => x.Position).Select(x => x.Id).ToList();
        }

        public void NotifyDeleteCategory(ClientPlanetCategory category)
        {
            _category_ids.Remove(category.Id);

            ClientCache.Categories.Remove(category.Id, out _);
        }

        /// <summary>
        /// Returns the primary channel of the planet
        /// </summary>
        public async Task<ClientPlanetChatChannel> GetPrimaryChannelAsync()
        {
            if (_channel_ids == null)
            {
                await LoadChannelsAsync();
            }

            return ClientCache.Channels[Main_Channel_Id];
        }

        /// <summary>
        /// Retrieves and returns categories of a planet by requesting from the server
        /// </summary>
        public async Task<List<ClientPlanetCategory>> GetCategoriesAsync(bool force_refresh = false)
        {
            if (_category_ids == null || force_refresh)
            {
                await LoadCategoriesAsync();
            }

            List<ClientPlanetCategory> categories = new();

            foreach (var id in _category_ids)
            {
                categories.Add(ClientCache.Categories[id]);
            }

            return categories;
        }

        /// <summary>
        /// Requests and caches categories from the server
        /// </summary>
        public async Task LoadCategoriesAsync()
        {
            var response = await ClientUserManager.Http.GetAsync($"api/planet/{Id}/categories", HttpCompletionOption.ResponseHeadersRead);

            var message = await response.Content.ReadAsStreamAsync();  

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("A fatal error occurred retrieving a planet's categories.");
                Console.WriteLine(new StreamReader(message).ReadToEnd());
                return;
            }

            List<ClientPlanetCategory> result = await JsonSerializer.DeserializeAsync<List<ClientPlanetCategory>>(message);

            foreach (var category in result)
            {
                ClientCache.Categories[category.Id] = category;
            }
            
            _category_ids = result.OrderBy(x => x.Position).Select(x => x.Id).ToList();
        }

        /// <summary>
        /// Retrieves and returns channels of a planet by requesting from the server
        /// </summary>
        public async Task<List<ClientPlanetChatChannel>> GetChannelsAsync(bool force_refresh = false)
        {
            if (_channel_ids == null || force_refresh)
            {
                await LoadChannelsAsync();
            }

            List<ClientPlanetChatChannel> channels = new();

            foreach (var id in _channel_ids)
            {
                channels.Add(ClientCache.Channels[id]);
            }
            
            return channels;
        }

        /// <summary>
        /// Requests and caches channels from the server
        /// </summary>
        public async Task LoadChannelsAsync()
        {
            var response = await ClientUserManager.Http.GetAsync($"/api/planet/{Id}/channels", HttpCompletionOption.ResponseHeadersRead);

            var message = await response.Content.ReadAsStreamAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("A fatal error occurred retrieving planet channels from the server.");
                Console.WriteLine(new StreamReader(message).ReadToEnd());
            }

            List<ClientPlanetChatChannel> channels = await JsonSerializer.DeserializeAsync<List<ClientPlanetChatChannel>>(message);

            foreach (var channel in channels)
            {
                ClientCache.Channels[channel.Id] = channel;
            }
            
            _channel_ids = channels.OrderBy(x => x.Position).Select(x => x.Id).ToList();
        }

        /// <summary>
        /// Attempts to set the name of the planet
        /// </summary>
        public async Task<TaskResult> TrySetNameAsync(string name)
        {
            StringContent content = new(name);

            var response = await ClientUserManager.Http.PutAsync($"api/planet/{Id}/name", content);

            string message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to set planet name");
                Console.WriteLine(message);
            }
            else
            {
                Name = name;
            }

            return new TaskResult(response.IsSuccessStatusCode, message);
        }

        /// <summary>
        /// Attempts to set the description of the planet
        /// </summary>
        public async Task<TaskResult> TrySetDescriptionAsync(string description)
        {
            StringContent content = new(description);

            var response = await ClientUserManager.Http.PutAsync($"api/planet/{Id}/description", content);

            string message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to set planet description");
                Console.WriteLine(message);
            }
            else
            {
                Description = description;
            }

            return new TaskResult(response.IsSuccessStatusCode, message);
        }

        /// <summary>
        /// Attempts to set the public value of the planet
        /// </summary>
        public async Task<TaskResult> SetPublic(bool is_public)
        {
            JsonContent content = JsonContent.Create(is_public);
            
            var response = await ClientUserManager.Http.PutAsync($"api/planet/{Id}/public", content);

            string message = await response.Content.ReadAsStringAsync();
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to set planet publicity");
                Console.WriteLine(message);
            }
            else
            {
                Public = is_public;
            }

            return new TaskResult(response.IsSuccessStatusCode, message);
        }

        /// <summary>
        /// Retrieves and returns a client planet by requesting from the server
        /// </summary>
        public static async Task<ClientPlanet> GetPlanetAsync(ulong id)
        {
            var response = await ClientUserManager.Http.GetAsync($"api/planet/{id}");

            var message = await response.Content.ReadAsStreamAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("A fatal error occurred retrieving the planet.");
                Console.WriteLine(new StreamReader(message).ReadToEnd());
                return null;
            }

            return await JsonSerializer.DeserializeAsync<ClientPlanet>(message);
        }

        /// <summary>
        /// Returns every planet member
        /// </summary>
        public async Task<List<ClientPlanetMember>> GetCachedMembers()
        {
            return await ClientPlanetManager.Current.GetCachedPlanetMembers(this);

            //return await ClientPlanetManager.Current.GetPlanetMemberInfoAsync(Id);
        }

        /// <summary>
        /// Returns every planet role
        /// </summary>
        public async Task<List<PlanetRole>> GetRolesAsync()
        {
            return await ClientPlanetManager.Current.GetPlanetRoles(Id);
        }

        /// <summary>
        /// Returns the member for a given user id
        /// </summary>
        public async Task<ClientPlanetMember> GetMemberAsync(ulong user_id)
        {
            return await ClientPlanetMember.GetClientPlanetMemberAsync(user_id, Id);
        }
    }
}
