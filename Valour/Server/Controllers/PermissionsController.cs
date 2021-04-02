using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Planets;
using Valour.Shared;
using Valour.Shared.Oauth;
using Valour.Shared.Roles;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Controls all (most) routes for permissions
    /// Yeah I'd like to say all, but there will probably be specific stuff in the
    /// channel and category controllers that I'll need to continually shift here
    ///  /rant
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class PermissionsController
    {

        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;

        private readonly IMapper Mapper;

        // Dependency injection
        public PermissionsController(ValourDB context, IMapper mapper)
        {
            this.Context = context;
            this.Mapper = mapper;
        }

        public async Task<TaskResult<ChannelPermissionsNodeResponse>> GetPermissionsNode(ulong channel_id, ulong role_id, string token)
        {
            ChannelPermissionsNodeResponse response = new ChannelPermissionsNodeResponse()
            {
                Exists = false,
                Node = null
            };

            // Authenticate first
            AuthToken authToken = await Context.AuthTokens.FirstOrDefaultAsync(x => x.Id == token);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult<ChannelPermissionsNodeResponse>(false, $"Could not find channel {channel_id}", null);
            }

            if (authToken == null)
            {
                return new TaskResult<ChannelPermissionsNodeResponse>(false, "Failed to authorize user.", null);
            }

            if (!authToken.HasScope(UserPermissions.Membership))
            {
                return new TaskResult<ChannelPermissionsNodeResponse>(false, "Your token doesn't have planet membership scope.", null);
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult<ChannelPermissionsNodeResponse>(false, "You are not a member of the target planet.", null);
            }

            // Actually get the node
            ChannelPermissionsNode node = await Context.ChannelPermissionsNodes.FirstOrDefaultAsync(x => x.Channel_Id == channel_id &&
                                                                                                         x.Role_Id == role_id);

            response.Node = node;

            if (node == null)
            {
                return new TaskResult<ChannelPermissionsNodeResponse>(true, "The given node does not exist", response);
            }

            response.Exists = true;

            return new TaskResult<ChannelPermissionsNodeResponse>(true, "Returned permission node successfully", response);
        }
    }
}
