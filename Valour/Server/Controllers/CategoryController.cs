using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Channels;
using Valour.Shared.Messages;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using Valour.Shared.Categories;
using System.Text.RegularExpressions;

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
    public class CategoryController
    {

        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;

        // Dependency injection
        public CategoryController(ValourDB context)
        {
            this.Context = context;
        }
        
        /// <summary>
        /// Creates a server and if successful returns a task result with the created
        /// planet's id
        /// </summary>
        public async Task<TaskResult<ulong>> CreateCategory(string name, ulong userid, ulong parentid, ulong planet_id, string token)
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

            // User is verified and given channel info is valid by this point

            // Creates the channel channel

            PlanetCategory category = new PlanetCategory()
            {
                Name = name,
                Planet_Id = planet_id,
                Category_Id = parentid
            };

            // Add channel to database
            await Context.PlanetCategories.AddAsync(category);

            // Save changes to DB
            await Context.SaveChangesAsync();

            // Return success
            return new TaskResult<ulong>(true, "Successfully created category.", category.Id);
        }

        public Regex planetRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

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

        [HttpGet]
        public async Task<TaskResult<IEnumerable<PlanetCategory>>> GetPlanetCategoriesAsync(ulong planet_id)
        {
            IEnumerable<PlanetCategory> categories = await Task.Run(() => Context.PlanetCategories.Where(c => c.Planet_Id == planet_id).ToList());

            // in case theres 0 categories or "General" does not exist
            if (categories.Count() == 0 || !(categories.Any(x => x.Name == "General"))) {
                PlanetCategory category = new PlanetCategory();
                category.Name = "General";
                category.Planet_Id = planet_id;
                await Context.PlanetCategories.AddAsync(category);
                await Context.SaveChangesAsync();
                categories = await Task.Run(() => Context.PlanetCategories.Where(c => c.Planet_Id == planet_id).ToList());
            }

            return new TaskResult<IEnumerable<PlanetCategory>>(true, "Successfully retrieved Categories.", categories);
        }
    }
}
