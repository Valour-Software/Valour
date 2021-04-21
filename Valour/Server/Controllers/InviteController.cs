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
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using Valour.Shared.Users;
using Valour.Client.Users;
using Valour.Shared.Users.Identity;
using Newtonsoft.Json;
using Valour.Server.Oauth;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Provides routes for user-related functions on the server side.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class InviteController
    {
        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;
        private readonly UserManager UserManager;
        private readonly IMapper Mapper;

        // Dependency injection
        public InviteController(ValourDB context, UserManager userManager, IMapper mapper)
        {
            this.Context = context;
            this.UserManager = userManager;
            this.Mapper = mapper;
        }

        public async Task<TaskResult<List<PlanetInvite>>> GetInvites(ulong user_id, string token, ulong planet_id)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<List<PlanetInvite>>(false, "Failed to authorize user.", null);
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.Invite)))
            {
                return new TaskResult<List<PlanetInvite>>(false, "You are not authorized to do this.", null);
            }

            List<PlanetInvite> invites = await Task.Run(() => Context.PlanetInvites.Where(x => x.Planet_Id == planet_id).ToList());
        
            return new TaskResult<List<PlanetInvite>>(true, $"Retrieved {invites.Count} invites", invites);
        }

        public async Task<TaskResult> Join(string code, string token)
        {

            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult(false, $"Unable to authorize");
            }

            ulong user_id = authToken.User_Id;

            PlanetInvite invite = await Context.PlanetInvites.FirstOrDefaultAsync(x => x.Code == code);

            if (invite == null) {
                return new TaskResult(false, $"Invite code not found!");
            }

            if (await Context.PlanetBans.AnyAsync(x => x.User_Id == user_id && x.Planet_Id == invite.Planet_Id)) {
                return new TaskResult(false, $"User is banned from this planet!");
            }

            if (await Context.PlanetMembers.AnyAsync(x => x.User_Id == user_id && x.Planet_Id == invite.Planet_Id)) {
                return new TaskResult(false, $"User is already in this planet!");
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(invite.Planet_Id);

            if (!planet.Public) {
                return new TaskResult(false, $"Planet is set to private!");
            }

            User user = await Context.Users.FindAsync(user_id);

            await planet.AddMemberAsync(user, Context);

            return new TaskResult(true, $"Successfully joined planet.");

        }

        public async Task<TaskResult<PlanetInvite>> GetInvite(string code, ulong user_id)
        {

            PlanetInvite invite = await Context.PlanetInvites.FirstOrDefaultAsync(x => x.Code == code);


            if (invite.IsPermanent() == false) {
                if (DateTime.UtcNow > invite.Time.AddMinutes((double)invite.Hours)) {
                    
                    
                    Context.PlanetInvites.Remove(invite);

                    await Context.SaveChangesAsync();

                    return new TaskResult<PlanetInvite>(false, $"Invite is expired", null);
                }
            }

            PlanetBan ban = await Context.PlanetBans.FirstOrDefaultAsync(x => x.User_Id == user_id && x.Planet_Id == invite.Planet_Id);

            if (ban != null) {
                return new TaskResult<PlanetInvite>(false, $"User is banned from this planet!", null);
            }

            Planet planet = await Context.Planets.FirstOrDefaultAsync(x => x.Id == invite.Planet_Id);

            if (!planet.Public) {
                return new TaskResult<PlanetInvite>(false, $"Planet is set to private!", null);
            }

            return new TaskResult<PlanetInvite>(true, $"Successfully got invite", invite);
        }

        public async Task<TaskResult<PlanetInvite>> CreateInvite(ulong Planet_Id, string token, int hours)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<PlanetInvite>(false, "Failed to authorize user.", null);
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(Planet_Id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.Invite)))
            {
                return new TaskResult<PlanetInvite>(false, "You are not authorized to do this.", null);
            }

            PlanetInvite invite = new PlanetInvite()
            {
                Id = IdManager.Generate(),
                Planet_Id = Planet_Id,
                Issuer_Id = authToken.User_Id,
                Time = DateTime.UtcNow,
                Hours = hours
            };

            Random random = new Random();
            
            const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            
            string code = new string(Enumerable.Repeat(chars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
           
            PlanetInvite test = await Context.PlanetInvites.FirstOrDefaultAsync(x => x.Code == code);
            
            while (test != null) {
                code = new string(Enumerable.Repeat(chars, 8).Select(s => s[random.Next(s.Length)]).ToArray());
                test = await Context.PlanetInvites.Where(x => x.Code == code).FirstOrDefaultAsync();
            }

            invite.Code = code;

            if (hours == 0) {
                invite.Hours = null;
            }
            
            await Context.PlanetInvites.AddAsync(invite);

            await Context.SaveChangesAsync();

            return new TaskResult<PlanetInvite>(true, $"Successfully created invite", invite);
        }

        /// <summary>
        /// Returns the planet name using an invite code as authorization
        /// </summary>
        public async Task<TaskResult<string>> GetPlanetName(string invite_code)
        {
            PlanetInvite invite = await Context.PlanetInvites.FirstOrDefaultAsync(x => x.Code == invite_code);

            if (invite == null)
            {
                return new TaskResult<string>(false, "Could not find invite.", null);
            }

            ServerPlanet planet = await Context.Planets.FindAsync(invite.Planet_Id);

            if (planet == null)
            {
                return new TaskResult<string>(false, $"Could not find planet {invite.Planet_Id}", null);
            }

            return new TaskResult<string>(true, $"Success", planet.Name);
        }

        /// <summary>
        /// Returns the planet icon using an invite code as authorization
        /// </summary>
        public async Task<TaskResult<string>> GetPlanetIcon(string invite_code)
        {
            PlanetInvite invite = await Context.PlanetInvites.FirstOrDefaultAsync(x => x.Code == invite_code);

            if (invite == null)
            {
                return new TaskResult<string>(false, "Could not find invite.", null);
            }

            ServerPlanet planet = await Context.Planets.FindAsync(invite.Planet_Id);

            if (planet == null)
            {
                return new TaskResult<string>(false, $"Could not find planet {invite.Planet_Id}", null);
            }

            return new TaskResult<string>(true, $"Success", planet.Image_Url);
        }
    }
}
