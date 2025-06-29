using Microsoft.AspNetCore.Mvc;
using Valour.Database;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Queries;
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
        var user = await userService.GetByNameAndTagAsync(name);
        return user is null ? ValourResult.NotFound<User>() : Results.Json(user);
    }

    [ValourRoute(HttpVerbs.Put, "api/users/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] User user,
        long id,
        UserService userService)
    {
        var currentUser = await userService.GetCurrentUserAsync();

        if (user.Id != id)
            return ValourResult.BadRequest("Route id does not match user id");

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
        ValourDb db)
    {
        var confirmCode = await db.EmailConfirmCodes.FirstOrDefaultAsync(x => x.Code == code);
        if (confirmCode is null)
            return ValourResult.NotFound("Invalid code.");
        
        
        var result = await userService.VerifyAsync(code);
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        // Check for invite code

        var query = "";

        var userInfo = await db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == confirmCode.UserId);
        if (!string.IsNullOrWhiteSpace(userInfo.JoinInviteCode))
        {
            query = $"?redirect=/i/{userInfo.JoinInviteCode}";
        }
        
        return Results.LocalRedirect("/FromVerify" + query, true, false);
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/compliance/{birthDate}/{locality}")]
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

    [ValourRoute(HttpVerbs.Post, "api/users/me/logout")]
    public static async Task<IResult> LogOutRouteAsync(UserService userService)
    {
        var result = await userService.Logout();
        return Results.Ok("Come back soon!");
    }

    [ValourRoute(HttpVerbs.Get, "api/users/me")]
    public static async Task<IResult> SelfRouteAsync(
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();

        if (user is null) // This case would be bad for whoever is using this lol
            return ValourResult.NotFound<User>(); // I mean really this should not happen but you know how life is
                                                  // Sometimes things do be wrong

        return Results.Json(user);
    }

    [ValourRoute(HttpVerbs.Get, "api/users/me/channelstates")]
    public static async Task<IResult> ChannelStatesRouteAsync(
        UserService userService)
    {
        var channelStates = await userService.GetUserChannelStatesAsync(await userService.GetCurrentUserIdAsync());

        return Results.Json(channelStates);
    }

    [RateLimit("login")]
    [ValourRoute(HttpVerbs.Post, "api/users/token")]
    public static async Task<IResult> GetTokenRouteAsync(
        [FromBody] TokenRequest tokenRequest,
        HttpContext ctx,
        UserService userService,
        MultiAuthService multiAuthService)
    {
        if (tokenRequest is null)
            return ValourResult.BadRequest("Include request in body.");

        UserPrivateInfo userPrivateInfo = await userService.GetUserPrivateInfoAsync(tokenRequest.Email);

        if (userPrivateInfo is null)
            return ValourResult.InvalidToken();

        if (!userPrivateInfo.Verified)
            return ValourResult.Forbid("This account needs email verification. Please check your email.");

        var user = await userService.GetAsync(userPrivateInfo.UserId);

        if (user.Disabled)
            return ValourResult.Forbid("Your account is disabled.");

        Models.AuthToken? token = null;
        
        // Check for multi auth
        var multiAuths = await multiAuthService.GetAppMultiAuthTypes(userPrivateInfo.UserId);
        var requiresMultiAuth = multiAuths.Count > 0;
        if (requiresMultiAuth){
            if (string.IsNullOrWhiteSpace(tokenRequest.MultiFactorCode))
            {
                return Results.Json(new AuthResult
                {
                    Success = true,
                    Token = null,
                    Message = "Multi-auth is required for this account.",
                    RequiresMultiAuth = true
                });
            }
            else
            {
                var mfaValid = multiAuthService.VerifyAppMultiAuth(userPrivateInfo.UserId, tokenRequest.MultiFactorCode);
                if (!mfaValid.Result.Success)
                    return ValourResult.Forbid("Invalid code.");
            }
        }

        var validResult = await userService.ValidateCredentialAsync(CredentialType.PASSWORD, tokenRequest.Email, tokenRequest.Password);
        if (validResult.Success) {            
            var result = await userService.GetTokenAfterLoginAsync(ctx, userPrivateInfo.UserId);
            if (!result.Success)
                return ValourResult.Problem(result.Message);

            token = result.Data;
        }

        return Results.Json(new AuthResult {
            Success = validResult.Success,
            Token = token,
            Message = validResult.Message,
            RequiresMultiAuth = false
        });
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/recovery")]
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

        UserPrivateInfo userPrivateInfo = await userService.GetUserPrivateInfoAsync(request.Email);

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
        var userEmail = await userService.GetUserPrivateInfoAsync(email, true);

        if (userEmail is null)
            return ValourResult.NotFound<UserPrivateInfo>();

        var result = await userService.SendPasswordResetEmail(userEmail, email, ctx);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "api/users/me/planets"),]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetPlanetsRouteAsync(
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        var planets = await userService.GetJoinedPlanetInfo(userId);

        return Results.Json(planets);
    }

    [ValourRoute(HttpVerbs.Get, "api/users/me/planetids")]
    [UserRequired(UserPermissionsEnum.Membership)]

    public static async Task<IResult> GetPlanetIdsRouteAsync(
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        var planets = (await userService.GetJoinedPlanetInfo(userId)).Select(x => x.Id).ToList();

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
            added = result.outgoing,
            addedBy = result.incoming
        });
    }
    
    [ValourRoute(HttpVerbs.Get, "api/users/me/multiAuth")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetMultiFactorRouteAsync(
        UserService userService,
        MultiAuthService multiAuthService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await multiAuthService.GetAppMultiAuthTypes(userId);
        return Results.Json(result);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/users/me/multiAuth")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> SetupMultiFactorRouteAsync(
        [FromBody] CreateAppMultiAuthResponse request,
        UserService userService,
        MultiAuthService multiAuthService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await multiAuthService.CreateAppMultiAuth(userId);
        
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/users/me/multiAuth/remove")]
    public static async Task<IResult> RemoveMultiFactorRouteAsync(
        [FromBody] RemoveMfaRequest request,
        UserService userService,
        MultiAuthService multiAuthService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        
        var currentCredential = await userService.GetCredentialAsync(await userService.GetCurrentUserIdAsync());
        var validResult = await userService.ValidateCredentialAsync(CredentialType.PASSWORD, currentCredential.Identifier, request.Password);
        if (!validResult.Success)
            return ValourResult.Forbid(validResult.Message);

        var result = await multiAuthService.RemoveAppMultiAuth(userId);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok();
    }
    
    [ValourRoute(HttpVerbs.Post, "api/users/me/multiAuth/verify/{code}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> VerifyMultiFactorRouteAsync(
        string code,
        UserService userService,
        MultiAuthService multiAuthService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await multiAuthService.VerifyAppMultiAuth(userId, code);
        
        return Results.Json(result.Success);
    }
    
    [ValourRoute(HttpVerbs.Get, "api/users/me/tenorfavorites")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetTenorFavoritesRouteAsync(
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        return Results.Json(await userService.GetTenorFavoritesAsync(userId));
    }

    [ValourRoute(HttpVerbs.Get, "api/users/me/referrals")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetReferralsAsync(UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        return Results.Json(await userService.GetReferralDataAsync(userId));
    }
    
    [ValourRoute(HttpVerbs.Post, "api/users/me/password")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ChangePasswordRouteAsync(
        [FromBody] ChangePasswordRequest request,
        UserService userService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");
        
        // Ensure current password is valid
        var currentCredential = await userService.GetCredentialAsync(await userService.GetCurrentUserIdAsync());
        var validResult = await userService.ValidateCredentialAsync(CredentialType.PASSWORD, currentCredential.Identifier, request.OldPassword);
        if (!validResult.Success)
            return ValourResult.Forbid(validResult.Message);

        var passValid = UserUtils.TestPasswordComplexity(request.NewPassword);
        if (!passValid.Success)
            return ValourResult.BadRequest(passValid.Message);

        var userId = await userService.GetCurrentUserIdAsync();
        var result = await userService.ChangePasswordAsync(userId, request.NewPassword);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();
    }
    
    [ValourRoute(HttpVerbs.Post, "api/users/me/username")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ChangeUsernameRouteAsync(
        [FromBody] ChangeUsernameRequest request,
        UserService userService)
    {
        if (request is null)
        {
            return ValourResult.BadRequest("Include request in body.");
        }
        
        var credential = await userService.GetCredentialAsync(await userService.GetCurrentUserIdAsync());
        
        // Verify password
        var validResult = await userService.ValidateCredentialAsync(CredentialType.PASSWORD, credential.Identifier, request.Password);
        if (!validResult.Success)
            return ValourResult.Forbid(validResult.Message);
        
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await userService.ChangeUsernameAsync(userId, request.NewUsername);
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/hardDelete")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> DeleteAccountAsync(UserService userService, [FromBody] DeleteAccountModel model)
    {
        // Get user id
        var user = await userService.GetCurrentUserAsync();
        var cred = await userService.GetCredentialAsync(user.Id);
        
        // Check password
        var passResult = await userService.ValidateCredentialAsync(CredentialType.PASSWORD, cred.Identifier, model.Password);

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
    
    [ValourRoute(HttpVerbs.Post, "api/staff/users/query")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [StaffRequired]
    public static async Task<IResult> QueryUsersAsync(
        [FromBody] QueryRequest queryRequest,
        UserService userService)
    {
        var result = await userService.QueryUsersAsync(queryRequest);
        return Results.Json(result);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/user/me/tutorial/{id}/{value}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> SetTutorialFinishedAsync(
        int id,
        bool value,
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await userService.SetTutorialStepFinishedAsync(userId, id, value);
        return Results.Json(result);
    }
    
}