using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models.Economy;

namespace Valour.Server.Api.Dynamic;

public class EcoApi
{
    [ValourRoute(HttpVerbs.Get, "api/eco/currencies/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetCurrencyAsync(
        long id,
        EcoService ecoService,
        PlanetMemberService planetMemberService)
    {
        var currency = await ecoService.GetCurrencyAsync(id);
        if (currency is null)
            return ValourResult.NotFound("Currency not found");

        // Non-global currencies require membership checks
        if (id != ISharedCurrency.ValourCreditsId) {
            var member = await planetMemberService.GetCurrentAsync(currency.PlanetId);
            if (member is null)
                return ValourResult.NotPlanetMember();
        }

        return Results.Json(currency);
    }

    [ValourRoute(HttpVerbs.Post, "api/eco/currencies")]
    [UserRequired(UserPermissionsEnum.Membership,
                  UserPermissionsEnum.PlanetManagement,
                  UserPermissionsEnum.EconomyPlanetView)]
    public static async Task<IResult> CreateCurrencyAsync(
        [FromBody] Currency currency,
        EcoService ecoService,
        PlanetMemberService planetMemberService)
    {
        if (currency is null)
            return ValourResult.BadRequest("Include currency in body");

        var member = await planetMemberService.GetCurrentAsync(currency.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await planetMemberService.HasPermissionAsync(member, PlanetPermissions.ManageCurrency))
            return ValourResult.LacksPermission(PlanetPermissions.ManageCurrency);

        var result = await ecoService.CreateCurrencyAsync(currency);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Created($"api/eco/currencies/{result.Data.Id}", result.Data);
    }
}
