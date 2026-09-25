namespace Valour.Sdk.E2ee;

/// <summary>
/// How a message's text is stored.
/// </summary>
public static class MessageEncryption
{
    /// <summary>
    /// Plain text readable by the server: messages stored before encryption
    /// that have not been sealed yet, and text the server writes before it
    /// seals it.
    /// </summary>
    public const int None = 0;

    /// <summary>Encrypted by the sender's device with the channel key.</summary>
    public const int EndToEnd = 1;

    /// <summary>
    /// Sealed by the server to the channel's public key: webhook messages,
    /// system messages, and history from before the channel was encrypted.
    /// The server saw the text once and kept only the sealed copy.
    /// </summary>
    public const int ServerSealed = 2;
}

/// <summary>
/// The signed, unencrypted header of an end-to-end encrypted message. The server
/// reads it to route and validate the message. It never contains message text.
/// </summary>
public sealed class MessageEnvelopeHeader
{
    public long ChannelId { get; init; }
    public long PlanetId { get; init; }
    public int Generation { get; init; }

    /// <summary>Random per message; stays the same across edits.</summary>
    public byte[] MessageNonce { get; init; }

    /// <summary>Zero when sent, then incremented by each edit.</summary>
    public int Revision { get; init; }

    public long AuthorUserId { get; init; }
    public string AuthorDeviceId { get; init; }
    public long ReplyToId { get; init; }
    public long TimestampMs { get; init; }

    /// <summary>
    /// Commits to the text without revealing it. A recipient who later reports
    /// the message reveals the franking key, which proves the author sent exactly
    /// that text.
    /// </summary>
    public byte[] FrankingCommitment { get; init; }

    /// <summary>
    /// Hash of the sorted search terms uploaded with the message. Recipients
    /// recompute the terms from the text, so a sender cannot hide words from
    /// automod by uploading different terms.
    /// </summary>
    public byte[] TermsHash { get; init; }

    public byte[] Encode() => new E2eeWriter()
        .WriteMagic("VMH1")
        .WriteInt64(ChannelId)
        .WriteInt64(PlanetId)
        .WriteInt32(Generation)
        .WriteFixed(MessageNonce, 16)
        .WriteInt32(Revision)
        .WriteInt64(AuthorUserId)
        .WriteString(AuthorDeviceId)
        .WriteInt64(ReplyToId)
        .WriteInt64(TimestampMs)
        .WriteFixed(FrankingCommitment, E2eeCrypto.HashSize)
        .WriteFixed(TermsHash, E2eeCrypto.HashSize)
        .ToArray();

    public static MessageEnvelopeHeader Decode(byte[] data)
    {
        var reader = new E2eeReader(data);
        reader.ReadMagic("VMH1");
        var header = new MessageEnvelopeHeader
        {
            ChannelId = reader.ReadInt64(),
            PlanetId = reader.ReadInt64(),
            Generation = reader.ReadInt32(),
            MessageNonce = reader.ReadFixed(16),
            Revision = reader.ReadInt32(),
            AuthorUserId = reader.ReadInt64(),
            AuthorDeviceId = reader.ReadString(),
            ReplyToId = reader.ReadInt64(),
            TimestampMs = reader.ReadInt64(),
            FrankingCommitment = reader.ReadFixed(E2eeCrypto.HashSize),
            TermsHash = reader.ReadFixed(E2eeCrypto.HashSize)
        };
        reader.EnsureEnd();
        if (header.Generation < 1 || header.Revision < 0)
            throw new E2eeFormatException("Invalid message header.");
        return header;
    }
}

/// <summary>
/// The encrypted part of a message.
/// </summary>
public sealed class MessagePayload
{
    public string Content { get; init; }

    /// <summary>Serialized embed JSON, or null.</summary>
    public string Embed { get; init; }

    /// <summary>Random key that opens the franking commitment.</summary>
    public byte[] FrankingKey { get; init; }

