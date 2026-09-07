#nullable enable

using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Server.Services.Villages;
using Valour.Shared.Villages;

namespace Valour.Server.Api.Dynamic;

public class VillageApi
{
    private const string DisabledMessage = "The village is disabled for this planet.";

    private static async Task<bool> IsEnabledAsync(long planetId, PlanetService planetService) =>
        (await planetService.GetAsync(planetId))?.EnableVillage == true;

    [ValourRoute(HttpVerbs.Get, "api/planets/{id}/village/poc")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetProofOfConceptRouteAsync(
        long id,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillageWorldService worldService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var planet = await planetService.GetAsync(id);
        if (planet is null)
            return ValourResult.NotFound("Planet not found");
        if (!planet.EnableVillage)
            return ValourResult.Forbid(DisabledMessage);

        var channels = await planetService.GetAllChannelsAsync(id);
        var canManage = await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
        var scene = await worldService.GetOrCreateSceneAsync(planet, channels, member, canManage);
        return Results.Json(scene);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{id}/village/maps/{mapId}/build")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> EditMapRouteAsync(
        long id,
        long mapId,
        [FromBody] VillageBuildRequest request,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillageWorldService worldService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(id, planetService))
            return ValourResult.Forbid(DisabledMessage);

        var canManage = await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
        var result = await worldService.EditMapAsync(id, mapId, member.Id, canManage, request);
        return result.Success && result.Data is not null
            ? Results.Json(result.Data)
            : ValourResult.BadRequest(result.Message ?? "The village edit could not be applied.");
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{id}/village/buildings/{buildingId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> UpdateBuildingRouteAsync(
        long id,
        long buildingId,
        [FromBody] VillageBuildingUpdateRequest request,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillageWorldService worldService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(id, planetService))
            return ValourResult.Forbid(DisabledMessage);

        var canManage = await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
        var result = await worldService.UpdateBuildingAsync(buildingId, id, member.Id, canManage, request);
        return result.Success ? Results.Json(true) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{id}/village/plots/{plotId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> UpdatePlotRouteAsync(
        long id,
        long plotId,
        [FromBody] VillagePlotUpdateRequest request,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillageWorldService worldService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(id, planetService))
            return ValourResult.Forbid(DisabledMessage);

        var canManage = await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
        var result = await worldService.UpdatePlotAsync(plotId, id, member.Id, canManage, request);
        return result.Success ? Results.Json(true) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{id}/village/maps/{mapId}/plots")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> CreatePlotRouteAsync(
        long id, long mapId, [FromBody] VillagePlotCreateRequest request,
        PlanetMemberService memberService, PlanetService planetService, VillageWorldService worldService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null) return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(id, planetService)) return ValourResult.Forbid(DisabledMessage);
        var canManage = await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
        if (!canManage) return ValourResult.Forbid("Manage Village permission is required.");
        var result = await worldService.CreatePlotAsync(id, mapId, request);
        return result.Success && result.Data is not null
            ? Results.Json(result.Data)
            : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{id}/village/plots/{plotId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> DeletePlotRouteAsync(
        long id, long plotId, PlanetMemberService memberService, PlanetService planetService,
        VillageWorldService worldService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null) return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(id, planetService)) return ValourResult.Forbid(DisabledMessage);
        var canManage = await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
        if (!canManage) return ValourResult.Forbid("Manage Village permission is required.");
        var result = await worldService.DeletePlotAsync(id, plotId);
        return result.Success ? Results.NoContent() : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{id}/village/buildings/{buildingId}/room")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> AcquireBuildingRoomRouteAsync(
        long id,
        long buildingId,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillagePresenceService presenceService,
        VillageRoomService roomService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(id, planetService))
            return ValourResult.Forbid(DisabledMessage);

        if (!presenceService.GetBuildingOccupants(id, buildingId).Any(x => x.UserId == member.UserId))
            return ValourResult.BadRequest("Enter this village area before opening its temporary room.");

        var result = await roomService.AcquireAsync(id, buildingId, member.UserId);
        return result.Success && result.Data is not null
            ? Results.Json(result.Data)
            : ValourResult.BadRequest(result.Message ?? "Could not create the village room.");
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{id}/village/maps/{mapId}/room")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> AcquireMapRoomRouteAsync(
        long id,
        long mapId,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillagePresenceService presenceService,
        VillageRoomService roomService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(id, planetService))
            return ValourResult.Forbid(DisabledMessage);

        if (!presenceService.GetMapOccupants(id, mapId).Any(x => x.UserId == member.UserId))
            return ValourResult.BadRequest("Enter this village map before opening its temporary room.");

        var result = await roomService.AcquireMapAsync(id, mapId, member.UserId);
        return result.Success && result.Data is not null
            ? Results.Json(result.Data)
            : ValourResult.BadRequest(result.Message ?? "Could not create the village room.");
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{id}/village/buildings/{buildingId}/room")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> ReleaseBuildingRoomRouteAsync(
        long id,
        long buildingId,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillageRoomService roomService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(id, planetService))
            return ValourResult.Forbid(DisabledMessage);

        await roomService.ReleaseAsync(id, buildingId, member.UserId);
        return Results.Json(true);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{id}/village/maps/{mapId}/room")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> ReleaseMapRoomRouteAsync(
        long id,
        long mapId,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillageRoomService roomService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(id, planetService))
            return ValourResult.Forbid(DisabledMessage);

        await roomService.ReleaseMapAsync(id, mapId, member.UserId);
        return Results.Json(true);
    }
}
