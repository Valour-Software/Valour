using System.Text.Json.Serialization;

namespace Valour.Sdk.Models.Embeds;

/// <summary>
/// A live update to an embed on an existing message, pushed by the bot that
/// authored it. Sent to a single user (personal) when <see cref="TargetUserId"/>
/// is set, otherwise to everyone in the channel. Carries either a full
/// replacement embed or a set of changed items.
///
/// The update travels encrypted with the channel's key. The bot's SDK fills
/// <see cref="Encrypted"/> from <see cref="NewEmbedContent"/> or
/// <see cref="ChangedItemsContent"/>, and the recipient's SDK fills them back
/// in after decrypting. The plain fields are never serialized.
/// </summary>
public class EmbedUpdate
{
    public long TargetMessageId { get; set; }

    public long TargetChannelId { get; set; }

    /// <summary>The planet of the target message, used to find its channel key.</summary>
    public long? PlanetId { get; set; }

    /// <summary>
    /// When set, only this user receives the update.
    /// </summary>
    public long? TargetUserId { get; set; }

    /// <summary>
    /// Monotonic revision; clients drop updates older than the revision
    /// they are displaying. Bots that leave this 0 opt out of ordering.
    /// </summary>
    public long Revision { get; set; }

    /// <summary>The channel key generation the update is encrypted with.</summary>
    public int KeyGeneration { get; set; }

    /// <summary>The encrypted update.</summary>
    public byte[] Encrypted { get; set; }

    /// <summary>
    /// A full replacement embed payload (serialized <see cref="Embed"/>).
    /// </summary>
    [JsonIgnore]
    public string? NewEmbedContent { get; set; }

    /// <summary>
    /// A serialized list of changed items (by Id) for a targeted update.
    /// Ignored when <see cref="NewEmbedContent"/> is set.
    /// </summary>
    [JsonIgnore]
    public string? ChangedItemsContent { get; set; }
}
