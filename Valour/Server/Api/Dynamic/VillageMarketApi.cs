#nullable enable

using Microsoft.AspNetCore.Mvc;
using Valour.Server.Services.Villages;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

/// <summary>
/// Listing and buying village land and buildings.
///
/// Listing requires ManageVillage, because putting community property on the
/// market is an administrative act. Buying only requires membership - the
/// economy itself is what gates whether someone can afford it.
/// </summary>
public class VillageMarketApi
{
    public class SaleListingRequest
    {
        public bool ForSale { get; set; }
        public decimal Price { get; set; }
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/village/plots/{plotId}/listing")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> SetPlotListingAsync(
        long planetId,
        long plotId,
        [FromBody] SaleListingRequest request,
        PlanetMemberService memberService,
        VillageMarketService marketService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage))
            return ValourResult.LacksPermission(PlanetPermissions.ManageVillage);

        var result = await marketService.SetPlotForSaleAsync(plotId, planetId, request.ForSale, request.Price);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/village/buildings/{buildingId}/listing")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> SetBuildingListingAsync(
        long planetId,
        long buildingId,
        [FromBody] SaleListingRequest request,
        PlanetMemberService memberService,
        VillageMarketService marketService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageVillage))
            return ValourResult.LacksPermission(PlanetPermissions.ManageVillage);

        var result = await marketService.SetBuildingForSaleAsync(buildingId, planetId, request.ForSale, request.Price);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/village/plots/{plotId}/purchase")]
    [UserRequired(UserPermissionsEnum.Membership, UserPermissionsEnum.EconomyPlanetSend)]
    public static async Task<IResult> PurchasePlotAsync(
        long planetId,
        long plotId,
        PlanetMemberService memberService,
        VillageMarketService marketService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var result = await marketService.PurchasePlotAsync(plotId, planetId, member.Id, member.UserId);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/village/buildings/{buildingId}/purchase")]
    [UserRequired(UserPermissionsEnum.Membership, UserPermissionsEnum.EconomyPlanetSend)]
    public static async Task<IResult> PurchaseBuildingAsync(
        long planetId,
        long buildingId,
        PlanetMemberService memberService,
        VillageMarketService marketService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var result = await marketService.PurchaseBuildingAsync(buildingId, planetId, member.Id, member.UserId);
        return result.Success ? Results.Ok() : ValourResult.BadRequest(result.Message);
    }
}