    /// <summary>
    /// A digest of each file the author attached, in order, from
    /// <see cref="AttachmentBinding.Digest"/>. The server stores attachments
    /// beside the ciphertext, so recipients show only attachments listed here.
    /// </summary>
    public IReadOnlyList<byte[]> AttachmentDigests { get; init; } = [];

    public byte[] Encode()
    {
        var digests = AttachmentDigests ?? [];
        if (digests.Count > AttachmentBinding.MaxAttachments)
            throw new E2eeFormatException("A message has too many attachments.");

        var writer = new E2eeWriter()
            .WriteMagic("VMP2")
            .WriteString(Content ?? string.Empty)
            .WriteBool(Embed is not null)
            .WriteString(Embed ?? string.Empty)
            .WriteFixed(FrankingKey, E2eeCrypto.KeySize)
            .WriteInt32(digests.Count);
        foreach (var digest in digests)
            writer.WriteFixed(digest, E2eeCrypto.HashSize);
        return writer.ToArray();
    }

    public static MessagePayload Decode(byte[] data)
    {
        var reader = new E2eeReader(data);
        reader.ReadMagic("VMP2");
        var content = reader.ReadString();
        var hasEmbed = reader.ReadBool();
        var embed = reader.ReadString();
        var frankingKey = reader.ReadFixed(E2eeCrypto.KeySize);
        var count = reader.ReadInt32();
        if (count < 0 || count > AttachmentBinding.MaxAttachments)
            throw new E2eeFormatException("A message has too many attachments.");
        var digests = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
            digests.Add(reader.ReadFixed(E2eeCrypto.HashSize));
        reader.EnsureEnd();
        return new MessagePayload
        {
            Content = content, Embed = hasEmbed ? embed : null, FrankingKey = frankingKey, AttachmentDigests = digests
        };
    }
}

/// <summary>
/// Binds the files attached to an end-to-end encrypted message to its signed
/// payload. The server stores attachments beside the ciphertext and could
/// otherwise add or change them. Embeds travel inside the payload and inline
/// link previews are checked against the text, so neither is listed.
/// </summary>
public static class AttachmentBinding
{
    /// <summary>The most attachments a payload lists.</summary>
    public const int MaxAttachments = 64;

    /// <summary>
    /// True for attachments the author lists in the payload: everything except
    /// embeds and the link previews the server generates.
    /// </summary>
    public static bool IsBound(Models.MessageAttachment attachment) =>
        attachment is not null && attachment.Type != Valour.Shared.Models.MessageAttachmentType.Embed &&
        !attachment.Inline;

    /// <summary>
    /// A digest of the attachment fields the server keeps as sent: the type,
    /// location, MIME type, file name, image size, whether it is a spoiler,
    /// where it is hosted, its reported hash, and its type-specific data. A
    /// missing MIME type or file name counts as empty; the sender sends empty
    /// values so the server does not fill them in.
    /// </summary>
    public static byte[] Digest(Models.MessageAttachment attachment) =>
        E2eeCrypto.Sha256(new E2eeWriter()
            .WriteMagic("VAB1")
            .WriteInt32((int)attachment.Type)
            .WriteString(attachment.Location ?? string.Empty)
            .WriteString(attachment.MimeType ?? string.Empty)
            .WriteString(attachment.FileName ?? string.Empty)
            .WriteInt32(attachment.Width)
            .WriteInt32(attachment.Height)
            .WriteBool(attachment.IsSpoiler)
            .WriteBool(attachment.PlanetHosted)
            .WriteString(attachment.ReportedSha256 ?? string.Empty)
            .WriteString(attachment.Data ?? string.Empty)
            .ToArray());

    /// <summary>The digests of the attachments a sender lists, in order.</summary>
    public static List<byte[]> Digests(IEnumerable<Models.MessageAttachment> attachments) =>
        (attachments ?? []).Where(IsBound).Select(Digest).ToList();

