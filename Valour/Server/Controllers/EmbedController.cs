using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Email;
using Valour.Shared.Oauth;
using Valour.Server.Planets;
using Valour.Server.Users;
using Valour.Server.Users.Identity;
using Valour.Shared;
using Valour.Shared.Messages;
using Valour.Shared.Planets;
using Valour.Shared.Users;
using Valour.Client.Users;
using Valour.Shared.Users.Identity;
using Newtonsoft.Json;
using Valour.Server.Roles;
using Valour.Client.Planets;
using Microsoft.AspNetCore.Http;
using Valour.Server.Oauth;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Provides routes for member-related functions on the server side.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class EmbedController
    {
        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;
        private readonly IMapper Mapper;

        // Dependency injection
        public EmbedController(ValourDB context, IMapper mapper)
        {
            this.Context = context;
            this.Mapper = mapper;
        }

        [HttpPost]
        public async Task<TaskResult> InteractionEvent(InteractionEvent interaction, string token)
        {
            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(token, Context);

            if (auth == null)
            {
                return new TaskResult(false, "Failed to authorize.");
            }

            ServerPlanetMember member = await Context.PlanetMembers.FindAsync(interaction.Member_Id);

            if (member == null)
            {
                return new TaskResult(false, "Member could not be found");
            }

            // make sure requester is the same as the member

            if (member.User_Id != auth.User_Id)
            {
                return new TaskResult(false, "Requester is not the same user as memberid!");
            }

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Id == interaction.Channel_Id);

            if (channel == null)
            {
                return new TaskResult(false, "Failed to do interaction: The given channel does not exist!");
            }

            if (!(await channel.HasPermission(member, ChatChannelPermissions.View, Context)))
            {
                return new TaskResult(false, "Failed to do interaction: You don't have permission to see this channel!");
            }

            // no need to await this

            PlanetHub.NotifyInteractionEvent(interaction);

            return new TaskResult(true, "Processed & Completed Interaction.");


        }
    }
}
