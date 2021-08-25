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
using Valour.Shared.Planets;
using Valour.Shared.Users;
using Valour.Client.Users;
using Valour.Shared.Users.Identity;
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
    public class MemberController
    {
        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;
        private readonly IMapper Mapper;

        // Dependency injection
        public MemberController(ValourDB context, IMapper mapper)
        {
            this.Context = context;
            this.Mapper = mapper;
        }

        public async Task<TaskResult<ServerPlanetMember>> GetMember(ulong id, string token)
        {
            ServerAuthToken auth = await ServerAuthToken.TryAuthorize(token, Context);

            if (auth == null)
            {
                return new TaskResult<ServerPlanetMember>(false, "Failed to authorize.", null);
            }

            ServerPlanetMember member = await Context.PlanetMembers.FindAsync(id);

            if (member == null)
            {
                return new TaskResult<ServerPlanetMember>(false, "Member could not be found", null);
            }

            // Check if requester is in planet
            bool inPlanet = await Context.PlanetMembers.AnyAsync(x => x.Planet_Id == member.Planet_Id &&
                                                                      x.User_Id == auth.User_Id);

            // We don't want to confirm that the member exists for privacy reasons. Just give a generic
            // failed response if they are not within the planet.
            if (!inPlanet)
            {
                return new TaskResult<ServerPlanetMember>(false, "Member could not be found", null);
            }

            // Succeed
            return new TaskResult<ServerPlanetMember>(true, "Found member.", member);
        }
    }
}
