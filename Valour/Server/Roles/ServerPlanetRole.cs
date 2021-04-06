using AutoMapper;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Mapping;
using Valour.Shared.Oauth;
using Valour.Server.Users;
using Valour.Shared.Channels;
using Valour.Shared.Roles;
using Valour.Server.Planets;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;
using System.Text.Json.Serialization;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


namespace Valour.Server.Roles
{
    public class ServerPlanetRole : PlanetRole
    {
        [ForeignKey("Planet_Id")]
        [JsonIgnore]
        [Newtonsoft.Json.JsonIgnore]
        public virtual ServerPlanet Planet { get; set; }

        /// <summary>
        /// Returns a ServerPlanetRole using a PlanetRole as a base
        /// </summary>
        public static ServerPlanetRole FromBase(PlanetRole planetrole)
        {
            return MappingManager.Mapper.Map<ServerPlanetRole>(planetrole);
        }

        public List<ChatChannelPermissionsNode> GetAllChannelNodes()
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                return Context.ChatChannelPermissionsNodes.Where(x => x.Planet_Id == Planet_Id).ToList();
            }           
        }

        public async Task<ChatChannelPermissionsNode> GetChannelNodeAsync(PlanetChatChannel channel)
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                return await Context.ChatChannelPermissionsNodes.FirstOrDefaultAsync(x => x.Channel_Id == channel.Id &&
                                                                                      x.Role_Id == Id);
            }
        }

        public async Task<PermissionState> GetPermissionStateAsync(Permission permission, PlanetChatChannel channel)
        {
            return await GetPermissionStateAsync(permission, channel.Id);
        }

        public async Task<PermissionState> GetPermissionStateAsync(Permission permission, ulong channel_id)
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                ChatChannelPermissionsNode node = await Context.ChatChannelPermissionsNodes.FirstOrDefaultAsync(x => x.Role_Id == Id && x.Channel_Id == channel_id);
                return node.GetPermissionState(permission);
            }
        }

        /// <summary>
        /// Returns if the role has the permission
        /// </summary>
        /// <param name="permission"></param>
        /// <returns></returns>
        public bool HasPermission(PlanetPermission permission)
        {
            return Permission.HasPermission(Permissions, permission);
        }
    }
}
