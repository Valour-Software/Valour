using Microsoft.AspNetCore.Mvc;
using Valour.Database.Context;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared;
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
        var user = await userService.GetCurrentUser();
        
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
            
            var ownedPlanets = await userService.GetOwnedPlanetCount((await userService.GetCurrentUserId())!.Value);
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
                    throw new NotImplementedException($"Case for Permission with type {channel.PermissionsTargetType} not implemented");
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
        [FromBody] long[] order, 
        long id, 
        PlanetMemberService memberService,
        PlanetService planetService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var authority = await memberService.GetAuthorityAsync(member);

        // Remove duplicates
        order = order.Distinct().ToArray();

        // Ensure every role is accounted for
        var totalRoles = await db.PlanetRoles.CountAsync(x => x.PlanetId == id);

        if (totalRoles != order.Length)
            return Results.BadRequest("Your order does not contain all the planet roles.");

        using var tran = await db.Database.BeginTransactionAsync();

        List<PlanetRole> roles = new();

        

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/invites")]
    [UserRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id", PlanetPermissionsEnum.Invite)]
    public static async Task<IResult> GetInvitesRouteAsync(
        long id, 
        ValourDB db)
    {
        var invites = await db.PlanetInvites.Where(x => x.PlanetId == id).ToListAsync();
        return Results.Json(invites);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/inviteids"), TokenRequired]
    [UserRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired("id", PlanetPermissionsEnum.Invite)]
    public static async Task<IResult> GetInviteIdsRouteAsync(
        long id, 
        ValourDB db)
    {
        var invites = await db.PlanetInvites.Where(x => x.PlanetId == id).Select(x => x.Id).ToListAsync();
        return Results.Json(invites);
    }

    [ValourRoute(HttpVerbs.Get, "/discoverable"), TokenRequired]
    public static async Task<IResult> GetDiscoverables(ValourDB db)
    {
        var planets = await db.Planets.Include(x => x.Members)
                                      .Where(x => x.Public && x.Discoverable)
                                      .OrderByDescending(x => x.Members.Count())
                                      .ToListAsync();

        return Results.Json(planets);
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/discover"), TokenRequired]
    [UserRequired(UserPermissionsEnum.Invites)]
    public static async Task<IResult> JoinDiscoverable(
        long id, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService)
    {
        long userId = ctx.GetToken().UserId;

        if (await db.PlanetBans.AnyAsync(x => x.TargetId == userId && x.PlanetId == id))
            return Results.BadRequest("User is banned from the planet");

        if (await db.PlanetMembers.AnyAsync(x => x.UserId == userId && x.PlanetId == id))
            return Results.BadRequest("User is already a member");

        var planet = await FindAsync(id, db);

        if (!planet.Public)
            return Results.BadRequest("Planet is set to private");

        if (!planet.Discoverable)
            return Results.BadRequest("Planet is not discoverable");

        TaskResult<PlanetMember> result = await planet.AddMemberAsync(await FindAsync<User>(userId, db), db, hubService);

        if (result.Success)
            return Results.Created(result.Data.GetUri(), result.Data);
        else
            return ValourResult.Problem(result.Message);
    }
    
    
}