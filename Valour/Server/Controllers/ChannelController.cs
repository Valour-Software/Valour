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

        public async Task<TaskResult> SetName(ulong channel_id, string name, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult(false, $"Could not find channel {channel_id}");
            }

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You do not have planet channel management permissions.");
            }

            channel.Name = name;
            await Context.SaveChangesAsync();

            // Send channel refresh
            PlanetHub.NotifyChatChannelChange(channel);

            return new TaskResult(true, "Successfully changed channel name.");
        }

        public async Task<TaskResult> SetDescription(ulong channel_id, string description, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult(false, $"Could not find channel {channel_id}");
            }

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You do not have planet channel management permissions.");
            }

            channel.Description = description;
            await Context.SaveChangesAsync();

            // Send channel refresh
            PlanetHub.NotifyChatChannelChange(channel);

            return new TaskResult(true, "Successfully changed channel description.");
        }

        public async Task<TaskResult> SetPermissionInheritMode(ulong channel_id, bool value, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult(false, $"Could not find channel {channel_id}");
            }

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You do not have planet channel management permissions.");
            }

            channel.Inherits_Perms = value;
            await Context.SaveChangesAsync();

            // Send channel refresh
            PlanetHub.NotifyChatChannelChange(channel);

            return new TaskResult(true, $"Successfully set permission inheritance to {value}.");
        }
    }
}
