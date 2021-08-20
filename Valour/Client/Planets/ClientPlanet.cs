using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Valour.Client.Channels;
using Valour.Shared;
using Valour.Shared.Planets;
using Valour.Shared.Roles;
using Valour.Client.Categories;
using System.Web;
using Newtonsoft.Json;

namespace Valour.Client.Planets
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add client funtionality to the Planet
    /// class. It does not, and should not, have any extra fields or properties.
    /// Just helper methods.
    /// </summary>
    public class ClientPlanet : Planet 
    {

        /// <summary>
        /// Returns the generic planet object
        /// </summary>
        public Planet Planet
        {
            get
            {
                return (Planet)this;
            }
        }

        // Cached values
        private List<ClientPlanetChatChannel> _channels = null;
        private List<ClientPlanetCategory> _categories = null;

        public async Task NotifyUpdateChannel(ClientPlanetChatChannel channel)
        {
            if (_channels == null)
            {
                await LoadChannelsAsync();
            }

            int index = _channels.FindIndex(x => x.Id == channel.Id);

            if (index == -1) {
                // add to cache
                _channels.Add(channel);
            }
            else {
                // replace
                _channels.RemoveAt(index);
                _channels.Insert(index, channel);
            }
        }

        public void NotifyDeleteChannel(ClientPlanetChatChannel channel)
        {
            int index = _channels.FindIndex(x => x.Id == channel.Id);

            if (index != -1)
            {
                _channels.RemoveAt(index);
            }
        }

        public async Task NotifyUpdateCategory(ClientPlanetCategory category)
        {
            if (_categories == null)
            {
                await RequestCategoriesAsync();
            }

            int index = _categories.FindIndex(x => x.Id == category.Id);

            if (index == -1)
            {
                // add to cache
                _categories.Add(category);
            }
            else
            {
                // replace
                _categories.RemoveAt(index);
                _categories.Insert(index, category);
            }
        }

        public void NotifyDeleteCategory(ClientPlanetCategory category)
        {
            int index = _categories.FindIndex(x => x.Id == category.Id);

            if (index != -1)
            {
                _categories.RemoveAt(index);
            }
        }

        /// <summary>
        /// Returns the primary channel of the planet
        /// </summary>
        public async Task<ClientPlanetChatChannel> GetPrimaryChannelAsync()
        {
            var response = await ClientUserManager.Http.GetAsync($"api/planet/{Id}/primary_channel");

            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("A fatal error occurred retrieving a planet's primary category.");
                Console.WriteLine(message);
                return null;
            }

            ClientPlanetChatChannel result = JsonConvert.DeserializeObject<ClientPlanetChatChannel>(message);

            return result;
        }

        /// <summary>
        /// Retrieves and returns categories of a planet by requesting from the server
        /// </summary>
        public async Task<List<ClientPlanetCategory>> GetCategoriesAsync(bool force_refresh = false)
        {

            if (_categories == null || force_refresh)
            {
                await RequestCategoriesAsync();
            }

            return _categories;
        }

        /// <summary>
        /// Requests and caches categories from the server
        /// </summary>
        public async Task RequestCategoriesAsync()
        {
            var response = await ClientUserManager.Http.GetAsync($"api/planet/{Id}/categories");

            var message = await response.Content.ReadAsStringAsync();  

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("A fatal error occurred retrieving a planet's categories.");
                Console.WriteLine(message);
                return;
            }

            List<ClientPlanetCategory> result = JsonConvert.DeserializeObject<List<ClientPlanetCategory>>(message);

            _categories = result;
        }

        /// <summary>
        /// Retrieves and returns channels of a planet by requesting from the server
        /// </summary>
        public async Task<List<ClientPlanetChatChannel>> GetChannelsAsync(bool force_refresh = false)
        {

            if (_channels == null || force_refresh)
            {
                await LoadChannelsAsync();
            }

            return _channels;
        }

        /// <summary>
        /// Requests and caches channels from the server
        /// </summary>
        public async Task LoadChannelsAsync()
        {
            string encoded_token = HttpUtility.UrlEncode(ClientUserManager.UserSecretToken);

            var response = await ClientUserManager.Http.GetAsync($"/api/planet/{Id}/channels");

            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("A fatal error occurred retrieving planet channels from the server.");
                Console.WriteLine(message);
            }

            List<ClientPlanetChatChannel> channels = JsonConvert.DeserializeObject<List<ClientPlanetChatChannel>>(message);

            _channels = channels;
        }

        /// <summary>
        /// Attempts to set the name of the planet
        /// </summary>
        public async Task<TaskResult> TrySetNameAsync(string name)
        {
            StringContent content = new StringContent(name);

            var response = await ClientUserManager.Http.PutAsync($"api/planet/{Id}/name", content);

            string message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to set planet name");
                Console.WriteLine(response.Content);
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
            StringContent content = new StringContent(description);

            var response = await ClientUserManager.Http.PutAsync($"api/planet/{Id}/description", content);

            string message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("Failed to set planet description");
                Console.WriteLine(response.Content);
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
        public async Task<TaskResult> SetPublic(bool ispublic)
        {
            string json = await ClientUserManager.Http.GetStringAsync($"Planet/SetPublic?planet_id={Id}&ispublic={ispublic}&token={ClientUserManager.UserSecretToken}");

            TaskResult result = JsonConvert.DeserializeObject<TaskResult>(json);

            if (result.Success)
            {
                Public = ispublic;
            }

            return result;
        }

        /// <summary>
        /// Retrieves and returns a client planet by requesting from the server
        /// </summary>
        public static async Task<ClientPlanet> GetPlanetAsync(ulong id)
        {
            var response = await ClientUserManager.Http.GetAsync($"api/planet/{id}");

            var message = await response.Content.ReadAsStringAsync();

            Console.WriteLine(message);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine("A fatal error occurred retrieving the planet.");
                Console.WriteLine(message);
                return null;
            }

            ClientPlanet planet = JsonConvert.DeserializeObject<ClientPlanet>(message);

            return planet;
        }

        /// <summary>
        /// Deserializes json
        /// </summary>
        public static ClientPlanet Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<ClientPlanet>(json);
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
