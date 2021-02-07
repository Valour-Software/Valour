using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Oauth;
using Valour.Shared.Channels;
using Valour.Shared.Roles;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */


namespace Valour.Server.Roles
{
    public class ServerPlanetRole : PlanetRole
    {
        public List<ChannelPermissionsNode> GetAllChannelNodes()
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                return Context.ChannelPermissionsNodes.Where(x => x.Planet_Id == Planet_Id).ToList();
            }           
        }

        public async Task<bool> HasChannelPermission(Permission permission, PlanetChatChannel channel)
        {
            return await HasChannelPermission(permission, channel.Id);
        }

        public async Task<bool> HasChannelPermission(Permission permission, ulong channel_id)
        {
            using (ValourDB Context = new ValourDB(ValourDB.DBOptions))
            {
                ulong perms = 0x00;

                ChannelPermissionsNode node = await Context.ChannelPermissionsNodes.FirstOrDefaultAsync(x => x.Role_Id == Id && x.Channel_Id == channel_id);

                if (node != null)
                {
                    perms = node.Code;
                }
                // Use default permissions
                else
                {

                }
                
            }
        }
    }
}
