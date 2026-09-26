using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Valour.Sdk.E2ee;

/// <summary>
/// Keeps the message keys that let a device show the text of a notification
/// while the app is closed. The app's background code, such as a browser
/// service worker or an Android push handler, reads them from here, so a store
/// must be readable by that code.
///
/// The keys only decrypt messages. They are the content keys of channel key
/// generations the app has already verified, and they cannot open key boxes,
/// sealed messages, or search terms.
/// </summary>
public interface INotificationKeyStore
{
    /// <summary>Returns the stored key set, or null.</summary>
    Task<string> LoadAsync();

    Task SaveAsync(string keySet);

    Task ClearAsync();
}

/// <summary>
/// The content key of one channel key generation.
/// </summary>
public sealed class NotificationKey
{
    // IDs are strings so that JavaScript readers keep their full precision.

    [JsonPropertyName("p")]
    public string PlanetId { get; init; }

    [JsonPropertyName("c")]
    public string ChannelId { get; init; }

    [JsonPropertyName("g")]
    public int Generation { get; init; }

    /// <summary>The generation's content key, base64.</summary>
    [JsonPropertyName("k")]
    public string Key { get; init; }
}

/// <summary>
/// The notification keys of one account, most recently used channels first.
/// The stored form is JSON:
/// <c>{"v":1,"u":"userId","keys":[{"p":"planetId","c":"channelId","g":generation,"k":"base64"}]}</c>,
/// where the planet ID is "0" for direct chats.
/// </summary>
public sealed class NotificationKeySet
{
    public const int Version = 1;

    /// <summary>Channels kept. Channels used least recently are dropped first.</summary>
    public const int MaxChannels = 400;

    /// <summary>
    /// Generations kept per channel. A message may be sent with the previous
    /// generation while a new one is being shared.
    /// </summary>
    public const int GenerationsPerChannel = 2;

    [JsonPropertyName("v")]
    public int FormatVersion { get; init; } = Version;

    [JsonPropertyName("u")]
    public string UserId { get; init; }

    [JsonPropertyName("keys")]
    public List<NotificationKey> Keys { get; init; } = [];

