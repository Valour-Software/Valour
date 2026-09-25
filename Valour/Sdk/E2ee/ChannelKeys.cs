namespace Valour.Sdk.E2ee;

public enum ChannelKeyRotationReason : byte
{
    /// <summary>The channel's first key.</summary>
    Initial = 0,

    /// <summary>Someone lost access, so later messages must use a key they never had.</summary>
    MemberRemoved = 1,

    /// <summary>A member chose to replace the key.</summary>
    Manual = 2,

    /// <summary>
    /// The channel hides history from new members, so a new member received a
    /// key that cannot unlock earlier generations.
    /// </summary>
    NewMemberWithoutHistory = 3,

    /// <summary>A moderator replaced the search and automod index key.</summary>
    IndexRotation = 4,

    /// <summary>
    /// The server created the channel's first key so it could seal webhook,
    /// system, and earlier plain-text messages before any member published one.
    /// The server knew this key, so members replace it before sending.
    /// </summary>
    ServerSealing = 5,

    /// <summary>
    /// A member who could not get the current key from anyone online started a
    /// new one so they could send. It cannot unlock earlier keys; members who
    /// hold those share them later.
    /// </summary>
    KeyUnavailable = 6
}

/// <summary>
/// How a channel's key generations link to each other. Each generation
/// normally unlocks the one before it, so holding the newest key unlocks all
/// history. A generation that does not unlock the previous one starts a new
/// chain; the newest key of each earlier chain is a separate head that must be
/// shared on its own. Breaks made to hide history from new members are not
/// heads, because those members must not receive earlier keys.
/// </summary>
public static class ChannelKeyChain
{
    public readonly record struct Link(bool UnlocksPrevious, ChannelKeyRotationReason Reason);

    /// <summary>The generations a member needs directly to read all shared history.</summary>
    public static List<int> Heads(IReadOnlyDictionary<int, Link> links, int latest)
    {
        var heads = new List<int>();
        if (latest < 1)
            return heads;

        heads.Add(latest);
        for (var generation = latest - 1; generation >= 1; generation--)
        {
            if (links.TryGetValue(generation + 1, out var next) && !next.UnlocksPrevious &&
                next.Reason != ChannelKeyRotationReason.NewMemberWithoutHistory)
                heads.Add(generation);
        }

        return heads;
    }

    /// <summary>Every generation reachable from the generations a member holds directly.</summary>
    public static HashSet<int> Reachable(IReadOnlyDictionary<int, Link> links, IEnumerable<int> held)
    {
        var reachable = new HashSet<int>();
        foreach (var start in held)
        {
            var generation = start;
            while (generation >= 1 && reachable.Add(generation) &&
                   links.TryGetValue(generation, out var link) && link.UnlocksPrevious)
                generation--;
        }

        return reachable;
    }
}

/// <summary>
/// The rules a channel key was created under, signed into its record so a
/// device can check them without trusting what the server reports.
/// </summary>
public readonly record struct ChannelKeyTerms(ChannelKeyPolicy Policy, bool SharesHistory, int AccessLogSeq)
{
    /// <summary>Terms for a channel without a membership log.</summary>
    public static ChannelKeyTerms Ungoverned(ChannelKeyPolicy policy, bool sharesHistory) =>
        new(policy, sharesHistory, -1);

    /// <summary>True when a signed membership log decides who receives the key.</summary>
    public bool IsGoverned => Policy is ChannelKeyPolicy.Group or ChannelKeyPolicy.PlanetInviteOnly;
}

/// <summary>
/// A signed channel key generation as stored by the server. The secret itself
/// is never here; members receive it in sealed boxes.
/// </summary>
public sealed class ChannelKeyGenerationEntry
{
    public long ChannelId { get; set; }
    public int Generation { get; set; }
    public byte[] Body { get; set; }
    public byte[] Signature { get; set; }
}

public sealed class ChannelKeyGenerationRecord
{
    public long ChannelId { get; init; }
    public long PlanetId { get; init; }
    public int Generation { get; init; }
    public long CreatorUserId { get; init; }
    public string CreatorDeviceId { get; init; }
    public long TimestampMs { get; init; }
    public ChannelKeyRotationReason Reason { get; init; }

    /// <summary>
    /// Public key derived from the secret. Anyone, including the server, can
    /// seal data to the channel with it, but only members can open it. It also
    /// lets a recipient confirm that a boxed secret belongs to this generation.
    /// </summary>
    public byte[] SealPublicKey { get; init; }

    /// <summary>
    /// The generation whose secret derives the search and automod index key.
    /// It changes only on index rotations so automod keeps working when the
    /// content key rotates without a moderator online.
    /// </summary>
    public int IndexGeneration { get; init; }