    /// <summary>
    /// Keeps the attachments a recipient may show. A listed attachment is
    /// kept once for each time the payload lists it. The server replaces a
    /// file that was deleted or quarantined with a placeholder, which is kept
    /// in place of a listed file, so no more placeholders are kept than the
    /// payload lists files. Embeds and link previews are kept for the caller
    /// to check. Returns the kept attachments and whether any were removed.
    /// </summary>
    public static (List<Models.MessageAttachment> Kept, bool Removed) Filter(
        IReadOnlyList<Models.MessageAttachment> attachments, IReadOnlyList<byte[]> digests)
    {
        var kept = new List<Models.MessageAttachment>();
        var removed = false;
        if (attachments is null)
            return (kept, false);

        var remaining = (digests ?? []).ToList();
        var bound = attachments.Where(IsBound).ToList();

        // Listed files are matched first, so a placeholder never takes the
        // place of a file the server still returned.
        var matched = new HashSet<Models.MessageAttachment>(ReferenceEqualityComparer.Instance);
        foreach (var attachment in bound.Where(a => !a.Missing))
        {
            var digest = Digest(attachment);
            var index = remaining.FindIndex(d => E2eeCrypto.FixedTimeEquals(d, digest));
            if (index < 0)
                continue;
            remaining.RemoveAt(index);
            matched.Add(attachment);
        }

        foreach (var attachment in attachments)
        {
            if (attachment is null)
                continue;

            if (!IsBound(attachment))
            {
                kept.Add(attachment);
                continue;
            }

            if (matched.Contains(attachment))
            {
                kept.Add(attachment);
            }
            else if (attachment.Missing && remaining.Count > 0)
            {
                remaining.RemoveAt(0);
                kept.Add(Models.MessageAttachment.CreateMissing());
            }
            else
            {
                removed = true;
            }
        }

        return (kept, removed);
    }
}

/// <summary>
/// Checks the link previews the server generated for an end-to-end encrypted
/// message against the links in its decrypted text. The server fetches
/// previews for links the sender lists, so it chooses what a preview of a
/// listed link shows, but a preview must come from a link the text contains.
/// </summary>
public static class InlinePreviewBinding
{
    /// <summary>
    /// Returns the preview URL from <paramref name="textUrls"/> an inline
    /// attachment was generated from, or null when none could have produced
    /// it. The URLs are the ones <c>MessagePreviewUrls.Extract</c> returns, so
    /// a link inside a spoiler keeps its || markers.
    /// </summary>
    public static string SourceOf(Models.MessageAttachment attachment, IEnumerable<string> textUrls)
    {
        if (attachment is null || !attachment.Inline)
            return null;

        foreach (var entry in textUrls ?? [])
        {
            var url = IsSpoiler(entry) ? entry[2..^2] : entry;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                continue;

            if (Produces(attachment, uri))
                return entry;
        }

        return null;
    }

    public static bool IsSpoiler(string url) =>
        url is { Length: > 4 } && url.StartsWith("||", StringComparison.Ordinal) &&
        url.EndsWith("||", StringComparison.Ordinal);

    private static bool Produces(Models.MessageAttachment attachment, Uri uri)
    {
        var absolute = uri.AbsoluteUri;
        if (string.Equals(attachment.Location, absolute, StringComparison.Ordinal) ||
            string.Equals(attachment.OpenGraph?.Url, absolute, StringComparison.Ordinal))
            return true;

        // Media from other sites is copied to the content CDN under a hash
        // of the link, keeping the file's extension.
        if (Uri.TryCreate(attachment.Location, UriKind.Absolute, out var location))
        {
            var extension = Path.GetExtension(Path.GetFileName(absolute)).ToLowerInvariant();
            var hash = Convert.ToHexString(E2eeCrypto.Sha256(System.Text.Encoding.UTF8.GetBytes(absolute)))
                .ToLowerInvariant();
            if (location.AbsolutePath.EndsWith("/proxy/" + hash + extension, StringComparison.Ordinal))
                return true;
        }

        // Players and cards for known sites use an embed address on that
        // site, so the link's site must be the one the preview's type names.
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        if (!Valour.Shared.Cdn.CdnUtils.TryGetVirtualAttachmentType(host, out var type))
            return false;

        return attachment.Type == type ||
               (type == Valour.Shared.Models.MessageAttachmentType.ValourThread &&
                attachment.Type == Valour.Shared.Models.MessageAttachmentType.ValourWikiPage);
    }
}

