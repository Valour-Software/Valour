namespace Valour.Sdk.E2ee;

public enum ServerSealedKind : byte
{
    /// <summary>Posted by a webhook, which sends plain text to the server.</summary>
    Webhook = 1,

    /// <summary>Written by the server, such as an automod response.</summary>
    System = 2,

    /// <summary>History written before the channel was encrypted.</summary>
    Legacy = 3
}

/// <summary>
/// Header of a message the server sealed to a channel's public key. The server
/// signs an attestation over a salted hash of the text. It keeps the
/// attestation but discards the salt and the text, so the attestation reveals
/// nothing until someone who can read the message reports it.
///
/// The header binds the planet and channel but not the message ID, because a
/// planet that moves between nodes keeps its channel IDs but its messages
/// receive new IDs. A random nonce makes each attestation unique instead.
/// </summary>
public sealed class ServerSealedHeader
{
    public long ChannelId { get; init; }
    public long PlanetId { get; init; }
    public int Generation { get; init; }
    public byte[] Nonce { get; init; }
    public ServerSealedKind Kind { get; init; }
    public long AuthorUserId { get; init; }
    public long TimeSentMs { get; init; }
    public string ServerKeyId { get; init; }
    public byte[] Attestation { get; init; }

    public byte[] Encode() => new E2eeWriter()
        .WriteMagic("VSH2")
        .WriteInt64(ChannelId)
        .WriteInt64(PlanetId)
        .WriteInt32(Generation)
        .WriteFixed(Nonce, 16)
        .WriteByte((byte)Kind)
        .WriteInt64(AuthorUserId)
        .WriteInt64(TimeSentMs)
        .WriteString(ServerKeyId)
        .WriteFixed(Attestation, E2eeCrypto.SignatureSize)
        .ToArray();

    public static ServerSealedHeader Decode(byte[] data)
    {
        var reader = new E2eeReader(data);
        reader.ReadMagic("VSH2");
        var header = new ServerSealedHeader
        {
            ChannelId = reader.ReadInt64(),
            PlanetId = reader.ReadInt64(),
            Generation = reader.ReadInt32(),
            Nonce = reader.ReadFixed(16),
            Kind = (ServerSealedKind)reader.ReadByte(),
            AuthorUserId = reader.ReadInt64(),
            TimeSentMs = reader.ReadInt64(),
            ServerKeyId = reader.ReadString(),
            Attestation = reader.ReadFixed(E2eeCrypto.SignatureSize)
        };
        reader.EnsureEnd();
        return header;
    }
}

public sealed class ServerSealedPayload
{
    /// <summary>The most embeds one server-sealed message carries, matching the webhook limit.</summary>
    public const int MaxEmbeds = 5;

    public string Content { get; init; }

    /// <summary>The message's embeds as serialized embed JSON, in order. Empty when it has none.</summary>
    public IReadOnlyList<string> Embeds { get; init; } = [];

    public byte[] Salt { get; init; }

    /// <summary>
    /// The embed value the server's attestation covers. See
    /// <see cref="ServerSealing.AttestedEmbed"/>.
    /// </summary>
    public string Embed => ServerSealing.AttestedEmbed(Embeds);

    public byte[] Encode()
    {
        var writer = new E2eeWriter()
            .WriteMagic("VSP2")
            .WriteString(Content ?? string.Empty)
            .WriteInt32(Embeds?.Count ?? 0);
        foreach (var embed in Embeds ?? [])
            writer.WriteString(embed);
        return writer.WriteFixed(Salt, 16).ToArray();
    }

    public static ServerSealedPayload Decode(byte[] data)
    {
        var reader = new E2eeReader(data);
        reader.ReadMagic("VSP2");
        var content = reader.ReadString();
        var count = reader.ReadInt32();
        if (count < 0 || count > MaxEmbeds)
            throw new E2eeFormatException("A sealed message has too many embeds.");
        var embeds = new List<string>(count);
        for (var i = 0; i < count; i++)
            embeds.Add(reader.ReadString());
        var salt = reader.ReadFixed(16);
        reader.EnsureEnd();
        return new ServerSealedPayload { Content = content, Embeds = embeds, Salt = salt };
    }
}

public static class ServerSealing
{
    /// <summary>
    /// The single embed value a server attestation and report evidence cover:
    /// null for no embeds, the embed JSON for one, and a JSON array of the
    /// embed JSON strings for several. Reporters rebuild it from the embeds
    /// they read, so it must be computed the same way everywhere.
    /// </summary>
    public static string AttestedEmbed(IReadOnlyList<string> embeds) => embeds?.Count switch
    {
        null or 0 => null,
        1 => embeds[0],
        _ => System.Text.Json.JsonSerializer.Serialize(embeds)
    };