    /// <summary>
    /// The previous generation's secret encrypted under this one, or empty when
    /// this generation deliberately does not unlock history.
    /// </summary>
    public byte[] PreviousWrapped { get; init; }

    /// <summary>
    /// Who the creator sealed the key to and whether new members may read
    /// history. <see cref="ChannelKeyTerms.AccessLogSeq"/> is the newest
    /// membership log entry the creator had verified, so a device can tell
    /// whether someone was removed after the key was made.
    /// </summary>
    public ChannelKeyTerms Terms { get; init; }

    /// <summary>The creator user ID of a generation the server created.</summary>
    public const long ServerCreatorId = 0;

    /// <summary>Prefix of the creator device ID of a generation the server created.</summary>
    public const string ServerDevicePrefix = "server:";

    public DateTime Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(TimestampMs).UtcDateTime;

    public bool IsServerCreated => Reason == ChannelKeyRotationReason.ServerSealing;

    public bool UnlocksPrevious => PreviousWrapped is { Length: > 0 };

    public byte[] Encode() => new E2eeWriter()
        .WriteMagic("VCK2")
        .WriteInt64(ChannelId)
        .WriteInt64(PlanetId)
        .WriteInt32(Generation)
        .WriteInt64(CreatorUserId)
        .WriteString(CreatorDeviceId)
        .WriteInt64(TimestampMs)
        .WriteByte((byte)Reason)
        .WriteFixed(SealPublicKey, E2eeCrypto.KeySize)
        .WriteInt32(IndexGeneration)
        .WriteBytes(PreviousWrapped ?? [])
        .WriteByte((byte)Terms.Policy)
        .WriteByte(Terms.SharesHistory ? (byte)1 : (byte)0)
        .WriteInt32(Terms.AccessLogSeq)
        .ToArray();

    public static ChannelKeyGenerationRecord Decode(byte[] body)
    {
        var reader = new E2eeReader(body);
        reader.ReadMagic("VCK2");
        var record = new ChannelKeyGenerationRecord
        {
            ChannelId = reader.ReadInt64(),
            PlanetId = reader.ReadInt64(),
            Generation = reader.ReadInt32(),
            CreatorUserId = reader.ReadInt64(),
            CreatorDeviceId = reader.ReadString(),
            TimestampMs = reader.ReadInt64(),
            Reason = (ChannelKeyRotationReason)reader.ReadByte(),
            SealPublicKey = reader.ReadFixed(E2eeCrypto.KeySize),
            IndexGeneration = reader.ReadInt32(),
            PreviousWrapped = reader.ReadBytes(),
            Terms = new ChannelKeyTerms((ChannelKeyPolicy)reader.ReadByte(), reader.ReadByte() switch
            {
                0 => false,
                1 => true,
                _ => throw new E2eeFormatException("Invalid history flag.")
            }, reader.ReadInt32())
        };
        reader.EnsureEnd();

        if (!Enum.IsDefined(record.Terms.Policy) || record.Terms.AccessLogSeq < -1)
            throw new E2eeFormatException("Invalid channel key terms.");

        if (record.Generation < 1)
            throw new E2eeFormatException("Invalid channel key generation.");
        if (record.IndexGeneration < 1 || record.IndexGeneration > record.Generation)
            throw new E2eeFormatException("Invalid index generation.");
        if (record.Generation == 1 && record.UnlocksPrevious)
            throw new E2eeFormatException("The first generation cannot unlock a previous key.");

        // Only a channel's first key may come from the server, and it can
        // never claim to be a member's key or the reverse.
        var serverCreated = record.CreatorUserId == ServerCreatorId ||
                            record.CreatorDeviceId.StartsWith(ServerDevicePrefix, StringComparison.Ordinal);
        if (serverCreated != record.IsServerCreated ||
            (record.IsServerCreated && (record.Generation != 1 || record.CreatorUserId != ServerCreatorId)))
            throw new E2eeFormatException("Invalid server-created channel key.");
        return record;
    }

    /// <summary>
    /// Verifies a generation the server created, signed with one of its
    /// attestation keys.
    /// </summary>
    public static ChannelKeyGenerationRecord VerifyServer(ChannelKeyGenerationEntry entry,
        Func<string, byte[]> getServerPublicKey)
    {
        var record = Decode(entry.Body);
        if (record.ChannelId != entry.ChannelId || record.Generation != entry.Generation)
            throw new E2eeVerificationException("Channel key generation does not match its envelope.");
        if (!record.IsServerCreated)
            throw new E2eeVerificationException("Channel key generation was not created by the server.");

        var key = getServerPublicKey(record.CreatorDeviceId[ServerDevicePrefix.Length..]);
        if (key is null || !E2eeCrypto.Verify(key, entry.Body, entry.Signature))
            throw new E2eeVerificationException("Channel key generation signature is invalid.");

        return record;
    }

