using System.Text.Json.Serialization;
using Valour.Config.Configs;
using Valour.Shared.Models;

namespace Valour.Server.Services.ExternalAuth;

/// <summary>Sign in with Google, using OpenID Connect's userinfo endpoint.</summary>
public class GoogleAuthProvider : ExternalAuthProvider
{
    public GoogleAuthProvider(IHttpClientFactory httpClientFactory, ILogger<GoogleAuthProvider> logger)
        : base(httpClientFactory, logger)
    {
    }

    public override string Name => ExternalAuthProviders.Google;
    public override string DisplayName => "Google";
    public override string CredentialType => Valour.Database.CredentialType.GOOGLE;

    protected override ExternalAuthProviderConfig Config => ExternalAuthConfig.Current?.Google;
    protected override string AuthorizeUrl => "https://accounts.google.com/o/oauth2/v2/auth";
    protected override string TokenUrl => "https://oauth2.googleapis.com/token";
    protected override string Scope => "openid email profile";

    // Lets people with several Google accounts choose one instead of silently
    // using whichever is signed in.
    protected override IEnumerable<KeyValuePair<string, string>> ExtraAuthorizeParameters =>
        [new("prompt", "select_account")];

    protected override async Task<ExternalIdentity> ReadIdentityAsync(HttpClient http, string accessToken)
    {
        var user = await GetJsonAsync<UserInfo>(http, "https://openidconnect.googleapis.com/v1/userinfo", accessToken);
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

    private sealed class UserInfo
    {
        [JsonPropertyName("sub")] public string Sub { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; }
        [JsonPropertyName("email_verified")] public bool EmailVerified { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("picture")] public string Picture { get; set; }
    }
}
