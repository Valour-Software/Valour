using System.Text.Json.Serialization;
using Valour.Config.Configs;
using Valour.Shared.Models;

namespace Valour.Server.Services.ExternalAuth;

/// <summary>Sign in with Discord. Discord accounts also suggest a username.</summary>
public class DiscordAuthProvider : ExternalAuthProvider
{
    public DiscordAuthProvider(IHttpClientFactory httpClientFactory, ILogger<DiscordAuthProvider> logger)
        : base(httpClientFactory, logger)
    {
    }

    public override string Name => ExternalAuthProviders.Discord;
    public override string DisplayName => "Discord";
    public override string CredentialType => Valour.Database.CredentialType.DISCORD;

    protected override ExternalAuthProviderConfig Config => ExternalAuthConfig.Current?.Discord;
    protected override string AuthorizeUrl => "https://discord.com/oauth2/authorize";
    protected override string TokenUrl => "https://discord.com/api/oauth2/token";
    protected override string Scope => "identify email";

    protected override async Task<ExternalIdentity> ReadIdentityAsync(HttpClient http, string accessToken)
    {
        var user = await GetJsonAsync<DiscordUser>(http, "https://discord.com/api/users/@me", accessToken);
        if (string.IsNullOrEmpty(user?.Id))
            return null;

        var avatarUrl = string.IsNullOrEmpty(user.Avatar)
            ? null
            : $"https://cdn.discordapp.com/avatars/{user.Id}/{user.Avatar}.{(user.Avatar.StartsWith("a_") ? "gif" : "png")}?size=512";

        return new ExternalIdentity(
            Name,
            CredentialType,
            user.Id,
            user.Email,
            user.Verified,
            SuggestedUsername: SuggestUsername(user.GlobalName ?? user.Username),
            Label: "@" + user.Username,
            AvatarUrl: avatarUrl);
    }

    /// <summary>The display name, trimmed to Valour's username length.</summary>
    private static string SuggestUsername(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var cleaned = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return cleaned.Length > 32 ? cleaned[..32] : cleaned;
    }

    private sealed class DiscordUser
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; }
        [JsonPropertyName("global_name")] public string GlobalName { get; set; }
        [JsonPropertyName("avatar")] public string Avatar { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; }
        [JsonPropertyName("verified")] public bool Verified { get; set; }
    }
}
