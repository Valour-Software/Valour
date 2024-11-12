using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class PlanetApi
{
    [ValourRoute(HttpVerbs.Get, "api/planets/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRouteAsync(
        long id,
        PlanetService service,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        
        // Get the planet
        var planet = await service.GetAsync(id);
        if (planet is null)
            return ValourResult.NotFound("Planet not found");

        // Return json
        return Results.Json(planet);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] Planet planet,
        PlanetService planetService,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        
        if (planet is null)
            return ValourResult.BadRequest("Include planet in body.");
        
        if (!user.ValourStaff)
        {
            var ownedPlanets = await userService.GetOwnedPlanetCount(user.Id);
            if (ownedPlanets > ISharedUser.MaxOwnedPlanets)
                return ValourResult.BadRequest("You have reached the maximum owned planets!");
        }

        planet.Id = IdManager.Generate();
        planet.OwnerId = user.Id;

        var result = await planetService.CreateAsync(planet, user);
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.Created($"api/planets/{result.Data.Id}", result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] Planet planet,
        long id,
        PlanetService planetService,
        UserService userService,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        if (planet is null)
            return Results.BadRequest("Include planet in body.");

        // Ensure id matches route
        planet.Id = id;

        // We need to get the old planet to check special case for owner change
        var old = await planetService.GetAsync(planet.Id);
        
        // Owner change check
        if (old.OwnerId != planet.OwnerId)
        {
            // Only owner can do this
            if (member.UserId != old.OwnerId)
                return ValourResult.Forbid("Only a planet owner can transfer ownership.");

            // Ensure new owner is a member of the planet
            if (!await memberService.ExistsAsync(planet.OwnerId, planet.Id))
                return Results.BadRequest("You cannot transfer ownership to a non-member.");
            
            var ownedPlanets = await userService.GetOwnedPlanetCount(await userService.GetCurrentUserIdAsync());
            if (ownedPlanets >= ISharedUser.MaxOwnedPlanets)
                return Results.BadRequest("That new owner has the maximum owned planets!");
        }

        var result = await planetService.UpdateAsync(planet);

        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.Json(planet);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        PlanetService planetService,
        UserService userService)
    {
        var planet = await planetService.GetAsync(id);
        if (planet is null)
            return ValourResult.NotFound("Planet not found");
        
        var userId = await userService.GetCurrentUserIdAsync();

        if (userId != planet.OwnerId)
            return ValourResult.Forbid("You are not the owner of this planet.");

        await planetService.DeleteAsync(planet);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/channels")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetChannelsRouteAsync(
        long id,
        PlanetService planetService,
        PlanetMemberService memberService)
    {
        // Get current member for planet
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        // Get all planet channels
        var channels = await planetService.GetMemberChannelsAsync(id, member.Id);
        
        // Collection for channels that member can see
        var allowedChannels = new List<Channel>();

        foreach (var channel in channels)
        {
            if (await memberService.HasPermissionAsync(member, channel, ChannelPermissions.View))
                allowedChannels.Add(channel);
        }

        return Results.Json(allowedChannels);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/channels/chat")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetChatChannelsRouteAsync(
        long id,
        PlanetService planetService,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        
        var chatChannels = await planetService.GetMemberChatChannelsAsync(id, member.Id);

        var allowedChannels = new List<Channel>();

        foreach (var channel in chatChannels)
        {
            if (await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View));
                allowedChannels.Add(channel);
        }
        
        return Results.Json(allowedChannels);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/channels/voice")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetVoiceChannelsRouteAsync(
        long id, 
        PlanetService planetService,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        
        var voiceChannels = await planetService.GetMemberVoiceChannelsAsync(id, member.Id);

        var allowedChannels = new List<Channel>();

        foreach (var channel in voiceChannels)
        {
            if (await memberService.HasPermissionAsync(member, channel, VoiceChannelPermissions.View))
                allowedChannels.Add(channel);
        }

        return Results.Json(allowedChannels);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/categories")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetCategoriesRouteAsync(
        long id, 
        PlanetService planetService,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        
        var categories = await planetService.GetMemberCategoriesAsync(id, member.Id);

        var allowedCategories = new List<Channel>();

        foreach (var category in categories)
        {
            if (await memberService.HasPermissionAsync(member, category, CategoryPermissions.View))
                allowedCategories.Add(category);
        }

        return Results.Json(allowedCategories);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/memberinfo")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetMemberInfoRouteAsync(
        long id, 
        PlanetMemberService memberService,
        PlanetService planetService,
        int page = 0)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var memberInfo = await planetService.GetMemberInfoAsync(id, page);

        return Results.Json(memberInfo);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/roles")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRolesRouteAsync(
        long id, 
        PlanetMemberService memberService,
        PlanetService planetService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var roles = await planetService.GetRolesAsync(id);
        return Results.Json(roles);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/roles/ids")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRoleIdsRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetService planetService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var roleIds = await planetService.GetRoleIdsAsync(id);
        
        return Results.Json(roleIds);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/roles/counts")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRoleCountsRouteAsync(
        long id, 
        PlanetMemberService memberService,
        PlanetService planetService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var counts = await planetService.GetRoleMembershipCountsAsync(id);
        return Results.Json(counts);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{id}/roles/order")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> SetRoleOrderRouteAsync(
        [FromBody] List<long> order,
        long id,
        PlanetMemberService memberService,
        PlanetService planetService,
        PlanetRoleService roleService)
    {
        // Remove duplicates
        order = order.Distinct().ToList();

        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var authority = await memberService.GetAuthorityAsync(member);

        List<long> newList = new();

        // Make sure that there is permission for any changes
        var pos = 0;
        foreach (var roleId in order)
        {
            var role = await roleService.GetAsync(roleId);
            if (role is null)
                return ValourResult.BadRequest("One or more of the given roles does not exist.");

            // Only need to check permission if the position is being changed
            if (pos != role.Position && role.GetAuthority() >= authority)
                return ValourResult.Forbid($"The role {role.Name} does not have a lower authority than you.");

            newList.Add(role.Id);

            pos++;
        }

        var result = await planetService.SetRoleOrderAsync(id, newList);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/invites")]
    [UserRequired(UserPermissionsEnum.Invites)]
    public static async Task<IResult> GetInvitesRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetService planetService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Invite))
            return ValourResult.Forbid("You do not have permission for invites.");

        var invites = await planetService.GetInvitesAsync(id);
        return Results.Json(invites);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/invites/ids")]
    [UserRequired(UserPermissionsEnum.Invites)]
    public static async Task<IResult> GetInviteIdsRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetService planetService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Invite))
            return ValourResult.Forbid("You do not have permission for invites.");

        var inviteIds = await planetService.GetInviteIdsAsync(id);
        return Results.Json(inviteIds);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/discoverable")]
    public static async Task<IResult> GetDiscoverables(PlanetService planetService)
    {
        var planets = await planetService.GetDiscoverablesAsync();
        return Results.Json(planets);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{id}/discover")]
    [UserRequired(UserPermissionsEnum.Invites)]
    public static async Task<IResult> JoinDiscoverable(
        long id, 
        UserService userService,
        PlanetMemberService memberService,
        PlanetService planetService)
    {
        var user = await userService.GetCurrentUserAsync();
        var planet = await planetService.GetAsync(id);

        if (!planet.Public)
            return Results.BadRequest("Planet is set to private");

        if (!planet.Discoverable)
            return Results.BadRequest("Planet is not discoverable");


        var result = await memberService.AddMemberAsync(planet.Id, user.Id);

        if (result.Success)
            return Results.Created($"api/members/{result.Data.Id}", result.Data);
        else
            return ValourResult.Problem(result.Message);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/planets/{id}/join")]
    [UserRequired(UserPermissionsEnum.Invites)]
    public static async Task<IResult> Join(
        long id, 
        UserService userService,
        PlanetMemberService memberService,
        PlanetService planetService,
        PlanetInviteService inviteService,
        string inviteCode = null)
    {
        var user = await userService.GetCurrentUserAsync();
        var planet = await planetService.GetAsync(id);

        if (!planet.Public)
            return Results.BadRequest("Planet is set to private");

        if (!planet.Public)
        {
            if (inviteCode is null)
                return ValourResult.Forbid("The planet is not public. Please include inviteCode.");

            var invite = await inviteService.GetAsync(inviteCode, id);
            if (invite is null || (invite.TimeExpires is not null && invite.TimeExpires < DateTime.UtcNow))
                return ValourResult.Forbid("The invite code is invalid or expired.");
        }


        var result = await memberService.AddMemberAsync(planet.Id, user.Id);

        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.Created($"api/members/{result.Data.Id}", result.Data);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/planetChannels/insert")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> InsertChildRouteAsync(
        [FromBody] InsertChannelChildModel model,
        long planetId,
        PlanetMemberService memberService,
        ChannelService channelService,
        PlanetService planetService)
    {
        if (planetId != model.PlanetId)
            return ValourResult.BadRequest("PlanetId mismatch.");

        // Get member
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();
        
        if (model.ParentId is not null)
        {
            // Get the category
            var category = await channelService.GetAsync(model.ParentId.Value);
            if (category is null || category.ChannelType != ChannelTypeEnum.PlanetCategory)
                return ValourResult.NotFound("Category not found");
            
            if (!await memberService.HasPermissionAsync(member, category, CategoryPermissions.ManageCategory))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }

        // If the child currently belongs to another category (not planet), we need to check permissions for it
        var inserting = await channelService.GetAsync(model.InsertId);
        if (inserting.ParentId == model.ParentId)
            return ValourResult.BadRequest("Channel is already in this category.");
        
        // We need to get the old category and ensure we have permissions in it
        if (inserting.ParentId is not null)
        {
            var oldCategory = await channelService.GetAsync(inserting.ParentId.Value);
            if (!await memberService.HasPermissionAsync(member, oldCategory, CategoryPermissions.ManageCategory))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }
        
        // We have permission for the insert, the target category, and the old category if applicable.
        // Actually do the changes.
        var result = await planetService.InsertChildAsync(model.ParentId, inserting.Id, model.Position);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return ValourResult.Ok("Success");
    }
    
    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/planetChannels/order")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> SetChildOrderRouteAsync(
        [FromBody] OrderChannelsModel model,
        long planetId,
        PlanetMemberService memberService,
        ChannelService channelService)
    {
        if (model.PlanetId != planetId)
            return ValourResult.BadRequest("PlanetId mismatch.");
        
        // Get member
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();
        
        // Get the category
        if (model.CategoryId is not null)
        {
            var category = await channelService.GetAsync(model.CategoryId.Value);
            if (category is null || category.ChannelType != ChannelTypeEnum.PlanetCategory)
                return ValourResult.NotFound("Category not found");
            
            if (!await memberService.HasPermissionAsync(member, category, CategoryPermissions.ManageCategory))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }
        else
        {
            // Top level requires planet management perms
            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
                return ValourResult.LacksPermission(PlanetPermissions.Manage);
        }

        model.Order = model.Order.Distinct().ToList();

        // We have to check permissions for ALL changes in this ordering. Fuuuuuuun!
        //var pos = 0;
        foreach (var childId in model.Order)
        {
            var child = await channelService.GetAsync(childId);
            if (child is null)
                return ValourResult.NotFound($"Child {childId} not found");

            if (child.ParentId != model.CategoryId)
                return ValourResult.BadRequest("Use the category insert route to change parent id");
            
            // Change in position requires perms
            /*
             
            Retrospect: This is silly. If someone has permissions to a category, they should be able to move channels in it
             
            if (child.Position != pos)
            {
                // Require permission for the child being moved
                if (!await memberService.HasPermissionAsync(member, child, ChannelPermissions.Manage))
                    return ValourResult.LacksPermission(ChannelPermissions.Manage);
            }
            */
            
            //pos++;
        }
        
        // Actually do the changes
        var result = await channelService.SetChildOrderAsync(planetId, model.CategoryId, model.Order);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();
    }
    
}