    public static NotificationKeySet Parse(string json)
    {
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            var set = JsonSerializer.Deserialize<NotificationKeySet>(json);
            return set?.FormatVersion == Version && set.Keys is not null ? set : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public string Serialize() => JsonSerializer.Serialize(this);

    /// <summary>Returns the content key of a generation, or null.</summary>
    public byte[] Find(long planetId, long channelId, int generation)
    {
        var planet = planetId.ToString();
        var channel = channelId.ToString();
        var key = Keys.FirstOrDefault(k => k.PlanetId == planet && k.ChannelId == channel && k.Generation == generation);
        if (key?.Key is null)
            return null;

        try
        {
            var bytes = Convert.FromBase64String(key.Key);
            return bytes.Length == E2eeCrypto.KeySize ? bytes : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Adds a generation's content key and moves its channel to the front.
    /// Returns false when nothing changed.
    /// </summary>
    public bool Add(long planetId, long channelId, int generation, byte[] contentKey)
    {
        var planet = planetId.ToString();
        var channel = channelId.ToString();
        var encoded = Convert.ToBase64String(contentKey);

        var existing = Keys.Where(k => k.PlanetId == planet && k.ChannelId == channel).ToList();
        var channelKeys = existing.Where(k => k.Generation != generation)
            .Append(new NotificationKey { PlanetId = planet, ChannelId = channel, Generation = generation, Key = encoded })
            .OrderByDescending(k => k.Generation)
            .Take(GenerationsPerChannel)
            .ToList();

        // A generation older than every kept one is not worth keeping.
        if (channelKeys.All(k => k.Generation != generation))
            return false;

        var alreadyFirst = Keys.Take(existing.Count).SequenceEqual(existing) &&
                           channelKeys.Count == existing.Count &&
                           channelKeys.Zip(existing).All(p => p.First.Generation == p.Second.Generation &&
                                                              p.First.Key == p.Second.Key);
        if (alreadyFirst)
            return false;

        Keys.RemoveAll(k => k.PlanetId == planet && k.ChannelId == channel);
        Keys.InsertRange(0, channelKeys);
        DropExtraChannels();
        return true;
    }

    /// <summary>
    /// Adds the channels of another set that this one does not have, after
    /// its own, for example keys another tab of the app saved.
    /// </summary>
    public void MergeFrom(NotificationKeySet other)
    {
        var present = Keys.Select(k => (k.PlanetId, k.ChannelId)).ToHashSet();
        Keys.AddRange(other.Keys.Where(k => !present.Contains((k.PlanetId, k.ChannelId))));
        DropExtraChannels();
    }

    // A channel's keys are kept together, so once the limit is passed every
    // later key belongs to a dropped channel.
    private void DropExtraChannels()
    {
        var channels = new HashSet<(string, string)>();
        Keys.RemoveAll(k =>
        {
            channels.Add((k.PlanetId, k.ChannelId));
            return channels.Count > MaxChannels;
        });
    }
}

/// <summary>
/// What a notification shows for a message.
/// </summary>
public sealed class NotificationPreview
{
    public string Content { get; init; }
    public bool HasEmbed { get; init; }
    public int AttachmentCount { get; init; }
}

/// <summary>
/// Decrypts the message envelope carried in a push payload.
/// </summary>
public static class NotificationPreviewDecryptor
{
    /// <summary>
    /// Decrypts an envelope with a content key from the set. Returns null when
    /// the set has no key for it or it does not decrypt.
    ///
    /// Only the channel key authenticates the text, so it came from someone
    /// who holds that key. The author's signature is not checked, because the
    /// author's device keys are not available without the app. The text is
    /// never shown as more than a preview: opening the notification loads the
    /// message and checks it fully.
    /// </summary>
    public static NotificationPreview TryDecrypt(NotificationKeySet keys, long planetId, long channelId, byte[] envelope)
    {
        if (keys is null || envelope is not { Length: > 0 })
            return null;

        try
        {
            var (decoded, header) = MessageCrypto.ReadHeader(envelope);
            if (header.ChannelId != channelId || header.PlanetId != planetId)
                return null;

            var contentKey = keys.Find(planetId, channelId, header.Generation);
            if (contentKey is null)
                return null;

            var messageKey = E2eeCrypto.Hkdf(contentKey, header.MessageNonce, "valour-e2ee/message/v1|" + header.Revision);
            var payload = MessagePayload.Decode(E2eeCrypto.Decrypt(messageKey, decoded.Body, decoded.Header));

            if (!Franking.Verify(header, payload.FrankingKey, payload.Content, payload.Embed) ||
                payload.Content.Length > MessageCrypto.MaxContentLength ||
                payload.FirstUnsupportedRequired() is not null)
                return null;

            return new NotificationPreview
            {
                Content = payload.Content,
                HasEmbed = payload.Embed is not null,
                AttachmentCount = payload.AttachmentDigests.Count
            };
        }
        catch (Exception e) when (e is E2eeFormatException or E2eeVerificationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The line shown for a message with no text.
    /// </summary>
    public static string DescribeWithoutText(NotificationPreview preview) =>
        preview.AttachmentCount switch
        {
            1 => "Sent an attachment",
            > 1 => $"Sent {preview.AttachmentCount} attachments",
            _ => preview.HasEmbed ? "Sent an embed" : null
        };
}

/// <summary>
/// Turns message markdown into the one line a notification shows. Browser
/// service workers apply the same rules in notification-preview.js, so a
/// message reads the same on every device.
/// </summary>
public static class NotificationPreviewText
{
    public const int MaxLength = 300;

    private static readonly Regex CodeFence = new("```[A-Za-z0-9_+-]*", RegexOptions.Compiled);
    // Spoilers can nest, so everything from the first marker to the last is
    // hidden, even text between two separate spoilers.
    private static readonly Regex Spoiler = new(@"\|\|[\s\S]+\|\|", RegexOptions.Compiled);
    private static readonly Regex UserMention = new("«@[mu]-[0-9]{1,20}»", RegexOptions.Compiled);
    private static readonly Regex RoleMention = new("«@r-[0-9]{1,20}»", RegexOptions.Compiled);
    private static readonly Regex ChannelMention = new("«@c-[0-9]{1,20}»", RegexOptions.Compiled);
    private static readonly Regex CustomEmoji = new("«e-:([a-z0-9_]{2,32}):~[0-9]{1,20}»", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]\n]*)\]\([^)\s]*\)", RegexOptions.Compiled);
    private static readonly Regex InlineCode = new("`([^`\n]+)`", RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*([^*\n]+)\*\*", RegexOptions.Compiled);
    private static readonly Regex Underline = new("__([^_\n]+)__", RegexOptions.Compiled);
    private static readonly Regex Strikethrough = new("~~([^~\n]+)~~", RegexOptions.Compiled);
    private static readonly Regex Italic = new(@"\*([^*\n]+)\*", RegexOptions.Compiled);
    private static readonly Regex LinePrefix = new(@"^[ \t]*(#{1,6}[ \t]+|>[ \t]?|-#[ \t]+)",
        RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

    /// <summary>
    /// Returns the text to show, or an empty string when nothing is left.
    /// Spoilers are hidden, and mentions, which need names only the app can
    /// look up, are shown by kind.
    /// </summary>
    public static string Format(string content, int maxLength = MaxLength)
    {
        if (string.IsNullOrWhiteSpace(content))
            return string.Empty;

        var text = CodeFence.Replace(content, " ");
        text = Spoiler.Replace(text, "(spoiler)");
        text = UserMention.Replace(text, "@user");
        text = RoleMention.Replace(text, "@role");
        text = ChannelMention.Replace(text, "#channel");
        text = CustomEmoji.Replace(text, ":$1:");
        text = Link.Replace(text, "$1");
        text = InlineCode.Replace(text, "$1");
        text = Bold.Replace(text, "$1");
        text = Underline.Replace(text, "$1");
        text = Strikethrough.Replace(text, "$1");
        text = Italic.Replace(text, "$1");
        text = LinePrefix.Replace(text, "");
        text = Whitespace.Replace(text, " ").Trim();

        if (text.Length <= maxLength)
            return text;

        var cut = maxLength - 1;
        if (cut > 0 && char.IsHighSurrogate(text[cut - 1]))
            cut--;
        return text[..cut].TrimEnd() + "\u2026";
    }

    /// <summary>
    /// The notification text for a decrypted preview, or null when there is
    /// nothing to show.
    /// </summary>
    public static string Describe(NotificationPreview preview)
    {
        if (preview is null)
            return null;

        var text = Format(preview.Content);
        return text.Length > 0 ? text : NotificationPreviewDecryptor.DescribeWithoutText(preview);
    }
}
