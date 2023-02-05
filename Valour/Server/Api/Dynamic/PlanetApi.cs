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

        // Default image to start
        planet.IconUrl = "_content/Valour.Client/media/logo/logo-512.png";

        planet.Id = IdManager.Generate();
        planet.OwnerId = user.Id;

        var result = await planetService.CreateAsync(planet);
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.Created($"api/planets/{planet.Id}", planet);
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
            
            var ownedPlanets = await userService.GetOwnedPlanetCount(await userService.GetCurrentUserId());
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
        
        var userId = await userService.GetCurrentUserId();

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
        var channels = await planetService.GetChannelsAsync(id);
        
        // Collection for channels that member can see
        var allowedChannels = new List<PlanetChannel>();

        foreach (var channel in channels)
        {
            switch (channel)
            {
                case PlanetChatChannel:
                {
                    if (await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View))
                        allowedChannels.Add(channel);

                    break;
                }
                case PlanetVoiceChannel:
                {
                    if (await memberService.HasPermissionAsync(member, channel, VoiceChannelPermissions.View))
                        allowedChannels.Add(channel);

                    break;
                }
                case PlanetCategory:
                {
                    if (await memberService.HasPermissionAsync(member, channel, CategoryPermissions.View))
                        allowedChannels.Add(channel);
                    
                    break;
                }
                default:
                    throw new NotImplementedException($"Case for Permission with type {channel.PermType} not implemented");
            }
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
        
        var chatChannels = await planetService.GetChatChannelsAsync(id);

        var allowedChannels = new List<PlanetChatChannel>();

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
        
        var voiceChannels = await planetService.GetVoiceChannelsAsync(id);

        var allowedChannels = new List<PlanetVoiceChannel>();

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
        
        var categories = await planetService.GetCategoriesAsync(id);

        var allowedCategories = new List<PlanetCategory>();

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

        List<PlanetRole> newList = new();

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

            newList.Add(role);

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


        var result = await memberService.AddMemberAsync(planet, user);

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


        var result = await memberService.AddMemberAsync(planet, user);

        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.Created($"api/members/{result.Data.Id}", result.Data);
    }
    
    
}