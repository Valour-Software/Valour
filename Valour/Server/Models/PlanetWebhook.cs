using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetWebhook : ServerModel<long>, ISharedPlanetWebhook
{
    /// <summary>
    /// The id of the planet this webhook belongs to
    /// </summary>
    public long PlanetId { get; set; }

    /// <summary>
    /// The channel this webhook posts to
    /// </summary>
    public long ChannelId { get; set; }

    /// <summary>
    /// The default display name for messages sent by this webhook
    /// </summary>
    public string Name { get; set; }

    /// <summary>
    /// The default avatar for messages sent by this webhook
    /// </summary>
    public string AvatarUrl { get; set; }

    /// <summary>
    /// The secret token used to execute the webhook. Stripped before any
    /// broadcast; only returned directly to members with ManageWebhooks.
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// The user who created the webhook
    /// </summary>
    public long CreatorUserId { get; set; }

    public DateTime TimeCreated { get; set; }

    /// <summary>
    /// A copy safe to broadcast or return to members without the token.
    /// </summary>
    public PlanetWebhook WithoutToken() =>
        new()
        {
            Id = Id,
            PlanetId = PlanetId,
            ChannelId = ChannelId,
            Name = Name,
            AvatarUrl = AvatarUrl,
            Token = null,
            CreatorUserId = CreatorUserId,
            TimeCreated = TimeCreated,
        };
}
