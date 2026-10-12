using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Valour.Config.Configs;
using Valour.Shared.Models;

namespace Valour.Server.Services.ExternalAuth;

/// <summary>
/// Sign in with Apple. Apple differs from the other providers in three ways:
/// the client secret is a short-lived token signed with a private key, the
/// account comes from the ID token instead of a userinfo endpoint, and Apple
/// posts the result to the callback instead of redirecting with a query string.
/// Apple also requires apps to revoke its tokens when an account is deleted,
/// so the refresh token is kept with the credential (see <see cref="RevokeAsync"/>).
/// </summary>
public class AppleAuthProvider : ExternalAuthProvider
{
    private const string Issuer = "https://appleid.apple.com";

    public AppleAuthProvider(IHttpClientFactory httpClientFactory, ILogger<AppleAuthProvider> logger)
        : base(httpClientFactory, logger)
    {
    }

    public override string Name => ExternalAuthProviders.Apple;
    public override string DisplayName => "Apple";
    public override string CredentialType => Valour.Database.CredentialType.APPLE;

    private static AppleAuthProviderConfig AppleConfig => ExternalAuthConfig.Current?.Apple;

    protected override ExternalAuthProviderConfig Config => AppleConfig;
    protected override string AuthorizeUrl => "https://appleid.apple.com/auth/authorize";
    protected override string TokenUrl => "https://appleid.apple.com/auth/token";
    protected override string Scope => "email";

    // Apple requires form_post whenever a scope is requested.
    protected override IEnumerable<KeyValuePair<string, string>> ExtraAuthorizeParameters =>
        [new("response_mode", "form_post")];

    public override async Task<ExternalIdentity> GetIdentityAsync(string code, string redirectUri)
    {
        var http = HttpClientFactory.CreateClient(HttpClientName);

        using var response = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = AppleConfig!.ClientId!,
            ["client_secret"] = CreateClientSecret(),
        }));

        if (!response.IsSuccessStatusCode)
        {
            Logger.LogWarning("Apple token exchange returned {Status}", (int)response.StatusCode);
            return null;
        }

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>();
        if (string.IsNullOrEmpty(token?.IdToken))
            return null;

        // The ID token came straight from Apple over TLS in exchange for our
        // signed client secret, so its claims can be trusted as they are.
        var idToken = new JsonWebToken(token.IdToken);
        if (idToken.Issuer != Issuer || !idToken.Audiences.Contains(AppleConfig.ClientId))
        {
            Logger.LogWarning("Apple returned an ID token for another issuer or client");
            return null;
        }

        var subject = idToken.Subject;
        if (string.IsNullOrEmpty(subject))
            return null;

        idToken.TryGetPayloadValue<string>("email", out var email);

        return new ExternalIdentity(
            Name,
            CredentialType,
            subject,
            email,
            // Apple sends this as a string or a boolean. Apple only issues
            // verified addresses, including private relay addresses.
            EmailVerified: ReadBoolClaim(idToken, "email_verified"),
            SuggestedUsername: null,
            Label: email ?? "Apple account",
            AvatarUrl: null,
            RefreshToken: token.RefreshToken);
    }

    protected override Task<ExternalIdentity> ReadIdentityAsync(HttpClient http, string accessToken) =>
        throw new NotSupportedException("Apple accounts are read from the ID token.");

    /// <summary>
    /// Revokes a refresh token so the account is disconnected from Valour in
    /// the person's Apple ID settings. Returns false if Apple could not be
    /// reached or refused.
    /// </summary>
    public async Task<bool> RevokeAsync(string refreshToken)
    {
        if (string.IsNullOrEmpty(refreshToken) || !IsConfigured)
            return false;

        try
        {
            var http = HttpClientFactory.CreateClient(HttpClientName);
            using var response = await http.PostAsync("https://appleid.apple.com/auth/revoke",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = AppleConfig!.ClientId!,
                    ["client_secret"] = CreateClientSecret(),
                    ["token"] = refreshToken,
                    ["token_type_hint"] = "refresh_token",
                }));

            if (response.IsSuccessStatusCode)
                return true;

            Logger.LogWarning("Apple token revocation returned {Status}", (int)response.StatusCode);
            return false;
        }
        catch (Exception e)
        {
            Logger.LogWarning(e, "Apple token revocation failed");
            return false;
        }
    }

    /// <summary>
    /// Revokes the stored tokens of Apple credentials that were just removed.
    /// Other credential types are skipped. Revocation is best effort: the
    /// credentials are already gone, so a failure is only logged.
    /// </summary>
    public async Task RevokeCredentialsAsync(IEnumerable<Valour.Database.Credential> credentials)
    {
        foreach (var credential in credentials)
        {
            if (credential.CredentialType != Valour.Database.CredentialType.APPLE ||
                credential.Secret is not { Length: > 0 })
                continue;

            if (!await RevokeAsync(System.Text.Encoding.UTF8.GetString(credential.Secret)))
                Logger.LogWarning("Could not revoke the Apple token for credential {CredentialId}", credential.Id);
        }
    }

    /// <summary>
    /// The client secret Apple expects: an ES256 token signed with the Sign in
    /// with Apple key. Apple accepts lifetimes up to six months; a few minutes
    /// is enough because a new one is made for each request.
    /// </summary>
    private static string CreateClientSecret()
    {
        var config = AppleConfig!;
        var pem = !string.IsNullOrWhiteSpace(config.PrivateKey)
            ? config.PrivateKey
            : File.ReadAllText(config.PrivateKeyPath!);

        var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(pem);

        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = config.TeamId,
            Audience = Issuer,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(5),
            Claims = new Dictionary<string, object> { ["sub"] = config.ClientId! },
            SigningCredentials = new SigningCredentials(
                new ECDsaSecurityKey(ecdsa) { KeyId = config.KeyId }, SecurityAlgorithms.EcdsaSha256),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }

    private static bool ReadBoolClaim(JsonWebToken token, string name)
    {
        if (token.TryGetPayloadValue<bool>(name, out var value))
            return value;

        return token.TryGetPayloadValue<string>(name, out var text) &&
               string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("id_token")] public string IdToken { get; set; }
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; }
    }
}
