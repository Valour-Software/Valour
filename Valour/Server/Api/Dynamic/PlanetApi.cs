#nullable enable

using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database;
using Valour.Server.Requests;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Queries;
using Valour.Server.Models;
using Valour.Server.Services;

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

        await planetService.DeleteAsync(planet.Id);

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

    [ValourRoute(HttpVerbs.Post, "api/planets/{id}/members/query")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> QueryMembersRouteAsync(
        [FromBody] QueryRequest? queryRequest,
        long id,
        PlanetMemberService memberService)
    {
        if (queryRequest is null)
            return ValourResult.BadRequest("Include query in body.");
        
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var members = await memberService.QueryPlanetMembersAsync(id, queryRequest);

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
        PlanetService planetService)
    {
        if (order is null || order.Length == 0)
            return ValourResult.BadRequest("Role order cannot be empty.");

        if (order.Length > 256)
            return ValourResult.BadRequest("Too many roles in order.");

        // Check for duplicates using HashSet - O(n) instead of O(n²)
        var seen = new HashSet<long>();
        foreach (var roleId in order)
        {
            if (!seen.Add(roleId))
                return ValourResult.BadRequest($"Duplicate role in order ({roleId})");
        }

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        // Check ManageRoles permission
        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageRoles))
            return ValourResult.LacksPermission(PlanetPermissions.ManageRoles);

        var roles = (await planetService.GetRolesAsync(planetId))
            .OrderBy(x => x.Position)
            .ToArray();

        if (order.Length > roles.Length)
            return ValourResult.BadRequest("Order contains more roles than exist on the planet.");

        var roleById = roles.ToDictionary(x => x.Id);
        foreach (var roleId in order)
        {
            if (!roleById.ContainsKey(roleId))
                return ValourResult.BadRequest($"One or more given roles do not exist on this planet ({roleId}).");
        }

        long[] normalizedOrder;
        if (order.Length == roles.Length)
        {
            normalizedOrder = order;
        }
        else
        {
            // Keep omitted roles fixed in-place, and only reorder the subset provided by the client.
            var provided = seen;
            var queue = new Queue<long>(order);
            normalizedOrder = new long[roles.Length];

            for (int i = 0; i < roles.Length; i++)
            {
                var currentRoleId = roles[i].Id;
                normalizedOrder[i] = provided.Contains(currentRoleId) ? queue.Dequeue() : currentRoleId;
            }
        }

        var authority = await memberService.GetAuthorityAsync(member);
        
        // Make sure that there is permission for any changes
        for (int pos = 0; pos < normalizedOrder.Length; pos++)
        {
            var roleId = normalizedOrder[pos];
            var role = roleById[roleId];
            
            // Only need to check permission if the position is being changed
            if (pos != role.Position && role.GetAuthority() >= authority)
                return ValourResult.Forbid($"The role {role.Name} does not have a lower authority than you.");
            
            if (role.IsDefault && pos != normalizedOrder.Length - 1)
                return ValourResult.Forbid("The default role must be last in the order.");
        }

        var result = await planetService.SetRoleOrderAsync(planetId, normalizedOrder);
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

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/bans/query")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> QueryBansRouteAsync(
        [FromBody] QueryRequest? queryRequest,
        long planetId,
        PlanetMemberService memberService,
        PlanetBanService banService)
    {
        if (queryRequest is null)
            return ValourResult.BadRequest("Include query in body.");
        
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var bans = await banService.QueryPlanetBansAsync(planetId, queryRequest);

        return Results.Json(bans);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/automod/triggers/query")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> QueryAutomodTriggersAsync(
        [FromBody] QueryRequest? queryRequest,
        long planetId,
        PlanetMemberService memberService,
        AutomodService automodService)
    {
        if (queryRequest is null)
            return ValourResult.BadRequest("Include query in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var result = await automodService.QueryPlanetTriggersAsync(planetId, queryRequest);
        return Results.Json(result);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/automod/triggers/{triggerId}/actions/query")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> QueryAutomodActionsAsync(
        [FromBody] QueryRequest? queryRequest,
        long planetId,
        Guid triggerId,
        PlanetMemberService memberService,
        AutomodService automodService)
    {
        if (queryRequest is null)
            return ValourResult.BadRequest("Include query in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var result = await automodService.QueryTriggerActionsAsync(triggerId, queryRequest);
        return Results.Json(result);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/info")]
    public static async Task<IResult> GetPlanetInfoAsync(
        long id,
        PlanetService planetService)
    {
        var planetInfo = await planetService.GetPlanetInfoAsync(id);
        if (planetInfo is null)
            return ValourResult.NotFound("Planet not found or not public");
        
        return Results.Json(planetInfo);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/discoverable")]
    public static async Task<IResult> GetDiscoverables(PlanetService planetService)
    {
        var planets = await planetService.GetDiscoverablesAsync();
        return Results.Json(planets);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/discoverable/query")]
    public static async Task<IResult> QueryDiscoverables(
        [FromBody] QueryRequest? queryRequest,
        PlanetService planetService)
    {
        if (queryRequest is null)
            return ValourResult.BadRequest("Include query in body.");
        
        var planets = await planetService.QueryDiscoverablePlanetsAsync(queryRequest);
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
                if (request.InsideCategory)
                {
                    // User dropped into the middle zone of a category - move inside it
                    category = destination;
                }
                else
                {
                    // User dropped on the top/bottom edge of a category - treat as sibling
                    // Find the category's parent (if any) for permission checks
                    if (destination.ParentId is not null)
                    {
                        category = await channelService.GetChannelAsync(planetId, destination.ParentId!.Value);
                        if (category is null)
                            return ValourResult.BadRequest("Internal error - Parent category not found for destination");
                    }
                    // else: root level, category stays null
                }
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
        var result = await channelService.MoveChannelAsync(channel.PlanetId!.Value, channel.Id, destination?.Id, request.PlaceBefore, request.InsideCategory);

        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/import/discord")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> ImportDiscordTemplateAsync(
        [FromBody] ImportDiscordTemplateRequest request,
        DiscordImportService importService,
        UserService userService)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.TemplateCodeOrUrl))
            return ValourResult.BadRequest("Template code or URL is required.");

        var user = await userService.GetCurrentUserAsync();

        if (!user.ValourStaff)
        {
            var ownedPlanets = await userService.GetOwnedPlanetCount(user.Id);
            if (ownedPlanets > ISharedUser.MaxOwnedPlanets)
                return ValourResult.BadRequest("You have reached the maximum owned planets!");
        }

        var result = await importService.ImportAsync(request.TemplateCodeOrUrl, user, request.PlanetName);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/planets/{result.Data.Id}", result.Data);
    }
}
