using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Messages;
using Valour.Shared.Oauth;
using Valour.Shared.Users;
using Valour.Shared.Planets;
using System.Text.RegularExpressions;
using System.Collections.Specialized;
using Valour.Server.Messages;
using Microsoft.EntityFrameworkCore;
using Valour.Server.Workers;
using Valour.Server.Planets;
using AutoMapper;
using Valour.Server.Oauth;
using Valour.Server.Categories;
using WebPush;
using Valour.Server.MPS;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Controls all routes for channels
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class ChannelController
    {

        public static List<PlanetMessage> messageCache = new List<PlanetMessage>();

        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;

        private readonly IMapper Mapper;
        private readonly WebPushClient PushClient;

        // Dependency injection
        public ChannelController(ValourDB context, IMapper mapper, WebPushClient pushClient)
        {
            this.Context = context;
            this.Mapper = mapper;
            this.PushClient = pushClient;
        }
        
        // These should be in planet smh

        public async Task<TaskResult<IEnumerable<ulong>>> GetChannelIdsAsync(ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<IEnumerable<ulong>>(false, "Please supply a valid authentication token.", null);
            }
            
            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, planet_id, Context);

            if (member == null)
            {
                return new TaskResult<IEnumerable<ulong>>(false, "You are not a member of this planet.", null);
            }

            var channels = Context.PlanetChatChannels.Where(c => c.Planet_Id == planet_id);

            List<ulong> result = new List<ulong>();

            foreach (var channel in channels)
            {
                if (await channel.HasPermission(member, ChatChannelPermissions.View, Context))
                {
                    result.Add(channel.Id);
                }
            }

            return new TaskResult<IEnumerable<ulong>>(true, "Successfully retrieved channels.", result);;
        }

        [HttpGet]
        public async Task<TaskResult<IEnumerable<PlanetChatChannel>>> GetPlanetChannelsAsync(ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<IEnumerable<PlanetChatChannel>>(false, "Please supply a valid authentication token.", null);
            }

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, planet_id, Context);

            if (member == null)
            {
                return new TaskResult<IEnumerable<PlanetChatChannel>>(false, "You are not a member of this planet.", null);
            }

            var channels = Context.PlanetChatChannels.Where(c => c.Planet_Id == planet_id);

            List<PlanetChatChannel> result = new List<PlanetChatChannel>();

            foreach (var channel in channels)
            {
                if (await channel.HasPermission(member, ChatChannelPermissions.View, Context))
                {
                    result.Add(channel);
                }
            }

            return new TaskResult<IEnumerable<PlanetChatChannel>>(true, "Successfully retrieved channels.", result);
        }

    }
}
