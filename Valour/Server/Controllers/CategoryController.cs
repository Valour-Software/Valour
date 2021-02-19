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
using Microsoft.EntityFrameworkCore;
using Valour.Server.Planets;
using Valour.Server.Oauth;
using AutoMapper;

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
    public class CategoryController
    {

        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;

        private readonly IMapper Mapper;

        // Dependency injection
        public CategoryController(ValourDB context, IMapper mapper)
        {
            this.Context = context;
            this.Mapper = mapper;
        }

        public async Task<TaskResult> SetName(string name, ulong id, ulong user_id, string token)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != user_id)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            PlanetCategory category = await Context.PlanetCategories.Where(x => x.Id == id).FirstOrDefaultAsync();

            ServerPlanet planet = await ServerPlanet.FindAsync(category.Planet_Id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.ManageCategories)))
            {
                return new TaskResult(false, "You are not authorized to do this.");
            }

            category.Name = name;

            await Context.SaveChangesAsync();

            await PlanetHub.Current.Clients.Group($"p-{category.Planet_Id}").SendAsync("RefreshChannelList", "");

            return new TaskResult(true, "Successfully set name.");
        }

        public async Task<TaskResult> Delete(ulong id, ulong user_id, string token)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != user_id)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            PlanetCategory category = await Context.PlanetCategories.FindAsync(id);

            ServerPlanet planet = await ServerPlanet.FindAsync(category.Planet_Id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.ManageCategories)))
            {
                return new TaskResult(false, "You are not authorized to do this.");
            }

            List<PlanetCategory> cate = await Task.Run(() => Context.PlanetCategories.Where(x => x.Planet_Id == category.Planet_Id).ToList());

            if (cate.Count == 1) {
                return new TaskResult(false, "You can not delete your last category!");
            }

            List<PlanetCategory> categories = await Task.Run(() => Context.PlanetCategories.Where(x => x.Parent_Id == id).ToList());

            List<PlanetChatChannel> channels = await Task.Run(() => Context.PlanetChatChannels.Where(x => x.Parent_Id == id).ToList());

            //Check if any channels in this category are the main channel
            foreach(PlanetChatChannel channel in channels) {
                if (channel.Id == planet.Main_Channel_Id) {
                    return new TaskResult(false, "You can not delete your main channel!");
                }
            }
            //If not, then delete the channels
            foreach(PlanetChatChannel channel in channels) {
                Context.PlanetChatChannels.Remove(channel);
            }

            foreach(PlanetCategory Category in categories)
            {
                Category.Parent_Id = null;
                
            }

            ulong parentId = category.Planet_Id;

            Context.PlanetCategories.Remove(category);
            
            await Context.SaveChangesAsync();

            await PlanetHub.Current.Clients.Group($"p-{parentId}").SendAsync("RefreshChannelList", "");

            return new TaskResult(true, "Successfully deleted.");
        }

        public async Task<TaskResult> SetParentId(ulong id, ulong parentId, ulong user_id, string token)
        {
            AuthToken authToken = await Context.AuthTokens.FindAsync(token);

            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != user_id)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            PlanetCategory category = await Context.PlanetCategories.Where(x => x.Id == id).FirstOrDefaultAsync();

            ServerPlanet planet = await ServerPlanet.FindAsync(category.Planet_Id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.ManageCategories)))
            {
                return new TaskResult(false, "You are not authorized to do this.");
            }

            if (parentId == 0) {
                category.Parent_Id = null;
            }
            else {
                category.Parent_Id = parentId;
            }

            await Context.SaveChangesAsync();

            await PlanetHub.Current.Clients.Group($"p-{planet.Id}").SendAsync("RefreshChannelList", "");
            
            return new TaskResult(true, "Successfully set parent id.");
            
        }

        /// <summary>
        /// Creates a server and if successful returns a task result with the created
        /// planet's id
        /// </summary>
        public async Task<TaskResult<ulong>> CreateCategory(string name, ulong user_id, ulong parentid, ulong planet_id, string token)
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
            if (authToken == null || authToken.User_Id != user_id)
            {
                return new TaskResult<ulong>(false, "Failed to authorize user.", 0);
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.ManageCategories)))
            {
                return new TaskResult<ulong>(false, "You are not authorized to do this.", 0);
            }

            // User is verified and given channel info is valid by this point

            // Creates the channel channel

            PlanetCategory category = new PlanetCategory()
            {
                Id = IdManager.Generate(),
                Name = name,
                Planet_Id = planet_id,
                Parent_Id = parentid
            };

            // Add channel to database
            await Context.PlanetCategories.AddAsync(category);

            // Save changes to DB
            await Context.SaveChangesAsync();

            await PlanetHub.Current.Clients.Group($"p-{planet_id}").SendAsync("RefreshChannelList", "");

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
