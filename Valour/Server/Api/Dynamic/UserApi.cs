using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Valour.Database;
using Valour.Server.Services;
using Valour.Server.Users;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using ChannelState = Valour.Server.Models.ChannelState;
using PasswordRecovery = Valour.Server.Models.PasswordRecovery;
using User = Valour.Server.Models.User;
using UserPrivateInfo = Valour.Server.Models.UserPrivateInfo;

namespace Valour.Server.Api.Dynamic;

public class UserApi
{
    [ValourRoute(HttpVerbs.Get, "api/users/count")]
    public static async Task<IResult> GetCountRouteAsync(
        UserService userService)
    {
        return Results.Json(await userService.GetUserCountAsync());
    }

    [ValourRoute(HttpVerbs.Get, "api/users/new/{count}")]
    public static async Task<IResult> GetNewAsync(int count, UserService userService)
    {
        return Results.Json(await userService.GetNewUsersAsync(count));
    }
    
    [ValourRoute(HttpVerbs.Get, "api/users/ping")]
    [UserRequired]
    public static async Task PingOnlineAsync(
        UserOnlineService onlineService, 
        UserService userService,
        [FromQuery] bool isMobile = false)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        await onlineService.UpdateOnlineState(userId);
    }

    [ValourRoute(HttpVerbs.Get, "api/users/{id}")]
    public static async Task<IResult> GetUserRouteAsync(
        long id, 
        UserService userService)
    {
        var user = await userService.GetAsync(id);
        return user is null ? ValourResult.NotFound<User>() : Results.Json(user);
    }

    [ValourRoute(HttpVerbs.Get, "api/users/byName/{name}")]
    public static async Task<IResult> GetUserByNameRouteAsync(
        string name, 
        UserService userService)
    {
        var user = await userService.GetByNameAsync(name);
        return user is null ? ValourResult.NotFound<User>() : Results.Json(user);
    }

    [ValourRoute(HttpVerbs.Put, "api/users/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] User user, 
        UserService userService)
    {
        var currentUser = await userService.GetCurrentUserAsync();

        // Unlike most other entities, we are just copying over a few fields here and
        // ignoring the rest. There are so many things that *should not* be touched by
        // the API it's smarter to just only do what *should*

        if (user.Id != currentUser.Id)
            return ValourResult.Forbid("You can only change your own user info.");

        if (user.Status is not null)
        {
            if (user.Status.Length > 64)
                return ValourResult.BadRequest("Max status length is 64 characters.");
        }

        if (user.UserStateCode > 4)
            return ValourResult.BadRequest($"User state {user.UserStateCode} does not exist.");
        
        // If we are changing the tag, make sure we are stargazer or above
        if (currentUser.Tag != user.Tag)
        {
            // Make sure user is a stargazer or above
            if (currentUser.SubscriptionType is null)
            {
                return ValourResult.Forbid("You must be a stargazer or above to change your tag.");
            }
        }
        

        var result = await userService.UpdateAsync(user);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    // This HAS to be GET so that we can forward it from the generic valour.gg domain
    [ValourRoute(HttpVerbs.Get, "api/users/verify/{code}")]
    public static async Task<IResult> VerifyEmailRouteAsync(
        string code,
        UserService userService,
        ValourDB db)
    {
        var confirmCode = await db.EmailConfirmCodes.FirstOrDefaultAsync(x => x.Code == code);
        if (confirmCode is null)
            return ValourResult.NotFound("Invalid code.");
        
        
        var result = await userService.VerifyAsync(code);
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        // Check for invite code

        var query = "";

        var userInfo = await db.UserEmails.FirstOrDefaultAsync(x => x.UserId == confirmCode.UserId);
        if (!string.IsNullOrWhiteSpace(userInfo.JoinInviteCode))
        {
            query = $"?redirect=/i/{userInfo.JoinInviteCode}";
        }
        
        return Results.LocalRedirect("/FromVerify" + query, true, false);
    }

    [ValourRoute(HttpVerbs.Post, "api/users/self/compliance/{birthDate}/{locality}")]
    [UserRequired(UserPermissionsEnum.FullControl)] // Require direct login
    public static async Task<IResult> SetComplianceData(UserService service, DateTime? birthDate, Locality? locality)
    {
        var userId = await service.GetCurrentUserIdAsync();
        
        if (birthDate is null)
            return ValourResult.BadRequest("Birth date cannot be null.");

        if (locality is null)
            return ValourResult.BadRequest("Locality cannot be null");

        var notNullBirthDate = birthDate.Value;
        var notNullLocality = locality.Value;

        var result = await service.SetUserComplianceData(userId, notNullBirthDate, notNullLocality);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/users/self/logout")]
    public static async Task<IResult> LogOutRouteAsync(UserService userService)
    {
        var result = await userService.Logout();
        return Results.Ok("Come back soon!");
    }

    [ValourRoute(HttpVerbs.Get, "api/users/self")]
    public static async Task<IResult> SelfRouteAsync(
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();

        if (user is null) // This case would be bad for whoever is using this lol
            return ValourResult.NotFound<User>(); // I mean really this should not happen but you know how life is
                                                  // Sometimes things do be wrong

        return Results.Json(user);
    }

    [ValourRoute(HttpVerbs.Get, "api/users/self/channelstates")]
    public static async Task<IResult> ChannelStatesRouteAsync(
        UserService userService)
    {
        var channelStates = await userService.GetUserChannelStatesAsync(await userService.GetCurrentUserIdAsync());

        return Results.Json(channelStates);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/users/self/statedata")]
    public static async Task<IResult> ChannelStateDataRouteAsync(
        UserService userService,
        ChannelStateService channelStateService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        
        var userChannelStates = (await userService.GetUserChannelStatesAsync(userId)).ToDictionary(x => x.ChannelId);
        var channelStates = await channelStateService.GetChannelStates(userId);

        List<ChannelStateData> stateData = new();

        foreach (var pair in channelStates)
        {
            var hasUserState = userChannelStates.TryGetValue(pair.Key, out var userState);

            DateTime userStateTime;
            
            if (!hasUserState)
            {
                userStateTime = DateTime.MaxValue;
            }
            else
            {
                userStateTime = userState.LastViewedTime;
            }
            
            stateData.Add(new ChannelStateData()
            {
                ChannelId = pair.Key,
                LastViewedTime = userStateTime,
                ChannelState = new ChannelState()
                {
                    ChannelId = pair.Value.ChannelId,
                    LastUpdateTime = pair.Value.LastUpdateTime,
                    PlanetId = pair.Value.PlanetId
                }
            });
        }

        return Results.Json(stateData);
    }

    [RateLimit("login")]
    [ValourRoute(HttpVerbs.Post, "api/users/token")]
    public static async Task<IResult> GetTokenRouteAsync(
        [FromBody] TokenRequest tokenRequest,
        HttpContext ctx,
        UserService userService)
    {
        if (tokenRequest is null)
            return ValourResult.BadRequest("Include request in body.");

        UserPrivateInfo userPrivateInfo = await userService.GetUserEmailAsync(tokenRequest.Email);

        if (userPrivateInfo is null)
            return ValourResult.InvalidToken();

        if (!userPrivateInfo.Verified)
            return ValourResult.Forbid("This account needs email verification. Please check your email.");

        var user = await userService.GetAsync(userPrivateInfo.UserId);

        if (user.Disabled)
            return ValourResult.Forbid("Your account is disabled.");

        var validResult = await userService.ValidateAsync(Valour.Database.CredentialType.PASSWORD, tokenRequest.Email, tokenRequest.Password);
        if (!validResult.Success)
            return Results.Unauthorized();

        var result = await userService.GetTokenAfterLoginAsync(ctx, userPrivateInfo.UserId);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/users/self/recovery")]
    public static async Task<IResult> RecoverPasswordRouteAsync(
        [FromBody] PasswordRecoveryRequest request,
        UserService userService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var passValid = UserUtils.TestPasswordComplexity(request.Password);
        if (!passValid.Success)
            return ValourResult.BadRequest(passValid.Message);

        var recovery = await userService.GetPasswordRecoveryAsync(request.Code);
        if (recovery is null)
            return ValourResult.NotFound<PasswordRecovery>();

        // Old credentialsto set 
        Valour.Database.Credential cred = await userService.GetCredentialAsync(recovery.UserId);
        if (cred is null)
            return ValourResult.BadRequest("No old credentials found. Do you log in via third party service (Like Google)?");

        var result = await userService.RecoveryUserAsync(request, recovery, cred);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/users/register")]
    public static async Task<IResult> RegisterUserRouteAsync(
        [FromBody] RegisterUserRequest request, 
        UserService userService,
        RegisterService registerService,
        HttpContext ctx)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body");

        // Prevent trailing whitespace
        request.Username = request.Username.Trim();
        // Prevent comparisons issues
        request.Email = request.Email.ToLower();

        var result = await registerService.RegisterUserAsync(request, ctx);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok("Your confirmation email has been sent!");
    }

    [ValourRoute(HttpVerbs.Post, "api/users/resendemail")]
    public static async Task<IResult> ResendRegistrationEmail(
        [FromBody] RegisterUserRequest request,
        UserService userService,
        RegisterService registerService,
        HttpContext ctx)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body");

        UserPrivateInfo userPrivateInfo = await userService.GetUserEmailAsync(request.Email);

        if (userPrivateInfo is null)
            return ValourResult.NotFound("Could not find user. Retry registration?");

        if (userPrivateInfo.Verified)
            return Results.Ok("You are already verified, you can close this!");

        var result = await registerService.ResendRegistrationEmail(userPrivateInfo, ctx, request);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return ValourResult.Ok("Confirmation email has been resent!");
    }

    [ValourRoute(HttpVerbs.Post, "api/users/resetpassword")]
    public static async Task<IResult> ResetPasswordRouteAsync(
        [FromBody] string email,
        UserService userService,
        HttpContext ctx)
    {
        var userEmail = await userService.GetUserEmailAsync(email, true);

        if (userEmail is null)
            return ValourResult.NotFound<UserPrivateInfo>();

        var result = await userService.SendPasswordResetEmail(userEmail, email, ctx);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "api/users/self/planets"),]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetPlanetsRouteAsync(
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        var planets = await userService.GetPlanetsUserIn(userId);

        return Results.Json(planets);
    }

    [ValourRoute(HttpVerbs.Get, "api/users/self/planetids")]
    [UserRequired(UserPermissionsEnum.Membership)]

    public static async Task<IResult> GetPlanetIdsRouteAsync(
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        var planets = (await userService.GetPlanetsUserIn(userId)).Select(x => x.Id).ToList();

        return Results.Json(planets);
    }

    [ValourRoute(HttpVerbs.Get, "api/users/{id}/friends")]
    [UserRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> GetFriendsRouteAsync(
        long id, 
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        if (id != userId)
            return ValourResult.Forbid("You cannot currently view another user's friends.");

        return Results.Json(await userService.GetFriends(id));
    }

    [ValourRoute(HttpVerbs.Get, "api/users/{id}/frienddata")]
    [UserRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> GetFriendDataRouteAsync(
        long id, 
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        if (id != userId)
            return ValourResult.Forbid("You cannot currently view another user's friend data.");

        var result = await userService.GetFriendsDataAsync(userId);

        return Results.Json(new
        {
            added = result.added,
            addedBy = result.addedBy
        });
    }
    
    [ValourRoute(HttpVerbs.Get, "api/users/self/tenorfavorites")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetTenorFavoritesRouteAsync(
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        return Results.Json(await userService.GetTenorFavoritesAsync(userId));
    }

    [ValourRoute(HttpVerbs.Get, "api/users/self/referrals")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetReferralsAsync(UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        return Results.Json(await userService.GetReferralDataAsync(userId));
    }

    [ValourRoute(HttpVerbs.Post, "api/users/self/hardDelete")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> DeleteAccountAsync(UserService userService, [FromBody] DeleteAccountModel model)
    {
        // Get user id
        var user = await userService.GetCurrentUserAsync();
        var cred = await userService.GetCredentialAsync(user.Id);
        
        // Check password
        var passResult = await userService.ValidateAsync(CredentialType.PASSWORD, cred.Identifier, model.Password);

        if (!passResult.Success)
        {
            return ValourResult.Forbid(passResult.Message);
        }
        
        // Validated
        var result =  await userService.HardDelete(user);
        if (!result.Success)
        {
            return ValourResult.Problem(result.Message);
        }

        return ValourResult.Ok("Deleted.");
    }
    
    [ValourRoute(HttpVerbs.Post, "api/users/query")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [StaffRequired]
    public static async Task<IResult> QueryUsersAsync(
        UserService userService,
        [FromBody] UserQueryModel query,
        [FromQuery] int amount = 50,
        [FromQuery] int page = 0)
    {
        var result = await userService.QueryUsersAsync(query.UsernameAndTag, amount * page, amount);
        return Results.Json(result);
    }
}