    /// <summary>True when a generation body can be read and was created by the server.</summary>
    public static bool TryReadIsServerCreated(byte[] body)
    {
        try
        {
            return Decode(body).IsServerCreated;
        }
        catch (E2eeFormatException)
        {
            return false;
        }
    }

    public static ChannelKeyGenerationRecord Verify(ChannelKeyGenerationEntry entry, UserKeyState creator)
    {
        var record = Decode(entry.Body);
        if (record.ChannelId != entry.ChannelId || record.Generation != entry.Generation)
            throw new E2eeVerificationException("Channel key generation does not match its envelope.");
        if (creator is null || creator.UserId != record.CreatorUserId)
            throw new E2eeVerificationException("Channel key creator is unknown.");

        if (record.IsServerCreated)
            throw new E2eeVerificationException("A server-created key must be verified with the server's key.");

        var device = creator.GetDeviceAt(record.CreatorDeviceId, record.Timestamp);
        if (device is null || !E2eeCrypto.Verify(device.SignPublicKey, entry.Body, entry.Signature))
            throw new E2eeVerificationException("Channel key generation signature is invalid.");

        return record;
    }

    /// <summary>
    /// Checks the signature with any device the creator ever registered,
    /// whatever the record's date. Such a record can still be used to read
    /// messages, since each message is signed by its author, but never to
    /// send: its date may fall outside the device's time on the account.
    /// </summary>
    public static ChannelKeyGenerationRecord VerifyForReading(ChannelKeyGenerationEntry entry, UserKeyState creator)
    {
        var record = Decode(entry.Body);
        if (record.ChannelId != entry.ChannelId || record.Generation != entry.Generation)
            throw new E2eeVerificationException("Channel key generation does not match its envelope.");
        if (creator is null || creator.UserId != record.CreatorUserId || record.IsServerCreated ||
            !creator.EverDevices.TryGetValue(record.CreatorDeviceId, out var device) ||
            !E2eeCrypto.Verify(device.Keys.SignPublicKey, entry.Body, entry.Signature))
            throw new E2eeVerificationException("Channel key generation signature is invalid.");

        return record;
    }
}

/// <summary>
/// The secret for one channel key generation, and the keys derived from it.
/// </summary>
public sealed class ChannelKeySecret
{
    public long ChannelId { get; }
    public int Generation { get; }
    public byte[] Secret { get; }

    private byte[] _contentKey;
    private byte[] _sealPrivateKey;
    private byte[] _sealPublicKey;
    private byte[] _indexKey;

    public ChannelKeySecret(long channelId, int generation, byte[] secret)
    {
        if (secret?.Length != E2eeCrypto.KeySize)
            throw new E2eeFormatException("Invalid channel key.");
        ChannelId = channelId;
        Generation = generation;
        Secret = secret;
    }

    public static ChannelKeySecret Generate(long channelId, int generation) =>
        new(channelId, generation, E2eeCrypto.RandomBytes(E2eeCrypto.KeySize));

    public byte[] ContentKey => _contentKey ??= Derive("content");

    public byte[] SealPrivateKey => _sealPrivateKey ??= Derive("seal");

    public byte[] SealPublicKey => _sealPublicKey ??= E2eeCrypto.X25519PublicKey(SealPrivateKey);

    /// <summary>
    /// Key for search and automod terms. Only meaningful for a generation that
    /// is some generation's <see cref="ChannelKeyGenerationRecord.IndexGeneration"/>.
    /// </summary>
    public byte[] IndexKey => _indexKey ??= Derive("index");

    /// <summary>
    /// Key for the search terms of messages the server sealed. The server
    /// chooses the text of those messages, so hashing them with
    /// <see cref="IndexKey"/> would let it learn the terms of any word it
    /// likes. Terms under this key only match other sealed messages, whose
    /// text the server knew anyway.
    /// </summary>
    public byte[] SealedIndexKey => _sealedIndexKey ??=
        E2eeCrypto.Hkdf(IndexKey, Salt(ChannelId, Generation), "valour-e2ee/channel/sealed-index/v1");

    private byte[] _sealedIndexKey;

    private byte[] Derive(string purpose) =>
        E2eeCrypto.Hkdf(Secret, Salt(ChannelId, Generation), "valour-e2ee/channel/" + purpose + "/v1");

