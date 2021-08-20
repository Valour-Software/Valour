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
using Valour.Shared.Categories;
using Valour.Shared.Oauth;
using Valour.Shared.Planets;
using System;
using System.Collections.Generic;
using System.Linq;
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

        // Dependency injection
        public PlanetController(ValourDB context)
        {
            this.Context = context;
        }

        /// <summary>
        /// Sets the description of a planet
        /// </summary>
        public async Task<TaskResult> SetPublic(ulong planet_id, bool ispublic, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanet planet = await Context.Planets.Include(x => x.Members.Where(x => x.User_Id == authToken.User_Id))
                                                       .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet == null)
            {
                return new TaskResult(false, "The given planet id does not exist.");
            }

            ServerPlanetMember member = planet.Members.FirstOrDefault();

            if (!await planet.HasPermissionAsync(member, PlanetPermissions.Manage, Context))
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

            var members = Context.PlanetMembers.Where(x => x.Planet_Id == planet_id)
                                               .Include(x => x.User)
                                               .Include(x => x.RoleMembership.OrderBy(x => x.Role.Position))
                                               .ThenInclude(x => x.Role);

            //Console.WriteLine("Elapsed: " + sw.ElapsedMilliseconds);

            List<PlanetMemberInfo> info = new List<PlanetMemberInfo>();

            foreach (var member in members)
            {
                PlanetMemberInfo planetInfo = new PlanetMemberInfo()
                {
                    Member = member,
                    User = member.User,
                    RoleIds = member.RoleMembership.Select(x => x.Role_Id),
                    State = "Currently browsing"
                };

                info.Add(planetInfo);
            }

            return new TaskResult<List<PlanetMemberInfo>>(true, $"Retrieved {members.Count()} member info objects", info);
        }

        public async Task<TaskResult> KickUser(ulong target_id, ulong planet_id, string token)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanet planet = await Context.Planets.Include(x => x.Members.Where(x => x.User_Id == authToken.User_Id))
                                                       .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet == null)
            {
                return new TaskResult(false, "The given planet id does not exist.");
            }

            ServerPlanetMember member = planet.Members.FirstOrDefault();

            if (!await planet.HasPermissionAsync(member, PlanetPermissions.Kick, Context))
            {
                return new TaskResult(false, "You are not authorized to kick in this planet.");
            }

            ServerPlanetMember target = await Context.PlanetMembers.FirstOrDefaultAsync(x => x.Id == target_id);

            if (target == null)
            {
                return new TaskResult(true, $"Could not find PlanetMember {target_id}");
            }

            List<ServerPlanetRoleMember> roles = await Task.Run(() => Context.PlanetRoleMembers.Where(x => x.Member_Id == target_id).ToList());

            foreach(ServerPlanetRoleMember role in roles) {
                Context.PlanetRoleMembers.Remove(role);
            }

            Context.PlanetMembers.Remove(target);

            await Context.SaveChangesAsync();

            return new TaskResult(true, $"Successfully kicked user {target_id}");
        }

        public async Task<TaskResult> BanUser(ulong target_id, ulong planet_id, string reason, string token, uint time)
        {
            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            ServerPlanet planet = await Context.Planets.Include(x => x.Members.Where(x => x.User_Id == authToken.User_Id))
                                                       .FirstOrDefaultAsync(x => x.Id == planet_id);

            if (planet == null)
            {
                return new TaskResult(false, "The given planet id does not exist.");
            }

            ServerPlanetMember member = planet.Members.FirstOrDefault();

            if (!await planet.HasPermissionAsync(member, PlanetPermissions.Ban, Context))
            {
                return new TaskResult(false, "You are not authorized to ban in this planet.");
            }

            ServerPlanetMember target = await Context.PlanetMembers.FirstOrDefaultAsync(x => x.Id == target_id);

            PlanetBan ban = new PlanetBan()
            {
                Id = IdManager.Generate(),
                Reason = reason,
                Planet_Id = planet_id,
                User_Id = target.User_Id,
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

            Context.PlanetMembers.Remove(target);
            await Context.SaveChangesAsync();

            return new TaskResult(true, $"Successfully banned user {target.Nickname}");
        }

        /// <summary>
        /// Returns all of the roles in a planet
        /// </summary>
        public async Task<TaskResult<ICollection<ServerPlanetRole>>> GetPlanetRoles(ulong planet_id, string token){

            AuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<ICollection<ServerPlanetRole>>(false, "Failed to authorize user.", null);
            }

            ServerPlanetMember member = await Context.PlanetMembers.Include(x => x.Planet)
                                                                   .ThenInclude(x => x.Roles)
                                                                   .FirstOrDefaultAsync(x => x.Planet_Id == planet_id &&
                                                                                             x.User_Id == authToken.User_Id);

            if (member == null)
            {
                return new TaskResult<ICollection<ServerPlanetRole>>(false, "Failed to find member.", null);
            }

            return new TaskResult<ICollection<ServerPlanetRole>>(true, $"Found {member.Planet.Roles.Count} roles.", member.Planet.Roles);
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

            ServerPlanetMember member = await Context.PlanetMembers.FirstOrDefaultAsync(x => x.Planet_Id == role.Planet_Id &&
                                                                                             x.User_Id == authToken.User_Id);

            if (member != null)
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

            PlanetHub.NotifyRoleChange(role);
            
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

            PlanetHub.NotifyRoleChange(role);

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

            PlanetHub.NotifyRoleChange(role);

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
            PlanetHub.NotifyMemberChange(targetMember);

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
        public async Task<TaskResult<ulong>> GetMemberAuthority(ulong member_id, string token)
        {
            ServerAuthToken authToken = await ServerAuthToken.TryAuthorize(token, Context);

            if (authToken == null)
            {
                return new TaskResult<ulong>(false, "Failed to authorize user.", 0);
            }

            ServerPlanetMember member = await Context.PlanetMembers.Include(x => x.Planet)
                                                                   .FirstOrDefaultAsync(x => x.Id == member_id);

            if (member == null)
            {
                return new TaskResult<ulong>(false, "Member does not exist.", 0);
            }

            if (!(await member.Planet.IsMemberAsync(authToken.User_Id, Context)))
            {
                return new TaskResult<ulong>(false, "You are not in the planet.", 0);
            }

            return new TaskResult<ulong>(true, "Found authority", await member.GetAuthorityAsync());
        }

        public async Task<TaskResult> InsertCategory(ulong category_id, ulong planet_id, ushort position, string auth)
        {
            // Retrieve target planet
            ServerPlanet target_planet = await Context.Planets.FirstOrDefaultAsync(x => x.Id == planet_id);

            ServerAuthToken authToken = await ServerAuthToken.TryAuthorize(auth, Context);

            if (target_planet == null)
            {
                return new TaskResult(false, $"Could not find planet {planet_id}");
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

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, target_planet.Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!(await member.HasPermissionAsync(PlanetPermissions.ManageCategories)))
            {
                return new TaskResult(false, "You do not have planet category management permissions.");
            }

            ServerPlanetCategory category = await Context.PlanetCategories.FindAsync(category_id);

            // Ensure the planet matches
            if (category.Planet_Id != target_planet.Id)
            {
                Console.WriteLine($"Category with ID {category.Id} has different server than base! ({category.Planet_Id} vs {target_planet.Id} base)");
            }


            category.Parent_Id = null;
            category.Position = position;
            Context.Update(category);
            await Context.SaveChangesAsync();

            category.NotifyClientsChange();

            return new TaskResult(true, "Inserted category into planet.");
        }
    }
}
