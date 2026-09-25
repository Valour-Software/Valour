namespace Valour.Sdk.E2ee;

// Wire contracts for the end-to-end encryption API. Binary fields are sent as
// base64 by System.Text.Json.

public sealed class UserKeyBoxDto
{
    public long UserId { get; set; }
    public int Generation { get; set; }
    public string RecipientId { get; set; }
    public byte[] Box { get; set; }
}

public sealed class AppendKeyLogRequest
{
    public UserKeyLogEntry Entry { get; set; }

    /// <summary>
    /// User key boxes created alongside the entry, such as boxes for the
    /// remaining devices after a revocation.
    /// </summary>
    public List<UserKeyBoxDto> Boxes { get; set; } = new();
}

public sealed class UserKeyLogsRequest
{
    /// <summary>
    /// Users to fetch, with the number of entries the caller already has for
    /// each. Only newer entries are returned.
    /// </summary>
    public Dictionary<long, int> KnownCounts { get; set; } = new();
}

public sealed class DeviceDescriptorDto
{
    public string DeviceId { get; set; }
    public byte[] SignPublicKey { get; set; }
    public byte[] EncryptPublicKey { get; set; }
    public string Name { get; set; }
    public byte[] JoinProof { get; set; }

    public static DeviceDescriptorDto From(DeviceDescriptor descriptor) => new()
    {
        DeviceId = descriptor.Keys.DeviceId,
        SignPublicKey = descriptor.Keys.SignPublicKey,
        EncryptPublicKey = descriptor.Keys.EncryptPublicKey,
        Name = descriptor.Name,
        JoinProof = descriptor.JoinProof
    };

    public DeviceDescriptor ToDescriptor() => new()
    {
        Keys = new DevicePublicKeys(DeviceId, SignPublicKey, EncryptPublicKey),
        Name = Name,
        JoinProof = JoinProof
    };
}

public enum DeviceLinkStatus
{
    /// <summary>Waiting for the new device to join (existing device showed the code).</summary>
    WaitingForNewDevice = 0,

    /// <summary>Waiting for an existing device to approve.</summary>
    WaitingForApproval = 1,
    Approved = 2,
    Denied = 3,
    Expired = 4
}

public sealed class DeviceLinkSessionDto
{
    public string Id { get; set; }
    public long UserId { get; set; }
    public DeviceLinkMode Mode { get; set; }
    public DeviceLinkStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DeviceDescriptorDto Device { get; set; }
    public byte[] JoinMac { get; set; }
    public string ApprovedByDeviceId { get; set; }

    /// <summary>
    /// The approving device's proof that it read or showed the link secret,
    /// relayed to the new device. The server cannot check it.
    /// </summary>
    public byte[] ApprovalMac { get; set; }

    /// <summary>
    /// The approving device's pins, sealed to the new device. Included only
    /// when the session is fetched directly, not in realtime events.
    /// </summary>
    public byte[] ApprovalPins { get; set; }
}

public sealed class CreateDeviceLinkRequest
{
    public DeviceLinkMode Mode { get; set; }

    /// <summary>Required when the new device creates the session.</summary>
    public DeviceDescriptorDto Device { get; set; }

    /// <summary>
    /// When an existing device creates the session, the ID derived from its
    /// link secret, so a new device that types the code can find it.
    /// </summary>
    public string SessionId { get; set; }
}

public sealed class JoinDeviceLinkRequest
{
    public DeviceDescriptorDto Device { get; set; }
    public byte[] JoinMac { get; set; }
}

public sealed class ApproveDeviceLinkRequest
{
    public UserKeyLogEntry Entry { get; set; }
    public UserKeyBoxDto Box { get; set; }

    /// <summary>See <see cref="DeviceLinkVerification.ApprovalMac"/>.</summary>
    public byte[] ApprovalMac { get; set; }

    /// <summary>The approving device's pins, sealed to the new device. Optional.</summary>
    public byte[] Pins { get; set; }
}

public sealed class ChannelKeyBoxDto
{
    public long ChannelId { get; set; }
    public int Generation { get; set; }
    public long UserId { get; set; }
    public int UserKeyGeneration { get; set; }
    public byte[] Box { get; set; }
}

public enum ChannelKeyPolicy
{
    /// <summary>A one-to-one DM. Keys go to its two members.</summary>
    Direct = 0,

    /// <summary>A group DM governed by its access log.</summary>
    Group = 1,

    /// <summary>An open planet. Keys go to anyone the planet's permissions let view the channel.</summary>
    PlanetOpen = 2,

    /// <summary>An invite-only planet. Keys go only to viewers admitted by the planet's access log.</summary>
    PlanetInviteOnly = 3
}

public sealed class ChannelKeyStateDto
{
    public ChannelKeyPolicy Policy { get; set; }
    public int LatestGeneration { get; set; }