    public static byte[] ContentHash(byte[] salt, string content, string embed) =>
        E2eeCrypto.Sha256(new E2eeWriter()
            .WriteMagic("VSC1")
            .WriteFixed(salt, 16)
            .WriteString(content ?? string.Empty)
            .WriteBool(embed is not null)
            .WriteString(embed ?? string.Empty)
            .ToArray());

    public static byte[] AttestationData(byte[] nonce, long channelId, long planetId, long authorUserId,
        long timeSentMs, ServerSealedKind kind, byte[] contentHash) =>
        new E2eeWriter()
            .WriteMagic("VSA2")
            .WriteFixed(nonce, 16)
            .WriteInt64(channelId)
            .WriteInt64(planetId)
            .WriteInt64(authorUserId)
            .WriteInt64(timeSentMs)
            .WriteByte((byte)kind)
            .WriteFixed(contentHash, E2eeCrypto.HashSize)
            .ToArray();

    /// <summary>
    /// Seals text and embeds to a channel generation's public key. Called by
    /// the server, which holds only the public key and so cannot open what it
    /// sealed. A message carries at most <see cref="ServerSealedPayload.MaxEmbeds"/> embeds.
    /// </summary>
    public static byte[] Seal(byte[] channelSealPublicKey, long channelId, long planetId, int generation,
        ServerSealedKind kind, long authorUserId, long timeSentMs, string content, IReadOnlyList<string> embeds,
        string serverKeyId, Func<byte[], byte[]> serverSign)
    {
        embeds = embeds?.Where(e => !string.IsNullOrWhiteSpace(e)).ToList() ?? [];
        if (embeds.Count > ServerSealedPayload.MaxEmbeds)
            throw new E2eeFormatException($"A message may include at most {ServerSealedPayload.MaxEmbeds} embeds.");

        var embed = AttestedEmbed(embeds);
        var salt = E2eeCrypto.RandomBytes(16);
        var nonce = E2eeCrypto.RandomBytes(16);
        var attestation = serverSign(AttestationData(nonce, channelId, planetId, authorUserId, timeSentMs, kind,
            ContentHash(salt, content, embed)));

        var header = new ServerSealedHeader
        {
            ChannelId = channelId,
            PlanetId = planetId,
            Generation = generation,
            Nonce = nonce,
            Kind = kind,
            AuthorUserId = authorUserId,
            TimeSentMs = timeSentMs,
            ServerKeyId = serverKeyId,
            Attestation = attestation
        };

        var headerBytes = header.Encode();
        var payload = new ServerSealedPayload { Content = content, Embeds = embeds, Salt = salt };
        var sealedPayload = E2eeCrypto.Seal(channelSealPublicKey, payload.Encode(), Context(headerBytes));

        return new E2eeWriter()
            .WriteMagic("VSE1")
            .WriteBytes(headerBytes)
            .WriteBytes(sealedPayload)
            .ToArray();
    }

    public static (ServerSealedHeader Header, byte[] HeaderBytes, byte[] SealedPayload) Read(byte[] envelope)
    {
        var reader = new E2eeReader(envelope);
        reader.ReadMagic("VSE1");
        var headerBytes = reader.ReadBytes();
        var sealedPayload = reader.ReadBytes();
        reader.EnsureEnd();
        return (ServerSealedHeader.Decode(headerBytes), headerBytes, sealedPayload);
    }

    /// <summary>
    /// Opens a server-sealed message and checks the server's attestation.
    /// </summary>
    public static ServerSealedPayload Open(byte[] envelope, ChannelKeySecret channelKey,
        Func<string, byte[]> getServerPublicKey)
    {
        var (header, headerBytes, sealedPayload) = Read(envelope);
        if (header.ChannelId != channelKey.ChannelId || header.Generation != channelKey.Generation)
            throw new E2eeVerificationException("Message was sealed for a different channel key.");

        var payload = ServerSealedPayload.Decode(
            E2eeCrypto.Open(channelKey.SealPrivateKey, sealedPayload, Context(headerBytes)));

        if (!VerifyAttestation(header, payload.Salt, payload.Content, payload.Embed,
                getServerPublicKey(header.ServerKeyId)))
            throw new E2eeVerificationException("Server attestation is invalid.");

        return payload;
    }

    public static bool VerifyAttestation(ServerSealedHeader header, byte[] salt, string content, string embed,
        byte[] serverPublicKey)
    {
        if (serverPublicKey is null || salt?.Length != 16)
            return false;
        var data = AttestationData(header.Nonce, header.ChannelId, header.PlanetId, header.AuthorUserId,
            header.TimeSentMs, header.Kind, ContentHash(salt, content, embed));
        return E2eeCrypto.Verify(serverPublicKey, data, header.Attestation);
    }

    private static string Context(byte[] headerBytes) =>
        "server-sealed|" + Base64Url.Encode(E2eeCrypto.Sha256(headerBytes));
}
