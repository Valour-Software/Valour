using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using CredentialType = Valour.Database.CredentialType;

namespace Valour.Server.Api.Dynamic;

/// <summary>
/// Routes for sign-in methods other than a password: Google and Discord,
/// device keys, identity confirmation, and managing an account's sign-in
/// methods. Signing in itself still ends at api/users/token.
/// </summary>
public class SignInApi
{
    public const string DeviceKeyNeedsMultiFactorMessage = "Enter your authenticator code to turn on fingerprint sign-in.";

    // Google and Discord //

    [ValourRoute(HttpVerbs.Get, "api/auth/external/providers")]
    public static IResult GetProvidersRoute(ExternalAuthService externalAuth) =>
        Results.Json(externalAuth.GetConfiguredProviders());

    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/auth/external/{provider}/begin")]
    public static async Task<IResult> BeginExternalRouteAsync(
        string provider,
        [FromBody] ExternalAuthBeginRequest request,
        HttpContext ctx,
        TokenService tokenService,
        ExternalAuthService externalAuth)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        // Linking and confirming identity act on the signed-in account.
        long? userId = null;
        if (request.Intent != ExternalAuthIntent.Login)
        {
            var token = await tokenService.GetCurrentTokenAsync();
            if (token is null)
                return ValourResult.InvalidToken();
            if (!token.HasScope(UserPermissions.FullControl))
                return ValourResult.LacksPermission(UserPermissions.FullControl);
            userId = token.UserId;
        }

        var result = await externalAuth.BeginAsync(provider, request, userId, ctx.Request);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        if (result.Data.BrowserBindingSecret is not null)
        {
            ctx.Response.Cookies.Append(
                ExternalAuthService.BrowserBindingCookieName(result.Data.Response.FlowId),
                result.Data.BrowserBindingSecret,
                ExternalAuthService.BrowserBindingCookieOptions(ctx.Request));
        }

