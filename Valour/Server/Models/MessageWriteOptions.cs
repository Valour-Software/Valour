namespace Valour.Server.Models;

/// <summary>
/// Server-internal options for trusted message write paths (webhooks).
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
    /// Effective avatar override, already resolved against the webhook's default.
    /// </summary>
    public string OverrideAvatarUrl { get; set; }

    /// <summary>
    /// Strips role mentions; used when there is no member to check
    /// the MentionAll permission against.
    /// </summary>
    public bool SuppressRoleMentions { get; set; }
}
