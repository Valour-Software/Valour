using Microsoft.AspNetCore.Mvc;
using Valour.Server.Users;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class UserApi
{
    [ValourRoute(HttpVerbs.Get, "api/users/ping")]
    public static async Task PingOnlineAsync(
        UserOnlineService onlineService, 
        UserService userService,
        [FromQuery] bool isMobile = false)
    {
        var userId = await userService.GetCurrentUserId();
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
        var userId = await userService.GetCurrentUserId();

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

        var confirmCode = await userService.GetEmailConfirmCode(code);

        if (confirmCode is null)
            return ValourResult.NotFound<EmailConfirmCode>();

        var result = await userService.VerifyAsync(confirmCode);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.LocalRedirect("/FromVerify", true, false);
    }

    [ValourRoute(HttpVerbs.Post, "api/users/self/logout")]
    public static async Task<IResult> LogOutRouteAsync(
        TokenService tokenService,
        UserService userService)
    {
        var result = userService.Logout(await tokenService.GetCurrentToken());

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
        var channelStates = userService.GetUserChannelStatesAsync(await userService.GetCurrentUserId());

        return Results.Json(channelStates);
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

    [ValourRoute(HttpVerbs.Post, "/resendemail")]
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

    [ValourRoute(HttpVerbs.Post, "/resetpassword")]
    public static async Task<IResult> ResetPasswordRouteAsync(
        [FromBody] string email,
        HttpContext ctx, 
        ValourDB db,
        ILogger<User> logger)
    {
        var userEmail = await db.UserEmails.FirstOrDefaultAsync(x => x.Email.ToLower() == email.ToLower());

        if (userEmail is null)
            return ValourResult.NotFound<UserEmail>();

        try
        {
            var oldRecoveries = db.PasswordRecoveries.Where(x => x.UserId == userEmail.UserId);
            if (oldRecoveries.Any())
            {
                db.PasswordRecoveries.RemoveRange(oldRecoveries);
                await db.SaveChangesAsync();
            }

            string recoveryCode = Guid.NewGuid().ToString();

            PasswordRecovery recovery = new()
            {
                Code = recoveryCode,
                UserId = userEmail.UserId
            };

            await db.PasswordRecoveries.AddAsync(recovery);
            await db.SaveChangesAsync();

            var host = ctx.Request.Host.ToUriComponent();
            string link = $"{ctx.Request.Scheme}://{host}/RecoverPassword/{recoveryCode}";

            string emsg = $@"<body>
                              <h2 style='font-family:Helvetica;'>
                                Valour Password Recovery
                              </h2>
                              <p style='font-family:Helvetica;>
                                If you did not request this email, please ignore it.
                                To reset your password, please use the following link: 
                              </p>
                              <p style='font-family:Helvetica;'>
                                <a href='{link}'>Click here to recover</a>
                              </p>
                            </body>";

            string rawmsg = $"To reset your password, please go to the following link:\n{link}";

            var result = await EmailManager.SendEmailAsync(email, "Valour Password Recovery", rawmsg, emsg);

            if (!result.IsSuccessStatusCode)
            {
                logger.LogError($"Error issuing password reset email to {email}. Status code {result.StatusCode}.");
                return ValourResult.Problem("Sorry! There was an issue sending the email. Try again?");
            }
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return ValourResult.Problem("Sorry! An unexpected error occured. Try again?");
        }

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/self/planets"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]

    public static async Task<IResult> GetPlanetsRouteAsync(
        HttpContext ctx, 
        ValourDB db)
    {
        var token = ctx.GetToken();

        var planets = await db.PlanetMembers
            .Where(x => x.UserId == token.UserId)
            .Include(x => x.Planet)
            .Select(x => x.Planet)
            .ToListAsync();

        return Results.Json(planets);
    }

    [ValourRoute(HttpVerbs.Get, "/self/planetids"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]

    public static async Task<IResult> GetPlanetIdsRouteAsync(
        HttpContext ctx, 
        ValourDB db)
    {
        var token = ctx.GetToken();

        var planets = await db.PlanetMembers
            .Where(x => x.UserId == token.UserId)
            .Select(x => x.PlanetId)
            .ToListAsync();

        return Results.Json(planets);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/friends"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> GetFriendsRouteAsync(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
        var token = ctx.GetToken();

        if (id != token.UserId)
            return ValourResult.Forbid("You cannot currently view another user's friends.");

        // Users added by this user as a friend (user -> other)
        var added = db.UserFriends.Where(x => x.UserId == id);

        // Users who added this user as a friend (other -> user)
        var addedBy = db.UserFriends.Where(x => x.FriendId == id);

        // Mutual friendships
        var mutual = added.Select(x => x.FriendId).Intersect(addedBy.Select(x => x.UserId));

        var friends = await db.Users.Where(x => mutual.Contains(x.Id)).ToListAsync();

        return Results.Json(friends);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/frienddata"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Friends)]
    public static async Task<IResult> GetFriendDataRouteAsync(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
        var token = ctx.GetToken();

        if (id != token.UserId)
            return ValourResult.Forbid("You cannot currently view another user's friend data.");

        // Users added by this user as a friend (user -> other)
        var added = await db.UserFriends.Include(x => x.Friend).Where(x => x.UserId == id).Select(x => x.Friend).ToListAsync();

        // Users who added this user as a friend (other -> user)
        var addedBy = await db.UserFriends.Include(x => x.User).Where(x => x.FriendId == id).Select(x => x.User).ToListAsync();

        List<User> usersAdded = new();
        List<User> usersAddedBy = new();

        return Results.Json(new
        {
            added = added,
            addedBy = addedBy
        });
    }
    
    [ValourRoute(HttpVerbs.Get, "/self/tenorfavorites"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]

    public static async Task<IResult> GetTenorFavoritesRouteAsync(
        HttpContext ctx, 
        ValourDB db)
    {
        var token = ctx.GetToken();

        var favorites = await db.TenorFavorites
            .Where(x => x.UserId == token.UserId)
            .ToListAsync();

        return Results.Json(favorites);
    }
}