    /// <summary>
    /// True when someone who holds the newest key can no longer view the
    /// channel. The next sender must create a new generation.
    /// </summary>
    public bool RotationRequired { get; set; }

    /// <summary>Whether members who join later receive earlier generations.</summary>
    public bool SharesHistory { get; set; } = true;

    /// <summary>
    /// The recent generations and the ones the caller holds, with their search
    /// key generations. Older records are fetched from
    /// <c>api/e2ee/channels/{id}/generations</c> when needed.
    /// </summary>
    public List<ChannelKeyGenerationEntry> Generations { get; set; } = new();

    public List<ChannelKeyBoxDto> MyBoxes { get; set; } = new();

    /// <summary>The channel's newest search key generations, newest first, for encrypted search.</summary>
    public List<int> IndexGenerations { get; set; } = new();

    /// <summary>
    /// Set only in the response to creating a generation: recipients whose
    /// box was sealed to a user key they have since replaced. The server
    /// stored the generation without those boxes. The creator reloads these
    /// users' key logs and shares the key with them again.
    /// </summary>
    public List<long> StaleRecipients { get; set; } = new();
}

/// <summary>The result of sharing channel key boxes.</summary>
public sealed class ChannelKeyShareResultDto
{
    /// <summary>
    /// Recipients whose box was sealed to a user key they have since
    /// replaced. The server skipped those boxes and stored the rest. The
    /// sharer reloads these users' key logs and shares with them again.
    /// </summary>
    public List<long> StaleRecipients { get; set; } = new();
}

public sealed class CreateChannelKeyGenerationRequest
{
    public ChannelKeyGenerationEntry Entry { get; set; }
    public List<ChannelKeyBoxDto> Boxes { get; set; } = new();
}

public sealed class PendingKeyRecipientDto
{
    public long UserId { get; set; }

    /// <summary>
    /// True if the user already holds an earlier generation of this channel's
    /// key. Channels that hide history from new members start a generation
    /// that does not unlock earlier ones before sharing with a user who holds
    /// none.
    /// </summary>
    public bool HasEarlierKeys { get; set; }

    /// <summary>
    /// The generations to share with this user: the newest one if they lack
    /// it, and the head of each earlier chain they cannot reach. See
    /// <see cref="ChannelKeyChain"/>.
    /// </summary>
    public List<int> Generations { get; set; } = new();
}

public sealed class ChannelKeyRecipientsDto
{
    public long ChannelId { get; set; }
    public int LatestGeneration { get; set; }

    /// <summary>
    /// Users who can view the channel, have set up encryption, and are missing
    /// keys they may hold. For planet channels this lists only users who asked
    /// for keys; DMs list every member missing one.
    /// </summary>
    public List<PendingKeyRecipientDto> Pending { get; set; } = new();

    /// <summary>
    /// Users who hold the newest generation, most recent first, up to
    /// <see cref="MaxListedHolders"/>. <see cref="HolderCount"/> counts all of them.
    /// </summary>
    public List<long> Holders { get; set; } = new();

    /// <summary>How many users hold the newest generation.</summary>
    public int HolderCount { get; set; }

    /// <summary>The most holders <see cref="Holders"/> lists.</summary>
    public const int MaxListedHolders = 100;

    /// <summary>
    /// True when the newest generation does not unlock earlier keys and no
    /// message uses it yet. A planet that hides history can give it to new
    /// members without starting another one, because it reveals nothing.
    /// </summary>
    public bool LatestIsUnused { get; set; }
}

public sealed class KeyRequestDto
{
    public long ChannelId { get; set; }
}

public sealed class E2eeServerKeyDto
{
    public string KeyId { get; set; }
    public byte[] PublicKey { get; set; }
}

public sealed class EncryptedSearchRequest
{
    /// <summary>
    /// One term set per index key generation. A message matches when it
    /// contains every term of any set.
    /// </summary>
    public List<int[]> TermSets { get; set; } = new();

    public long? BeforeId { get; set; }
    public int Count { get; set; } = 50;
}

public sealed class MessageTermsDto
{
    public long MessageId { get; set; }
    public int[] Terms { get; set; }
}

public sealed class AutomodTermsWorkDto
{
    public long ChannelId { get; set; }
    public int IndexGeneration { get; set; }
    public List<AutomodTriggerWorkDto> Triggers { get; set; } = new();
}

public sealed class AutomodTriggerWorkDto
{
    public Guid TriggerId { get; set; }
    public int Type { get; set; }
    public string TriggerWords { get; set; }
}

public sealed class AutomodTermsUploadDto
{
    public Guid TriggerId { get; set; }
    public long ChannelId { get; set; }
    public int IndexGeneration { get; set; }
    public List<int[]> Alternatives { get; set; } = new();
}

