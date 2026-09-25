namespace Valour.Server.Models;

/// <summary>
/// Server-internal options for trusted message write paths (webhooks and
/// automod responses).
/// Never bound from client requests: the normal post path passes null,
/// which clears all webhook identity fields.
/// </summary>
public class MessageWriteOptions
{
    public long? WebhookId { get; set; }

    /// <summary>
    /// Effective display-name override, already resolved against the
    /// webhook's default.
    /// </summary>
    public string OverrideName { get; set; }

    /// <summary>
    /// Immutable avatar asset selected from the webhook at send time.
    /// </summary>
    public long? WebhookAvatarAssetId { get; set; }

    public bool WebhookAvatarAnimated { get; set; }

    /// <summary>
    /// Strips role mentions; used when there is no member to check
    /// the MentionAll permission against.
    /// </summary>
    public bool SuppressRoleMentions { get; set; }

    /// <summary>
    /// For plain text the server writes on someone's behalf, such as webhook
    /// posts. The server seals the text to the channel key as this kind and
    /// keeps only the sealed copy. When null, the message must be end-to-end
    /// encrypted by its sender, and plain text is refused.
    /// </summary>
    public Valour.Sdk.E2ee.ServerSealedKind? SealKind { get; set; }
}
