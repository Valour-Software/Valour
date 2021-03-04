using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using Valour.Client.Mapping;
using Valour.Shared;
using Valour.Shared.Planets;
using Valour.Shared.Roles;
using Valour.Shared.Users;

/*  Valour - A free and secure chat client
*  Copyright (C) 2021 Vooper Media LLC
*  This program is subject to the GNU Affero General Public license
*  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
*/

namespace Valour.Client.Planets
{
    public class ClientPlanetMember : PlanetMember
    {
        // Client cached properties //

        /// <summary>
        /// Cached roles
        /// </summary>
        private List<ulong> _roleids = null;

        /// <summary>
        /// Cached state
        /// </summary>
        private string _state = null;

        /// <summary>
        /// Cached user
        /// </summary>
        private User _user = null;

        public ulong TryGetPrimaryRoleId()
        {
            if (_roleids == null)
            {
                return ulong.MaxValue;
            }

            return _roleids[0];
        }

        public void SetCacheValues(PlanetMemberInfo info)
        {
            SetCacheValues(info.RoleIds, info.State, info.User);
        }

        public void SetCacheValues(List<ulong> role_ids, string state, User user)
        {
            _roleids = role_ids;
            _state = state;
            _user = user;
        }

        public async Task<bool> HasRole(ulong role_id)
        {
            if (_roleids == null)
            {
                await LoadRolesAsync();
            }

            return _roleids.Contains(role_id);
        }

        public async Task<List<PlanetRole>> GetPlanetRolesAsync()
        {
            if (_roleids == null)
            {
                await LoadRolesAsync();
            }

            List<PlanetRole> roles = new List<PlanetRole>();

            foreach (ulong id in _roleids)
            {
                roles.Add(await ClientPlanetManager.Current.GetPlanetRole(id));
            }

            return roles;
        }

        public async Task<string> GetStateAsync()
        {
            if (_state == null)
            {
                await LoadStateAsync();
            }

            return _state;
        }

        public async Task<User> GetUserAsync()
        {
            if (_user == null)
            {
                await LoadUserAsync();
            }

            return _user;
        }

        /// <summary>
        /// Returns generic planet member object
        /// </summary>
        public PlanetMember GetPlanetMember()
        {
            return (PlanetMember)this;
        }

        /// <summary>
        /// Returns the client version from the base
        /// </summary>
        public static ClientPlanetMember FromBase(PlanetMember member)
        {
            return MappingManager.Mapper.Map<ClientPlanetMember>(member);
        }

        /// <summary>
        /// Returns a planet member by requesting from the server
        /// </summary>
        public static async Task<ClientPlanetMember> GetClientPlanetMemberAsync(ulong user_id, ulong planet_id)
        {
            string json = await ClientUserManager.Http.GetStringAsync($"Planet/GetPlanetMember?user_id={user_id}&planet_id={planet_id}&auth={ClientUserManager.UserSecretToken}");

            TaskResult<ClientPlanetMember> result = JsonConvert.DeserializeObject<TaskResult<ClientPlanetMember>>(json);

            if (result == null)
            {
                Console.WriteLine("A fatal error occurred retrieving a planet member from the server.");
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
        /// Loads the user into cache, or reloads them if called again
        /// </summary>
        /// <returns></returns>
        public async Task LoadUserAsync()
        {
            string json = await ClientUserManager.Http.GetStringAsync($"User/GetUser?id={User_Id}");

            TaskResult<User> result = JsonConvert.DeserializeObject<TaskResult<User>>(json);

            if (result == null)
            {
                Console.WriteLine("A fatal error occurred retrieving a user from the server.");
                return;
            }

            if (!result.Success)
            {
                Console.WriteLine(result.ToString());
                return;
            }

            _user = result.Data;
        }

        /// <summary>
        /// Loads the user roles into cache, or reloads them if called again
        /// </summary>
        public async Task LoadRolesAsync()
        {
            string json = await ClientUserManager.Http.GetStringAsync($"Planet/GetPlanetMemberRoleIds?user_id={User_Id}&planet_id={Planet_Id}&token={ClientUserManager.UserSecretToken}");

            Console.WriteLine(json);

            TaskResult<List<ulong>> result = JsonConvert.DeserializeObject<TaskResult<List<ulong>>>(json);

            if (result == null)
            {
                Console.WriteLine("A fatal error occurred retrieving planet user roles from the server.");
            }

            if (!result.Success)
            {
                Console.WriteLine(result.ToString());
                Console.WriteLine($"Failed for {Id} in {Planet_Id}");
            }

            _roleids = result.Data;
        }

        /// <summary>
        /// Loads the current user state from the server
        /// </summary>
        public async Task LoadStateAsync()
        {
            // TODO: Make work
            _state = "Currently browsing";
        }

        /// <summary>
        /// Returns the top role for the planet user
        /// </summary>
        public async Task<PlanetRole> GetPrimaryRoleAsync()
        {
            if (_roleids == null)
            {
                await LoadRolesAsync();
            }

            //Console.WriteLine($"PlanetManager null: {ClientPlanetManager.Current == null}");
            //Console.WriteLine($"Roleids null: {_roleids == null}");
            //Console.WriteLine($"Role null: {_roleids[0] == null}");

            return await ClientPlanetManager.Current.GetPlanetRole(_roleids[0]);
        }

        /// <summary>
        /// Returns a hex color code for the main role color
        /// </summary>
        public async Task<string> GetColorHexAsync()
        {
            return (await GetPrimaryRoleAsync()).GetColorHex();
        }

        /// <summary>
        /// Returns the member's pfp
        /// </summary>
        public async Task<string> GetPfpAsync()
        {
            if (!string.IsNullOrWhiteSpace(Member_Pfp))
            {
                return Member_Pfp;
            }

            return (await GetUserAsync()).Pfp_Url;
        }

        /// <summary>
        /// Returns the member's name
        /// </summary>
        public async Task<string> GetNameAsync()
        {
            if (!string.IsNullOrWhiteSpace(Nickname))
            {
                return Nickname;
            }

            return (await GetUserAsync()).Username;
        }

        /// <summary>
        /// Deserializes json
        /// </summary>
        public static ClientPlanetMember Deserialize(string json)
        {
            return JsonConvert.DeserializeObject<ClientPlanetMember>(json);
        }
    }
}