/// <summary>
/// A signed and encrypted message: header, encrypted payload, and the author
/// device's signature over both.
/// </summary>
public sealed class MessageEnvelope
{
    public byte[] Header { get; init; }
    public byte[] Body { get; init; }
    public byte[] Signature { get; init; }

    public byte[] Encode() => new E2eeWriter()
        .WriteMagic("VME1")
        .WriteBytes(Header)
        .WriteBytes(Body)
        .WriteFixed(Signature, E2eeCrypto.SignatureSize)
        .ToArray();

    public static MessageEnvelope Decode(byte[] data)
    {
        var reader = new E2eeReader(data);
        reader.ReadMagic("VME1");
        var envelope = new MessageEnvelope
        {
            Header = reader.ReadBytes(),
            Body = reader.ReadBytes(),
            Signature = reader.ReadFixed(E2eeCrypto.SignatureSize)
        };
        reader.EnsureEnd();
        return envelope;
    }

    /// <summary>
    /// The signature covers the header and a hash of the body. Keeping only the
    /// hash lets the server retain proof of a deleted message without keeping
    /// its ciphertext.
    /// </summary>
    public static byte[] SignedData(byte[] header, byte[] bodyHash) =>
        new E2eeWriter().WriteMagic("VMS1").WriteBytes(header).WriteFixed(bodyHash, E2eeCrypto.HashSize).ToArray();

    public byte[] BodyHash() => E2eeCrypto.Sha256(Body);

    public static bool VerifySignature(byte[] header, byte[] bodyHash, byte[] signature, byte[] authorSignPublicKey) =>
        E2eeCrypto.Verify(authorSignPublicKey, SignedData(header, bodyHash), signature);
}

/// <summary>
/// Message franking: a commitment that lets a recipient prove what a message
/// said, to Valour only, when reporting it.
/// </summary>
public static class Franking
{
    public static byte[] Commit(byte[] frankingKey, long channelId, byte[] messageNonce, int revision,
        long authorUserId, string content, string embed) =>
        E2eeCrypto.HmacSha256(frankingKey, new E2eeWriter()
            .WriteMagic("VFC1")
            .WriteInt64(channelId)
            .WriteFixed(messageNonce, 16)
            .WriteInt32(revision)
            .WriteInt64(authorUserId)
            .WriteString(content ?? string.Empty)
            .WriteBool(embed is not null)
            .WriteString(embed ?? string.Empty)
            .ToArray());

    public static bool Verify(MessageEnvelopeHeader header, byte[] frankingKey, string content, string embed)
    {
        if (frankingKey?.Length != E2eeCrypto.KeySize)
            return false;
        var expected = Commit(frankingKey, header.ChannelId, header.MessageNonce, header.Revision,
            header.AuthorUserId, content, embed);
        return E2eeCrypto.FixedTimeEquals(expected, header.FrankingCommitment);
    }
}

/// <summary>
/// The result of opening an envelope.
/// </summary>
public sealed class OpenedMessage
{
    public MessageEnvelopeHeader Header { get; init; }
    public MessagePayload Payload { get; init; }
}

/// <summary>
/// Seals and opens end-to-end encrypted messages.
/// </summary>
public static class MessageCrypto
{
    /// <summary>
    /// The longest text a message may carry, the same limit the server applies
    /// to text it can read. Reports carry the text, so a longer message could
    /// not be reported.
    /// </summary>
    public const int MaxContentLength = 2048;

    public static byte[] MessageKey(ChannelKeySecret channelKey, byte[] messageNonce, int revision) =>
        E2eeCrypto.Hkdf(channelKey.ContentKey, messageNonce, "valour-e2ee/message/v1|" + revision);

