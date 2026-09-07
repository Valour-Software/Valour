namespace Valour.Shared.Models;

/// <summary>
/// An incoming webhook bound to a planet channel. Anyone holding the token
/// URL can post messages to the channel; management requires the
/// ManageWebhooks planet permission.
/// </summary>
public interface ISharedPlanetWebhook : ISharedPlanetModel<long>
{
    public const string BaseRoute = "api/planetwebhooks";
    public const int MaxNameLength = 32;

    /// <summary>
    /// Builds the anonymous execute route for a webhook.
    /// </summary>
    public static string GetExecuteRoute(long id, string token) => $"api/webhooks/{id}/{token}";

    /// <summary>
    /// The channel this webhook posts to
    /// </summary>
    long ChannelId { get; set; }

    /// <summary>
    /// The default display name for messages sent by this webhook
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// The immutable CDN asset currently used as this webhook's avatar.
    /// Null means the webhook uses the default avatar.
    /// </summary>
    long? AvatarAssetId { get; set; }

    /// <summary>
    /// Whether the current avatar asset has multiple frames.
    /// </summary>
    bool AvatarAnimated { get; set; }

    /// <summary>
    /// The secret token used to execute the webhook. Only returned to members
    /// with ManageWebhooks; stripped from live-sync broadcasts.
    /// </summary>
    string? Token { get; set; }

    /// <summary>
    /// The user who created the webhook
    /// </summary>
    long CreatorUserId { get; set; }

    DateTime TimeCreated { get; set; }

    public string? GetAvatar(AvatarFormat format = AvatarFormat.Webp128) =>
        GetAvatar(Id, AvatarAssetId, AvatarAnimated, format);

    /// <summary>
    /// Builds a URL for a Valour-managed webhook avatar. Webhook avatar
    /// assets are immutable, so messages can safely retain the exact asset
    /// that was active when they were sent.
    /// </summary>
    public static string? GetAvatar(
        long? webhookId,
        long? avatarAssetId,
        bool avatarAnimated,
        AvatarFormat format = AvatarFormat.Webp128)
    {
        if (webhookId is null || avatarAssetId is null)
            return null;

        var size = format switch
        {
            AvatarFormat.Jpeg64 or AvatarFormat.Gif64 or AvatarFormat.Webp64 or AvatarFormat.WebpAnimated64 => 64,
            AvatarFormat.Jpeg128 or AvatarFormat.Gif128 or AvatarFormat.Webp128 or AvatarFormat.WebpAnimated128 => 128,
            _ => 256,
        };

        var wantsAnimation = format is AvatarFormat.Gif64 or AvatarFormat.Gif128 or AvatarFormat.Gif256
            or AvatarFormat.WebpAnimated64 or AvatarFormat.WebpAnimated128 or AvatarFormat.WebpAnimated256;

        string fileName;
        if (wantsAnimation && avatarAnimated)
        {
            var extension = format is AvatarFormat.Gif64 or AvatarFormat.Gif128 or AvatarFormat.Gif256
                ? "gif"
                : "webp";
            fileName = $"anim-{size}.{extension}";
        }
        else
        {
            var extension = format is AvatarFormat.Jpeg64 or AvatarFormat.Jpeg128 or AvatarFormat.Jpeg256
                ? "jpg"
                : "webp";
            fileName = $"{size}.{extension}";
        }

        return $"{ValourHosts.PublicCdnBaseUrl}/valour-public/webhookavatars/{webhookId}/{avatarAssetId}/{fileName}";
    }
}
