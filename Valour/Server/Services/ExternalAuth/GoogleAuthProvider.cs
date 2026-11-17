using System.Text.Json.Serialization;
using Valour.Config.Configs;
using Valour.Server.Services.GoogleDrive;
using Valour.Shared.Models;

namespace Valour.Server.Services.ExternalAuth;

/// <summary>
/// What Google returned when someone let Valour use their Drive: the account,
/// and the long-lived token Valour copies files with.
/// </summary>
public record DriveGrant(string AccountId, string Email, string RefreshToken);

/// <summary>A provider that can also be asked for access to the person's cloud storage.</summary>
public interface IDriveConnectProvider
{
    /// <summary>
    /// Exchanges the code from the Drive consent page. Returns null when the
    /// exchange fails, or when the person did not allow Drive access.
    /// </summary>
    Task<DriveGrant> ExchangeDriveCodeAsync(string code, string redirectUri);
}

/// <summary>
/// Sign in with Google, using OpenID Connect's userinfo endpoint. It can also
/// ask for Drive access, but only when someone turns that feature on in
/// Attachments settings. Signing in never asks for it.
/// </summary>
public class GoogleAuthProvider : ExternalAuthProvider, IDriveConnectProvider
{
    /// <summary>
    /// The only Drive permission Valour asks for. It reaches files and folders
    /// Valour made in the person's Drive and nothing else there.
    /// </summary>
    public const string DriveFileScope = "https://www.googleapis.com/auth/drive.file";

    public GoogleAuthProvider(IHttpClientFactory httpClientFactory, ILogger<GoogleAuthProvider> logger)
        : base(httpClientFactory, logger)
    {
    }

    public override string Name => ExternalAuthProviders.Google;
    public override string DisplayName => "Google";
    public override string CredentialType => Valour.Database.CredentialType.GOOGLE;

    protected override ExternalAuthProviderConfig Config => ExternalAuthConfig.Current?.Google;
    protected override string AuthorizeUrl => GoogleEndpoints.Authorize;
    protected override string TokenUrl => GoogleEndpoints.Token;
    protected override string Scope => "openid email profile";

    // Lets people with several Google accounts choose one instead of silently
    // using whichever is signed in.
    protected override IEnumerable<KeyValuePair<string, string>> ExtraAuthorizeParameters =>
        [new("prompt", "select_account")];

    protected override string GetScope(ExternalAuthIntent intent) =>
        intent == ExternalAuthIntent.ConnectDrive ? $"openid email {DriveFileScope}" : Scope;

    protected override IEnumerable<KeyValuePair<string, string>> GetExtraAuthorizeParameters(ExternalAuthIntent intent) =>
        intent == ExternalAuthIntent.ConnectDrive
            // Offline access gives Valour a refresh token to copy files in the
            // background. Consent is shown every time so Google always issues
            // one, and the Drive can be a different account than the sign-in.
            ? [new("access_type", "offline"), new("prompt", "select_account consent"), new("include_granted_scopes", "false")]
            : ExtraAuthorizeParameters;

    public async Task<DriveGrant> ExchangeDriveCodeAsync(string code, string redirectUri)
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
            Logger.LogWarning("Google Drive token exchange returned {Status}", (int)response.StatusCode);
            return null;
        }

        var token = await response.Content.ReadFromJsonAsync<DriveTokenResponse>();
        if (string.IsNullOrEmpty(token?.AccessToken) || string.IsNullOrEmpty(token.RefreshToken))
            return null;

        // Google lets people untick individual permissions on the consent page,
        // so the grant has to be checked rather than assumed.
        var granted = (token.Scope ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!granted.Contains(DriveFileScope, StringComparer.Ordinal))
            return null;

        var user = await GetJsonAsync<UserInfo>(http, GoogleEndpoints.UserInfo, token.AccessToken);
        if (string.IsNullOrEmpty(user?.Sub))
            return null;

        return new DriveGrant(user.Sub, user.Email ?? user.Name ?? "Google account", token.RefreshToken);
    }

    protected override async Task<ExternalIdentity> ReadIdentityAsync(HttpClient http, string accessToken)
    {
        var user = await GetJsonAsync<UserInfo>(http, GoogleEndpoints.UserInfo, accessToken);
        if (string.IsNullOrEmpty(user?.Sub))
            return null;

        return new ExternalIdentity(
            Name,
            CredentialType,
            user.Sub,
            user.Email,
            user.EmailVerified,
            SuggestedUsername: null,
            Label: user.Email ?? user.Name ?? "Google account",
            AvatarUrl: LargeAvatarUrl(user.Picture));
    }

    /// <summary>Google profile photo URLs end in a size option. Ask for a large square.</summary>
    private static string LargeAvatarUrl(string picture)
    {
        if (string.IsNullOrEmpty(picture))
            return null;

        var index = picture.LastIndexOf('=');
        return (index > picture.LastIndexOf('/') ? picture[..index] : picture) + "=s512-c";
    }

    private sealed class DriveTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; }
        [JsonPropertyName("scope")] public string Scope { get; set; }
    }

    private sealed class UserInfo
    {
        [JsonPropertyName("sub")] public string Sub { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; }
        [JsonPropertyName("email_verified")] public bool EmailVerified { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("picture")] public string Picture { get; set; }
    }
}
