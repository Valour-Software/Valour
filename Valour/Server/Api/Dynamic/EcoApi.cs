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
        if (currency is null)
            return ValourResult.BadRequest("Include currency in body");

        if (currency.Id != id)
            return ValourResult.BadRequest("Id mismatch");

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

        var authToken = await tokenService.GetCurrentTokenAsync();

        // Resolving an account by id stays open so integrations can be built
        // against it, but the balance is owner-only. Account ids are
        // discoverable from a username via the byname route, so returning a
        // balance here would expose every user's holdings.
        var isOwner = account.UserId == authToken.UserId;

        if (account.CurrencyId == ISharedCurrency.ValourCreditsId)
        {
            if (!authToken.HasScope(UserPermissions.EconomyViewGlobal))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewGlobal);

            if (!isOwner)
                return Results.Json(WithoutBalance(account));
        }
        else
        {
            if (!authToken.HasScope(UserPermissions.EconomyViewPlanet))
                return ValourResult.LacksPermission(UserPermissions.EconomyViewPlanet);

            // Shared accounts are community funds, so members who can use the
            // economy may see them. Another member's personal balance needs
            // ManageEcoAccounts. Everyone else gets metadata only.
            if (!isOwner)
            {
                var member = await memberService.GetCurrentAsync(account.PlanetId);
                if (member is null ||
                    !await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
                {
                    return Results.Json(WithoutBalance(account));
                }

                var canViewMemberBalances = await CanViewMemberBalancesAsync(member, memberService);
                return Results.Json(ForViewer(account, authToken.UserId, canViewMemberBalances));
            }
        }

        return Results.Json(account);
    }

    /// <summary>
    /// Whether the member may see the balances of other members' personal
    /// accounts on their planet. Valour Credits balances stay private regardless.
    /// </summary>
    private static ValueTask<bool> CanViewMemberBalancesAsync(PlanetMember member, PlanetMemberService memberService) =>
        memberService.HasPermissionAsync(member, PlanetPermissions.ManageEcoAccounts);

    /// <summary>
    /// Returns the account as a planet member with economy access may see it.
    /// Shared accounts and the viewer's own accounts are returned whole. Another
    /// user's personal balance is stripped unless the viewer can manage economy
    /// accounts, and another user's Valour Credits balance is always stripped.
    /// </summary>
    private static EcoAccount ForViewer(EcoAccount account, long viewerUserId, bool canViewMemberBalances)
    {
        if (account is null ||
            account.AccountType == AccountType.Shared ||
            account.UserId == viewerUserId)
        {
            return account;
        }

        if (canViewMemberBalances && account.CurrencyId != ISharedCurrency.ValourCreditsId)
            return account;

        return WithoutBalance(account);
    }

    /// <summary>
    /// Copy of an account with the balance stripped, for callers allowed to
    /// resolve the account but not to see what is in it.
    /// </summary>
    private static EcoAccount WithoutBalance(EcoAccount account) => new()
    {
        Id = account.Id,
        Name = account.Name,
        AccountType = account.AccountType,
        UserId = account.UserId,
        PlanetId = account.PlanetId,
        PlanetMemberId = account.PlanetMemberId,
        CurrencyId = account.CurrencyId,
        BalanceValue = 0,
    };

    // Returns all planet accounts of the planet
    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/planet/{planetId}/planet")]
    [UserRequired]
    public static async Task<IResult> GetPlanetSharedAccountsAsync(
        long planetId, 
        EcoService ecoService,
        PlanetMemberService memberService,
        int skip = 0,
        int take = 50)
    {
        if (take > 50)
            take = 50;
        
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        
        var accounts = await ecoService.GetPlanetSharedAccountsAsync(planetId, skip, take);
        return Results.Json(accounts);
    }
    
    // Returns all user accounts of the planet
    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/planet/{planetId}/user")]
    [UserRequired]
    public static async Task<IResult> GetPlanetUserAccountsAsync(
        long planetId, 
        EcoService ecoService,
        PlanetMemberService memberService,
        TokenService tokenService,
        int skip = 0,
        int take = 50)
    {
        if (take > 50)
            take = 50;
        
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        
        var authToken = await tokenService.GetCurrentTokenAsync();
        var canViewMemberBalances = await CanViewMemberBalancesAsync(member, memberService);

        // Ordering by balance would rank members by wealth even with the
        // balances stripped, so it is reserved for callers who can see them.
        var accounts = await ecoService.GetPlanetUserAccountsAsync(planetId, skip, take, canViewMemberBalances);
        accounts.Items = accounts.Items
            .Select(x => ForViewer(x, authToken.UserId, canViewMemberBalances))
            .ToList();

        return Results.Json(accounts);
    }
    
    // Returns all user accounts (with members) of the planet
    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/planet/{planetId}/member")]
    [UserRequired]
    public static async Task<IResult> GetPlanetUserAccountMembersAsync(
        long planetId, 
        EcoService ecoService,
        PlanetMemberService memberService,
        TokenService tokenService,
        int skip = 0,
        int take = 50)
    {
        if (take > 50)
            take = 50;
        
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        
        var authToken = await tokenService.GetCurrentTokenAsync();
        var canViewMemberBalances = await CanViewMemberBalancesAsync(member, memberService);

        var accounts = await ecoService.GetPlanetUserAccountMembersAsync(planetId, skip, take, canViewMemberBalances);
        foreach (var item in accounts.Items)
            item.Account = ForViewer(item.Account, authToken.UserId, canViewMemberBalances);

        return Results.Json(accounts);
    }
    
    // Returns the user account in a planet with the given id
    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/planet/{planetId}/byuser/{userId}")]
    [UserRequired]
    public static async Task<IResult> GetUserAccountAsync(
        long planetId,
        long userId,
        EcoService ecoService,
        PlanetMemberService memberService,
        TokenService tokenService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        
        var account = await ecoService.GetUserAccountAsync(userId, planetId);
        if (account is null || account.UserId == member.UserId)
            return Results.Json(account);

        var canViewMemberBalances = await CanViewMemberBalancesAsync(member, memberService);
        return Results.Json(ForViewer(account, member.UserId, canViewMemberBalances));
    }
    
    // Returns all accounts of the planet the given user can send to
    [ValourRoute(HttpVerbs.Post, "api/eco/accounts/planet/canSend")]
    [UserRequired]
    public static async Task<IResult> GetPlanetAccountsCanSendAsync(
        [FromBody] EcoPlanetAccountSearchRequest request,
        EcoService ecoService,
        PlanetMemberService memberService,
        TokenService tokenService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include search request in body");

        var member = await memberService.GetCurrentAsync(request.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);
        
        var authToken = await tokenService.GetCurrentTokenAsync();
        var canViewMemberBalances = await CanViewMemberBalancesAsync(member, memberService);

        var accounts = await ecoService.GetPlanetAccountsCanSendAsync(request.PlanetId, request.AccountId, request.Filter);
        foreach (var result in accounts)
            result.Account = ForViewer(result.Account, authToken.UserId, canViewMemberBalances);

        return Results.Json(accounts);
    }

    [ValourRoute(HttpVerbs.Get, "api/eco/accounts/self")]
    [UserRequired]
    public static async Task<IResult> GetSelfAccountsAsync(
        EcoService ecoService, 
        TokenService tokenService)
    {
        var authToken = await tokenService.GetCurrentTokenAsync();
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
        var authToken = await tokenService.GetCurrentTokenAsync();
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
        var user = await userService.GetByNameAndTagAsync(username);
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
        if (account is null)
            return ValourResult.BadRequest("Include account in body");

        var token = await tokenService.GetCurrentTokenAsync();

        if (account.UserId != token.UserId)
            return ValourResult.Forbid("You cannot create an account for another user");

        if (!Enum.IsDefined(account.AccountType))
            return ValourResult.BadRequest("Invalid account type");

        if (account.BalanceValue != 0)
            return ValourResult.BadRequest("Initial balance must be zero");

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
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);

        // Shared accounts hold community funds, so only economy managers open them
        if (account.AccountType == AccountType.Shared &&
            !await memberService.HasPermissionAsync(member, PlanetPermissions.ManageEcoAccounts))
            return ValourResult.LacksPermission(PlanetPermissions.ManageEcoAccounts);

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
        if (account is null)
            return ValourResult.BadRequest("Include account in body");

        if (id != account.Id)
            return ValourResult.BadRequest("Id mismatch");

        // Only the name can change, and it is required
        if (string.IsNullOrWhiteSpace(account.Name))
            return ValourResult.BadRequest("Account name is required");

        // Authorize against the stored account, not the ownership fields in the body
        var stored = await ecoService.GetAccountAsync(id);
        if (stored is null)
            return ValourResult.NotFound("Account not found");

        var token = await tokenService.GetCurrentTokenAsync();

        if (stored.CurrencyId == ISharedCurrency.ValourCreditsId)
        {
            if (stored.UserId != token.UserId)
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

        var member = await memberService.GetCurrentAsync(stored.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.UseEconomy))
            return ValourResult.LacksPermission(PlanetPermissions.UseEconomy);

        if (stored.AccountType == AccountType.User)
        {
            if (stored.UserId != token.UserId)
                return ValourResult.Forbid("You cannot update an account for another user");
        }
        else
        {
            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageEcoAccounts))
                    return ValourResult.LacksPermission(PlanetPermissions.ManageEcoAccounts);
        }

        var result = await ecoService.UpdateEcoAccountNameAsync(stored.Id, account.Name);
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
        var token = await tokenService.GetCurrentTokenAsync();

        var account = await ecoService.GetAccountAsync(id);
        if (account is null)
            return ValourResult.NotFound("Account not found");
        
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
            if (member is null)
                return ValourResult.NotPlanetMember();

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

        var metadata = EcoService.ValidateTransactionMetadata(transaction);
        if (!metadata.Success)
            return ValourResult.BadRequest(metadata.Message);

        var authToken = await tokenService.GetCurrentTokenAsync();
        var account = await ecoService.GetAccountAsync(transaction.AccountFromId);
        if (account is null)
            return ValourResult.NotFound("Account not found");

        bool issuing = false;

        // The sender identity is derived from the account and the caller rather
        // than trusted from the body, because recipients are notified in the
        // sender's name. The service fills in the planet and recipient user
        // from the stored accounts.
        long userFromId;
        long? forcedBy = null;

        // User account can only be used by the owner
        if (account.AccountType == AccountType.User)
        {
            if (account.Id == transaction.AccountToId)
                return ValourResult.BadRequest("You cannot send to yourself");

            if (account.CurrencyId == ISharedCurrency.ValourCreditsId)
            {
                if (!authToken.HasScope(UserPermissions.EconomyViewGlobal))
                    return ValourResult.LacksPermission(UserPermissions.EconomyViewGlobal);

                if (!authToken.HasScope(UserPermissions.EconomySendGlobal))
                    return ValourResult.LacksPermission(UserPermissions.EconomySendGlobal);
            }
            else
            {
                if (!authToken.HasScope(UserPermissions.EconomyViewPlanet))
                    return ValourResult.LacksPermission(UserPermissions.EconomyViewPlanet);

                if (!authToken.HasScope(UserPermissions.EconomySendPlanet))
                    return ValourResult.LacksPermission(UserPermissions.EconomySendPlanet);
            }

            // Trying to send for someone else
            if (account.UserId != authToken.UserId)
            {
                var member = await memberService.GetCurrentAsync(account.PlanetId);
                if (member is null)
                    return ValourResult.NotPlanetMember();

                if (transaction.ForcedBy != authToken.UserId)
                {
                    return ValourResult.Forbid("You must mark a transaction as forced if you are not the sender");
                }

                if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ForceTransactions))
                {
                    return ValourResult.Forbid("You do not have permission to create transactions for other users");
                }

                forcedBy = authToken.UserId;
            }

            userFromId = account.UserId;
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

            // Shared accounts have no single owner, so the sender is whoever moved the funds
            userFromId = authToken.UserId;
        }

        transaction.UserFromId = userFromId;
        transaction.ForcedBy = forcedBy;

        var result = await ecoService.CreateTransactionAsync(transaction, issuing);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Created($"api/eco/transaction/{result.Data.Id}", result.Data);
    }

}
