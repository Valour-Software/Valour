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

    // Returns all planet accounts of the planet
    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/planet/{planetId}/planet")]
    [UserRequired]
    public static async Task<IResult> GetPlanetPlanetAccountsAsync(
        long planetId, 
        EcoService ecoService,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        
        var accounts = await ecoService.GetPlanetPlanetAccountsAsync(planetId);
        return Results.Json(accounts);
    }
    
    // Returns all user accounts of the planet
    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/planet/{planetId}/user")]
    [UserRequired]
    public static async Task<IResult> GetPlanetUserAccountsAsync(
        long planetId, 
        EcoService ecoService,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        
        var accounts = await ecoService.GetPlanetUserAccountsAsync(planetId);
        return Results.Json(accounts);
    }
    
    // Returns all accounts of the planet the given user can send to
    [ValourRoute(HttpVerbs.Post, "api/eco/accounts/planet/canSend")]
    [UserRequired]
    public static async Task<IResult> GetPlanetAccountsCanSendAsync(
        [FromBody] EcoPlanetAccountSearchRequest request,
        EcoService ecoService,
        PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(request.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        
        var accounts = await ecoService.GetPlanetAccountsCanSendAsync(request.PlanetId, request.AccountId, request.Filter);
        return Results.Json(accounts);
    }

    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/self")]
    [UserRequired]
    public static async Task<IResult> GetSelfAccountsAsync(
        EcoService ecoService, 
        TokenService tokenService)
    {
        var authToken = await tokenService.GetCurrentToken();
        var accounts = await ecoService.GetAccountsAsync(authToken.UserId);

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
    
    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/self/global")]
    [UserRequired]
    public static async Task<IResult> GetSelfGlobalAccountAsync(
        EcoService ecoService, 
        TokenService tokenService)
    {
        var authToken = await tokenService.GetCurrentToken();
        var account = await ecoService.GetGlobalAccountAsync(authToken.UserId);
        if (account is null)
            return ValourResult.NotFound("Account not found");
        
        if (!authToken.HasScope(UserPermissions.EconomyViewGlobal))
        {
            return ValourResult.LacksPermission(UserPermissions.EconomyViewGlobal);
        }

        return Results.Json(account);
    }

    /// <summary>
    /// This only returns the account's id - just because someone has your username does not
    /// mean they should be able to see your balance or details
    /// </summary>
    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/byname/{username}")]
    [UserRequired]
    public static async Task<IResult> GetGlobalAccountByNameAsync(
        string username,
        EcoService ecoService,
        UserService userService)
    {
        var user = await userService.GetByNameAsync(username);
        if (user is null)
            return ValourResult.NotFound("Account not found");
        
        var account = await ecoService.GetGlobalAccountAsync(user.Id);
        if (account is null)
            return ValourResult.NotFound("Account not found");
        
        return Results.Json(new EcoGlobalAccountSearchResult()
        {
            AccountId = account.Id,
            UserId = user.Id,
        });
    }

    [ValourRoute(HttpVerbs.Post, "api/eco/accounts")]
    [UserRequired]
    public static async Task<IResult> CreateAccountAsync(
        [FromBody] EcoAccount account,
        TokenService tokenService,
        EcoService ecoService,
        PlanetMemberService memberService)
    {
        var token = await tokenService.GetCurrentToken();
        
        if (account.UserId != token.UserId)
            return ValourResult.Forbid("You cannot create an account for another user");
        
        if (account.CurrencyId == ISharedCurrency.ValourCreditsId)
        {
            if (!token.HasScope(UserPermissions.EconomyViewGlobal))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewGlobal);
            if (!token.HasScope(UserPermissions.EconomySendGlobal))
                return ValourResult.LacksPermission(UserPermissions.EconomySendGlobal);
        }
        else
        {
            if (!token.HasScope(UserPermissions.EconomyViewPlanet))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewPlanet);
            if (!token.HasScope(UserPermissions.EconomySendPlanet))
                return ValourResult.LacksPermission(UserPermissions.EconomySendPlanet);
        }

        var member = await memberService.GetCurrentAsync(account.PlanetId);
        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        
        var result = await ecoService.CreateEcoAccountAsync(account);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.Created($"api/eco/accounts/{result.Data.Id}", result.Data);
    }
    
    [ValourRoute(HttpVerbs.Put, "api/eco/accounts/{id}")]
    [UserRequired]
    public static async Task<IResult> UpdateAccountAsync(
        long id,
        [FromBody] EcoAccount account,
        TokenService tokenService,
        EcoService ecoService,
        PlanetMemberService memberService)
    {
        if (id != account.Id)
            return ValourResult.BadRequest("Id mismatch");
        
        var token = await tokenService.GetCurrentToken();

        if (account.CurrencyId == ISharedCurrency.ValourCreditsId)
        {
            if (account.UserId != token.UserId)
                return ValourResult.Forbid("You cannot update an account for another user");
            
            if (!token.HasScope(UserPermissions.EconomyViewGlobal))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewGlobal);
            if (!token.HasScope(UserPermissions.EconomySendGlobal))
                return ValourResult.LacksPermission(UserPermissions.EconomySendGlobal);
        }
        else
        {
            if (!token.HasScope(UserPermissions.EconomyViewPlanet))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewPlanet);
            if (!token.HasScope(UserPermissions.EconomySendPlanet))
                return ValourResult.LacksPermission(UserPermissions.EconomySendPlanet);
        }

        var member = await memberService.GetCurrentAsync(account.PlanetId);
        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        
        if (account.AccountType == AccountType.User)
        {
            if (account.UserId != token.UserId)
                return ValourResult.Forbid("You cannot update an account for another user");
        }
        else
        {
            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageEcoAccounts))
                    return ValourResult.LacksPermission(PlanetPermissions.ManageEcoAccounts);
        }

        var result = await ecoService.UpdateEcoAccountAsync(account);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Delete, "api/eco/accounts/{id}")]
    [UserRequired]
    public static async Task<IResult> UpdateAccountAsync(
        long id,
        TokenService tokenService,
        EcoService ecoService,
        PlanetMemberService memberService)
    {
        var token = await tokenService.GetCurrentToken();

        var account = await ecoService.GetAccountAsync(id);
        
        if (account.BalanceValue != 0)
            return ValourResult.BadRequest("You cannot delete an account with a balance");

        if (account.CurrencyId == ISharedCurrency.ValourCreditsId)
            return ValourResult.Forbid("You cannot delete a Valour Credits account");

        if (!token.HasScope(UserPermissions.EconomyViewPlanet))
            return ValourResult.LacksPermission(UserPermissions.EconomyViewPlanet);
        if (!token.HasScope(UserPermissions.EconomySendPlanet))
            return ValourResult.LacksPermission(UserPermissions.EconomySendPlanet);
        
        
        if (account.AccountType == AccountType.User)
        {
            if (account.UserId != token.UserId)
                return ValourResult.Forbid("You cannot delete an account for another user");
        }
        else
        {
            var member = await memberService.GetCurrentAsync(account.PlanetId);
            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageEcoAccounts))
                    return ValourResult.LacksPermission(PlanetPermissions.ManageEcoAccounts);
        }

        var result = await ecoService.DeleteEcoAccountAsync(account.Id);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json("Successfully deleted account");
    }
    
    //////////////////
    // Transactions //
    //////////////////
    
    // Careful now, this is what gets Jacob VERY excited

    // Returns data required to render a receipt
    [ValourRoute(HttpVerbs.Get, "api/eco/transactions/{id}/receipt")]
    [UserRequired]
    public static async Task<IResult> GetTransactionReceiptAsync(
        string id, 
        EcoService ecoService)
    {
        var receipt = await ecoService.GetReceiptAsync(id);
        if (receipt is null)
            return ValourResult.NotFound("Transaction not found");
        
        return Results.Json(receipt);
    }

    [ValourRoute(HttpVerbs.Get, "api/eco/transactions/{id}")]
    [UserRequired]
    public static async Task<IResult> GetTransactionAsync(
        string id,
        EcoService ecoService)
    {
        // We don't need to really verify anything because the GUID is impossible to guess.
        // Only someone involved in the transaction can get it.
        var transaction = await ecoService.GetTransactionAsync(id);
        if (transaction is null)
            return ValourResult.NotFound("Transaction not found");
        
        return Results.Json(transaction);
    }
    
    
    [ValourRoute(HttpVerbs.Post, "api/eco/transactions")]
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
        var account = await ecoService.GetAccountAsync(transaction.AccountFromId);
        if (account is null)
            return ValourResult.NotFound("Account not found");

        bool issuing = false;
        
        // User account can only be used by the owner
        if (account.AccountType == AccountType.User)
        {
            if (account.Id == transaction.AccountToId)
                return ValourResult.BadRequest("You cannot send to yourself");
            
            if (!authToken.HasScope(UserPermissions.EconomyViewGlobal))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewGlobal);

            if (!authToken.HasScope(UserPermissions.EconomySendGlobal))
                return ValourResult.LacksPermission(UserPermissions.EconomySendGlobal);
            
            if (account.UserId != authToken.UserId)
                return ValourResult.Forbid("You cannot access this account");
            
            if (transaction.UserFromId != authToken.UserId)
            {
                var member = await memberService.GetCurrentAsync(account.PlanetId);
                if (member is null)
                    return ValourResult.NotPlanetMember();
                
                // Trying to send for someone else
                if (transaction.UserFromId != member.UserId)
                {
                    if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ForceTransactions))
                    {
                        return ValourResult.Forbid("You do not have permission to create transactions for other users");
                    }

                    transaction.ForcedBy = authToken.UserId;
                }
            }
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

            if (account.Id == transaction.AccountToId)
                issuing = true;
        }
        
        var result = await ecoService.CreateTransactionAsync(transaction, issuing);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Created($"api/eco/transaction/{result.Data.Id}", result.Data);
    }

}
