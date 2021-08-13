using AutoMapper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Web;
using Valour.Client.Planets;
using Valour.Shared;
using Valour.Shared.Categories;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using Valour.Shared.Roles;

namespace Valour.Client.Categories
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add client funtionality to the PlanetCategory
    /// class. It does not, and should not, have any extra fields or properties.
    /// Just helper methods.
    /// </summary>
    public class ClientPlanetCategory : PlanetCategory, IClientPlanetListItem
    {
        /// <summary>
        /// Converts to a client version of planet category
        /// </summary>
        public static ClientPlanetCategory FromBase(PlanetCategory channel, IMapper mapper)
        {
            return mapper.Map<ClientPlanetCategory>(channel);
        }

        /// <summary>
        /// Returns the planet this category belongs to
        /// </summary>
        public async Task<ClientPlanet> GetPlanetAsync()
        {
            return await ClientPlanetManager.Current.GetPlanetAsync(Planet_Id);
        }

        public ChannelListItemType ItemType => ChannelListItemType.Category;

        /// <summary>
        /// Attempts to set the name of the channel and returns the result
        /// </summary>
        public async Task<TaskResult> SetNameAsync(string name)
        {
            string encodedName = HttpUtility.UrlEncode(name);

            StringContent content = new StringContent(encodedName);

            var response = await ClientUserManager.Http.PutAsync($"api/category/{Id}/name", content);

            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to set name for category {Id}");
                Console.WriteLine(message);
            }

            return new TaskResult(response.IsSuccessStatusCode, message);
        }

        /// <summary>
        /// Attempts to set the description of the channel and returns the result
        /// </summary>
        public async Task<TaskResult> SetDescriptionAsync(string desc)
        {
            string encodedDesc = HttpUtility.UrlEncode(desc);

            StringContent content = new StringContent(encodedDesc);

            var response = await ClientUserManager.Http.PutAsync($"api/category/{Id}/description", content);

            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to set description for category {Id}");
                Console.WriteLine(message);
            }

            return new TaskResult(response.IsSuccessStatusCode, message);
        }

        /// <summary>
        /// Attempts to set the parent of the channel and returns the result
        /// </summary>
        public async Task<TaskResult> SetParentIdAsync(ulong parent_id)
        {
            StringContent content = new StringContent(parent_id.ToString());

            var response = await ClientUserManager.Http.PutAsync($"api/category/{Id}/parent_id", content);

            var message = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Failed to set parent id for category {Id}");
                Console.WriteLine(message);
            }

            return new TaskResult(response.IsSuccessStatusCode, message);
        }

        public string GetItemTypeName()
        {
            return "Category";
        }

        public async Task<PermissionsNode> GetPermissionsNodeAsync(PlanetRole role)
        {
            return await GetCategoryPermissionsNode(role);
        }

        public async Task<CategoryPermissionsNode> GetCategoryPermissionsNode(PlanetRole role)
        {

            // For SOME reason the args need to be in this order
            string json = await ClientUserManager.Http.GetStringAsync($"Permissions/GetCategoryNode?category_id={Id}" +
                                                                                                 $"&token={ClientUserManager.UserSecretToken}" +
                                                                                                 $"&role_id={role.Id}");

            Console.WriteLine(json);

            TaskResult<CategoryPermissionsNode> result = JsonConvert.DeserializeObject<TaskResult<CategoryPermissionsNode>>(json);

            if (result == null)
            {
                Console.WriteLine("Failed to deserialize result from GetPermissionsNode in category");
                return null;
            }

            if (!result.Success)
            {
                Console.WriteLine("Permissions/GetCategoryNode failed.");
                Console.WriteLine(result.Message);
                return null;
            }

            // Return the node - it may be null, but that's ok
            return result.Data;
        }
    }
}
