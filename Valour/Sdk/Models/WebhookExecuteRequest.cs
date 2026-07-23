using Valour.Sdk.Models.Embeds;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

/// <summary>
/// The body of a webhook execute call (POST api/webhooks/{id}/{token}).
/// At least one of Content, Embeds, or Attachments is required.
/// </summary>
public class WebhookExecuteRequest
{
    public const int MaxEmbeds = 5;

    /// <summary>
    /// The message text. Markdown supported; role mentions are stripped.
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Embeds to attach as serialized v2 embed JSON (the wire form used
    /// everywhere embeds travel), up to <see cref="MaxEmbeds"/>. Validated
    /// with the same rules as user-sent embeds. Serialize an
    /// <see cref="Embed"/> with <see cref="EmbedParser.Serialize"/>.
    /// </summary>
    public List<string>? Embeds { get; set; }

    /// <summary>
    /// Adds an embed to the request.
    /// </summary>
    public WebhookExecuteRequest WithEmbed(Embed embed)
    {
        Embeds ??= new();
        Embeds.Add(EmbedParser.Serialize(embed));
        return this;
    }

    /// <summary>
    /// Media attachments. Locations must pass the standard media URI scan.
    /// </summary>
    public List<MessageAttachment>? Attachments { get; set; }

    /// <summary>
    /// Overrides the webhook's display name for this message only.
    /// </summary>
    public string? OverrideName { get; set; }

    /// <summary>
    /// Overrides the webhook's avatar for this message only. Must be https.
    /// </summary>
    public string? OverrideAvatarUrl { get; set; }

    /// <summary>
    /// Optional message in the same channel to reply to.
    /// </summary>
    public long? ReplyToId { get; set; }
}

/// <summary>
/// The body of a webhook message edit (PUT api/webhooks/{id}/{token}/messages/{messageId}).
/// </summary>
public class WebhookMessageEditRequest
{
    public string? Content { get; set; }

    /// <summary>
    /// Replaces the message's embeds when provided (serialized v2 embed JSON);
    /// null leaves them unchanged, an empty list removes them.
    /// </summary>
    public List<string>? Embeds { get; set; }

    /// <summary>
    /// Adds a replacement embed to the request.
    /// </summary>
    public WebhookMessageEditRequest WithEmbed(Embed embed)
    {
        Embeds ??= new();
        Embeds.Add(EmbedParser.Serialize(embed));
        return this;
    }
}
