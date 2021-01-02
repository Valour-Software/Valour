using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Messaging;
using Valour.Server.Planets;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Messaging;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using Valour.Shared.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2020 Vooper Media LLC
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
    public class PlanetController
    {
        /// <summary>
        /// The maximum planets a user is allowed to have. This will increase after 
        /// the alpha period is complete.
        /// </summary>
        public static int MAX_OWNED_PLANETS = 5;

        /// <summary>
        /// The maximum planets a user is allowed to join. This will increase after the 
        /// alpha period is complete.
        /// </summary>
        public static int MAX_JOINED_PLANETS = 20;

        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;

        // Dependency injection
        public PlanetController(ValourDB context)
        {
            this.Context = context;
        }

        /// <summary>
        /// Creates a server and if successful returns a task result with the created
        /// planet's id
        /// </summary>
        public async Task<TaskResult<ulong>> CreatePlanet(string name, string image_url, ulong userid, string token)
        {
            TaskResult nameValid = ValidateName(name);

            if (!nameValid.Success)
            {
                return new TaskResult<ulong>(false, nameValid.Message, 0);
            }

            if (image_url.Length > 255)
            {
                return new TaskResult<ulong>(false, "Image url must be under 255 characters.", 0);
            }

            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult<ulong>(false, "Failed to authorize user.", 0);
            }

            // User is verified and given planet info is valid by this point
            // We don't actually need the user object which is cool

            Planet planet = new Planet()
            {
                Name = name,
                Member_Count = 1,
                Description = "A Valour server.",
                Image_Url = image_url,
                Public = true,
                Owner_Id = userid
            };

            await Context.Planets.AddAsync(planet);
            
            // Have to do this first for auto-incremented ID
            await Context.SaveChangesAsync();

            PlanetMember member = new PlanetMember()
            {
                User_Id = userid,
                Planet_Id = planet.Id
            };

            // Add the owner to the planet as a member
            await Context.PlanetMembers.AddAsync(member);


            // Create general channel
            PlanetChatChannel channel = new PlanetChatChannel()
            {
                Name = "General",
                Planet_Id = planet.Id,
                Message_Count = 0
            };

            // Add channel to database
            await Context.PlanetChatChannels.AddAsync(channel);

            // Save changes to DB
            await Context.SaveChangesAsync();

            // Return success
            return new TaskResult<ulong>(true, "Successfully created planet.", planet.Id);
        }

        public Regex planetRegex = new Regex(@"^[a-zA-Z0-9_-]+$");

        /// <summary>
        /// Validates that a given name is allowable for a server
        /// </summary>
        public TaskResult ValidateName(string name)
        {
            if (name.Length > 32)
            {
                return new TaskResult(false, "Planet names must be 32 characters or less.");
            }

            if (!planetRegex.IsMatch(name))
            {
                return new TaskResult(false, "Planet names may only include letters, numbers, dashes, and underscores.");
            }

            return new TaskResult(true, "The given name is valid.");
        }

        /// <summary>
        /// Returns a planet object (if permitted)
        /// </summary>
        public async Task<TaskResult<Planet>> GetPlanet(ulong planetid, ulong userid, string token)
        {
            ServerPlanet planet = await ServerPlanet.FindAsync(planetid);

            if (planet == null)
            {
                return new TaskResult<Planet>(false, "The given planet id does not exist.", null);
            }

            if (!(await planet.AuthorizedAsync(userid, token))){
                return new TaskResult<Planet>(false, "You are not authorized to access this planet.", null);
            }

            return new TaskResult<Planet>(true, "Successfully retrieved planet.", planet);
        }

        /// <summary>
        /// Returns a planet's primary channel
        /// </summary>
        public async Task<TaskResult<PlanetChatChannel>> GetPrimaryChannel(ulong planetid, ulong userid, string token)
        {
            ServerPlanet planet = await ServerPlanet.FindAsync(planetid);

            if (!(await planet.AuthorizedAsync(userid, token)))
            {
                return new TaskResult<PlanetChatChannel>(false, "You are not authorized to access this planet.", null);
            }

            return new TaskResult<PlanetChatChannel>(true, "Successfully retireved channel.", await planet.GetPrimaryChannelAsync());
        }
    }
}
