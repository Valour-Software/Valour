using AutoMapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Valour.Server.Categories;
using Valour.Server.Database;
using Valour.Server.Planets;
using Valour.Shared;
using Valour.Shared.Oauth;
using Valour.Shared.Roles;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Controllers
{
    /// <summary>
    /// Controls all (most) routes for permissions
    /// Yeah I'd like to say all, but there will probably be specific stuff in the
    /// channel and category controllers that I'll need to continually shift here
    ///  /rant
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]
    public class PermissionsController
    {

        /// <summary>
        /// Database context
        /// </summary>
        private readonly ValourDB Context;

        private readonly IMapper Mapper;

        // Dependency injection
        public PermissionsController(ValourDB context, IMapper mapper)
        {
            this.Context = context;
            this.Mapper = mapper;
        }

        public async Task<TaskResult<ChatChannelPermissionsNode>> GetChatChannelNode(ulong channel_id, ulong role_id, string token)
        {
            // Authenticate first
            AuthToken authToken = await Context.AuthTokens.FirstOrDefaultAsync(x => x.Id == token);

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet)
                                                                              .FirstOrDefaultAsync(x => x.Id == channel_id);

            if (channel == null)
            {
                return new TaskResult<ChatChannelPermissionsNode>(false, $"Could not find channel {channel_id}", null);
            }

            if (authToken == null)
            {
                return new TaskResult<ChatChannelPermissionsNode>(false, "Failed to authorize user.", null);
            }

            if (!authToken.HasScope(UserPermissions.Membership))
            {
                return new TaskResult<ChatChannelPermissionsNode>(false, "Your token doesn't have planet membership scope.", null);
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, channel.Planet.Id);

            if (member == null)
            {
                return new TaskResult<ChatChannelPermissionsNode>(false, "You are not a member of the target planet.", null);
            }

            // Actually get the node
            ChatChannelPermissionsNode node = await Context.ChatChannelPermissionsNodes.FirstOrDefaultAsync(x => x.Channel_Id == channel_id &&
                                                                                                         x.Role_Id == role_id);

            if (node == null)
            {
                return new TaskResult<ChatChannelPermissionsNode>(true, "The given node does not exist", node);
            }

            return new TaskResult<ChatChannelPermissionsNode>(true, "Returned permission node successfully", node);
        }

        public async Task<TaskResult<CategoryPermissionsNode>> GetCategoryNode(ulong category_id, ulong role_id, string token)
        {
            // Authenticate first
            AuthToken authToken = await Context.AuthTokens.FirstOrDefaultAsync(x => x.Id == token);

            ServerPlanetCategory category = await Context.PlanetCategories.Include(x => x.Planet)
                                                                          .FirstOrDefaultAsync(x => x.Id == category_id);

            if (category == null)
            {
                return new TaskResult<CategoryPermissionsNode>(false, $"Could not find category {category_id}", null);
            }

            if (authToken == null)
            {
                return new TaskResult<CategoryPermissionsNode>(false, "Failed to authorize user.", null);
            }

            if (!authToken.HasScope(UserPermissions.Membership))
            {
                return new TaskResult<CategoryPermissionsNode>(false, "Your token doesn't have planet membership scope.", null);
            }

            // Membership check

            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, category.Planet.Id);

            if (member == null)
            {
                return new TaskResult<CategoryPermissionsNode>(false, "You are not a member of the target planet.", null);
            }

            // Actually get the node
            CategoryPermissionsNode node = await Context.CategoryPermissionsNodes.FirstOrDefaultAsync(x => x.Category_Id == category_id &&
                                                                                                           x.Role_Id == role_id);

            if (node == null)
            {
                return new TaskResult<CategoryPermissionsNode>(true, "The given node does not exist", node);
            }

            return new TaskResult<CategoryPermissionsNode>(true, "Returned permission node successfully", node);
        }

        public async Task<TaskResult> UpdateChatChannelNode([FromBody] ChatChannelPermissionsNode node, string token)
        {
            // Authenticate first
            AuthToken authToken = await Context.AuthTokens.FirstOrDefaultAsync(x => x.Id == token);

            // Membership check
            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, node.Planet_Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            PlanetRole role = await Context.PlanetRoles.FindAsync(node.Role_Id);

            if (role == null)
            {
                return new TaskResult(false, $"Can't find role with ID {node.Role_Id}. This really shouldn't happen, and means the node data sent is incorrect.");
            }

            // Ensure the role being edited is under the user's authority
            if (await member.GetAuthorityAsync() < role.GetAuthority())
            {
                return new TaskResult(false, $"You cannot edit permissions for a role that is not under your own.");
            }

            ServerPlanetChatChannel channel = await Context.PlanetChatChannels.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Id == node.Channel_Id);

            if (channel == null)
            {
                return new TaskResult(false, $"Can't find channel with ID {node.Channel_Id}. This really shouldn't happen, and means the node data sent is incorrect.");
            }

            if (!(await channel.HasPermission(member, ChatChannelPermissions.View)))
            {
                return new TaskResult(false, "You don't have access to this channel.");
            }

            if (!(await channel.Planet.HasPermissionAsync(member, PlanetPermissions.ManageChannels)))
            {
                return new TaskResult(false, "You don't have permission to manage channels.");
            }

            // If planet permission to manage roles, there is global permission
            if (!(await channel.Planet.HasPermissionAsync(member, PlanetPermissions.ManageRoles)))
            {
                // Otherwise, see if there's channel-specific perms
                if (!(await channel.HasPermission(member, ChatChannelPermissions.ManagePermissions, Context)))
                {
                    return new TaskResult(false, "You don't have permission to manage permissions in this channel.");
                }
            }

            // Check if the node already exists
            var oldNode = await Context.ChatChannelPermissionsNodes.FirstOrDefaultAsync(x => x.Channel_Id == node.Channel_Id &&
                                                                                             x.Role_Id == node.Role_Id);

            if (oldNode != null)
            {
                oldNode.Code = node.Code;
                oldNode.Code_Mask = node.Code_Mask;

                Context.ChatChannelPermissionsNodes.Update(oldNode);           
            }
            else
            {
                node.Id = IdManager.Generate();
                await Context.ChatChannelPermissionsNodes.AddAsync(node);
            }

            await Context.SaveChangesAsync();

            return new TaskResult(true, "Successfully set node");
        }

        public async Task<TaskResult> UpdateCategoryNode([FromBody] CategoryPermissionsNode node, string token)
        {
            // Authenticate first
            AuthToken authToken = await Context.AuthTokens.FirstOrDefaultAsync(x => x.Id == token);

            // Membership check
            ServerPlanetMember member = await ServerPlanetMember.FindAsync(authToken.User_Id, node.Planet_Id);

            if (member == null)
            {
                return new TaskResult(false, "You are not a member of the target planet.");
            }

            if (!authToken.HasScope(UserPermissions.PlanetManagement))
            {
                return new TaskResult(false, "Your token doesn't have planet management scope.");
            }

            PlanetRole role = await Context.PlanetRoles.FindAsync(node.Role_Id);

            if (role == null)
            {
                return new TaskResult(false, $"Can't find role with ID {node.Role_Id}. This really shouldn't happen, and means the node data sent is incorrect.");
            }

            // Ensure the role being edited is under the user's authority
            if (await member.GetAuthorityAsync() < role.GetAuthority())
            {
                return new TaskResult(false, $"You cannot edit permissions for a role that is not under your own.");
            }

            ServerPlanetCategory category = await Context.PlanetCategories.Include(x => x.Planet).FirstOrDefaultAsync(x => x.Id == node.Category_Id);

            if (category == null)
            {
                return new TaskResult(false, $"Can't find category with ID {node.Category_Id}. This really shouldn't happen, and means the node data sent is incorrect.");
            }

            if (!(await category.HasPermission(member, CategoryPermissions.View)))
            {
                return new TaskResult(false, "You don't have access to this category.");
            }

            if (!(await category.Planet.HasPermissionAsync(member, PlanetPermissions.ManageCategories)))
            {
                return new TaskResult(false, "You don't have permission to manage categories.");
            }

            // If planet permission to manage roles, there is global permission
            if (!(await category.Planet.HasPermissionAsync(member, PlanetPermissions.ManageRoles)))
            {
                // Otherwise, see if there's channel-specific perms
                if (!(await category.HasPermission(member, ChatChannelPermissions.ManagePermissions, Context)))
                {
                    return new TaskResult(false, "You don't have permission to manage permissions in this category.");
                }
            }

            // Check if the node already exists
            var oldNode = await Context.CategoryPermissionsNodes.FirstOrDefaultAsync(x => x.Category_Id == node.Category_Id &&
                                                                                          x.Role_Id == node.Role_Id);

            if (oldNode != null)
            {
                oldNode.Code = node.Code;
                oldNode.Code_Mask = node.Code_Mask;

                oldNode.ChatChannel_Code = node.ChatChannel_Code;
                oldNode.ChatChannel_Code_Mask = node.ChatChannel_Code_Mask;

                Context.CategoryPermissionsNodes.Update(oldNode);
            }
            else
            {
                node.Id = IdManager.Generate();
                await Context.CategoryPermissionsNodes.AddAsync(node);
            }

            await Context.SaveChangesAsync();

            return new TaskResult(true, "Successfully set node");
        }
    }
}
