using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Sockets;
using Microsoft.AspNetCore.SignalR;
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
using Valour.Shared.Oauth;
using Valour.Server.MSP;
using Valour.Server.Roles;
using Valour.Shared.Roles;
using Valour.Shared.Users;
using Valour.Server.Users;
using Valour.Server.Oauth;
using Valour.Server.Categories;


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

        [HttpPost]
        public async Task<TaskResult> UpdateOrder([FromBody] Dictionary<ushort, List<ulong>> json, ulong user_id, string token, ulong Planet_Id)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);
            
            // Return the same if the token is for the wrong user to prevent someone
            // from knowing if they cracked another user's token. This is basically 
            // impossible to happen by chance but better safe than sorry in the case that
            // the literal impossible odds occur, more likely someone gets a stolen token
            // but is not aware of the owner but I'll shut up now - Spike
            if (authToken == null || authToken.User_Id != user_id)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(Planet_Id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You are not authorized to do this.");
            }

            Console.WriteLine(json);
            var values = json;//JsonConvert.DeserializeObject<Dictionary<ulong, ulong>>(data);

            foreach (var value in values) {
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

            await PlanetHub.Current.Clients.Group($"p-{Planet_Id}").SendAsync("RefreshChannelList", "");
            return new TaskResult(true, "Updated Order!");
        }


        /// <summary>
        /// Creates a server and if successful returns a task result with the created
        /// planet's id
        /// </summary>
        public async Task<TaskResult<ulong>> CreatePlanet(string name, string image_url, string token)
        {
            TaskResult nameValid = ValidateName(name);

            if (!nameValid.Success)
            {
                return new TaskResult<ulong>(false, nameValid.Message, 0);
            }

            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<ulong>(false, "Failed to authorize user.", 0);
            }

            User user = await Context.Users.FindAsync(authToken.User_Id);

            if (await Context.Planets.CountAsync(x => x.Owner_Id == user.Id) > MAX_OWNED_PLANETS - 1)
            {
                return new TaskResult<ulong>(false, "You have hit your maximum planets!", 0);
            }

            // User is verified and given planet info is valid by this point
            // We don't actually need the user object which is cool

            // Use MSP for proxying image

            MSPResponse proxyResponse = await MSPManager.GetProxy(image_url);

            if (string.IsNullOrWhiteSpace(proxyResponse.Url) || !proxyResponse.Is_Media)
            {
                image_url = "https://valour.gg/image.png";
            }
            else
            {
                image_url = proxyResponse.Url;
            }

            ulong planet_id = IdManager.Generate();

            // Create general category
            ServerPlanetCategory category = new ServerPlanetCategory()
            {
                Id = IdManager.Generate(),
                Name = "General",
                Parent_Id = null,
                Planet_Id = planet_id,
                Position = 0
            };

            // Create general channel
            ServerPlanetChatChannel channel = new ServerPlanetChatChannel()
            {
                Id = IdManager.Generate(),
                Planet_Id = planet_id,
                Name = "General",
                Message_Count = 0,
                Description = "General chat channel",
                Parent_Id = category.Id
            };

            // Create default role
            ServerPlanetRole defaultRole = new ServerPlanetRole()
            {
                Id = IdManager.Generate(),
                Planet_Id = planet_id,
                Position = uint.MaxValue,
                Color_Blue = 255,
                Color_Green = 255,
                Color_Red = 255,
                Name = "@everyone"
            };

            ServerPlanet planet = new ServerPlanet()
            {
                Id = planet_id,
                Name = name,
                Member_Count = 1,
                Description = "A Valour server.",
                Image_Url = image_url,
                Public = true,
                Owner_Id = user.Id,
                Default_Role_Id = defaultRole.Id,
                Main_Channel_Id = channel.Id
            };

            // Add planet to database
            await Context.Planets.AddAsync(planet);
            await Context.SaveChangesAsync(); // We must do this first to prevent foreign key errors

            // Add category to database
            await Context.PlanetCategories.AddAsync(category);
            // Add channel to database
            await Context.PlanetChatChannels.AddAsync(channel);
            // Add default role to database
            await Context.PlanetRoles.AddAsync(defaultRole);
            // Save changes
            await Context.SaveChangesAsync();
            // Add owner to planet
            await planet.AddMemberAsync(user);

            // Return success
            return new TaskResult<ulong>(true, "Successfully created planet.", planet.Id);
        }

        public Regex planetRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

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
            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id);

            if (planet == null)
            {
                return new TaskResult<Planet>(false, "The given planet id does not exist.", null);
            }

            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.View))) {
                return new TaskResult<Planet>(false, "You are not authorized to access this planet.", null);
            }

            return new TaskResult<Planet>(true, "Successfully retrieved planet.", planet);
        }

        /// <summary>
        /// Returns a planet's primary channel
        /// </summary>
        public async Task<TaskResult<PlanetChatChannel>> GetPrimaryChannel(ulong planet_id, ulong user_id, string token)
        {
            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id);

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
            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id);

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
            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id);

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
            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id);

            if (!(await planet.AuthorizedAsync(token, PlanetPermissions.Manage)))
            {
                return new TaskResult(false, "You are not authorized to manage this planet.");
            }

            planet.Public = ispublic;

            Context.Planets.Update(planet);
            await Context.SaveChangesAsync();

            return new TaskResult(true, "Changed public value successfully");
        }

        /// <summary>
        /// Returns a planet member given the user and planet id
        /// </summary>
        public async Task<TaskResult<PlanetMember>> GetPlanetMember(ulong user_id, ulong planet_id, string auth)
        {
            // Retrieve planet
            ServerPlanet planet = ServerPlanet.FromBase(await Context.Planets.FindAsync(planet_id));

            if (planet == null) return new TaskResult<PlanetMember>(false, "The planet could not be found.", null);

            // Authentication flow
            AuthToken token = await Context.AuthTokens.FindAsync(auth);

            // If authorizor is not a member of the planet, they do not have authority to get member info
            if (token == null || !(await planet.IsMemberAsync(token.User_Id)))
            {
                return new TaskResult<PlanetMember>(false, "Failed to authorize.", null);
            }

            // At this point the request is authorized

            PlanetMember member = await Context.PlanetMembers.FirstOrDefaultAsync(x => x.User_Id == user_id && x.Planet_Id == planet_id);

            if (member == null)
            {
                return new TaskResult<PlanetMember>(false, "Could not find member.", null);
            }

            return new TaskResult<PlanetMember>(true, "Successfully retrieved planet user.", member);
        }

        /// <summary>
        /// Returns a planet member's role ids given the user and planet id
        /// </summary>
        public async Task<TaskResult<List<ulong>>> GetPlanetMemberRoleIds(ulong user_id, ulong planet_id, string token)
        {
            // Retrieve planet
            ServerPlanet planet = ServerPlanet.FromBase(await Context.Planets.FindAsync(planet_id));

            if (planet == null) return new TaskResult<List<ulong>>(false, "The planet could not be found.", null);

            // Authentication flow
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            // If authorizor is not a member of the planet, they do not have authority to get member info
            if (authToken == null || !(await planet.IsMemberAsync(authToken.User_Id)))
            {
                return new TaskResult<List<ulong>>(false, "Failed to authorize.", null);
            }

            var roles = Context.PlanetRoleMembers.Include(x => x.Role).Where(x => x.User_Id == user_id && x.Planet_Id == planet_id).OrderBy(x => x.Role.Position);
            var roleids = roles.Select(x => x.Role_Id).ToList();

            return new TaskResult<List<ulong>>(true, $"Retrieved {roleids.Count} roles.", roleids);
        }

        /// <summary>
        /// Returns the planet membership of a user
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        public async Task<TaskResult<List<Planet>>> GetPlanetMembership(ulong user_id, string token)
        {
            if (token == null)
            {
                return new TaskResult<List<Planet>>(false, "Please supply an authentication token", null);
            }

            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (!Permission.HasPermission(authToken.Scope, UserPermissions.Membership))
            {
                return new TaskResult<List<Planet>>(false, $"The given token does not have membership scope", null);
            }

            User user = await Context.Users.FindAsync(user_id);

            if (user == null)
            {
                return new TaskResult<List<Planet>>(false, $"Could not find user {user_id}", null);
            }

            List<Planet> membership = new List<Planet>();

            ServerPlanet valourServer = await ServerPlanet.FindAsync(735703679107072);

            if (valourServer != null)
            {
                // Remove this after pre-pre-alpha
                if (!(await valourServer.IsMemberAsync(user_id, Context)))
                {
                    await valourServer.AddMemberAsync(user, Context);
                }
            }

            foreach (PlanetMember member in Context.PlanetMembers.Where(x => x.User_Id == user_id))
            {
                Planet planet = await Context.Planets.FindAsync(member.Planet_Id);

                if (planet != null)
                {
                    membership.Add(planet);
                }
            }

            return new TaskResult<List<Planet>>(true, $"Retrieved {membership.Count} planets", membership);
        }

        public async Task<TaskResult<List<ulong>>> GetPlanetUserIds(ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            // If not a member of planet
            if (authToken == null || !(await Context.PlanetMembers.AnyAsync(x => x.User_Id == authToken.User_Id && x.Planet_Id == planet_id)))
            {
                return new TaskResult<List<ulong>>(false, $"Could not authenticate.", null);
            }

            var list = Context.PlanetMembers.Where(x => x.Planet_Id == planet_id).Select(x => x.User_Id).ToList();

            return new TaskResult<List<ulong>>(true, $"Retrieved {list.Count()} users", list);
        }

        public async Task<TaskResult<List<PlanetMemberInfo>>> GetPlanetMemberInfo(ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            // If not a member of planet
            if (authToken == null || !(await Context.PlanetMembers.AnyAsync(x => x.User_Id == authToken.User_Id && x.Planet_Id == planet_id)))
            {
                return new TaskResult<List<PlanetMemberInfo>>(false, $"Could not authenticate.", null);
            }

            var members = Context.PlanetMembers.AsQueryable()
                                               .Where(x => x.Planet_Id == planet_id)
                                               .Include(x => x.User)
                                               .Include(x => x.RoleMembership
                                               .OrderBy(x => x.Role.Position))
                                               .ThenInclude(x => x.Role);

            List<PlanetMemberInfo> info = new List<PlanetMemberInfo>();

            foreach (var member in members)
            {
                PlanetMemberInfo planetInfo = new PlanetMemberInfo()
                {
                    Member = member,
                    User = member.User,
                    RoleIds = member.RoleMembership.Select(x => x.Role_Id).ToList(),
                    State = "Currently browsing"
                };

                info.Add(planetInfo);
            }

            return new TaskResult<List<PlanetMemberInfo>>(true, $"Retrieved {members.Count()} member info objects", info);
        }

        public async Task<TaskResult> KickUser(ulong target_id, ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.Kick)))
            {
                return new TaskResult(false, "You are not authorized to do this.");
            }

            ServerPlanetMember member = await Context.PlanetMembers.FirstOrDefaultAsync(x => x.Id == target_id && x.Planet_Id == planet_id);

            if (member == null)
            {
                return new TaskResult(true, $"Could not find PlanetMember {target_id}");
            }

            List<ServerPlanetRoleMember> roles = await Task.Run(() => Context.PlanetRoleMembers.Where(x => x.Member_Id == target_id && x.Planet_Id == planet_id).ToList());

            foreach(ServerPlanetRoleMember role in roles) {
                Context.PlanetRoleMembers.Remove(role);
            }

            Context.PlanetMembers.Remove(member);

            await Context.SaveChangesAsync();

            return new TaskResult(true, $"Successfully kicked user {target_id}");
        }

        public async Task<TaskResult> BanUser(ulong target_id, ulong planet_id, string reason, string token, uint time)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(planet_id);

            if (!(await planet.AuthorizedAsync(authToken, PlanetPermissions.Ban)))
            {
                return new TaskResult(false, "You are not authorized to do this.");
            }

            ServerPlanetMember member = await Context.PlanetMembers.FirstOrDefaultAsync(x => x.Id == target_id &&
                                                                                             x.Planet_Id == planet_id);

            PlanetBan ban = new PlanetBan()
            {
                Id = IdManager.Generate(),
                Reason = reason,
                Planet_Id = planet_id,
                User_Id = member.User_Id,
                Banner_Id = authToken.User_Id,
                Time = DateTime.UtcNow,
                Permanent = false
            };

            if (time <= 0)
            {
                ban.Permanent = true;
            }
            else
            {
                ban.Minutes = time;
            }

            // Add channel to database
            await Context.PlanetBans.AddAsync(ban);

            List<ServerPlanetRoleMember> roles = await Task.Run(() => Context.PlanetRoleMembers.Where(x => x.Member_Id == target_id && x.Planet_Id == planet_id).ToList());

            foreach(ServerPlanetRoleMember role in roles) {
                Context.PlanetRoleMembers.Remove(role);
            }

            Context.PlanetMembers.Remove(member);
            await Context.SaveChangesAsync();

            return new TaskResult(true, $"Successfully banned user {member.Nickname}");
        }

        /// <summary>
        /// Returns all of the roles in a planet
        /// </summary>
        public async Task<TaskResult<List<ServerPlanetRole>>> GetPlanetRoles(ulong planet_id, string token){

            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<List<ServerPlanetRole>>(false, "Failed to authorize user.", null);
            }

            ServerPlanet planet = await Context.Planets.FindAsync(planet_id);

            if (planet == null)
            {
                return new TaskResult<List<ServerPlanetRole>>(false, $"Could not find planet {planet_id}", null);
            }

            if (!(await planet.IsMemberAsync(authToken.User_Id, Context)))
            {
                return new TaskResult<List<ServerPlanetRole>>(false, "Failed to authorize user.", null);
            }

            var roles = await planet.GetRolesAsync(Context);

            return new TaskResult<List<ServerPlanetRole>>(true, $"Found {roles.Count} roles.", roles);
        }

        /// <summary>
        /// Returns a role in a planet
        /// </summary>
        public async Task<TaskResult<PlanetRole>> GetPlanetRole(ulong role_id, string token)
        {

            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            PlanetRole role = await Context.PlanetRoles.FindAsync(role_id);

            if (authToken == null)
            {
                return new TaskResult<PlanetRole>(false, "Failed to authorize user.", null);
            }

            ServerPlanet planet = await ServerPlanet.FindAsync(role.Planet_Id);

            if (planet == null)
            {
                return new TaskResult<PlanetRole>(false, $"Could not find planet {role.Planet_Id}", null);
            }

            if (!(await planet.IsMemberAsync(authToken.User_Id)))
            {
                return new TaskResult<PlanetRole>(false, "Failed to authorize user.", null);
            }

            return new TaskResult<PlanetRole>(true, $"Retrieved role.", role);
        }

        /// <summary>
        /// Returns the planet name
        /// </summary>
        public async Task<TaskResult<string>> GetPlanetName(ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<string>(false, "Failed to authorize user.", null);
            }

            ServerPlanet planet = await Context.Planets.FindAsync(planet_id);

            if (planet == null)
            {
                return new TaskResult<string>(false, $"Could not find planet {planet_id}", null);
            }

            ServerUser user = await Context.Users.FindAsync(authToken.User_Id);

            if (!(await planet.IsMemberAsync(authToken.User_Id, Context)))
            {
                return new TaskResult<string>(false, "You are not a member.", null);
            }

            return new TaskResult<string>(true, $"Success", planet.Name);
        }


        /// <summary>
        /// Creates the requested role
        /// </summary>
        public async Task<TaskResult> CreateRole([FromBody] ServerPlanetRole role, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!Permission.HasPermission(authToken.Scope, UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "You don't have planet management scope.");
            }

            ServerPlanetMember member = await Context.PlanetMembers.Include(x => x.User)
                                                                   .FirstOrDefaultAsync(x => x.User_Id == authToken.User_Id &&
                                                                                             x.Planet_Id == role.Planet_Id);

            if (member == null)
            {
                return new TaskResult(false, "You're not in the planet!");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageRoles)))
            {
                return new TaskResult(false, "You don't have role management permissions!");
            }

            // Set role id
            role.Id = IdManager.Generate();

            // Set to next open position
            role.Position = (uint) await Context.PlanetRoles.Where(x => x.Planet_Id == role.Planet_Id).CountAsync();

            await Context.PlanetRoles.AddAsync(role);
            await Context.SaveChangesAsync();

            await PlanetHub.NotifyRoleChange(role);
            
            return new TaskResult(true, $"Role {role.Id} successfully added to position {role.Position}.");
        }

        /// <summary>
        /// Creates the requested role
        /// </summary>
        public async Task<TaskResult> EditRole([FromBody] ServerPlanetRole role, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!Permission.HasPermission(authToken.Scope, UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "You don't have planet management scope.");
            }

            if (!(await Context.PlanetRoles.AnyAsync(x => x.Id == role.Id)))
            {
                return new TaskResult(false, $"The role {role.Id} does not exist.");
            }

            ServerPlanetMember member = await Context.PlanetMembers.Include(x => x.User)
                                                                   .Include(x => x.Planet)
                                                                   .FirstOrDefaultAsync(x => x.User_Id == authToken.User_Id &&
                                                                                             x.Planet_Id == role.Planet_Id);

            if (member == null)
            {
                return new TaskResult(false, "You're not in the planet!");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageRoles)))
            {
                return new TaskResult(false, "You don't have role management permissions!");
            }

            // Do not allow modifying roles with a lower position than your own (lower is more powerful)
            if (member.User_Id != member.Planet.Owner_Id)
            {
                if (((await member.GetPrimaryRoleAsync()).Position > role.Position))
                {
                    return new TaskResult(false, "You can't edit a role above your own!");
                }
            }

            Context.PlanetRoles.Update(role);
            await Context.SaveChangesAsync();

            await PlanetHub.NotifyRoleChange(role);

            return new TaskResult(true, $"Successfully edited role {role.Id}.");
        }

        /// <summary>
        /// Deletes the given role
        /// </summary>
        public async Task<TaskResult> DeleteRole(ulong role_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            if (!Permission.HasPermission(authToken.Scope, UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "You don't have planet management scope.");
            }

            ServerPlanetRole role = await Context.PlanetRoles.FindAsync(role_id);

            if (!(await Context.PlanetRoles.AnyAsync(x => x.Id == role.Id)))
            {
                return new TaskResult(false, $"The role {role.Id} does not exist.");
            }

            ServerPlanetMember member = await Context.PlanetMembers.Include(x => x.User)
                                                                   .Include(x => x.Planet)
                                                                   .FirstOrDefaultAsync(x => x.User_Id == authToken.User_Id &&
                                                                                             x.Planet_Id == role.Planet_Id);

            if (member == null)
            {
                return new TaskResult(false, "You're not in the planet!");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageRoles)))
            {
                return new TaskResult(false, "You don't have role management permissions!");
            }

            // Do not allow modifying roles with a lower position than your own (lower is more powerful)
            if (member.User_Id != member.Planet.Owner_Id)
            {
                if (((await member.GetPrimaryRoleAsync()).Position > role.Position))
                {
                    return new TaskResult(false, "You can't delete a role above your own!");
                }
            }

            if (member.Planet.Default_Role_Id == role.Id)
            {
                return new TaskResult(false, "You can't delete the default role!");
            }

            // Remove role members first

            var roleMembers = Context.PlanetRoleMembers.Where(x => x.Role_Id == role_id);
            Context.PlanetRoleMembers.RemoveRange(roleMembers);

            // Then remove role nodes

            var roleChannelNodes = Context.ChatChannelPermissionsNodes.Where(x => x.Role_Id == role_id);
            Context.ChatChannelPermissionsNodes.RemoveRange(roleChannelNodes);

            // Finally remove the role

            Context.PlanetRoles.Remove(role);

            await Context.SaveChangesAsync();

            await PlanetHub.NotifyRoleChange(role);

            return new TaskResult(true, $"Successfully removed role {role.Id}.");
        }

        /// <summary>
        /// Sets whether or not a member is in a role
        /// </summary>
        public async Task<TaskResult> SetMemberRoleMembership(ulong role_id, ulong member_id, bool value, string token)
        {
            ServerAuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult(false, "Failed to authorize user.");
            }

            // Oauth protection
            if (!Permission.HasPermission(authToken.Scope, UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "You don't have planet management scope.");
            }

            // Retrieve role
            ServerPlanetRole role = await Context.PlanetRoles.Include(x => x.Planet)
                                                             .FirstOrDefaultAsync(x => x.Id == role_id);

            if (role == null)
            {
                return new TaskResult(false, $"Role {role_id} could not be found.");
            }

            ServerPlanetMember authMember = await Context.PlanetMembers.Include(x => x.RoleMembership)
                                                                       .ThenInclude(x => x.Role)
                                                                       .FirstOrDefaultAsync(x => x.User_Id == authToken.User_Id &&
                                                                       x.Planet_Id == role.Planet_Id);

            // If the authorizor is not in the planet
            if (authMember == null)
            {
                return new TaskResult(false, $"You are not in the target planet!");
            }

            // Get target member

            var targetMember = await Context.PlanetMembers.FindAsync(member_id);

            if (targetMember == null)
            {
                return new TaskResult(false, $"Could not find member with id {member_id}");
            }

            // Get auth primary role
            var primaryAuthRole = authMember.RoleMembership.OrderBy(x => x.Role.Position).FirstOrDefault();

            if (authMember.Planet_Id != authMember.Planet.Owner_Id && primaryAuthRole == null)
            {
                return new TaskResult(false, $"Error: Issue retrieving primary role for authorizor");
            }

            if (!(await authMember.HasPermissionAsync(PlanetPermissions.ManageRoles, Context)))
            {
                return new TaskResult(false, $"You don't have permission to manage roles");
            }

            // Ensure that the role being set is *lower* than their own role
            if (role.Planet.Owner_Id != authMember.User_Id &&
                role.Position <= primaryAuthRole.Role.Position)
            {
                return new TaskResult(false, $"You cannot set roles that aren't below your own");
            }
            
            // At this point, authorization should be complete
            
            // Add the role
            if (value)
            {
                // Ensure it isn't already there
                if (await Context.PlanetRoleMembers.AnyAsync(x => x.Member_Id == targetMember.Id &&
                                                                  x.Role_Id == role.Id))
                {
                    return new TaskResult(true, $"The user already has the role.");
                }

                ServerPlanetRoleMember roleMember = new ServerPlanetRoleMember()
                {
                    Id = IdManager.Generate(),
                    Member_Id = targetMember.Id,
                    Role_Id = role.Id,
                    User_Id = targetMember.User_Id,
                    Planet_Id = targetMember.Planet_Id
                };

                await Context.PlanetRoleMembers.AddAsync(roleMember);
            }
            // Remove the role
            else
            {
                if (role.Id == authMember.Planet.Default_Role_Id)
                {
                    return new TaskResult(false, $"Cannot remove the default role!");
                }

                var currentRoleMember = await Context.PlanetRoleMembers.FirstOrDefaultAsync(x => x.Member_Id == targetMember.Id &&
                                                                                                 x.Role_Id == role.Id);

                // Ensure the user actually has the role
                if (currentRoleMember == null)
                {
                    return new TaskResult(true, $"The user doesn't have the role.");
                }

                Context.PlanetRoleMembers.Remove(currentRoleMember);
            }

            // Save changes
            await Context.SaveChangesAsync();

            // Send ping that the member was modified (new role)
            await PlanetHub.NotifyMemberChange(targetMember);

            return new TaskResult(true, $"Successfully set role membership to {value}");
        }

        /// <summary>
        /// Returns the roles for a given member
        /// </summary>
        public async Task<TaskResult<List<ServerPlanetRole>>> GetMemberRoles(ulong member_id, string token)
        {
            ServerAuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<List<ServerPlanetRole>>(false, "Failed to authorize user.", null);
            }

            ServerPlanetMember member = await Context.PlanetMembers.Include(x => x.Planet)
                                                                   .FirstOrDefaultAsync(x => x.Id == member_id);

            if (member == null)
            {
                return new TaskResult<List<ServerPlanetRole>>(false, "Member does not exist.", null);
            }

            if (!(await member.Planet.IsMemberAsync(authToken.User_Id, Context))){
                return new TaskResult<List<ServerPlanetRole>>(false, "You are not in the planet.", null);
            }

            var roles = await member.GetRolesAsync(Context);

            return new TaskResult<List<ServerPlanetRole>>(true, $"Found {roles.Count} roles.", roles);
        }

        /// <summary>
        /// Returns the primary role for a given member
        /// </summary>
        public async Task<TaskResult<ServerPlanetRole>> GetMemberPrimaryRole(ulong member_id, string token)
        {
            ServerAuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<ServerPlanetRole>(false, "Failed to authorize user.", null);
            }

            ServerPlanetMember member = await Context.PlanetMembers.Include(x => x.Planet)
                                                                   .FirstOrDefaultAsync(x => x.Id == member_id);

            if (member == null)
            {
                return new TaskResult<ServerPlanetRole>(false, "Member does not exist.", null);
            }

            if (!(await member.Planet.IsMemberAsync(authToken.User_Id, Context)))
            {
                return new TaskResult<ServerPlanetRole>(false, "You are not in the planet.", null);
            }

            var role = await member.GetPrimaryRoleAsync(Context);

            return new TaskResult<ServerPlanetRole>(true, $"Found primary role.", role);
        }

        /// <summary>
        /// Returns the authority of the requested member
        /// </summary>
        public async Task<TaskResult<uint>> GetMemberAuthority(ulong member_id, string token)
        {
            ServerAuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<uint>(false, "Failed to authorize user.", 0);
            }

            ServerPlanetMember member = await Context.PlanetMembers.Include(x => x.Planet)
                                                                   .FirstOrDefaultAsync(x => x.Id == member_id);

            if (member == null)
            {
                return new TaskResult<uint>(false, "Member does not exist.", 0);
            }

            if (!(await member.Planet.IsMemberAsync(authToken.User_Id, Context)))
            {
                return new TaskResult<uint>(false, "You are not in the planet.", 0);
            }

            return new TaskResult<uint>(true, "Found authority", await member.GetAuthorityAsync());
        }
    }
}
