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
using Valour.Shared.Items;
using Valour.Shared.Roles;

namespace Valour.Client.Categories
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    public class ClientPlanetCategory : IPlanetCategory, IClientNamedItem, IClientPlanetListItem
    {
        /// <summary>
        /// The id of this category
        /// </summary>
        public ulong Id { get; set; }

        /// <summary>
        /// The name of this category
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The position of this category
        /// </summary>
        public ushort Position { get; set; }

        /// <summary>
        /// The id of the parent of this category (if it exists)
        /// </summary>
        public ulong? Parent_Id { get; set; }

        /// <summary>
        /// The id of the planet this category belongs to
        /// </summary>
        public ulong Planet_Id { get; set; }

        /// <summary>
        /// The description of this category
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The type of this item
        /// </summary>
        public ItemType ItemType => ItemType.Category;

        /// <summary>
        /// Attempts to delete this category
        /// </summary>
        public async Task<TaskResult> TryDeleteAsync()
        {
            var response = await ClientUserManager.Http.DeleteAsync($"api/category/{Id}");

            return new TaskResult(
                response.IsSuccessStatusCode,
                await response.Content.ReadAsStringAsync()
            );
        }

        /// <summary>
        /// Attempts to set the name of the channel and returns the result
        /// </summary>
        public async Task<TaskResult> TrySetNameAsync(string name)
        {
            string encodedName = HttpUtility.UrlEncode(name);
            StringContent content = new StringContent(encodedName);

            var response = await ClientUserManager.Http.PutAsync($"api/category/{Id}/name", content);

            return new TaskResult(
                response.IsSuccessStatusCode,
                await response.Content.ReadAsStringAsync()
            );
        }

        /// <summary>
        /// Attempts to set the description of the channel and returns the result
        /// </summary>
        public async Task<TaskResult> TrySetDescriptionAsync(string desc)
        {
            string encodedDesc = HttpUtility.UrlEncode(desc);
            StringContent content = new StringContent(encodedDesc);

            var response = await ClientUserManager.Http.PutAsync($"api/category/{Id}/description", content);

            return new TaskResult(
                response.IsSuccessStatusCode,
                await response.Content.ReadAsStringAsync()
            );
        }

        /// <summary>
        /// Attempts to set the parent of the channel and returns the result
        /// </summary>
        public async Task<TaskResult> TrySetParentIdAsync(ulong parent_id)
        {
            StringContent content = new StringContent(parent_id.ToString());
            var response = await ClientUserManager.Http.PutAsync($"api/category/{Id}/parent_id", content);

            return new TaskResult(
                response.IsSuccessStatusCode,
                await response.Content.ReadAsStringAsync()
            );
        }

        /// <summary>
        /// Returns the string representation of the item type
        /// </summary>
        /// <returns></returns>
        public string GetItemTypeName()
        {
            return "Category";
        }

        /// <summary>
        /// Returns the planet this category belongs to
        /// </summary>
        public async Task<ClientPlanet> GetPlanetAsync()
        {
            return await ClientPlanetManager.Current.GetPlanetAsync(Planet_Id);
        }

        /// <summary>
        /// Returns the permissions node for this category
        /// </summary>
        public async Task<PermissionsNode> GetPermissionsNodeAsync(PlanetRole role)
        {
            return await GetCategoryPermissionsNode(role);
        }

        /// <summary>
        /// Returns the category permissions node for this category
        /// </summary>
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

        public Task<TaskResult> TrySetName(string name)
        {
            throw new NotImplementedException();
        }
    }
}
