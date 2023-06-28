using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Valour.Server.Services;
using Valour.Server.Users;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class UserApi
{
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

    [ValourRoute(HttpVerbs.Put, "api/users/{id}")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] User user, 
        UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        // Unlike most other entities, we are just copying over a few fields here and
        // ignoring the rest. There are so many things that *should not* be touched by
        // the API it's smarter to just only do what *should*

        if (user.Id != userId)
            return ValourResult.Forbid("You can only change your own user info.");

        if (user.Status.Length > 64)
            return ValourResult.BadRequest("Max status length is 64 characters.");

        if (user.UserStateCode > 4)
            return ValourResult.BadRequest($"User state {user.UserStateCode} does not exist.");

        var result = await userService.UpdateAsync(user);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    // This HAS to be GET so that we can forward it from the generic valour.gg domain
    [ValourRoute(HttpVerbs.Get, "api/users/verify/{code}")]
    public static async Task<IResult> VerifyEmailRouteAsync(
        string code,
        UserService userService)
    {
        var result = await userService.VerifyAsync(code);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.LocalRedirect("/FromVerify", true, false);
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

        var userChannels = await userService.GetAccessiblePlanetChatChannelIdsAsync(userId);
        var userChannelStates = (await userService.GetUserChannelStatesAsync(userId)).ToDictionary(x => x.ChannelId);
        var channelStates = await channelStateService.GetChannelStates(userChannels);

        List<ChannelStateData> stateData = new();

        foreach (var channelId in userChannels)
        {
            userChannelStates.TryGetValue(channelId, out var userChannelState);
            channelStates.TryGetValue(channelId, out var channelState);
            stateData.Add(new ChannelStateData()
            {
                ChannelId = channelId,
                LastViewedTime = userChannelState?.LastViewedTime ?? DateTime.MaxValue,
                ChannelState = channelState?.ToModel(),
            });
        }

        return Results.Json(stateData);
    }

    [ValourRoute(HttpVerbs.Post, "api/users/token")]
    public static async Task<IResult> GetTokenRouteAsync(
        [FromBody] TokenRequest tokenRequest,
        HttpContext ctx,
        UserService userService)
    {
        if (tokenRequest is null)
            return ValourResult.BadRequest("Include request in body.");

        UserEmail userEmail = await userService.GetUserEmailAsync(tokenRequest.Email);

        if (userEmail is null)
            return ValourResult.InvalidToken();

        if (!userEmail.Verified)
            return ValourResult.Forbid("This account needs email verification. Please check your email.");

        var user = await userService.GetAsync(userEmail.UserId);

        if (user.Disabled)
            return ValourResult.Forbid("Your account is disabled.");

        var validResult = await userService.ValidateAsync(Valour.Database.CredentialType.PASSWORD, tokenRequest.Email, tokenRequest.Password);
        if (!validResult.Success)
            return Results.Unauthorized();

        var result = await userService.GetTokenAfterLoginAsync(ctx, userEmail.UserId);
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
        HttpContext ctx)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body");

        // Prevent trailing whitespace
        request.Username = request.Username.Trim();
        // Prevent comparisons issues
        request.Email = request.Email.ToLower();

        var result = await userService.RegisterUserAsync(request, ctx);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok("Your confirmation email has been sent!");
    }

    [ValourRoute(HttpVerbs.Post, "api/users/resendemail")]
    public static async Task<IResult> ResendRegistrationEmail(
        [FromBody] RegisterUserRequest request,
        UserService userService,
        HttpContext ctx)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body");

        UserEmail userEmail = await userService.GetUserEmailAsync(request.Email);

        if (userEmail is null)
            return ValourResult.NotFound("Could not find user. Retry registration?");

        if (userEmail.Verified)
            return Results.Ok("You are already verified, you can close this!");

        var result = await userService.ResendRegistrationEmail(userEmail, ctx, request);
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
            return ValourResult.NotFound<UserEmail>();

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
}