    private static byte[] Salt(long channelId, int generation) =>
        new E2eeWriter().WriteMagic("VCS1").WriteInt64(channelId).WriteInt32(generation).ToArray();

    public bool Matches(ChannelKeyGenerationRecord record) =>
        record.ChannelId == ChannelId && record.Generation == Generation &&
        E2eeCrypto.FixedTimeEquals(record.SealPublicKey, SealPublicKey);

    // Boxes to members

    public static string BoxContext(long channelId, int generation, long userId, int userKeyGeneration) =>
        $"channel-key|{channelId}|{generation}|{userId}|{userKeyGeneration}";

    public byte[] SealTo(long userId, UserPublicKey userKey) =>
        E2eeCrypto.Seal(userKey.EncryptPublicKey, Secret, BoxContext(ChannelId, Generation, userId, userKey.Generation));

    public static ChannelKeySecret OpenBox(long channelId, int generation, long userId, UserKeyPair userKey, byte[] box) =>
        new(channelId, generation,
            E2eeCrypto.Open(userKey.EncryptPrivateKey, box, BoxContext(channelId, generation, userId, userKey.Generation)));

    // History chain

    public byte[] WrapPrevious(ChannelKeySecret previous)
    {
        if (previous.ChannelId != ChannelId || previous.Generation != Generation - 1)
            throw new E2eeFormatException("Previous channel key does not precede this one.");
        return E2eeCrypto.Encrypt(Derive("previous"), previous.Secret, PreviousAad());
    }

    public ChannelKeySecret UnwrapPrevious(byte[] wrapped) =>
        new(ChannelId, Generation - 1, E2eeCrypto.Decrypt(Derive("previous"), wrapped, PreviousAad()));

    private byte[] PreviousAad() =>
        new E2eeWriter().WriteMagic("VCP1").WriteInt64(ChannelId).WriteInt32(Generation).ToArray();
}

/// <summary>
/// Builds signed channel key generations.
/// </summary>
public static class ChannelKeyGenerationBuilder
{
    public static (ChannelKeyGenerationEntry Entry, ChannelKeyGenerationRecord Record) Create(
        ChannelKeySecret secret, long planetId, long creatorUserId, DeviceKeyPair creatorDevice,
        ChannelKeyRotationReason reason, int indexGeneration, ChannelKeySecret previousToWrap, long timestampMs,
        ChannelKeyTerms terms) =>
        Create(secret, planetId, creatorUserId, creatorDevice.DeviceId, creatorDevice.Sign, reason, indexGeneration,
            previousToWrap, timestampMs, terms);

    /// <summary>
    /// Creates a channel's first key on the server, signed with the server's
    /// attestation key.
    /// </summary>
    public static (ChannelKeyGenerationEntry Entry, ChannelKeyGenerationRecord Record) CreateByServer(
        ChannelKeySecret secret, long planetId, string serverKeyId, Func<byte[], byte[]> serverSign, long timestampMs,
        ChannelKeyTerms terms)
    {
        if (secret.Generation != 1)
            throw new ArgumentException("The server can only create a channel's first key.", nameof(secret));

        return Create(secret, planetId, ChannelKeyGenerationRecord.ServerCreatorId,
            ChannelKeyGenerationRecord.ServerDevicePrefix + serverKeyId, serverSign,
            ChannelKeyRotationReason.ServerSealing, 1, null, timestampMs, terms);
    }

    private static (ChannelKeyGenerationEntry Entry, ChannelKeyGenerationRecord Record) Create(
        ChannelKeySecret secret, long planetId, long creatorUserId, string creatorDeviceId, Func<byte[], byte[]> sign,
        ChannelKeyRotationReason reason, int indexGeneration, ChannelKeySecret previousToWrap, long timestampMs,
        ChannelKeyTerms terms)
    {
        var record = new ChannelKeyGenerationRecord
        {
            ChannelId = secret.ChannelId,
            PlanetId = planetId,
            Generation = secret.Generation,
            CreatorUserId = creatorUserId,
            CreatorDeviceId = creatorDeviceId,
            TimestampMs = timestampMs,
            Reason = reason,
            SealPublicKey = secret.SealPublicKey,
            IndexGeneration = indexGeneration,
            PreviousWrapped = previousToWrap is null ? [] : secret.WrapPrevious(previousToWrap),
            Terms = terms
        };

        var body = record.Encode();
        var entry = new ChannelKeyGenerationEntry
        {
            ChannelId = secret.ChannelId,
            Generation = secret.Generation,
            Body = body,
            Signature = sign(body)
        };
        return (entry, record);
    }
}
