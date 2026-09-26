using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Valour.Server.Database;
using Valour.Shared;
using Valour.Server.Services.ExternalAuth;
using Valour.Shared.Models;

namespace Valour.Server.Services;

/// <summary>
/// Sign in with other services (see <see cref="ExternalAuthProvider"/>). The
/// server runs the OAuth authorization code flow itself, so client secrets
/// never reach an app. The result of each
/// flow is a short-lived ticket that the client redeems through the normal
/// sign-in, registration, linking, and identity routes, which keeps two-factor
/// checks, email rules, and session creation in one place.
/// </summary>
public class ExternalAuthService
{
    /// <summary>The custom URL scheme the Android app receives results on.</summary>
    public const string AndroidCallbackUrl = "gg.valour.app://auth";

    private const string StateKind = "ext-state";
    private const string LoginKind = "ext-login";
    private const string RegistrationKind = "ext-register";
    private const string ResultKind = "ext-result";
    private const string GrantKind = "ext-grant";

    private static readonly TimeSpan StateLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LoginTicketLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan RegistrationTicketLifetime = TimeSpan.FromMinutes(30);

    private const int MaxAvatarBytes = 8 * 1024 * 1024;

    /// <summary>The providers' profile picture hosts.</summary>
    private static readonly HashSet<string> AvatarHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cdn.discordapp.com",
        "lh3.googleusercontent.com",
        "lh4.googleusercontent.com",
        "lh5.googleusercontent.com",
        "lh6.googleusercontent.com",
    };

    private static readonly HashSet<string> AvatarContentTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    private readonly AuthTicketStore _tickets;
    private readonly SignInMethodService _signInMethods;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ValourDb _db;
    private readonly ILogger<ExternalAuthService> _logger;
    private readonly Dictionary<string, ExternalAuthProvider> _providers;

    public ExternalAuthService(
        IEnumerable<ExternalAuthProvider> providers,
        AuthTicketStore tickets,
        SignInMethodService signInMethods,
        IHttpClientFactory httpClientFactory,
        ValourDb db,
        ILogger<ExternalAuthService> logger)
    {
        _providers = providers.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        _tickets = tickets;
        _signInMethods = signInMethods;
        _httpClientFactory = httpClientFactory;
        _db = db;
        _logger = logger;
    }

    private record FlowState(
        string Provider,
        ExternalAuthIntent Intent,
        ExternalAuthClient Client,
        int? LoopbackPort,
        string VerifierHash,
        long? UserId,
        string RedirectUri,
        string BrowserBindingHash);

    /// <summary>
    /// The cookie that ties a web flow to the browser that started it. Only
    /// that browser can finish the flow, so a provider link sent to someone
    /// else can't sign them in on the sender's behalf.
    /// </summary>
    public static string BrowserBindingCookieName(string flowId) => "valour-ext-" + flowId;

    /// <summary>The cookie value a web client's browser must present when the provider returns.</summary>
    public record BeginResult(ExternalAuthBeginResponse Response, string BrowserBindingSecret);

    public record LoginTicket(long UserId, long CredentialId, string VerifierHash);

    public record RegistrationTicket(ExternalIdentity Identity, string VerifierHash);

    /// <summary>
    /// A confirmed provider account waiting to be linked, or a confirmed
    /// identity waiting to become a proof. The callback only records this.
    /// The client that started the flow redeems it with its verifier and its
    /// own session, so a provider link sent to someone else can't link their
    /// account or confirm identity for the sender.
    /// </summary>
    public record Grant(
        ExternalAuthIntent Intent,
        long UserId,
        string CredentialType,
        string ProviderUserId,
        string Label,
        string VerifierHash);

    private ExternalAuthProvider GetConfiguredProvider(string name) =>
        name is not null && _providers.TryGetValue(name, out var provider) && provider.IsConfigured ? provider : null;

    /// <summary>Providers this server has OAuth clients for.</summary>
    public List<string> GetConfiguredProviders() =>
        _providers.Values.Where(x => x.IsConfigured).Select(x => x.Name).ToList();

    /// <summary>The readable name of a credential type, such as "Google".</summary>
    public string GetDisplayName(string credentialType) =>
        _providers.Values.FirstOrDefault(x => x.CredentialType == credentialType)?.DisplayName ?? credentialType;

    // Starting a flow //

    /// <summary>
    /// Starts a sign-in with a provider and returns the provider page to open,
    /// with the flow's ID, which a web client uses to collect the result.
    /// <paramref name="userId"/> is the signed-in user, required to link or
    /// confirm identity.
    /// </summary>
    public async Task<TaskResult<BeginResult>> BeginAsync(string providerName, ExternalAuthBeginRequest request, long? userId, HttpRequest httpRequest)
    {
        var provider = GetConfiguredProvider(providerName);
        if (provider is null)
            return TaskResult<BeginResult>.FromFailure("This sign-in method is not available.");

        if (string.IsNullOrWhiteSpace(request.VerifierHash) || request.VerifierHash.Length != 43)
            return TaskResult<BeginResult>.FromFailure("Missing verifier.");

        switch (request.Client)
        {
            case ExternalAuthClient.Web:
                break;
            case ExternalAuthClient.Desktop:
                if (request.LoopbackPort is null or < 1024 or > 65535)
                    return TaskResult<BeginResult>.FromFailure("Invalid loopback port.");
                break;
            case ExternalAuthClient.Android:
                break;
            default:
                return TaskResult<BeginResult>.FromFailure("Unknown client.");
        }

        if (request.Intent != ExternalAuthIntent.Login && userId is null)
            return TaskResult<BeginResult>.FromFailure("Sign in first.");

        if (request.Intent == ExternalAuthIntent.Link)
        {
            // Linking adds a lasting way into the account, so a stolen session
            // alone is not enough.
            var confirmed = await _signInMethods.ConfirmIdentityAsync(userId!.Value, null, request.ReauthProof);
            if (!confirmed.Success)
                return TaskResult<BeginResult>.FromFailure(confirmed.Message);
        }

        var redirectUri = $"{PublicLinks.GetApiBaseUrl(httpRequest)}/api/auth/external/{provider.Name}/callback";

        // Web results wait on the server for whoever holds the verifier, so the
        // flow is also tied to this browser. Apps receive the result on their
        // own device, so they don't need this.
        var browserSecret = request.Client == ExternalAuthClient.Web ? AuthTicketStore.NewId() : null;

        var state = new FlowState(
            provider.Name,
            request.Intent,
            request.Client,
            request.LoopbackPort,
            request.VerifierHash,
            request.Intent == ExternalAuthIntent.Login ? null : userId,
            redirectUri,
            browserSecret is null ? null : AuthTicketStore.HashVerifier(browserSecret));

        var stateId = await _tickets.CreateAsync(StateKind, state, StateLifetime);

        return TaskResult<BeginResult>.FromData(new BeginResult(new ExternalAuthBeginResponse
        {
            AuthorizationUrl = provider.BuildAuthorizationUrl(redirectUri, stateId),
            FlowId = stateId,
        }, browserSecret));
    }

    /// <summary>
    /// The binding cookie is sent back only to the sign-in routes, is hidden
    /// from scripts, and survives the provider's top-level redirect back.
    /// </summary>
    public static CookieOptions BrowserBindingCookieOptions(HttpRequest request) => new()
    {
        HttpOnly = true,
        // Local development servers run over plain HTTP.
        Secure = !IsLoopback(request),
        SameSite = SameSiteMode.Lax,
        Path = "/api/auth/external",
        MaxAge = StateLifetime,
    };

    private static bool IsLoopback(HttpRequest request) =>
        request.Host.Host is "localhost" or "127.0.0.1" or "[::1]";

    // Finishing a flow //

    /// <summary>
    /// Handles the provider's redirect back to the server and sends the result
    /// to the client that started the flow.
    /// </summary>
    public async Task<IResult> HandleCallbackAsync(string providerName, string code, string stateId, string error, HttpContext httpContext)
    {
        var state = await _tickets.TakeAsync<FlowState>(StateKind, stateId);
        if (state is null || !string.Equals(state.Provider, providerName, StringComparison.OrdinalIgnoreCase))
            return PlainPage("This sign-in link has expired. Go back to Valour and try again.");

        if (state.BrowserBindingHash is not null)
        {
            var cookieName = BrowserBindingCookieName(stateId);
            var presented = httpContext.Request.Cookies[cookieName];
            httpContext.Response.Cookies.Delete(cookieName, BrowserBindingCookieOptions(httpContext.Request));

            if (!AuthTicketStore.VerifierMatches(presented, state.BrowserBindingHash))
                return await DeliverAsync(state, stateId, ExternalAuthResults.Error,
                    message: "Sign-in has to finish in the same browser it started in. Go back to Valour and try again.");
        }

        var provider = GetConfiguredProvider(state.Provider);
        if (provider is null)
            return await DeliverAsync(state, stateId, ExternalAuthResults.Error, message: "This sign-in method is not available.");

        if (!string.IsNullOrEmpty(error) || string.IsNullOrEmpty(code))
            return await DeliverAsync(state, stateId, ExternalAuthResults.Error, message: "Sign-in was cancelled.");

        ExternalIdentity identity;
        try
        {
            identity = await provider.GetIdentityAsync(code, state.RedirectUri);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "{Provider} sign-in failed while reading the account", provider.DisplayName);
            identity = null;
        }

        if (identity is null)
            return await DeliverAsync(state, stateId, ExternalAuthResults.Error, message: $"We couldn't read your {provider.DisplayName} account. Try again.");

        var credential = await _db.Credentials.AsNoTracking().FirstOrDefaultAsync(x =>
            x.CredentialType == provider.CredentialType && x.Identifier == identity.ProviderUserId);

        switch (state.Intent)
        {
            case ExternalAuthIntent.Login:
                return await FinishLoginAsync(stateId, state, provider, identity, credential);
            case ExternalAuthIntent.Link:
                return await FinishLinkAsync(stateId, state, provider, identity, credential);
            case ExternalAuthIntent.Reauth:
                if (credential is null || credential.UserId != state.UserId)
                    return await DeliverAsync(state, stateId, ExternalAuthResults.Error,
                        message: $"That {provider.DisplayName} account isn't linked to your Valour account.");

                await _signInMethods.MarkUsedAsync(credential.Id);
                return await DeliverAsync(state, stateId, ExternalAuthResults.Reauth,
                    ticket: await CreateGrantAsync(state, provider, identity));
            default:
                return await DeliverAsync(state, stateId, ExternalAuthResults.Error, message: "Unknown sign-in request.");
        }
    }

    private async Task<IResult> FinishLoginAsync(string stateId, FlowState state, ExternalAuthProvider provider, ExternalIdentity identity, Valour.Database.Credential credential)
    {
        if (credential is not null)
        {
            var ticket = await _tickets.CreateAsync(LoginKind,
                new LoginTicket(credential.UserId, credential.Id, state.VerifierHash), LoginTicketLifetime);
            return await DeliverAsync(state, stateId, ExternalAuthResults.Login, ticket: ticket);
        }

        if (string.IsNullOrWhiteSpace(identity.Email) || !identity.EmailVerified)
            return await DeliverAsync(state, stateId, ExternalAuthResults.Error,
                message: $"Your {provider.DisplayName} account needs a verified email address. Verify it with {provider.DisplayName}, then try again.");

        // The provider has verified this address, so saying that it is taken
        // reveals nothing the person doesn't already control.
        var email = identity.Email.Trim().ToLower();
        var existing = await _db.PrivateInfos.AsNoTracking().FirstOrDefaultAsync(x => x.Email.ToLower() == email);
        if (existing is not null && existing.Verified)
            return await DeliverAsync(state, stateId, ExternalAuthResults.Error,
                message: $"An account with this email already exists. Sign in with your password, then link {provider.DisplayName} in Settings, under Connections.");

        var registration = await _tickets.CreateAsync(RegistrationKind,
            new RegistrationTicket(identity, state.VerifierHash), RegistrationTicketLifetime);
        return await DeliverAsync(state, stateId, ExternalAuthResults.Register, ticket: registration);
    }

    private async Task<IResult> FinishLinkAsync(string stateId, FlowState state, ExternalAuthProvider provider, ExternalIdentity identity, Valour.Database.Credential credential)
    {
        // Obvious refusals are reported now. Linking itself waits for the
        // client that started the flow (see Grant), which checks again.
        if (credential is not null && credential.UserId != state.UserId)
            return await DeliverAsync(state, stateId, ExternalAuthResults.Error,
                message: $"This {provider.DisplayName} account is already linked to another Valour account.");

        return await DeliverAsync(state, stateId, ExternalAuthResults.Linked,
            ticket: await CreateGrantAsync(state, provider, identity));
    }

    private Task<string> CreateGrantAsync(FlowState state, ExternalAuthProvider provider, ExternalIdentity identity) =>
        _tickets.CreateAsync(GrantKind,
            new Grant(state.Intent, state.UserId!.Value, provider.CredentialType, identity.ProviderUserId, identity.Label, state.VerifierHash),
            StateLifetime);

    // Redeeming tickets //

    /// <summary>
    /// Reads a sign-in ticket for the client that holds the verifier. The
    /// ticket stays valid until <see cref="RemoveLoginTicketAsync"/>, so a
    /// two-factor prompt can reuse it.
    /// </summary>
    public async Task<LoginTicket> GetLoginTicketAsync(string ticket, string verifier)
    {
        var value = await _tickets.GetAsync<LoginTicket>(LoginKind, ticket);
        return value is not null && AuthTicketStore.VerifierMatches(verifier, value.VerifierHash) ? value : null;
    }

    public Task RemoveLoginTicketAsync(string ticket) => _tickets.RemoveAsync(LoginKind, ticket);

    public async Task<RegistrationTicket> GetRegistrationTicketAsync(string ticket, string verifier)
    {
        var value = await _tickets.GetAsync<RegistrationTicket>(RegistrationKind, ticket);
        return value is not null && AuthTicketStore.VerifierMatches(verifier, value.VerifierHash) ? value : null;
    }

    public Task RemoveRegistrationTicketAsync(string ticket) => _tickets.RemoveAsync(RegistrationKind, ticket);

    /// <summary>
    /// Spends a link or identity grant for the signed-in user who started the
    /// flow and holds its verifier. Returns null if any of that doesn't match.
    /// </summary>
    public async Task<Grant> TakeGrantAsync(string ticket, string verifier, ExternalAuthIntent intent, long userId)
    {
        var value = await _tickets.GetAsync<Grant>(GrantKind, ticket);
        if (value is null || value.Intent != intent || value.UserId != userId ||
            !AuthTicketStore.VerifierMatches(verifier, value.VerifierHash))
            return null;

        // Only one redemption wins if two arrive at once.
        return await _tickets.TakeAsync<Grant>(GrantKind, ticket) is null ? null : value;
    }

    /// <summary>Links the provider account from a grant to the signed-in user.</summary>
    public async Task<TaskResult> RedeemLinkAsync(string ticket, string verifier, long userId)
    {
        var grant = await TakeGrantAsync(ticket, verifier, ExternalAuthIntent.Link, userId);
        if (grant is null)
            return TaskResult.FromFailure("This link has expired. Try again.");

        return await _signInMethods.LinkProviderAsync(userId, grant.CredentialType, grant.ProviderUserId, grant.Label,
            GetDisplayName(grant.CredentialType));
    }

    public static ExternalRegistrationInfo ToRegistrationInfo(RegistrationTicket ticket) => new()
    {
        Provider = ticket.Identity.Provider,
        Email = ticket.Identity.Email,
        SuggestedUsername = ticket.Identity.SuggestedUsername,
        HasAvatar = !string.IsNullOrEmpty(ticket.Identity.AvatarUrl),
    };

    /// <summary>
    /// Downloads the linked account's profile picture. Only the providers'
    /// own image hosts are fetched.
    /// </summary>
    public async Task<(MemoryStream Data, string ContentType)> DownloadAvatarAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            return (null, null);

        if (!AvatarHosts.Contains(uri.Host))
            return (null, null);

        try
        {
            // The SSRF-safe client, even though the hosts are fixed above.
            var http = _httpClientFactory.CreateClient("ProxyFetch");
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                return (null, null);

            if (response.Content.Headers.ContentLength > MaxAvatarBytes)
                return (null, null);

            // The picture is served back from the API's own address, so only
            // image types are passed along.
            var contentType = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant();
            if (contentType is null || !AvatarContentTypes.Contains(contentType))
                return (null, null);

            await using var stream = await response.Content.ReadAsStreamAsync();
            var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                if (buffer.Length + read > MaxAvatarBytes)
                {
                    await buffer.DisposeAsync();
                    return (null, null);
                }
                buffer.Write(chunk, 0, read);
            }

            buffer.Position = 0;
            return (buffer, contentType);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Could not download a linked account's avatar");
            return (null, null);
        }
    }

    // Returning the result //

    private record WebResult(string Result, string Ticket, string Message, string VerifierHash);

    /// <summary>
    /// Sends the result to the client that started the flow. Apps receive it on
    /// their callback address. A web popup can't reliably talk to the window
    /// that opened it after visiting a provider (their pages may cut that link),
    /// so the result waits here and the web app collects it with its verifier.
    /// </summary>
    private async Task<IResult> DeliverAsync(FlowState state, string stateId, string result, string ticket = null, string message = null)
    {
        var values = new Dictionary<string, string> { ["result"] = result };
        if (ticket is not null) values["ticket"] = ticket;
        if (message is not null) values["message"] = message;

        switch (state.Client)
        {
            case ExternalAuthClient.Android:
                return Results.Redirect(QueryHelpers.AddQueryString(AndroidCallbackUrl, values));
            case ExternalAuthClient.Desktop:
                // Other local programs can reach the port too. The app accepts
                // only the request that carries its own flow ID.
                values["flow"] = stateId;
                return Results.Redirect(QueryHelpers.AddQueryString($"http://127.0.0.1:{state.LoopbackPort}/callback", values));
            default:
                await _tickets.SetAsync(ResultKind, stateId, new WebResult(result, ticket, message, state.VerifierHash), StateLifetime);
                return ClosingPage(result == ExternalAuthResults.Error
                    ? message ?? "Sign-in didn't finish."
                    : "Done! You can close this window and return to Valour.");
        }
    }

    /// <summary>
    /// Collects a web flow's result for the client that holds the verifier.
    /// Returns null while the person is still on the provider's page.
    /// </summary>
    public async Task<ExternalAuthResultResponse> TakeWebResultAsync(string flowId, string verifier)
    {
        var value = await _tickets.GetAsync<WebResult>(ResultKind, flowId);
        if (value is null || !AuthTicketStore.VerifierMatches(verifier, value.VerifierHash))
            return null;

        await _tickets.RemoveAsync(ResultKind, flowId);
        return new ExternalAuthResultResponse { Result = value.Result, Ticket = value.Ticket, Message = value.Message };
    }

    /// <summary>A page that shows a message and closes the popup it is in.</summary>
    private static IResult ClosingPage(string message)
    {
        const string script = "window.close();";
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
        var html =
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>Valour</title></head>" +
            "<body style=\"background:#040d14;color:#fff;font-family:sans-serif\"><p>" +
            System.Net.WebUtility.HtmlEncode(message) + "</p>" +
            $"<script>{script}</script></body></html>";

        return new PageResult(html, $"default-src 'none'; script-src 'sha256-{hash}'; style-src 'unsafe-inline'; frame-ancestors 'none'");
    }

    private static IResult PlainPage(string message) =>
        new PageResult(
            "<!doctype html><html><head><meta charset=\"utf-8\"><title>Valour</title></head>" +
            "<body style=\"background:#040d14;color:#fff;font-family:sans-serif\"><p>" +
            System.Net.WebUtility.HtmlEncode(message) + "</p></body></html>",
            "default-src 'none'; style-src 'unsafe-inline'; frame-ancestors 'none'");

    private sealed class PageResult(string html, string contentSecurityPolicy) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.ContentSecurityPolicy = contentSecurityPolicy;
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.ContentType = "text/html; charset=utf-8";
            return httpContext.Response.WriteAsync(html);
        }
    }
}
