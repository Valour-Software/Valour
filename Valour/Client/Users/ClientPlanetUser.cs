using AutoMapper;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Valour.Shared;
using Valour.Shared.Roles;
using Valour.Shared.Users;

namespace Valour.Client.Users
{
    /*  Valour - A free and secure chat client
     *  Copyright (C) 2021 Vooper Media LLC
     *  This program is subject to the GNU Affero General Public license
     *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
     */

    /// <summary>
    /// This class exists to add client funtionality to the PlanetUser
    /// class. It does not, and should not, have any extra fields.
    /// Just helper methods and properties.
    /// </summary>
    public class ClientPlanetUser : PlanetUser
    {

        /// <summary>
        /// Returns the generic planetuser object
        /// </summary>
        [JsonIgnore]
        public PlanetUser PlanetUser
        {
            get
            {
                return (PlanetUser)this;
            }
        }

        /// <summary>
        /// Returns the client version from the base
        /// </summary>
        public static ClientPlanetUser FromBase(PlanetUser planetuser, IMapper mapper)
        {
            return mapper.Map<ClientPlanetUser>(planetuser);
        }

        /// <summary>
        /// Returns a clientplanetuser by requesting from the server
        /// </summary>
        public static async Task<ClientPlanetUser> GetClientPlanetUserAsync(ulong userid, ulong planet_id)
        {
            string json = await ClientUserManager.Http.GetStringAsync($"User/GetPlanetUser?userid={userid}&planet_id={planet_id}&auth={ClientUserManager.UserSecretToken}");

            TaskResult<ClientPlanetUser> result = JsonConvert.DeserializeObject<TaskResult<ClientPlanetUser>>(json);

            if (result == null)
            {
                Console.WriteLine("A fatal error occurred retrieving a planet user from the server.");
                return null;
            }

            if (!result.Success)
            {
                Console.WriteLine(result.ToString());
                return null;
            }

            return result.Data;
        }

        /// <summary>
        /// Cached roles
        /// </summary>
        public List<PlanetRole> Roles = null;

        /// <summary>
        /// Loads the user roles into cache, or reloads them if called again
        /// </summary>
        public async Task LoadRolesAsync()
        {
            string json = await ClientUserManager.Http.GetStringAsync($"User/GetPlanetUserRoles?userid={Id}&planet_id={Planet_Id}&auth={ClientUserManager.UserSecretToken}");

            TaskResult<List<PlanetRole>> result = JsonConvert.DeserializeObject<TaskResult<List<PlanetRole>>>(json);

            if (result == null)
            {
                Console.WriteLine("A fatal error occurred retrieving planet user roles from the server.");
            }

            if (!result.Success)
            {
                Console.WriteLine(result.ToString());
            }

            Roles = result.Data;
        }

        /// <summary>
        /// Returns the top role for the planet user
        /// </summary>
        public async Task<PlanetRole> GetPrimaryRoleAsync()
        {
            if (Roles == null)
            {
                await LoadRolesAsync();
            }

            return Roles[0];
        }

        public string State = null;

        public async Task LoadStateAsync()
        {
            // TODO: Make work
            State = "Currently browsing";
        }

        /// <summary>
        /// Returns the state of the planet user
        /// </summary>
        public async Task<string> GetStateAsync()
        {
            if (State == null)
            {
                await LoadStateAsync();
            }

            return State;
        }

        public async Task<string> GetColorHexAsync()
        {
            return (await GetPrimaryRoleAsync()).GetColorHex();
        }

        /// <summary>
        /// Deserializes json
        /// </summary>
        public static ClientPlanetUser Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<ClientPlanetUser>(json);
        }
    }
}
