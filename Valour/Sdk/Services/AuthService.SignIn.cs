using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Services;

/// <summary>
/// Sign-in methods other than a password: Google and Discord, fingerprint
/// device keys, identity confirmation, and the account's list of sign-in methods.
/// </summary>
public partial class AuthService
{
    // Google and Discord //

    /// <summary>The providers the server can sign in with, such as "google" and "discord".</summary>
    public async Task<List<string>> GetExternalProvidersAsync()
    {
        try
        {
            return await _client.Http.GetFromJsonAsync<List<string>>("api/auth/external/providers") ?? [];
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return [];
        }
    }

    /// <summary>
    /// A random secret the client keeps while a provider sign-in is in
    /// progress. The server only sees its hash until the result is redeemed.
    /// </summary>
    public static string CreateExternalAuthVerifier() =>
        Base64Url(RandomNumberGenerator.GetBytes(32));

    public static string HashExternalAuthVerifier(string verifier) =>
        Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// Starts a provider sign-in and returns the page to open. Linking and
    /// confirming identity send the current session.
    /// </summary>
    public async Task<TaskResult<ExternalAuthBeginResponse>> BeginExternalAuthAsync(string provider, ExternalAuthBeginRequest request)
    {
        var uri = $"api/auth/external/{Uri.EscapeDataString(provider)}/begin";

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, uri) { Content = JsonContent.Create(request) };

            // Linking and confirming identity act on the signed-in account.
            if (request.Intent != ExternalAuthIntent.Login && !string.IsNullOrEmpty(Token))
                message.Headers.TryAddWithoutValidation("authorization", Token);

            // In a browser, the server ties a web sign-in to this browser with
            // a cookie, which fetch only keeps when credentials are included.
            // Native HTTP handlers ignore this option.
            message.Options.Set(BrowserFetchOptions, new Dictionary<string, object> { ["credentials"] = "include" });

            using var response = await _client.Http.SendAsync(message);
            if (!response.IsSuccessStatusCode)
                return TaskResult<ExternalAuthBeginResponse>.FromFailure(await response.Content.ReadAsStringAsync(), (int)response.StatusCode);

