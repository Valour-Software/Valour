using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models.Economy;

namespace Valour.Server.Api.Dynamic;

public class EcoApi
{
    ////////////////
    // Currencies //
    ////////////////

    [ValourRoute(HttpVerbs.Get, "api/eco/currencies/byPlanet/{planetId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetPlanetCurrencyAsync(
        long planetId,
        EcoService ecoService,
        PlanetMemberService planetMemberService)
    {
        var currency = await ecoService.GetPlanetCurrencyAsync(planetId);
        if (currency is null)
            return ValourResult.NotFound("Currency not found");

        // Non-global currencies require membership checks
        if (currency.Id != ISharedCurrency.ValourCreditsId)
        {
            var member = await planetMemberService.GetCurrentAsync(currency.PlanetId);
            if (member is null)
                return ValourResult.NotPlanetMember();
        }
        
        return Results.Json(currency);
    }

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

    [ValourRoute(HttpVerbs.Put, "api/eco/currencies/{id}")]
    [UserRequired(UserPermissionsEnum.Membership,
                  UserPermissionsEnum.PlanetManagement,
                  UserPermissionsEnum.EconomyPlanetView)]
    public static async Task<IResult> UpdateCurrencyAsync(
        long id,
        [FromBody] Currency currency,
        EcoService ecoService,
        PlanetMemberService planetMemberService)
    {
        if (currency.Id != id)
            return ValourResult.BadRequest("Id mismatch");
        
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
        TokenService tokenService)
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

            // It's not actually an issue if someone simply wants to look at their account but doesnt have
            // planet eco perms. They just shouldn't be able to *use* it.
            
            //var member = await memberService.GetCurrentAsync(account.PlanetId);
            //if (member is null)
            //    return ValourResult.NotPlanetMember();

            //if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            //    return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        }

        return Results.Json(account);
    }

    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/planet/{planetId}")]
    [UserRequired]
    public static async Task<IResult> GetPlanetAccountsAsync(
        long planetId, 
        EcoService ecoService,
        TokenService tokenService,
        PlanetMemberService memberService)
    {
        var authToken = await tokenService.GetCurrentToken();

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageEcoAccounts))
            return ValourResult.LacksPermission(PlanetPermissions.ManageEcoAccounts);
        
        var accounts = await ecoService.GetPlanetAccountsAsync(planetId);
        return Results.Json(accounts);
    }

    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/self")]
    [UserRequired]
    public static async Task<IResult> GetAccountsAsync(
        long userId,
        EcoService ecoService, 
        TokenService tokenService)
    {
        var authToken = await tokenService.GetCurrentToken();
        var accounts = await ecoService.GetAccountsAsync(userId);

        List<EcoAccount> results = new();
        
        var globalAccess = authToken.HasScope(UserPermissions.EconomyViewGlobal);
        var planetAccess = authToken.HasScope(UserPermissions.EconomyViewPlanet);
        
        foreach (var account in accounts)
        {
            if (account.CurrencyId == ISharedCurrency.ValourCreditsId)
            {
                if (globalAccess)
                    results.Add(account);
            }
            else
            {
                if (planetAccess)
                    results.Add(account);
            }
        }

        return Results.Json(results);
    }
    
    //////////////////
    // Transactions //
    //////////////////
    
    // Careful now, this is what gets Jacob VERY excited
    
    [ValourRoute(HttpVerbs.Post, "api/eco/transaction")]
    [UserRequired]
    public static async Task<IResult> CreateTransactionAsync(
        [FromBody] Transaction transaction,
        EcoService ecoService,
        TokenService tokenService,
        PlanetMemberService memberService)
    {
        if (transaction is null)
            return ValourResult.BadRequest("Include transaction in body");

        var authToken = await tokenService.GetCurrentToken();
        var account = await ecoService.GetAccountAsync(transaction.UserFromId);
        if (account is null)
            return ValourResult.NotFound("Account not found");

        // User account can only be used by the owner
        if (account.AccountType == AccountType.User)
        {
            if (!authToken.HasScope(UserPermissions.EconomyViewGlobal))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewGlobal);

            if (!authToken.HasScope(UserPermissions.EconomySendGlobal))
                return ValourResult.LacksPermission(UserPermissions.EconomySendGlobal);
            
            if (account.UserId != authToken.UserId)
                return ValourResult.Forbid("You cannot access this account");

            if (transaction.UserFromId != authToken.UserId)
                return ValourResult.Forbid("You cannot create a transaction for an account you do not own");
        }
        // Planet accounts can be used by those with permission
        else
        {
            if (!authToken.HasScope(UserPermissions.EconomyViewPlanet))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewPlanet);

            if (!authToken.HasScope(UserPermissions.EconomySendPlanet))
                return ValourResult.LacksPermission(UserPermissions.EconomySendPlanet);
            
            var member = await memberService.GetCurrentAsync(account.PlanetId);
            if (member is null)
                return ValourResult.NotPlanetMember();

            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageEcoAccounts))
                return ValourResult.LacksPermission(PlanetPermissions.ManageEcoAccounts);
        }
        
        var result = await ecoService.CreateTransactionAsync(transaction);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Created($"api/eco/transaction/{result.Data.Id}", result.Data);
    }

}
