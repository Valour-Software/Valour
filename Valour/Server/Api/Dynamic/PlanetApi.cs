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
    
    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/initialData")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetInitialDataRouteAsync(
        long id,
        PlanetService planetService,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var data = await planetService.GetInitialDataAsync(id, member.Id);
        return Results.Json(data);
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

        if (!await memberService.HasPermissionAsync(member.Id, PlanetPermissions.Manage))
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
        var channels = await planetService.GetMemberChannelsAsync(member.Id);
        
        return Results.Json(channels?.List ?? []);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/channels/primary")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetPrimaryChannelRouteAsync(
        long id,
        PlanetService planetService,
        PlanetMemberService memberService)
    {
        // Get current member for planet
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var channel = await planetService.GetPrimaryChannelAsync(id);

        return Results.Json(channel);
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

    [ValourRoute(HttpVerbs.Post, "api/planets/{id}/members")]
    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/members")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> GetMembersRouteAsync(
        [FromBody] PlanetMemberQueryModel? queryModel,
        long id,
        PlanetMemberService memberService,
        int skip = 0,
        int take = 50)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var members = await memberService.QueryPlanetMembersAsync(id, skip, take);

        return Results.Json(members);
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

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/roles/order")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> SetRoleOrderRouteAsync(
        [FromBody] long[] order,
        long planetId,
        PlanetMemberService memberService,
        PlanetService planetService,
        PlanetRoleService roleService)
    {
        if (order.Length > 256)
            return ValourResult.BadRequest("Too many roles in order.");
        
        // Check for duplicates
        for (var i = 0; i < order.Length; i++)
        {
            var a = order[i];
            
            for (var j = i + 1; j < order.Length; j++)
            {
                var b = order[j];
                
                if (a == b)
                {
                    return ValourResult.BadRequest($"Duplicate role in order ({a})");
                }
            }
        }

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var authority = await memberService.GetAuthorityAsync(member);
        
        // Make sure that there is permission for any changes
        var pos = 0;
        foreach (var roleId in order)
        {
            var role = await roleService.GetAsync(planetId, roleId);
            if (role is null)
                return ValourResult.BadRequest("One or more of the given roles does not exist.");
            
            // Only need to check permission if the position is being changed
            if (pos != role.Position && role.GetAuthority() >= authority)
                return ValourResult.Forbid($"The role {role.Name} does not have a lower authority than you.");
            
            if (role.IsDefault && pos != order.Length - 1)
                return ValourResult.Forbid("The default role must be last in the order.");
            
            pos++;
        }

        var result = await planetService.SetRoleOrderAsync(planetId, order);
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

        if (!await memberService.HasPermissionAsync(member.Id, PlanetPermissions.Invite))
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

        if (!await memberService.HasPermissionAsync(member.Id, PlanetPermissions.Invite))
            return ValourResult.Forbid("You do not have permission for invites.");

        var inviteIds = await planetService.GetInviteIdsAsync(id);
        return Results.Json(inviteIds);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/bans")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> GetBansRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetBanService banService,
        int skip = 0,
        int take = 50)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var bans = await banService.QueryPlanetBansAsync(id, skip, take);

        return Results.Json(bans);
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
            return ValourResult.BadRequest(result.Message);
        
        return Results.Created($"api/members/{result.Data.Id}", result.Data);
    }

    
    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/channels/move")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> MoveChannelAsync(
        [FromBody] MoveChannelRequest request, 
        long planetId,
        PlanetMemberService memberService,
        ChannelService channelService)
    {
        if (request.PlanetId != planetId)
            return ValourResult.BadRequest("PlanetId mismatch.");
        
        // Get member
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();
        
        Channel destination = null;
        Channel category = null;
        
        // Either the target is the category, or it's within a category. Or, well, it's at the top level.
        // We have to determine this.
        if (request.DestinationChannel is not null)
        {
            destination = await channelService.GetChannelAsync(planetId, request.DestinationChannel.Value);
            if (destination is null)
                return ValourResult.NotFound("Destination channel not found");
            
            if (destination.ChannelType == ChannelTypeEnum.PlanetCategory)
            {
                // In this case, the destination was the category
                // ie: user dropped the channel directly on the category
                category = destination;
            }
            else
            {
                // In this case, the destination was a channel within a category
                // We have to go up a level to get the category
                if (destination.ParentId is not null)
                {
                    category = await channelService.GetChannelAsync(planetId, destination.ParentId!.Value);
                    
                    // If the category is null in this case, something is wrong
                    if (category is null)
                        return ValourResult.BadRequest("Internal error - Parent category not found for destination");
                }
                
                // There's a chance the destination's parent is null, which means it's a top level channel
                // ie: a channel not within a category. This is fine.
            }
            
            // If there is a category, ensure the member has permission to manage it
            if (category is not null)
            {
                if (!await memberService.HasPermissionAsync(member, category, CategoryPermissions.ManageCategory))
                    return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
            }
        }
        
        // Get the channel being moved
        var channel = await channelService.GetChannelAsync(planetId, request.MovingChannel);
        if (channel is null)
            return ValourResult.NotFound("Channel to move not found");
            
        // Always ensure the member has permission to manage the channel being moved
        if (!await memberService.HasPermissionAsync(member, channel, ChannelPermissions.Manage))
            return ValourResult.LacksPermission(ChannelPermissions.Manage);
        
        // Auth is finished. Toss down to the service to do the work.
        var result = await channelService.MoveChannelAsync(channel.PlanetId!.Value, channel.Id, destination?.Id, request.PlaceBefore);

        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.NoContent();
    }
}