            return TaskResult<ExternalAuthBeginResponse>.FromData(await response.Content.ReadFromJsonAsync<ExternalAuthBeginResponse>());
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return TaskResult<ExternalAuthBeginResponse>.FromFailure("Unable to reach the server. Check your connection and try again.");
        }
    }

    /// <summary>The request option Blazor WebAssembly's HTTP handler reads fetch settings from.</summary>
    private static readonly HttpRequestOptionsKey<IDictionary<string, object>> BrowserFetchOptions = new("WebAssemblyFetchOptions");

    /// <summary>
    /// Collects a web sign-in's result. Returns a null result while the person
    /// is still on the provider's page.
    /// </summary>
    public async Task<TaskResult<ExternalAuthResultResponse>> GetExternalResultAsync(string flowId, string verifier)
    {
        try
        {
            using var content = JsonContent.Create(new ExternalTicketRequest { Ticket = flowId, Verifier = verifier });
            using var response = await _client.Http.PostAsync("api/auth/external/result", content);
            if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                return TaskResult<ExternalAuthResultResponse>.FromData(null);
            if (!response.IsSuccessStatusCode)
                return TaskResult<ExternalAuthResultResponse>.FromFailure(await response.Content.ReadAsStringAsync());

            return TaskResult<ExternalAuthResultResponse>.FromData(await response.Content.ReadFromJsonAsync<ExternalAuthResultResponse>());
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return TaskResult<ExternalAuthResultResponse>.FromFailure("Unable to reach the server.");
        }
    }

    /// <summary>Signs in with a ticket from a provider sign-in.</summary>
    public Task<AuthResult> FetchTokenWithExternalTicket(string ticket, string verifier, string multiFactorCode = null) =>
        FetchToken(new TokenRequest
        {
            ExternalTicket = ticket,
            ExternalVerifier = verifier,
            MultiFactorCode = multiFactorCode
        });

    public Task<TaskResult<ExternalRegistrationInfo>> GetExternalRegistrationAsync(string ticket, string verifier) =>
        PostUnauthenticatedAsync<ExternalRegistrationInfo>("api/auth/external/registration",
            new ExternalTicketRequest { Ticket = ticket, Verifier = verifier });

    /// <summary>The linked account's profile picture for the sign-up preview, or null.</summary>
    public async Task<byte[]> GetExternalRegistrationAvatarAsync(string ticket, string verifier)
    {
        try
        {
            using var content = JsonContent.Create(new ExternalTicketRequest { Ticket = ticket, Verifier = verifier });
            using var response = await _client.Http.PostAsync("api/auth/external/registration/avatar", content);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Creates an account from a provider sign-in (the request's ExternalTicket
    /// and ExternalVerifier) and keeps the new session.
    /// </summary>
    public async Task<AuthResult> RegisterWithExternalAccountAsync(RegisterUserRequest request)
    {
        var result = await PostUnauthenticatedAsync<AuthResult>("api/users/register", request);
        if (!result.Success)
            return new AuthResult { Success = false, Message = result.Message, Code = result.Code ?? 0 };

        if (result.Data?.Token is not null)
            SetToken(result.Data.Token.Id);

        return result.Data ?? new AuthResult { Success = false, Message = "The server returned an empty response." };
    }

    // Fingerprint device keys //

    public Task<TaskResult<DeviceChallengeResponse>> GetDeviceChallengeAsync(string keyId) =>
        PostUnauthenticatedAsync<DeviceChallengeResponse>("api/auth/device/challenge",
            new DeviceChallengeRequest { KeyId = keyId });

    /// <summary>Signs in with this device's key and its signature over a challenge.</summary>
    public Task<AuthResult> FetchTokenWithDeviceKey(string keyId, string challengeId, string signature) =>
        FetchToken(new TokenRequest
        {
            DeviceKeyId = keyId,
            DeviceChallengeId = challengeId,
            DeviceSignature = signature
        });

    public async Task<TaskResult<string>> RegisterDeviceKeyAsync(DeviceKeyRegisterRequest request)
    {
        var result = await _client.PrimaryNode.PostAsyncWithResponse<DeviceKeyRegisterResponse>("api/auth/device/register", request);
        return result.Success
            ? TaskResult<string>.FromData(result.Data.KeyId)
            : TaskResult<string>.FromFailure(result.Message);
    }

    // Identity confirmation and sign-in methods //

    /// <summary>
    /// Confirms the signed-in person with their password or this device's key.
    /// The proof is accepted by sensitive changes for a few minutes.
    /// </summary>
    public Task<TaskResult<ReauthResponse>> ReauthAsync(ReauthRequest request) =>
        _client.PrimaryNode.PostAsyncWithResponse<ReauthResponse>("api/users/me/reauth", request);

    /// <summary>Links the account from a provider sign-in with the Link intent.</summary>
    public Task<TaskResult> LinkExternalAccountAsync(string ticket, string verifier) =>
        _client.PrimaryNode.PostAsync("api/users/me/signin-methods/link",
            new ExternalTicketRequest { Ticket = ticket, Verifier = verifier });

    public async Task<List<SignInMethodInfo>> GetSignInMethodsAsync()
    {
        var result = await _client.PrimaryNode.GetJsonAsync<List<SignInMethodInfo>>("api/users/me/signin-methods");
        return result.Success ? result.Data : [];
    }

    public Task<TaskResult> RemoveSignInMethodAsync(long id, string reauthProof) =>
        _client.PrimaryNode.PostAsync($"api/users/me/signin-methods/{id}/remove",
            new RemoveSignInMethodRequest { ReauthProof = reauthProof });

    public Task<TaskResult> AddPasswordAsync(string newPassword, string reauthProof) =>
        _client.PrimaryNode.PostAsync("api/users/me/password/add",
            new SetPasswordRequest { NewPassword = newPassword, ReauthProof = reauthProof });

    /// <summary>
    /// Posts JSON without the session header, for sign-in steps that happen
    /// before there is a session.
    /// </summary>
    private async Task<TaskResult<T>> PostUnauthenticatedAsync<T>(string uri, object body)
    {
        try
        {
            using var content = JsonContent.Create(body);
            using var request = new HttpRequestMessage(HttpMethod.Post, uri) { Content = content };
            using var response = await _client.Http.SendAsync(request);

            if (!response.IsSuccessStatusCode)
                return TaskResult<T>.FromFailure(await response.Content.ReadAsStringAsync(), (int)response.StatusCode);

            var data = await response.Content.ReadFromJsonAsync<T>();
            return TaskResult<T>.FromData(data);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or System.Text.Json.JsonException)
        {
            return TaskResult<T>.FromFailure("Unable to reach the server. Check your connection and try again.");
        }
    }
}
