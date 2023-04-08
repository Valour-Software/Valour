using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models.Economy;

namespace Valour.Server.Api.Dynamic;

public class EcoApi
{
    ////////////////
    // Currencies //
    ////////////////

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

    [ValourRoute(HttpVerbs.Put, "api/eco/currencies")]
    [UserRequired(UserPermissionsEnum.Membership,
                  UserPermissionsEnum.PlanetManagement,
                  UserPermissionsEnum.EconomyPlanetView)]
    public static async Task<IResult> UpdateCurrencyAsync(
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

        var result = await ecoService.UpdateCurrencyAsync(currency);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    //////////////
    // Accounts //
    //////////////

    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/{id}")]
    [UserRequired]
    //[UserRequired(UserPermissionsEnum.Membership,
    //              UserPermissionsEnum.EconomyViewGlobal)] // These depend on what we're requesting
    public static async Task<IResult> GetAccountAsync(
        long id,
        EcoService ecoService,
        TokenService tokenService,
        PlanetMemberService memberService)
    {
        var account = await ecoService.GetAccountAsync(id);
        if (account is null)
            return ValourResult.NotFound("Account not found");

        var authToken = await tokenService.GetCurrentToken();

        if (account.CurrencyId == ISharedCurrency.ValourCreditsId)
        {
            if (!authToken.HasScope(UserPermissions.EconomyViewGlobal))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewGlobal);

            // Only the owner of a global account can view it
            if (account.UserId != authToken.UserId)
                return ValourResult.Forbid("You cannot access this account");
        }
        else
        {
            if (!authToken.HasScope(UserPermissions.EconomyViewPlanet))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewPlanet);

            var member = await memberService.GetCurrentAsync(account.PlanetId);
            if (member is null)
                return ValourResult.NotPlanetMember();

            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
                return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        }

        return Results.Json(account);
    }

}