        return Results.Json(result.Data.Response);
    }

    /// <summary>
    /// The web app polls this while the provider popup is open. Responds with
    /// no content until the person finishes on the provider's page.
    /// </summary>
    [ValourRoute(HttpVerbs.Post, "api/auth/external/result")]
    public static async Task<IResult> GetExternalResultRouteAsync(
        [FromBody] ExternalTicketRequest request,
        ExternalAuthService externalAuth)
    {
        var result = await externalAuth.TakeWebResultAsync(request?.Ticket, request?.Verifier);
        return result is null ? Results.NoContent() : Results.Json(result);
    }

    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Get, "api/auth/external/{provider}/callback")]
    public static Task<IResult> ExternalCallbackRouteAsync(
        string provider,
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        HttpContext ctx,
        ExternalAuthService externalAuth) =>
        externalAuth.HandleCallbackAsync(provider, code, state, error, ctx);

    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/auth/external/registration")]
    public static async Task<IResult> GetExternalRegistrationRouteAsync(
        [FromBody] ExternalTicketRequest request,
        ExternalAuthService externalAuth)
    {
        var ticket = await externalAuth.GetRegistrationTicketAsync(request?.Ticket, request?.Verifier);
        if (ticket is null)
            return ValourResult.NotFound("This sign-up has expired. Start again.");

        return Results.Json(ExternalAuthService.ToRegistrationInfo(ticket));
    }

    /// <summary>
    /// The linked account's profile picture, for the sign-up form's preview.
    /// Served through Valour so the app does not load the provider's image host.
    /// </summary>
    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/auth/external/registration/avatar")]
    public static async Task<IResult> GetExternalRegistrationAvatarRouteAsync(
        [FromBody] ExternalTicketRequest request,
        ExternalAuthService externalAuth)
    {
        var ticket = await externalAuth.GetRegistrationTicketAsync(request?.Ticket, request?.Verifier);
        if (ticket?.Identity.AvatarUrl is null)
            return ValourResult.NotFound("No picture.");

        var (data, contentType) = await externalAuth.DownloadAvatarAsync(ticket.Identity.AvatarUrl);
        if (data is null)
            return ValourResult.NotFound("No picture.");

        return Results.File(data, contentType);
    }

    // Identity confirmation //

    /// <summary>
    /// Confirms the signed-in person with their password, this device's key,
    /// or a linked account (the ticket from api/auth/external/{provider}/begin
    /// with the Reauth intent), and returns a proof that sensitive changes from
    /// this session accept for a few minutes.
    /// </summary>
    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/reauth")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> ReauthRouteAsync(
        [FromBody] ReauthRequest request,
        UserService userService,
        SignInMethodService signInMethods,
        ExternalAuthService externalAuth)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var userId = await userService.GetCurrentUserIdAsync();

        if (!string.IsNullOrWhiteSpace(request.ExternalTicket))
        {
            var grant = await externalAuth.TakeGrantAsync(request.ExternalTicket, request.ExternalVerifier, ExternalAuthIntent.Reauth, userId);
            if (grant is null)
                return ValourResult.Forbid("Your confirmation expired. Confirm it's you again.");
        }
        else if (!string.IsNullOrWhiteSpace(request.DeviceKeyId))
        {
            var signed = await signInMethods.VerifyDeviceSignatureAsync(
                request.DeviceKeyId, request.DeviceChallengeId, request.DeviceSignature);
            if (!signed.Success || signed.Data.UserId != userId)
                return ValourResult.Forbid(signed.Message ?? "Fingerprint confirmation failed.");
        }
        else
        {
            var confirmed = await signInMethods.ConfirmIdentityAsync(userId, request.Password, null);
            if (!confirmed.Success)
                return ValourResult.Forbid(confirmed.Message);
        }

        return Results.Json(await signInMethods.CreateReauthProofAsync(userId));
    }

    // Managing sign-in methods //

    /// <summary>
    /// Finishes linking a Google or Discord account, with the ticket from
    /// api/auth/external/{provider}/begin with the Link intent.
    /// </summary>
    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/signin-methods/link")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> LinkSignInMethodRouteAsync(
        [FromBody] ExternalTicketRequest request,
        UserService userService,
        ExternalAuthService externalAuth)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var userId = await userService.GetCurrentUserIdAsync();
        var result = await externalAuth.RedeemLinkAsync(request.Ticket, request.Verifier, userId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "api/users/me/signin-methods")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> GetSignInMethodsRouteAsync(
        UserService userService,
        SignInMethodService signInMethods)
    {
        var userId = await userService.GetCurrentUserIdAsync();
        return Results.Json(await signInMethods.GetMethodsAsync(userId));
    }

    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/signin-methods/{id}/remove")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> RemoveSignInMethodRouteAsync(
        long id,
        [FromBody] RemoveSignInMethodRequest request,
        UserService userService,
        SignInMethodService signInMethods)
    {
        var userId = await userService.GetCurrentUserIdAsync();

        // Turning off fingerprint sign-in removes access rather than adding
        // it, so it needs no confirmation. Everything else does.
        var methods = await signInMethods.GetMethodsAsync(userId);
        var target = methods.FirstOrDefault(x => x.Id == id);
        if (target is null)
            return ValourResult.NotFound("Sign-in method not found.");

        if (target.Type != CredentialType.DEVICE_KEY)
        {
            var confirmed = await signInMethods.ConfirmIdentityAsync(userId, null, request?.ReauthProof);
            if (!confirmed.Success)
                return ValourResult.Forbid(confirmed.Message);
        }

        var result = await signInMethods.RemoveMethodAsync(userId, id);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }

    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/users/me/password/add")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> AddPasswordRouteAsync(
        [FromBody] SetPasswordRequest request,
        UserService userService,
        SignInMethodService signInMethods)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var userId = await userService.GetCurrentUserIdAsync();
        var confirmed = await signInMethods.ConfirmIdentityAsync(userId, null, request.ReauthProof);
        if (!confirmed.Success)
            return ValourResult.Forbid(confirmed.Message);

        var result = await signInMethods.AddPasswordAsync(userId, request.NewPassword);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }

    // Device keys //

    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/auth/device/register")]
    [UserRequired(UserPermissionsEnum.FullControl)]
    public static async Task<IResult> RegisterDeviceKeyRouteAsync(
        [FromBody] DeviceKeyRegisterRequest request,
        UserService userService,
        MultiAuthService multiAuthService,
        SignInMethodService signInMethods)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        // A device key outlives the session, so a stolen session alone must
        // not be able to add one.
        var userId = await userService.GetCurrentUserIdAsync();
        var confirmed = await signInMethods.ConfirmIdentityAsync(userId, null, request.ReauthProof);
        if (!confirmed.Success)
            return ValourResult.Forbid(confirmed.Message);

        // Fingerprint sign-in skips the authenticator code, so adding it needs
        // that code. Otherwise a stolen session and password could add a key
        // that gets around two-factor authentication for good. A proof from
        // signing in with the code a moment ago counts as the code.
        if ((await multiAuthService.GetAppMultiAuthTypes(userId)).Count > 0 &&
            !await signInMethods.ProofIncludesMultiFactorAsync(userId, request.ReauthProof))
        {
            if (string.IsNullOrWhiteSpace(request.MultiFactorCode))
                return ValourResult.Forbid(DeviceKeyNeedsMultiFactorMessage);

            var mfa = await multiAuthService.VerifyEstablishedAppMultiAuth(userId, request.MultiFactorCode);
            if (!mfa.Success)
                return ValourResult.Forbid(mfa.Message);
        }

        var result = await signInMethods.RegisterDeviceKeyAsync(userId, request.PublicKey, request.DeviceName);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(new DeviceKeyRegisterResponse { KeyId = result.Data });
    }

    [RateLimit(RateLimitPolicies.Auth)]
    [ValourRoute(HttpVerbs.Post, "api/auth/device/challenge")]
    public static async Task<IResult> DeviceChallengeRouteAsync(
        [FromBody] DeviceChallengeRequest request,
        SignInMethodService signInMethods)
    {
        var result = await signInMethods.CreateDeviceChallengeAsync(request?.KeyId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }
}
