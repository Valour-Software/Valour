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
    public const int MaxAvatarUrlLength = 512;

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
    /// The default avatar for messages sent by this webhook
    /// </summary>
    string? AvatarUrl { get; set; }

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
}
