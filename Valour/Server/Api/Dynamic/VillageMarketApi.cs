#nullable enable

using Microsoft.AspNetCore.Mvc;
using Valour.Server.Services.Villages;
using Valour.Shared.Authorization;
using Valour.Shared.Villages;

namespace Valour.Server.Api.Dynamic;

/// <summary>
/// Listing and buying village land and buildings.
///
/// Owners may list their own property; ManageVillage additionally permits
/// listing community or someone else's property. Buying only requires
/// membership and economy-send authority - the economy itself gates whether
/// someone can afford it.
/// </summary>
public class VillageMarketApi
{
    private const string DisabledMessage = "The village is disabled for this planet.";

    private static async Task<bool> IsEnabledAsync(long planetId, PlanetService planetService) =>
        (await planetService.GetAsync(planetId))?.EnableVillage == true;

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/village/plots/{plotId}/listing")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> SetPlotListingAsync(
        long planetId,
        long plotId,
        [FromBody] VillageSaleListingRequest request,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillageMarketService marketService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(planetId, planetService))
            return ValourResult.Forbid(DisabledMessage);

        var canManage = await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
        var result = await marketService.SetPlotForSaleAsync(
            plotId, planetId, member.Id, canManage, request.ForSale, request.Price);
        return result.Success ? Results.Json(true) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/village/buildings/{buildingId}/listing")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> SetBuildingListingAsync(
        long planetId,
        long buildingId,
        [FromBody] VillageSaleListingRequest request,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillageMarketService marketService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(planetId, planetService))
            return ValourResult.Forbid(DisabledMessage);

        var canManage = await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
        var result = await marketService.SetBuildingForSaleAsync(
            buildingId, planetId, member.Id, canManage, request.ForSale, request.Price);
        return result.Success ? Results.Json(true) : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/village/plots/{plotId}/purchase")]
    [UserRequired(UserPermissionsEnum.Membership, UserPermissionsEnum.EconomyPlanetSend)]
    public static async Task<IResult> PurchasePlotAsync(
        long planetId,
        long plotId,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillageMarketService marketService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(planetId, planetService))
            return ValourResult.Forbid(DisabledMessage);

        var result = await marketService.PurchasePlotAsync(plotId, planetId, member.Id, member.UserId);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/village/buildings/{buildingId}/purchase")]
    [UserRequired(UserPermissionsEnum.Membership, UserPermissionsEnum.EconomyPlanetSend)]
    public static async Task<IResult> PurchaseBuildingAsync(
        long planetId,
        long buildingId,
        PlanetMemberService memberService,
        PlanetService planetService,
        VillageMarketService marketService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();
        if (!await IsEnabledAsync(planetId, planetService))
            return ValourResult.Forbid(DisabledMessage);

        var result = await marketService.PurchaseBuildingAsync(buildingId, planetId, member.Id, member.UserId);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }
}
