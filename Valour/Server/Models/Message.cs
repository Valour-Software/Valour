using System.Text.Json.Serialization;
using Valour.Database;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class Message : ServerModel<long>, ISharedMessage
{
    public Message ReplyTo { get; set; }
    public List<MessageReaction> Reactions { get; set; }
    public List<Valour.Sdk.Models.MessageAttachment> Attachments { get; set; }
    public List<Mention> Mentions { get; set; }

    public Message AddReplyTo(Message replyTo)
    {
        ReplyTo = replyTo;
        return this;
    }

    public void SetAttachments(List<Valour.Sdk.Models.MessageAttachment> attachments)
    {
        Attachments = attachments;
    }

    public void SetMentions(List<Mention> mentions)
    {
        Mentions = mentions;
    }
    
    /// <summary>
    /// The id of the planet this message belongs to
    /// </summary>
    public long? PlanetId { get; set; }

    /// <summary>
    /// The message (if any) this is a reply to
    /// </summary>
    public long? ReplyToId { get; set; }

    /// <summary>
    /// The author's user ID
    /// </summary>
    public long AuthorUserId { get; set; }

    /// <summary>
    /// The author's member ID
    /// </summary>
    public long? AuthorMemberId { get; set; }

    /// <summary>
    /// String representation of message
    /// </summary>
    public string Content { get; set; }

    /// <summary>
    /// The time the message was sent (in UTC)
    /// </summary>
    public DateTime TimeSent { get; set; }

    /// <summary>
    /// Id of the channel this message belonged to
    /// </summary>
    public long ChannelId { get; set; }
    
    /// <summary>
    /// The time when the message was edited, or null if it was not
    /// </summary>
    public DateTime? EditedTime { get; set; }

    /// <summary>
    /// Server-managed provenance for content imported from another service.
    /// </summary>
    public string ImportSource { get; set; }

    /// <summary>
    /// Non-null when this message was sent through a webhook. Server-managed.
    /// </summary>
    public long? WebhookId { get; set; }

    /// <summary>
    /// Display-name override for webhook messages. Server-managed.
    /// </summary>
    public string OverrideName { get; set; }

    /// <summary>
    /// Immutable Valour CDN avatar asset used by this webhook message.
    /// </summary>
    public long? WebhookAvatarAssetId { get; set; }

    public bool WebhookAvatarAnimated { get; set; }

    /// <summary>
    /// How the text is stored. See <see cref="Valour.Sdk.E2ee.MessageEncryption"/>.
    /// </summary>
    public int EncryptionVersion { get; set; }

    /// <summary>
    /// The encrypted message when <see cref="EncryptionVersion"/> is nonzero.
    /// </summary>
    public byte[] Envelope { get; set; }

    /// <summary>
    /// The channel key generation used for the envelope.
    /// </summary>
    public int KeyGeneration { get; set; }

    /// <summary>
    /// Keyed search terms uploaded by the sender. Accepted on requests and
    /// never returned to clients.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int[] SearchTerms { get; set; }

    /// <summary>
    /// Validated search terms to store with the message. Kept separate from
    /// <see cref="SearchTerms"/> so they are persisted but never serialized
    /// to clients or relayed between nodes.
    /// </summary>
    [JsonIgnore]
    public int[] IndexedTerms { get; set; }

    /// <summary>
    /// For end-to-end encrypted messages, URLs the sender wants previews for.
    /// The server cannot read the text, so the sender lists them. Accepted on
    /// requests and never returned to clients.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string> PreviewUrls { get; set; }

    /// <summary>
    /// For end-to-end encrypted messages, the IDs of the custom planet emojis
    /// in the text, so the server can check them. Accepted on requests and
    /// never returned to clients.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<long> CustomEmojiIds { get; set; }

    /// <summary>
    /// Used to identify a message returned from the server
    /// </summary>
    public string Fingerprint { get; set; }
}