    /// <summary>
    /// Builds the envelope for a new message or an edit. The attachment
    /// digests list the files sent with it; see <see cref="AttachmentBinding"/>.
    /// </summary>
    public static (byte[] Envelope, MessageEnvelopeHeader Header) Seal(
        ChannelKeySecret channelKey, long planetId, long authorUserId, DeviceKeyPair device,
        byte[] messageNonce, int revision, long replyToId, string content, string embed, int[] terms, long timestampMs,
        IReadOnlyList<byte[]> attachmentDigests = null)
    {
        if (messageNonce?.Length != 16)
            throw new E2eeFormatException("Message nonce must be 16 bytes.");
        if ((content?.Length ?? 0) > MaxContentLength)
            throw new E2eeFormatException($"Messages can be at most {MaxContentLength} characters.");

        var frankingKey = E2eeCrypto.RandomBytes(E2eeCrypto.KeySize);
        var header = new MessageEnvelopeHeader
        {
            ChannelId = channelKey.ChannelId,
            PlanetId = planetId,
            Generation = channelKey.Generation,
            MessageNonce = messageNonce,
            Revision = revision,
            AuthorUserId = authorUserId,
            AuthorDeviceId = device.DeviceId,
            ReplyToId = replyToId,
            TimestampMs = timestampMs,
            FrankingCommitment = Franking.Commit(frankingKey, channelKey.ChannelId, messageNonce, revision,
                authorUserId, content, embed),
            TermsHash = SearchTerms.Hash(terms)
        };

        var headerBytes = header.Encode();
        var payload = new MessagePayload
        {
            Content = content, Embed = embed, FrankingKey = frankingKey, AttachmentDigests = attachmentDigests ?? []
        };
        var body = E2eeCrypto.Encrypt(MessageKey(channelKey, messageNonce, revision), payload.Encode(), headerBytes);
        var signature = device.Sign(MessageEnvelope.SignedData(headerBytes, E2eeCrypto.Sha256(body)));

        var envelope = new MessageEnvelope { Header = headerBytes, Body = body, Signature = signature };
        return (envelope.Encode(), header);
    }

    /// <summary>
    /// Verifies the author's signature, decrypts, and checks the franking
    /// commitment. A payload whose commitment does not match is rejected so
    /// that what a recipient sees is always what the author can be held to.
    /// </summary>
    public static OpenedMessage Open(byte[] envelopeBytes, ChannelKeySecret channelKey, UserKeyState author,
        DateTime sentAt)
    {
        var envelope = MessageEnvelope.Decode(envelopeBytes);
        var header = MessageEnvelopeHeader.Decode(envelope.Header);

        if (header.ChannelId != channelKey.ChannelId || header.Generation != channelKey.Generation)
            throw new E2eeVerificationException("Message was encrypted for a different channel key.");
        if (author is null || author.UserId != header.AuthorUserId)
            throw new E2eeVerificationException("Message author is unknown.");

        var device = author.GetDeviceAt(header.AuthorDeviceId, sentAt);
        if (device is null ||
            !MessageEnvelope.VerifySignature(envelope.Header, envelope.BodyHash(), envelope.Signature, device.SignPublicKey))
            throw new E2eeVerificationException("Message signature is invalid.");

        var plaintext = E2eeCrypto.Decrypt(MessageKey(channelKey, header.MessageNonce, header.Revision), envelope.Body,
            envelope.Header);
        var payload = MessagePayload.Decode(plaintext);

        if (!Franking.Verify(header, payload.FrankingKey, payload.Content, payload.Embed))
            throw new E2eeVerificationException("Message does not match its commitment.");
        if (payload.Content.Length > MaxContentLength)
            throw new E2eeVerificationException("Message is longer than messages may be.");

        return new OpenedMessage { Header = header, Payload = payload };
    }

    /// <summary>
    /// Reads the header without decrypting, for routing and validation.
    /// </summary>
    public static (MessageEnvelope Envelope, MessageEnvelopeHeader Header) ReadHeader(byte[] envelopeBytes)
    {
        var envelope = MessageEnvelope.Decode(envelopeBytes);
        return (envelope, MessageEnvelopeHeader.Decode(envelope.Header));
    }
}
