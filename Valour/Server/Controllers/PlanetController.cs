using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Server.Planets;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Categories;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using Valour.Server.Oauth;
using Valour.Server.MSP;


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

        /// <summary>
        /// Mapper object
        /// </summary>
        private readonly IMapper Mapper;

        // Dependency injection
        public PlanetController(ValourDB context, IMapper mapper)
        {
            this.Context = context;
            this.Mapper = mapper;
        }

        public async Task<TaskResult> BanUser(ulong id, ulong Planet_Id, string reason, ulong userid, string token, uint time)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(Planet_Id, Mapper);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.Ban)))
            {
                return new TaskResult(false, "You are not authorized to do this.");
            }

            PlanetBan ban = new PlanetBan()
            {
                Reason = reason,
                Planet_Id = Planet_Id,
                User_Id = id,
                Banner_Id = userid,
                Time = DateTime.UtcNow,
                Permanent = false
            };
            
            if (time <= 0) {
                ban.Permanent = true;
            }
            else {
                ban.Minutes = time;
            }

            // Add channel to database
            await Context.PlanetBans.AddAsync(ban);

            PlanetMember member = await Context.PlanetMembers.Where(x => x.User_Id == id).FirstOrDefaultAsync();

            Context.PlanetMembers.Remove(member);
            await Context.SaveChangesAsync();

            return new TaskResult(true, $"Successfully banned user {id}");
        }

        [HttpPost]
        public async Task<TaskResult> UpdateOrder([FromBody]Dictionary<ushort, List<ulong>> json, ulong userid, string token)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != userid)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }
            Console.WriteLine(json);
            var values = json;//JsonConvert.DeserializeObject<Dictionary<ulong, ulong>>(data);
            foreach(var value in values) {
                ushort position = value.Key;
                ulong id = value.Value[0];

            //checks if item is a channel
                Console.WriteLine(value.Value[0]);
                if (value.Value[1] == 0) {
                    PlanetChatChannel channel = await Context.PlanetChatChannels.Where(x => x.Id == id).FirstOrDefaultAsync();
                    channel.Position = position;
                }
                if (value.Value[1] == 1) {
                    PlanetCategory category = await Context.PlanetCategories.Where(x => x.Id == id).FirstOrDefaultAsync();
                    category.Position = position;
                }

               Console.WriteLine(value);
            }
            await Context.SaveChangesAsync();
            return new TaskResult(true, "Updated Order!");
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

            if (await Context.Planets.CountAsync(x => x.Owner_Id == userid) > MAX_OWNED_PLANETS - 1)
            {
                return new TaskResult<ulong>(false, "You have hit your maximum planets!", 0);
            }

            // User is verified and given planet info is valid by this point
            // We don't actually need the user object which is cool

            // Use MSP for proxying image

            MSPResponse proxyResponse = await MSPManager.GetProxy(image_url);

            if (string.IsNullOrWhiteSpace(proxyResponse.Url) ||!proxyResponse.Is_Media)
            {
                image_url = "https://valour.gg/image.png";
            }
            else
            {
                image_url = proxyResponse.Url;
            }
            

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
                Message_Count = 0,
                Description = "General chat channel"
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
            if (string.IsNullOrWhiteSpace(name))
            {
                return new TaskResult(false, "Planet names cannot be empty.");
            }

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
        public async Task<TaskResult<Planet>> GetPlanet(ulong planet_id, string token)
        {
            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id, Mapper);

            if (planet == null)
            {
                return new TaskResult<Planet>(false, "The given planet id does not exist.", null);
            }

            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.View))){
                return new TaskResult<Planet>(false, "You are not authorized to access this planet.", null);
            }

            return new TaskResult<Planet>(true, "Successfully retrieved planet.", planet);
        }

        /// <summary>
        /// Returns a planet's primary channel
        /// </summary>
        public async Task<TaskResult<PlanetChatChannel>> GetPrimaryChannel(ulong planet_id, ulong userid, string token)
        {
            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id, Mapper);

            if (!(await planet.AuthorizedAsync(token, PlanetPermissions.View)))
            {
                return new TaskResult<PlanetChatChannel>(false, "You are not authorized to access this planet.", null);
            }

            return new TaskResult<PlanetChatChannel>(true, "Successfully retireved channel.", await planet.GetPrimaryChannelAsync());
        }

        /// <summary>
        /// Sets the name of a planet
        /// </summary>
        public async Task<TaskResult> SetName(ulong planet_id, string name, string token)
        {
            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id, Mapper);

            if (!(await planet.AuthorizedAsync(token, PlanetPermissions.Manage)))
            {
                return new TaskResult(false, "You are not authorized to manage this planet.");
            }

            TaskResult validation = ValidateName(name);

            if (!validation.Success)
            {
                return validation;
            }

            planet.Name = name;

            Context.Planets.Update(planet);
            await Context.SaveChangesAsync();

            return new TaskResult(true, "Changed name successfully");
        }

        /// <summary>
        /// Sets the description of a planet
        /// </summary>
        public async Task<TaskResult> SetDescription(ulong planet_id, string description, string token)
        {
            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id, Mapper);

            if (!(await planet.AuthorizedAsync(token, PlanetPermissions.Manage)))
            {
                return new TaskResult(false, "You are not authorized to manage this planet.");
            }

            planet.Description = description;

            Context.Planets.Update(planet);
            await Context.SaveChangesAsync();

            return new TaskResult(true, "Changed description successfully");
        }

        /// <summary>
        /// Sets the description of a planet
        /// </summary>
        public async Task<TaskResult> SetPublic(ulong planet_id, bool ispublic, string token)
        {
            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id, Mapper);

            if (!(await planet.AuthorizedAsync(token, PlanetPermissions.Manage)))
            {
                return new TaskResult(false, "You are not authorized to manage this planet.");
            }

            planet.Public = ispublic;

            Context.Planets.Update(planet);
            await Context.SaveChangesAsync();

            return new TaskResult(true, "Changed public value successfully");
        }
    }
}
