using Microsoft.AspNetCore.Mvc;
using Valour.Database;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Queries;
using Valour.Server.Database;
using PasswordRecovery = Valour.Server.Models.PasswordRecovery;
using User = Valour.Server.Models.User;
using UserPrivateInfo = Valour.Server.Models.UserPrivateInfo;
using DbUserPreferences = Valour.Database.UserPreferences;

namespace Valour.Server.Api.Dynamic;

public class UserApi
{
    private const string GenericAuthFailureMessage = "The credentials were incorrect.";
    private const string GenericRecoveryResponse = "If an account exists for that email, a message has been sent.";
    private const string GenericRegistrationResponse = "If registration can be completed, a confirmation email has been sent.";

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
    public static async Task<IResult> PingOnlineAsync(
        UserOnlineQueueService onlineQueue,
        UserService userService,
        [FromQuery] bool isMobile = false)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        onlineQueue.Enqueue(userId, isMobile);
        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Get, "api/users/{id}")]
    public static async Task<IResult> GetUserRouteAsync(
        long id,
        UserService userService,
        TokenService tokenService,
        UserBlockService userBlockService)
    {
        var user = await userService.GetAsync(id);
        if (user is null)
            return ValourResult.NotFound<User>();

        // If authenticated, check for two-way blocks
        var token = await tokenService.GetCurrentTokenAsync();
        if (token is not null && token.UserId != id)
        {
            if (await userBlockService.IsTwoWayBlockedAsync(token.UserId, id))
                return ValourResult.NotFound<User>();
        }

        return Results.Json(user.ForViewer(token?.UserId));
    }

    [ValourRoute(HttpVerbs.Get, "api/users/byName/{name}")]
    public static async Task<IResult> GetUserByNameRouteAsync(
        string name,
        UserService userService,
        TokenService tokenService)
    {
        var user = await userService.GetByNameAndTagAsync(name);
        if (user is null)
            return ValourResult.NotFound<User>();

        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(user.ForViewer(token?.UserId));
    }

    [ValourRoute(HttpVerbs.Get, "api/users/byNameAndTag/{name}/{tag}")]
    public static async Task<IResult> GetUserByNameAndTagRouteAsync(
        string name,
        string tag,
        UserService userService,
        TokenService tokenService)
    {
        var user = await userService.GetUserAsync(name, tag);
        if (user is null)
            return ValourResult.NotFound<User>();

        var token = await tokenService.GetCurrentTokenAsync();
        return Results.Json(user.ForViewer(token?.UserId));
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

        // If we are changing star colors, make sure we are Stargazer Pro
        if (user.StarColor1 != currentUser.StarColor1 || user.StarColor2 != currentUser.StarColor2)
        {
            if (currentUser.SubscriptionType != UserSubscriptionTypes.StargazerPro.Name)
            {
                return ValourResult.Forbid("You must be a Stargazer Pro subscriber to customize star colors.");
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
        var confirmCode = await userService.GetEmailConfirmCode(code);
        if (confirmCode is null)
            return ValourResult.NotFound("Invalid code.");
        
        
        var result = await userService.VerifyAsync(code);
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        // Check for invite code

        var query = "";

        var userInfo = await db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == confirmCode.UserId);
        if (!string.IsNullOrWhiteSpace(userInfo?.JoinInviteCode))
        {
            query = $"?redirect=/i/{userInfo.JoinInviteCode}";
        }

        return Results.LocalRedirect("/FromVerify" + query, true, false);
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/compliance/{birthDate}")]
    [UserRequired(UserPermissionsEnum.FullControl)] // Require direct login
    public static async Task<IResult> SetComplianceData(UserService service, DateTime? birthDate)
    {
        var userId = await service.GetCurrentUserIdAsync();
        
        if (birthDate is null)
            return ValourResult.BadRequest("Birth date cannot be null.");

        var notNullBirthDate = birthDate.Value;

        var result = await service.SetUserComplianceData(userId, notNullBirthDate);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return Results.NoContent();
    }

    [UserRequired]
    [ValourRoute(HttpVerbs.Post, "api/users/me/logout")]
    public static async Task<IResult> LogOutRouteAsync(UserService userService)
    {
        var result = await userService.Logout();
        return Results.Ok("Come back soon!");
    }

    // Session management is FullControl-only: a narrow-scope OAuth token must never
    // be able to enumerate or revoke the account's other sessions.
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Get, "api/users/me/tokens")]
    public static async Task<IResult> GetTokensRouteAsync(
        UserService userService,
        TokenService tokenService)
    {
        var user = await userService.GetCurrentUserAsync();
        if (user is null)
            return ValourResult.NotFound<User>();

        var currentToken = await tokenService.GetCurrentTokenAsync();

        var tokens = await userService.GetUserTokensAsync(user.Id, currentToken?.Id);
        return Results.Json(tokens);
    }

    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Delete, "api/users/me/tokens/{handle}")]
    public static async Task<IResult> RevokeTokenRouteAsync(
        string handle,
        UserService userService)
    {
        var user = await userService.GetCurrentUserAsync();
        if (user is null)
            return ValourResult.NotFound<User>();

        var result = await userService.RevokeTokenAsync(user.Id, handle);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok("Token revoked successfully");
    }

    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Delete, "api/users/me/tokens")]
    public static async Task<IResult> RevokeAllOtherTokensRouteAsync(
        UserService userService,
        TokenService tokenService)
    {
        var user = await userService.GetCurrentUserAsync();
        if (user is null)
            return ValourResult.NotFound<User>();

        var currentToken = await tokenService.GetCurrentTokenAsync();
        if (currentToken is null)
            return ValourResult.InvalidToken();

        var result = await userService.RevokeAllOtherTokensAsync(user.Id, currentToken.Id);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok(result.Message);
    }

    // Called from session management with the caller's live session, like the
    // other session routes. (An expired token cannot authenticate at all.)
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/tokens/expired/revoke")]
    public static async Task<IResult> RevokeExpiredTokensRouteAsync(
        [FromBody] RevokeExpiredTokensRequest request,
        UserService userService,
        MultiAuthService multiAuthService)
    {
        var user = await userService.GetCurrentUserAsync();
        if (user is null)
            return ValourResult.NotFound<User>();

        var mfaMethods = await multiAuthService.GetAppMultiAuthTypes(user.Id);
        if (mfaMethods.Count > 0)
        {
            var mfa = await multiAuthService.VerifyEstablishedAppMultiAuth(user.Id, request?.MultiFactorCode);
            if (!mfa.Success)
                return ValourResult.BadRequest(mfa.Message);
        }

        var result = await userService.RevokeExpiredTokensAsync(user.Id);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok(result.Message);
    }

    /// <summary>
    /// Returns when a staff-scheduled MFA removal will execute for the
    /// current account, or 404 if none is pending.
    /// </summary>
    [UserRequired]
    [ValourRoute(HttpVerbs.Get, "api/users/me/mfaremoval")]
    public static async Task<IResult> GetPendingMfaRemovalRouteAsync(
        UserService userService,
        ValourDb db)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        var pending = await db.PendingMfaRemovals
            .Where(x => x.TargetUserId == userId && x.Status == Valour.Database.MfaRemovalStatus.Pending)
            .Select(x => (DateTime?)x.ExecuteAt)
            .FirstOrDefaultAsync();

        if (pending is null)
            return ValourResult.NotFound("No pending MFA removal.");

        return Results.Json(pending);
    }

    /// <summary>
    /// Lets the account owner cancel a staff-scheduled MFA removal — the
    /// escape hatch if the removal was socially engineered.
    /// </summary>
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/mfaremoval/cancel")]
    public static async Task<IResult> CancelPendingMfaRemovalRouteAsync(
        UserService userService,
        StaffService staffService)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        var result = await staffService.CancelMfaRemovalAsync(userId, userId, isStaff: false, "Cancelled by account owner");
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok(result.Message);
    }

    [UserRequired(UserPermissionsEnum.View)]
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

    [UserRequired(UserPermissionsEnum.Membership)]
    [ValourRoute(HttpVerbs.Get, "api/users/me/channelstates")]
    public static async Task<IResult> ChannelStatesRouteAsync(
        UserService userService)
    {
        var channelStates = await userService.GetUserChannelStatesAsync(await userService.GetCurrentUserIdAsync());

        return Results.Json(channelStates);
    }

    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/users/token")]
    public static async Task<IResult> GetTokenRouteAsync(
        [FromBody] TokenRequest tokenRequest,
        HttpContext ctx,
        UserService userService,
        MultiAuthService multiAuthService,
        SignInMethodService signInMethods,
        ExternalAuthService externalAuth)
    {
        if (tokenRequest is null)
            return ValourResult.BadRequest("Include request in body.");

        // Fingerprint sign-in. The key only signs after a fingerprint check on
        // the device that holds it, which already proves two factors, so no
        // authenticator code is asked for.
        if (!string.IsNullOrWhiteSpace(tokenRequest.DeviceKeyId))
        {
            var signed = await signInMethods.VerifyDeviceSignatureAsync(
                tokenRequest.DeviceKeyId, tokenRequest.DeviceChallengeId, tokenRequest.DeviceSignature);
            if (!signed.Success)
                return Results.Json(new ServerAuthResult { Success = false, Message = signed.Message });

            var deviceUser = await userService.GetAsync(signed.Data.UserId);
            var deviceSignIn = await FinishSignInAsync(ctx, deviceUser, false, null, userService, multiAuthService);
            if (deviceSignIn.SignedIn)
                await signInMethods.MarkUsedAsync(signed.Data.Id);
            return deviceSignIn.Response;
        }

        // Google or Discord sign-in. The ticket stays valid until sign-in
        // completes, so the client can send it again with an authenticator code.
        if (!string.IsNullOrWhiteSpace(tokenRequest.ExternalTicket))
        {
            var ticket = await externalAuth.GetLoginTicketAsync(tokenRequest.ExternalTicket, tokenRequest.ExternalVerifier);
            if (ticket is null)
                return Results.Json(new ServerAuthResult { Success = false, Message = "This sign-in has expired. Try again." });

            var externalUser = await userService.GetAsync(ticket.UserId);
            var externalSignIn = await FinishSignInAsync(ctx, externalUser, true, tokenRequest.MultiFactorCode, userService, multiAuthService);
            if (externalSignIn.SignedIn)
            {
                await externalAuth.RemoveLoginTicketAsync(tokenRequest.ExternalTicket);
                await signInMethods.MarkUsedAsync(ticket.CredentialId);
            }
            return externalSignIn.Response;
        }

        tokenRequest.Email = UserUtils.SanitizeEmail(tokenRequest.Email);
        if (string.IsNullOrWhiteSpace(tokenRequest.Email))
            return Results.Json(new ServerAuthResult { Success = false, Message = GenericAuthFailureMessage });

        var validResult = await userService.ValidateCredentialAsync(
            CredentialType.PASSWORD,
            tokenRequest.Email,
            tokenRequest.Password);

        // Keep credential failures indistinguishable from account state failures.
        // The lockout message is safe to show: it is keyed by the submitted email
        // and appears the same way whether or not that account exists.
        if (!validResult.Success || validResult.Data is null)
        {
            if (validResult.Code == UserService.AccountThrottledCode)
                return Results.Text(validResult.Message, statusCode: StatusCodes.Status429TooManyRequests);

            var disabled = validResult.Code == UserService.AccountDisabledCode;
            return Results.Json(new ServerAuthResult { Success = false, Message = GenericAuthFailureMessage, Disabled = disabled });
        }

        var user = validResult.Data;
        var userPrivateInfo = await userService.GetUserPrivateInfoAsync(tokenRequest.Email);
        if (userPrivateInfo is null || userPrivateInfo.UserId != user.Id)
            return Results.Json(new ServerAuthResult { Success = false, Message = GenericAuthFailureMessage });

        var passwordSignIn = await FinishSignInAsync(ctx, user, true, tokenRequest.MultiFactorCode, userService, multiAuthService);
        return passwordSignIn.Response;
    }

    /// <summary>
    /// The checks every sign-in method shares once it has identified the
    /// account: disabled accounts, email verification, the authenticator
    /// code when <paramref name="requireMultiFactor"/> is set, then a new session.
    /// </summary>
    private static async Task<(IResult Response, bool SignedIn)> FinishSignInAsync(
        HttpContext ctx,
        User user,
        bool requireMultiFactor,
        string multiFactorCode,
        UserService userService,
        MultiAuthService multiAuthService)
    {
        if (user is null)
            return (Results.Json(new ServerAuthResult { Success = false, Message = GenericAuthFailureMessage }), false);

        if (user.Disabled)
            return (Results.Json(new ServerAuthResult { Success = false, Message = GenericAuthFailureMessage, Disabled = true }), false);

        var userPrivateInfo = await userService.GetUserPrivateInfoAsync(user.Id);
        if (userPrivateInfo is null)
            return (Results.Json(new ServerAuthResult { Success = false, Message = GenericAuthFailureMessage }), false);

        if (!userPrivateInfo.Verified)
        {
            return (Results.Json(new ServerAuthResult
            {
                Success = false,
                Message = GenericAuthFailureMessage,
                RequiresEmailVerification = true
            }), false);
        }

        if (requireMultiFactor)
        {
            var multiAuths = await multiAuthService.GetAppMultiAuthTypes(user.Id);
            if (multiAuths.Count > 0)
            {
                if (string.IsNullOrWhiteSpace(multiFactorCode))
                {
                    return (Results.Json(new ServerAuthResult
                    {
                        Success = true,
                        Token = null,
                        Message = "Multi-factor authentication is required.",
                        RequiresMultiAuth = true
                    }), false);
                }

                var mfaValid = await multiAuthService.VerifyAppMultiAuth(user.Id, multiFactorCode);
                if (!mfaValid.Success)
                {
                    if (mfaValid.Code == StatusCodes.Status429TooManyRequests)
                        return (Results.Text(mfaValid.Message, statusCode: StatusCodes.Status429TooManyRequests), false);

                    return (ValourResult.Forbid(mfaValid.Message == "Invalid" ? "Invalid code." : mfaValid.Message), false);
                }
            }
        }

        var result = await userService.GetTokenAfterLoginAsync(ctx, user.Id);
        if (!result.Success)
            return (ValourResult.Problem(result.Message), false);

        return (Results.Json(new ServerAuthResult
        {
            Success = true,
            Token = result.Data,
            Message = "Succeeded",
            RequiresMultiAuth = false
        }), true);
    }

    [RateLimit(RateLimitPolicies.Email)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/recovery")]
    public static async Task<IResult> RecoverPasswordRouteAsync(
        [FromBody] PasswordRecoveryRequest request,
        UserService userService,
        SignInMethodService signInMethods)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var passValid = UserUtils.TestPasswordComplexity(request.Password);
        if (!passValid.Success)
            return ValourResult.BadRequest(passValid.Message);

        var recovery = await userService.GetPasswordRecoveryAsync(request.Code);
        if (recovery is null)
            return ValourResult.NotFound<PasswordRecovery>();

        // Accounts that only use Google or Discord get their first password here.
        var cred = await signInMethods.GetPasswordCredentialAsync(recovery.UserId);

        var result = await userService.RecoveryUserAsync(request, recovery, cred);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();
    }

    [RateLimit(RateLimitPolicies.Register)]
    [ValourRoute(HttpVerbs.Post, "api/users/register")]
    public static async Task<IResult> RegisterUserRouteAsync(
        [FromBody] RegisterUserRequest request, 
        UserService userService,
        RegisterService registerService,
        ExternalAuthService externalAuth,
        IServiceProvider services,
        HttpContext ctx)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body");

        // Prevent trailing whitespace
        request.Username = request.Username?.Trim();

        if (!string.IsNullOrWhiteSpace(request.ExternalTicket))
            return await RegisterWithExternalAccountAsync(request, userService, registerService, externalAuth, services, ctx);

        // Sanitize email: trim, lowercase, strip invisible chars
        request.Email = UserUtils.SanitizeEmail(request.Email);
        if (string.IsNullOrEmpty(request.Email))
            return ValourResult.BadRequest("Email is required.");

        var result = await registerService.RegisterUserAsync(request, ctx);
        if (!result.Success)
        {
            if (result.Message == RegisterService.EmailAlreadyRegisteredCode)
                return ValourResult.Ok(GenericRegistrationResponse);
            return ValourResult.Problem(result.Message);
        }

        return ValourResult.Ok(GenericRegistrationResponse);
    }

    /// <summary>
    /// Creates an account that signs in with Google or Discord. The provider
    /// has verified the email, so the new account is signed in right away.
    /// </summary>
    private static async Task<IResult> RegisterWithExternalAccountAsync(
        RegisterUserRequest request,
        UserService userService,
        RegisterService registerService,
        ExternalAuthService externalAuth,
        IServiceProvider services,
        HttpContext ctx)
    {
        var ticket = await externalAuth.GetRegistrationTicketAsync(request.ExternalTicket, request.ExternalVerifier);
        if (ticket is null)
            return ValourResult.BadRequest("This sign-up has expired. Start again.");

        var registered = await registerService.RegisterUserAsync(request, ctx, external: ticket.Identity);
        if (!registered.Success)
        {
            if (registered.Message == RegisterService.EmailAlreadyRegisteredCode)
                return ValourResult.BadRequest(
                    $"An account with this email already exists. Sign in with your password, then link {externalAuth.GetDisplayName(ticket.Identity.CredentialType)} in Settings, under Connections.");
            return ValourResult.BadRequest(registered.Message);
        }

        await externalAuth.RemoveRegistrationTicketAsync(request.ExternalTicket);
        var user = registered.Data;

        // The picture is a convenience, so a failed import doesn't fail sign-up.
        if (request.UseProviderAvatar && ticket.Identity.AvatarUrl is not null)
        {
            var (avatar, contentType) = await externalAuth.DownloadAvatarAsync(ticket.Identity.AvatarUrl);
            if (avatar is not null)
            {
                await using (avatar)
                {
                    await Valour.Server.Cdn.Api.UploadApi.SetUserAvatarAsync(user.Id, avatar, "avatar", contentType,
                        services.GetRequiredService<ValourDb>(),
                        services.GetRequiredService<CoreHubService>(),
                        services.GetRequiredService<Valour.Server.Cdn.CdnBucketService>(),
                        services.GetRequiredService<Valour.Server.Cdn.MediaSafetyService>());
                }
            }
        }

        var token = await userService.GetTokenAfterLoginAsync(ctx, user.Id);
        if (!token.Success)
            return ValourResult.Problem(token.Message);

        return Results.Json(new ServerAuthResult { Success = true, Token = token.Data, Message = "Succeeded" });
    }

    [RateLimit(RateLimitPolicies.Email)]
    [ValourRoute(HttpVerbs.Post, "api/users/resendemail")]
    public static async Task<IResult> ResendRegistrationEmail(
        [FromBody] RegisterUserRequest request,
        UserService userService,
        RegisterService registerService,
        HttpContext ctx)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body");

        request.Email = UserUtils.SanitizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(request.Email))
            return ValourResult.BadRequest("Email is required.");

        UserPrivateInfo userPrivateInfo = await userService.GetUserPrivateInfoAsync(request.Email);

        if (userPrivateInfo is not null && !userPrivateInfo.Verified)
        {
            // Only the person who chose the account's password may have a new
            // link sent. Otherwise someone could register another person's
            // address and get that person to verify an account whose password
            // the registrant knows. A mismatch gets the same generic reply.
            var credential = await userService.ValidateCredentialAsync(
                CredentialType.PASSWORD, userPrivateInfo.Email, request.Password);
            if (credential.Success && credential.Data?.Id == userPrivateInfo.UserId)
            {
                var result = await registerService.ResendRegistrationEmail(userPrivateInfo, ctx, request);
                if (!result.Success)
                    return ValourResult.Problem(result.Message);
            }
        }

        return ValourResult.Ok(GenericRecoveryResponse);
    }

    [RateLimit(RateLimitPolicies.Email)]
    [ValourRoute(HttpVerbs.Post, "api/users/resetpassword")]
    public static async Task<IResult> ResetPasswordRouteAsync(
        [FromBody] string email,
        UserService userService,
        HttpContext ctx)
    {
        email = UserUtils.SanitizeEmail(email);
        if (string.IsNullOrWhiteSpace(email))
            return ValourResult.BadRequest("Email is required.");

        var userEmail = await userService.GetUserPrivateInfoAsync(email, true);

        if (userEmail is not null)
        {
            var result = await userService.SendPasswordResetEmail(userEmail, userEmail.Email, ctx);
            if (!result.Success)
                return ValourResult.Problem(result.Message);
        }
	
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
        UserService userService,
        MultiAuthService multiAuthService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var result = await multiAuthService.CreateAppMultiAuth(userId);
        
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.Json(result.Data);
    }
    
    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/multiAuth/remove")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> RemoveMultiFactorRouteAsync(
        [FromBody] RemoveMfaRequest request,
        HttpContext ctx,
        UserService userService,
        TokenService tokenService,
        MultiAuthService multiAuthService,
        SignInMethodService signInMethods)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var userId = await userService.GetCurrentUserIdAsync();
        var currentToken = await tokenService.GetCurrentTokenAsync();
        if (currentToken is null)
            return ValourResult.InvalidToken();

        var confirmed = await signInMethods.ConfirmIdentityAsync(userId, request.Password, request.ReauthProof);
        if (!confirmed.Success)
            return ValourResult.Forbid(confirmed.Message);

        // A stolen session plus the password must not be enough to strip the
        // second factor. An authenticator that was never finished setting up
        // can be removed without a code.
        var mfaMethods = await multiAuthService.GetAppMultiAuthTypes(userId);
        if (mfaMethods.Count > 0)
        {
            var mfa = await multiAuthService.VerifyEstablishedAppMultiAuth(userId, request.MultiFactorCode);
            if (!mfa.Success)
                return ValourResult.Forbid(mfa.Message);
        }

        var result = await multiAuthService.RemoveAppMultiAuth(userId);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        // Rotate session token after MFA removal for security
        var rotateResult = await userService.RotateSessionTokenAsync(ctx, userId, currentToken.Id);
        if (!rotateResult.Success || rotateResult.Data is null)
            return ValourResult.Problem("MFA removed but failed to rotate session: " + rotateResult.Message);

        // Return the new token so the client can update their auth
        return Results.Json(new RemoveMfaResponse
        {
            NewToken = rotateResult.Data.Id,
            Message = "MFA removed. All other sessions have been logged out."
        });
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

    [ValourRoute(HttpVerbs.Get, "api/users/me/giffavorites")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetGifFavoritesRouteAsync(UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        return Results.Json(await userService.GetGifFavoritesAsync(userId));
    }

    [ValourRoute(HttpVerbs.Get, "api/users/me/referrals")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetReferralsAsync(UserService userService)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        return Results.Json(await userService.GetReferralDataAsync(userId));
    }
    
    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/password")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ChangePasswordRouteAsync(
        [FromBody] ChangePasswordRequest request,
        HttpContext ctx,
        UserService userService,
        TokenService tokenService,
        SignInMethodService signInMethods)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var userId = await userService.GetCurrentUserIdAsync();
        var currentToken = await tokenService.GetCurrentTokenAsync();

        // Ensure current password is valid
        var currentCredential = await signInMethods.GetPasswordCredentialAsync(userId);
        if (currentCredential is null || string.IsNullOrWhiteSpace(currentCredential.Identifier))
            return ValourResult.Forbid("This account has no password. Add one in Settings, under Connections.");

        var validResult = await userService.ValidateCredentialAsync(CredentialType.PASSWORD, currentCredential.Identifier, request.OldPassword);
        if (!validResult.Success)
            return ValourResult.Forbid(validResult.Message);

        var passValid = UserUtils.TestPasswordComplexity(request.NewPassword);
        if (!passValid.Success)
            return ValourResult.BadRequest(passValid.Message);

        var result = await userService.ChangePasswordAsync(userId, request.NewPassword);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        // Rotate session token after password change for security
        var rotateResult = await userService.RotateSessionTokenAsync(ctx, userId, currentToken.Id);
        if (!rotateResult.Success)
            return ValourResult.Problem("Password changed but failed to rotate session: " + rotateResult.Message);

        // Return the new token so the client can update their auth
        return Results.Json(new { newToken = rotateResult.Data.Id, message = "Password changed. All other sessions have been logged out." });
    }
    
    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/username")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ChangeUsernameRouteAsync(
        [FromBody] ChangeUsernameRequest request,
        UserService userService,
        SignInMethodService signInMethods)
    {
        if (request is null)
        {
            return ValourResult.BadRequest("Include request in body.");
        }
        
        var userId = await userService.GetCurrentUserIdAsync();
        var confirmed = await signInMethods.ConfirmIdentityAsync(userId, request.Password, request.ReauthProof);
        if (!confirmed.Success)
            return ValourResult.Forbid(confirmed.Message);

        var result = await userService.ChangeUsernameAsync(userId, request.NewUsername);
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.NoContent();
    }

    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/hardDelete")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> DeleteAccountAsync(
        UserService userService,
        SignInMethodService signInMethods,
        [FromBody] DeleteAccountModel model)
    {
        if (model is null)
            return ValourResult.BadRequest("Include request in body.");

        // Password, or a recent confirmation for accounts without one
        var user = await userService.GetCurrentUserAsync();
        var confirmed = await signInMethods.ConfirmIdentityAsync(user.Id, model.Password, model.ReauthProof);
        if (!confirmed.Success)
            return ValourResult.Forbid(confirmed.Message);
        
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


    [ValourRoute(HttpVerbs.Get, "api/users/me/preferences")]
    [UserRequired]
    public static async Task<IResult> GetPreferencesAsync(
        UserService userService,
        ValourDb db)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var prefs = await EnsurePreferencesAsync(userId, db);

        return Results.Json(prefs.ToModel());
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/badges/visibility")]
    [UserRequired]
    public static async Task<IResult> SetBadgeVisibilityAsync(
        [FromBody] SetUserBadgeVisibilityRequest request,
        UserService userService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include a badge visibility request.");

        var userId = await userService.GetCurrentUserIdAsync();
        var result = await userService.SetBadgeVisibilityAsync(
            userId, request.Badge, request.Visible);
        return result.Success && result.Data is not null
            ? Results.Json(result.Data)
            : ValourResult.BadRequest(result.Message);
    }

    [ValourRoute(HttpVerbs.Get, "api/users/me/planet-list-layout")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetPlanetListLayoutAsync(UserService userService, ValourDb db)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var folders = await db.UserPlanetFolders.AsNoTracking()
            .Where(x => x.UserId == userId).OrderBy(x => x.Position)
            .Select(x => new PlanetListFolder { Id = x.Id, Name = x.Name, Position = x.Position })
            .ToListAsync();
        var planets = await db.UserPlanetSettings.AsNoTracking()
            .Where(x => x.UserId == userId && x.Position != null)
            .OrderBy(x => x.Position)
            .Select(x => new PlanetListPlacement { PlanetId = x.PlanetId, FolderId = x.FolderId, Position = x.Position!.Value })
            .ToListAsync();
        return Results.Json(new PlanetListLayout { Folders = folders, Planets = planets });
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/planet-list-folders")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> CreatePlanetListFolderAsync(
        [FromBody] CreatePlanetListFolderRequest request, UserService userService, ValourDb db)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
            return ValourResult.BadRequest("Folder names must be between 1 and 64 characters.");

        var userId = await userService.GetCurrentUserIdAsync();
        var position = await db.UserPlanetFolders.Where(x => x.UserId == userId)
            .Select(x => (int?)x.Position).MaxAsync() ?? -1;
        var folder = new UserPlanetFolder
        {
            Id = IdManager.Generate(), UserId = userId, Name = name, Position = position + 1
        };
        db.UserPlanetFolders.Add(folder);
        await db.SaveChangesAsync();
        return Results.Json(new PlanetListFolder { Id = folder.Id, Name = folder.Name, Position = folder.Position });
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/planet-list-folders/{folderId}/rename")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> RenamePlanetListFolderAsync(
        long folderId, [FromBody] RenamePlanetListFolderRequest request, UserService userService, ValourDb db)
    {
        var name = request?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64)
            return ValourResult.BadRequest("Folder names must be between 1 and 64 characters.");
        var userId = await userService.GetCurrentUserIdAsync();
        var folder = await db.UserPlanetFolders.FirstOrDefaultAsync(x => x.Id == folderId && x.UserId == userId);
        if (folder is null) return ValourResult.NotFound("Folder not found.");
        folder.Name = name;
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Delete, "api/users/me/planet-list-folders/{folderId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> DeletePlanetListFolderAsync(
        long folderId, UserService userService, ValourDb db)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var folder = await db.UserPlanetFolders.FirstOrDefaultAsync(x => x.Id == folderId && x.UserId == userId);
        if (folder is null) return ValourResult.NotFound("Folder not found.");
        await db.UserPlanetSettings.Where(x => x.UserId == userId && x.FolderId == folderId)
            .ExecuteUpdateAsync(x => x.SetProperty(s => s.FolderId, (long?)null));
        db.UserPlanetFolders.Remove(folder);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/planet-list-layout")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> SavePlanetListLayoutAsync(
        [FromBody] SavePlanetListLayoutRequest request, UserService userService, ValourDb db)
    {
        if (request?.Planets is null || request.FolderIds is null || request.Planets.Any(x => x is null))
            return ValourResult.BadRequest("Include non-null planets and folderIds lists with valid entries.");
        var userId = await userService.GetCurrentUserIdAsync();
        var folderIds = await db.UserPlanetFolders.Where(x => x.UserId == userId).Select(x => x.Id).ToHashSetAsync();
        if (request.FolderIds.Count != request.FolderIds.Distinct().Count() ||
            request.FolderIds.Any(x => !folderIds.Contains(x)) ||
            request.Planets.Select(x => x.PlanetId).Distinct().Count() != request.Planets.Count ||
            request.Planets.Any(x => x.FolderId is not null && !folderIds.Contains(x.FolderId.Value)))
            return ValourResult.BadRequest("The layout contains invalid or duplicate entries.");

        var joinedIds = await db.PlanetMembers.Where(x => x.UserId == userId)
            .Select(x => x.PlanetId).ToHashSetAsync();
        joinedIds.UnionWith(await db.FederatedMemberships.Where(x => x.UserId == userId)
            .Select(x => x.PlanetId).ToListAsync());
        if (request.Planets.Any(x => !joinedIds.Contains(x.PlanetId)))
            return ValourResult.BadRequest("The layout contains a planet you have not joined.");

        var settings = await db.UserPlanetSettings.Where(x => x.UserId == userId).ToDictionaryAsync(x => x.PlanetId);
        foreach (var (placement, position) in request.Planets.Select((x, i) => (x, i)))
        {
            if (!settings.TryGetValue(placement.PlanetId, out var setting))
            {
                setting = new UserPlanetSetting { UserId = userId, PlanetId = placement.PlanetId };
                db.UserPlanetSettings.Add(setting);
            }
            setting.FolderId = placement.FolderId;
            setting.Position = position;
        }
        foreach (var (folderId, position) in request.FolderIds.Select((x, i) => (x, i)))
        {
            var folder = await db.UserPlanetFolders.FirstAsync(x => x.Id == folderId && x.UserId == userId);
            folder.Position = position;
        }
        await db.SaveChangesAsync();
        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/tutorials/{tutorialId}/complete")]
    [UserRequired]
    public static async Task<IResult> CompleteTutorialAsync(
        int tutorialId,
        UserService userService,
        ValourDb db)
    {
        // TutorialState is a long bitmask; bit 63 stays reserved to avoid sign games
        if (tutorialId is < 0 or > 62)
            return ValourResult.BadRequest("Invalid tutorial id.");

        var userId = await userService.GetCurrentUserIdAsync();
        var dbUser = await db.Users.FindAsync(userId);
        if (dbUser is null)
            return ValourResult.NotFound("User not found.");

        dbUser.TutorialState = UserTutorials.WithCompleted(dbUser.TutorialState, tutorialId);
        await db.SaveChangesAsync();

        return Results.Json(dbUser.TutorialState);
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/preferences/errorReporting/{state}")]
    [UserRequired]
    public static async Task<IResult> SetErrorReportingAsync(
        ErrorReportingState state,
        UserService userService,
        ValourDb db)
    {
        if (!Enum.IsDefined(state))
            return ValourResult.BadRequest("Invalid error reporting state.");

        var userId = await userService.GetCurrentUserIdAsync();
        var prefs = await SetErrorReportingStateAsync(userId, state, db);
        return prefs is null
            ? ValourResult.NotFound("User preferences are no longer available.")
            : Results.Json(prefs.ToModel());
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/preferences/notificationVolume/{volume}")]
    [UserRequired]
    public static async Task<IResult> SetNotificationVolumeAsync(
        int volume,
        UserService userService,
        ValourDb db)
    {
        var clamped = NotificationPreferences.ClampVolume(volume);
        var userId = await userService.GetCurrentUserIdAsync();
        var prefs = await EnsurePreferencesAsync(userId, db);
        prefs.NotificationVolume = clamped;

        await db.SaveChangesAsync();
        return Results.Json(prefs.ToModel());
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/preferences/notificationSource/{source}/enabled/{enabled}")]
    [UserRequired]
    public static async Task<IResult> SetNotificationSourceEnabledAsync(
        int source,
        bool enabled,
        UserService userService,
        ValourDb db)
    {
        if (!Enum.IsDefined(typeof(NotificationSource), source))
            return ValourResult.BadRequest("Invalid notification source.");

        var sourceEnum = (NotificationSource)source;
        if (!NotificationPreferences.IsSingleSource(sourceEnum))
            return ValourResult.BadRequest("Notification source must be a single value.");

        if (!NotificationPreferences.IsConfigurableSource(sourceEnum))
            return ValourResult.BadRequest("Notification source is not configurable.");

        var userId = await userService.GetCurrentUserIdAsync();
        var prefs = await EnsurePreferencesAsync(userId, db);

        prefs.EnabledNotificationSources = NotificationPreferences.SetSourceEnabled(
            prefs.EnabledNotificationSources,
            sourceEnum,
            enabled
        );

        await db.SaveChangesAsync();
        return Results.Json(prefs.ToModel());
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/preferences/dmPolicy/{policy}")]
    [UserRequired]
    public static async Task<IResult> SetDmPolicyAsync(
        DmPolicy policy,
        UserService userService,
        ValourDb db)
    {
        if (!Enum.IsDefined(policy))
            return ValourResult.BadRequest("Invalid DM policy.");

        var userId = await userService.GetCurrentUserIdAsync();
        var prefs = await EnsurePreferencesAsync(userId, db);
        prefs.DmPolicy = policy;

        await db.SaveChangesAsync();
        return Results.Json(prefs.ToModel());
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/preferences/callPolicy/{policy}")]
    [UserRequired]
    public static async Task<IResult> SetCallPolicyAsync(
        DmPolicy policy,
        UserService userService,
        ValourDb db)
    {
        if (!Enum.IsDefined(policy))
            return ValourResult.BadRequest("Invalid call policy.");

        var userId = await userService.GetCurrentUserIdAsync();
        var prefs = await EnsurePreferencesAsync(userId, db);
        prefs.CallPolicy = policy;

        await db.SaveChangesAsync();
        return Results.Json(prefs.ToModel());
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/preferences/activityCooldown/{seconds}")]
    [UserRequired]
    public static async Task<IResult> SetActivityCooldownAsync(
        int seconds,
        UserService userService,
        ValourDb db)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var prefs = await EnsurePreferencesAsync(userId, db);

        // 0 clears the personal cooldown (inherit each planet's cadence)
        prefs.ActivityCooldownSeconds = seconds == 0
            ? null
            : Math.Clamp(
                seconds,
                ChannelActivityPreferences.MinCooldownSeconds,
                ChannelActivityPreferences.MaxCooldownSeconds);

        await db.SaveChangesAsync();
        return Results.Json(prefs.ToModel());
    }

    [ValourRoute(HttpVerbs.Post, "api/users/me/preferences/forceGpuAcceleration/{enabled}")]
    [UserRequired]
    public static async Task<IResult> SetForceGpuAccelerationAsync(
        bool enabled,
        UserService userService,
        ValourDb db)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        var prefs = await EnsurePreferencesAsync(userId, db);
        prefs.ForceGpuAcceleration = enabled;

        await db.SaveChangesAsync();
        return Results.Json(prefs.ToModel());
    }

    internal static async Task<DbUserPreferences?> SetErrorReportingStateAsync(long userId, ErrorReportingState state, ValourDb db)
    {
        var defaults = await CreateDefaultPreferencesAsync(userId, db);
        var affected = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO user_preferences
                (id, error_reporting_state, notification_volume, enabled_notification_sources,
                 marketing_email_opt_out, dm_policy, call_policy, force_gpu_acceleration)
            SELECT id, {(int)state}, {defaults.NotificationVolume}, {defaults.EnabledNotificationSources},
                   {defaults.MarketingEmailOptOut}, {(int)defaults.DmPolicy}, {(int)defaults.CallPolicy}, {defaults.ForceGpuAcceleration}
            FROM users WHERE id = {userId}
            ON CONFLICT (id) DO UPDATE SET error_reporting_state = EXCLUDED.error_reporting_state;");
        if (affected == 0) return null;
        return await db.UserPreferences.AsNoTracking().FirstOrDefaultAsync(x => x.Id == userId);
    }

    internal static async Task<DbUserPreferences> EnsurePreferencesAsync(long userId, ValourDb db)
    {
        var prefs = await db.UserPreferences.FindAsync(userId);
        if (prefs is not null)
            return prefs;

        prefs = await CreateDefaultPreferencesAsync(userId, db);
        db.UserPreferences.Add(prefs);
        await db.SaveChangesAsync();
        return prefs;
    }

    private static async Task<DbUserPreferences> CreateDefaultPreferencesAsync(long userId, ValourDb db)
    {
        var dmPolicy = DmPolicy.Everyone;

        // Default under-18 accounts to FriendsOnly
        var privateInfo = await db.PrivateInfos.FirstOrDefaultAsync(x => x.UserId == userId);
        if (privateInfo?.BirthDate is not null)
        {
            var age = DateTime.UtcNow.Year - privateInfo.BirthDate.Value.Year;
            if (privateInfo.BirthDate.Value > DateTime.UtcNow.AddYears(-age))
                age--;

            if (age < 18)
                dmPolicy = DmPolicy.FriendsOnly;
        }

        return new DbUserPreferences
        {
            Id = userId,
            ErrorReportingState = ErrorReportingState.Unset,
            NotificationVolume = NotificationPreferences.DefaultNotificationVolume,
            EnabledNotificationSources = NotificationPreferences.AllNotificationSourcesMask,
            DmPolicy = dmPolicy,
            CallPolicy = DmPolicy.FriendsOnly,
            ForceGpuAcceleration = false
        };
    }
}
