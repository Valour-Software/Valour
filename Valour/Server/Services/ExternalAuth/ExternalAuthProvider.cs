using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Valour.Config.Configs;

namespace Valour.Server.Services.ExternalAuth;

/// <summary>An account at another service, as read after its sign-in.</summary>
public record ExternalIdentity(
    string Provider,
    string CredentialType,
    string ProviderUserId,
    string Email,
    bool EmailVerified,
    string SuggestedUsername,
    string Label,
    string AvatarUrl);

/// <summary>
/// One service people can sign in with. Each provider supplies its OAuth
/// endpoints and reads its own account data. The standard parts of the
/// authorization code flow live here, and the flow around them (state,
/// tickets, what happens after sign-in) is in <see cref="ExternalAuthService"/>.
/// </summary>
public abstract class ExternalAuthProvider
{
    public const string HttpClientName = "ExternalAuth";

    protected readonly IHttpClientFactory HttpClientFactory;
    protected readonly ILogger Logger;

    protected ExternalAuthProvider(IHttpClientFactory httpClientFactory, ILogger logger)
    {
        HttpClientFactory = httpClientFactory;
        Logger = logger;
    }

    /// <summary>The route segment, from <see cref="Valour.Shared.Models.ExternalAuthProviders"/>.</summary>
    public abstract string Name { get; }

    public abstract string DisplayName { get; }

    /// <summary>The credentials table type for accounts from this provider.</summary>
    public abstract string CredentialType { get; }

    protected abstract ExternalAuthProviderConfig Config { get; }
    protected abstract string AuthorizeUrl { get; }
    protected abstract string TokenUrl { get; }
    protected abstract string Scope { get; }

    /// <summary>Provider-specific parameters for the authorization page.</summary>
    protected virtual IEnumerable<KeyValuePair<string, string>> ExtraAuthorizeParameters => [];

    public bool IsConfigured => Config?.IsConfigured == true;

    public string BuildAuthorizationUrl(string redirectUri, string state)
    {
        var parameters = new Dictionary<string, string>
        {
            ["client_id"] = Config!.ClientId!,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["state"] = state,
        };
        foreach (var (key, value) in ExtraAuthorizeParameters)
            parameters[key] = value;

        return QueryHelpers.AddQueryString(AuthorizeUrl, parameters);
    }

    /// <summary>
    /// Exchanges the authorization code for an access token and reads the
    /// account. Returns null when either step fails.
    /// </summary>
    public virtual async Task<ExternalIdentity> GetIdentityAsync(string code, string redirectUri)
    {
        var http = HttpClientFactory.CreateClient(HttpClientName);

        using var response = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = Config!.ClientId!,
            ["client_secret"] = Config!.ClientSecret!,
        }));

        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("{Provider} token exchange returned {Status}", DisplayName, (int)response.StatusCode);
            return null;
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (string.IsNullOrEmpty(token?.AccessToken))
            return null;

        // The access token came straight from the provider over TLS in exchange
        // for our client secret, so the account it reads can be trusted as is.
        return await ReadIdentityAsync(http, token.AccessToken);
    }

    protected abstract Task<ExternalIdentity> ReadIdentityAsync(HttpClient http, string accessToken);

    /// <summary>Fetches JSON from a provider API with the access token.</summary>
    protected static async Task<T> GetJsonAsync<T>(HttpClient http, string url, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await http.SendAsync(request);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : default;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; }
    }
}
