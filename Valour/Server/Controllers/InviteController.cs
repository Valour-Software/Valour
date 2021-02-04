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
using Valour.Server.Oauth;
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

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
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

        public async Task<TaskResult<List<PlanetInvite>>> GetInvites(ulong userid, string token, ulong planet_id)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult<List<PlanetInvite>>(false, "Failed to authorize user.", null);
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id, Mapper);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.Invite)))
            {
                return new TaskResult<List<PlanetInvite>>(false, "You are not authorized to do this.", null);
            }

            List<PlanetInvite> invites = await Task.Run(() => Context.PlanetInvites.Where(x => x.Planet_Id == planet_id).ToList());
        
            return new TaskResult<List<PlanetInvite>>(true, $"Retrieved {invites.Count} invites", invites);
        }

        public async Task<TaskResult> Join(string code, ulong userid, string token)
        {

            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult(false, $"Incorrect token!");
            }

            PlanetInvite invite = await Context.PlanetInvites.FindAsync(code);

            if (invite == null) {
                return new TaskResult(false, $"Code is not found!");
            }

            PlanetBan ban = await Context.PlanetBans.FirstOrDefaultAsync(x => x.User_Id == userid && x.Planet_Id == invite.Planet_Id);

            if (ban != null) {
                return new TaskResult(false, $"User is banned from this planet!");
            }

            PlanetMember mem = await Context.PlanetMembers.FirstOrDefaultAsync(x => x.User_Id == userid && x.Planet_Id == invite.Planet_Id);

            if (mem != null) {
                return new TaskResult(false, $"User is already in this planet!");
            }

            Planet planet = await Context.Planets.FirstOrDefaultAsync(x => x.Id == invite.Planet_Id);

            if (!planet.Public) {
                return new TaskResult(false, $"Planet is set to private!");
            }

            PlanetMember member = new PlanetMember()
            {
                User_Id = userid,
                Planet_Id = invite.Planet_Id,
            };

            await Context.PlanetMembers.AddAsync(member);

            await Context.SaveChangesAsync();

            return new TaskResult(true, $"Joined Planet");

        }

        public async Task<TaskResult<ClientPlanetInvite>> GetInvite(string code, ulong userid)
        {

            PlanetInvite invite = await Context.PlanetInvites.FirstOrDefaultAsync(x => x.Code == code);


            if (invite.IsPermanent() == false) {
                if (DateTime.UtcNow > invite.Time.AddMinutes((double)invite.Hours)) {
                    
                    
                    Context.PlanetInvites.Remove(invite);

                    await Context.SaveChangesAsync();

                    return new TaskResult<ClientPlanetInvite>(false, $"Invite is expired", null);
                }
            }

            PlanetBan ban = await Context.PlanetBans.FirstOrDefaultAsync(x => x.User_Id == userid && x.Planet_Id == invite.Planet_Id);

            if (ban != null) {
                return new TaskResult<ClientPlanetInvite>(false, $"User is banned from this planet!", null);
            }
            
            PlanetMember member = await Context.PlanetMembers.FirstOrDefaultAsync(x => x.User_Id == userid && x.Planet_Id == invite.Planet_Id);

            if (member != null) {
                return new TaskResult<ClientPlanetInvite>(false, $"User is already in this planet!", null);
            }

            ClientPlanetInvite clientinvite = ClientPlanetInvite.FromBase(invite, Mapper);

            Planet planet = await Context.Planets.FirstOrDefaultAsync(x => x.Id == invite.Planet_Id);

            if (!planet.Public) {
                return new TaskResult<ClientPlanetInvite>(false, $"Planet is set to private!", null);
            }

            clientinvite.PlanetName = planet.Name;

            return new TaskResult<ClientPlanetInvite>(true, $"Successfully got invite", clientinvite);
        }

        public async Task<TaskResult<PlanetInvite>> CreateInvite(ulong Planet_Id, ulong userid, string token, int hours)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult<PlanetInvite>(false, "Failed to authorize user.", null);
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(Planet_Id, Mapper);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.Invite)))
            {
                return new TaskResult<PlanetInvite>(false, "You are not authorized to do this.", null);
            }

            PlanetInvite invite = new PlanetInvite()
            {
                Planet_Id = Planet_Id,
                Issuer_Id = userid,
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
    }
}