/// <summary>
/// Makes a planet public or private and sets whether new members can read
/// earlier messages. A public planet is open: its permissions decide who
/// receives keys. A private planet is invite-only: its signed membership log
/// decides.
/// </summary>
public sealed class SetPlanetPrivacyRequest
{
    public bool Public { get; set; }
    public bool SharesHistory { get; set; } = true;

    /// <summary>
    /// The membership log entry that makes the change, signed by the owner's
    /// device. Making a planet private needs a <see cref="AccessLogEntryType.Genesis"/>
    /// entry, or a <see cref="AccessLogEntryType.Restart"/> after it was made
    /// public. Making a private planet public needs an
    /// <see cref="AccessLogEntryType.Open"/> entry. Changes that keep the
    /// planet's privacy need none.
    /// </summary>
    public AccessLogEntry Entry { get; set; }
}

public enum ReportEvidenceVerification
{
    /// <summary>The server could not confirm the text.</summary>
    Unverified = 0,

    /// <summary>The text matched the author's franking commitment and device signature.</summary>
    AuthorSigned = 1,

    /// <summary>The text matched the server's attestation for a message it sealed.</summary>
    ServerAttested = 2
}

/// <summary>
/// A message a reporter reveals with a report.
/// </summary>
public sealed class MessageEvidenceDto
{
    public long MessageId { get; set; }

    /// <summary>The revision the reporter saw. Edits increase it.</summary>
    public int Revision { get; set; }

    public string Content { get; set; }
    public string Embed { get; set; }

    /// <summary>For end-to-end encrypted messages, the franking key from the payload.</summary>
    public byte[] FrankingKey { get; set; }

    /// <summary>For server-sealed messages, the salt from the payload.</summary>
    public byte[] Salt { get; set; }
}

public sealed class ReportEvidenceDto
{
    public long MessageId { get; set; }
    public int Revision { get; set; }
    public long ChannelId { get; set; }
    public long AuthorUserId { get; set; }
    public DateTime TimeSent { get; set; }
    public string Content { get; set; }
    public string Embed { get; set; }
    public ReportEvidenceVerification Verification { get; set; }
}

public static class E2eeRealtimeEventTypes
{
    /// <summary>A device-link session for the user changed.</summary>
    public const string LinkSession = "link-session";

    /// <summary>The user's own key log changed, for example a device was added or revoked.</summary>
    public const string KeyLogUpdated = "key-log";

    /// <summary>A member is waiting for a channel key.</summary>
    public const string KeyRequest = "key-request";

    /// <summary>New channel key boxes or a new generation are available.</summary>
    public const string KeysAvailable = "keys-available";

    /// <summary>An access log changed.</summary>
    public const string AccessLogUpdated = "access-log";

    /// <summary>Automod trigger terms need computing for a planet the user moderates.</summary>
    public const string AutomodTermsNeeded = "automod-terms";
}

/// <summary>
/// Codes at the start of failure messages that programs act on. A message
/// reads as the code, a colon, and a sentence for people.
/// </summary>
public static class E2eeErrorCodes
{
    /// <summary>
    /// A client sent or edited a message without encrypting it, such as a raw
    /// HTTP bot or an app or SDK from before encryption.
    /// </summary>
    public const string EncryptionRequired = "E2EE_REQUIRED";

    /// <summary>
    /// The channel key must rotate before the message can be sent. The SDK
    /// rotates and retries when it sees it.
    /// </summary>
    public const string RotationRequired = "E2EE_ROTATION_REQUIRED";

    /// <summary>The message used an older key generation. The SDK reloads the keys and retries.</summary>
    public const string StaleGeneration = "E2EE_STALE_GENERATION";

    /// <summary>
    /// Another member appended to the membership log at the same moment. The
    /// SDK reloads the log and retries.
    /// </summary>
    public const string AccessLogChanged = "E2EE_ACCESS_LOG_CHANGED";

    /// <summary>
    /// A device removal's membership log cutoff is past a log's end or before
    /// an entry the removed device signed. The SDK loads the logs and retries.
    /// </summary>
    public const string RemovalCutoffStale = "E2EE_REMOVAL_CUTOFF_STALE";

    /// <summary>True when a failure message carries the given code.</summary>
    public static bool Is(string message, string code) =>
        message?.Contains(code, StringComparison.Ordinal) == true;
}

/// <summary>
/// Realtime notification for end-to-end encryption. Sent as the hub method
/// <see cref="HubMethod"/> to a user's group or a channel's group.
/// </summary>
public sealed class E2eeRealtimeEvent
{
    public const string HubMethod = "E2ee-Event";

    public string Type { get; set; }
    public long? UserId { get; set; }
    public long? ChannelId { get; set; }
    public long? PlanetId { get; set; }
    public DeviceLinkSessionDto LinkSession { get; set; }
}
