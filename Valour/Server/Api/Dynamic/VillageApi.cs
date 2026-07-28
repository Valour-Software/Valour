#nullable enable

using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Server.Services.Villages;
using Valour.Shared.Villages;

namespace Valour.Server.Api.Dynamic;

public class VillageApi
{
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

        var channels = await planetService.GetAllChannelsAsync(id);
        var scene = await worldService.GetOrCreateSceneAsync(planet, channels, member);
        scene.CanManageVillage = await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
        return Results.Json(scene);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{id}/village/buildings/{buildingId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> UpdateBuildingRouteAsync(
        long id,
        long buildingId,
        [FromBody] VillageBuildingUpdateRequest request,
        PlanetMemberService memberService,
        VillageWorldService worldService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

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
        VillageWorldService worldService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var canManage = await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
        var result = await worldService.UpdatePlotAsync(plotId, id, member.Id, canManage, request);
        return result.Success ? Results.Json(true) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{id}/village/buildings/{buildingId}/room")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> AcquireBuildingRoomRouteAsync(
        long id,
        long buildingId,
        PlanetMemberService memberService,
        VillagePresenceService presenceService,
        VillageRoomService roomService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!presenceService.GetBuildingOccupants(id, buildingId).Any(x => x.UserId == member.UserId))
            return ValourResult.BadRequest("Enter this village area before opening its temporary room.");

        var result = await roomService.AcquireAsync(id, buildingId, member.UserId);
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
        VillageRoomService roomService)
    {
        var member = await memberService.GetCurrentAsync(id);
        if (member is null)
            return ValourResult.NotPlanetMember();

        await roomService.ReleaseAsync(id, buildingId, member.UserId);
        return Results.Json(true);
    }
}
