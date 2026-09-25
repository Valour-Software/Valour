namespace Valour.Sdk.E2ee;

public enum UserKeyLogEntryType : byte
{
    /// <summary>First entry. Registers the first device, user key, and recovery key.</summary>
    Genesis = 0,

    /// <summary>Adds a device. Signed by an existing device or the recovery key.</summary>
    AddDevice = 1,

    /// <summary>Removes a device and replaces the user key so it cannot read new data.</summary>
    RevokeDevice = 2,

    /// <summary>Replaces the user key without removing a device.</summary>
    RotateUserKey = 3,

    /// <summary>Sets or clears the recovery key.</summary>
    SetRecovery = 4,

    /// <summary>
    /// Starts over after every device and the recovery key were lost. Nothing
    /// earlier vouches for it, so other people's clients show a warning.
    /// </summary>
    Reset = 5
}

/// <summary>
/// A signed entry in a user's key log as stored by the server.
/// </summary>
public sealed class UserKeyLogEntry
{
    public long UserId { get; set; }
    public int Seq { get; set; }
    public byte[] Body { get; set; }
    public byte[] Signature { get; set; }

    public byte[] Hash() => E2eeCrypto.Sha256(Body, Signature);
}

/// <summary>
/// The newest entry of a membership log that a removed device may have
/// signed. Entries it signs in that log after this sequence number have no
/// effect, whatever time they claim, so a copy of its keys cannot let anyone
/// in after the device is removed. A sequence number of -1 means the device
/// signed nothing in the log.
/// </summary>
public readonly record struct AccessLogCutoff(AccessLogScope Scope, long ScopeId, int Seq)
{
    /// <summary>The most cutoffs one key log entry carries.</summary>
    public const int MaxPerEntry = 1000;
}

public sealed class DeviceDescriptor
{
    public DevicePublicKeys Keys { get; init; }
    public string Name { get; init; }
    public byte[] JoinProof { get; init; }
}

/// <summary>
/// The decoded contents of a <see cref="UserKeyLogEntry"/> body.
/// </summary>
public sealed class UserKeyLogRecord
{
    public long UserId { get; init; }
    public int Seq { get; init; }
    public int Epoch { get; init; }
    public byte[] PreviousHash { get; init; }
    public UserKeyLogEntryType Type { get; init; }
    public long TimestampMs { get; init; }
    public string SignerId { get; init; }

    public DeviceDescriptor Device { get; init; }
    public string TargetDeviceId { get; init; }
    public UserPublicKey NewUserKey { get; init; }
    public byte[] PreviousUserKeyWrapped { get; init; }
    public DevicePublicKeys Recovery { get; init; }

    /// <summary>
    /// On a <see cref="UserKeyLogEntryType.RevokeDevice"/> entry, the removed
    /// device's cutoff in each membership log the account belongs to. Null for
    /// other entries and for removals written before cutoffs existed; those
    /// devices fall back to the time they were removed. A
    /// <see cref="UserKeyLogEntryType.Reset"/> carries none, because only the
    /// new device signs it, so the server could forge one to undo entries.
    /// </summary>
    public List<AccessLogCutoff> AccessLogCutoffs { get; init; }

    public DateTime Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(TimestampMs).UtcDateTime;

    public byte[] Encode()
    {
        // Only removals changed format, so other entries stay readable by
        // apps from before cutoffs existed.
        var writer = new E2eeWriter()
            .WriteMagic(Type == UserKeyLogEntryType.RevokeDevice ? "VKL2" : "VKL1")
            .WriteInt64(UserId)
            .WriteInt32(Seq)
            .WriteInt32(Epoch)
            .WriteFixed(PreviousHash, E2eeCrypto.HashSize)
            .WriteByte((byte)Type)
            .WriteInt64(TimestampMs)
            .WriteString(SignerId);

        switch (Type)
        {
            case UserKeyLogEntryType.Genesis:
            case UserKeyLogEntryType.Reset:
                WriteDevice(writer, Device);
                WriteUserKey(writer, NewUserKey);
                WriteOptionalRecovery(writer, Recovery);
                break;
            case UserKeyLogEntryType.AddDevice:
                WriteDevice(writer, Device);
                break;
            case UserKeyLogEntryType.RevokeDevice:
                writer.WriteString(TargetDeviceId);
                WriteUserKey(writer, NewUserKey);
                writer.WriteBytes(PreviousUserKeyWrapped);
                WriteCutoffs(writer, AccessLogCutoffs);
                break;
            case UserKeyLogEntryType.RotateUserKey:
                WriteUserKey(writer, NewUserKey);
                writer.WriteBytes(PreviousUserKeyWrapped);
                break;
            case UserKeyLogEntryType.SetRecovery:
                WriteOptionalRecovery(writer, Recovery);
                break;
            default:
                throw new E2eeFormatException("Unknown key log entry type.");
        }

        return writer.ToArray();
    }

    public static UserKeyLogRecord Decode(byte[] body)
    {
        var reader = new E2eeReader(body);
        var hasCutoffs = reader.ReadMagicOf("VKL1", "VKL2") == "VKL2";
        var userId = reader.ReadInt64();
        var seq = reader.ReadInt32();
        var epoch = reader.ReadInt32();
        var previousHash = reader.ReadFixed(E2eeCrypto.HashSize);
        var type = (UserKeyLogEntryType)reader.ReadByte();
        var timestamp = reader.ReadInt64();
        var signerId = reader.ReadString();

        DeviceDescriptor device = null;
        string targetDeviceId = null;
        UserPublicKey newUserKey = null;
        byte[] previousWrapped = null;
        DevicePublicKeys recovery = null;
        List<AccessLogCutoff> cutoffs = null;

        switch (type)
        {
            case UserKeyLogEntryType.Genesis:
            case UserKeyLogEntryType.Reset:
                device = ReadDevice(reader);
                newUserKey = ReadUserKey(reader);
                recovery = ReadOptionalRecovery(reader);
                break;
            case UserKeyLogEntryType.AddDevice:
                device = ReadDevice(reader);
                break;
            case UserKeyLogEntryType.RevokeDevice:
                targetDeviceId = reader.ReadString();
                newUserKey = ReadUserKey(reader);
                previousWrapped = reader.ReadBytes();
                if (hasCutoffs)
                    cutoffs = ReadCutoffs(reader);
                break;
            case UserKeyLogEntryType.RotateUserKey:
                newUserKey = ReadUserKey(reader);
                previousWrapped = reader.ReadBytes();
                break;
            case UserKeyLogEntryType.SetRecovery:
                recovery = ReadOptionalRecovery(reader);
                break;
            default:
                throw new E2eeFormatException("Unknown key log entry type.");
        }

        reader.EnsureEnd();

        return new UserKeyLogRecord
        {
            UserId = userId,
            Seq = seq,
            Epoch = epoch,
            PreviousHash = previousHash,
            Type = type,
            TimestampMs = timestamp,
            SignerId = signerId,
            Device = device,
            TargetDeviceId = targetDeviceId,
            NewUserKey = newUserKey,
            PreviousUserKeyWrapped = previousWrapped,
            Recovery = recovery,
            AccessLogCutoffs = cutoffs
        };
    }

    private static void WriteCutoffs(E2eeWriter writer, List<AccessLogCutoff> cutoffs)
    {
        cutoffs ??= [];
        writer.WriteInt32(cutoffs.Count);
        foreach (var cutoff in cutoffs)
        {
            writer.WriteByte((byte)cutoff.Scope)
                .WriteInt64(cutoff.ScopeId)
                .WriteInt32(cutoff.Seq);
        }
    }

    private static List<AccessLogCutoff> ReadCutoffs(E2eeReader reader)
    {
        var count = reader.ReadInt32();
        if (count is < 0 or > AccessLogCutoff.MaxPerEntry)
            throw new E2eeFormatException("Too many membership log cutoffs.");

        var cutoffs = new List<AccessLogCutoff>(count);
        for (var i = 0; i < count; i++)
        {
            var scope = (AccessLogScope)reader.ReadByte();
            var scopeId = reader.ReadInt64();
            var seq = reader.ReadInt32();
            if (!Enum.IsDefined(scope) || seq < -1)
                throw new E2eeFormatException("Invalid membership log cutoff.");
            cutoffs.Add(new AccessLogCutoff(scope, scopeId, seq));
        }

        if (cutoffs.DistinctBy(c => (c.Scope, c.ScopeId)).Count() != cutoffs.Count)
            throw new E2eeFormatException("A membership log is listed twice.");
        return cutoffs;
    }

    private static void WriteDevice(E2eeWriter writer, DeviceDescriptor device)
    {
        writer.WriteString(device.Keys.DeviceId)
            .WriteFixed(device.Keys.SignPublicKey, E2eeCrypto.KeySize)
            .WriteFixed(device.Keys.EncryptPublicKey, E2eeCrypto.KeySize)
            .WriteString(device.Name ?? string.Empty)
            .WriteFixed(device.JoinProof, E2eeCrypto.SignatureSize);
    }

    private static DeviceDescriptor ReadDevice(E2eeReader reader)
    {
        var id = reader.ReadString();
        var sign = reader.ReadFixed(E2eeCrypto.KeySize);
        var encrypt = reader.ReadFixed(E2eeCrypto.KeySize);
        var name = reader.ReadString();
        var proof = reader.ReadFixed(E2eeCrypto.SignatureSize);
        return new DeviceDescriptor { Keys = new DevicePublicKeys(id, sign, encrypt), Name = name, JoinProof = proof };
    }

    private static void WriteUserKey(E2eeWriter writer, UserPublicKey key)
    {
        writer.WriteInt32(key.Generation)
            .WriteFixed(key.SignPublicKey, E2eeCrypto.KeySize)
            .WriteFixed(key.EncryptPublicKey, E2eeCrypto.KeySize);
    }

    private static UserPublicKey ReadUserKey(E2eeReader reader) =>
        new(reader.ReadInt32(), reader.ReadFixed(E2eeCrypto.KeySize), reader.ReadFixed(E2eeCrypto.KeySize));

    private static void WriteOptionalRecovery(E2eeWriter writer, DevicePublicKeys recovery)
    {
        writer.WriteBool(recovery is not null);
        if (recovery is null)
            return;
        writer.WriteString(recovery.DeviceId)
            .WriteFixed(recovery.SignPublicKey, E2eeCrypto.KeySize)
            .WriteFixed(recovery.EncryptPublicKey, E2eeCrypto.KeySize);
    }

    private static DevicePublicKeys ReadOptionalRecovery(E2eeReader reader)
    {
        if (!reader.ReadBool())
            return null;
        return new DevicePublicKeys(reader.ReadString(), reader.ReadFixed(E2eeCrypto.KeySize),
            reader.ReadFixed(E2eeCrypto.KeySize));
    }
}

public sealed class UserDeviceInfo
{
    public DevicePublicKeys Keys { get; init; }
    public string Name { get; init; }
    public DateTime AddedAt { get; init; }
    public string AddedBy { get; init; }
    public int Epoch { get; init; }
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// For a device removed with cutoffs, the newest entry it may have signed
    /// in each listed membership log. See <see cref="AccessLogCutoff"/>.
    /// </summary>
    public Dictionary<(AccessLogScope Scope, long ScopeId), int> AccessLogCutoffs { get; set; }

    public string DeviceId => Keys.DeviceId;
}

/// <summary>
/// The verified result of replaying a user's key log.
/// </summary>
public sealed class UserKeyState
{
    public long UserId { get; init; }
    public int Epoch { get; internal set; }
    public int HeadSeq { get; internal set; } = -1;
    public byte[] HeadHash { get; internal set; } = new byte[E2eeCrypto.HashSize];

    /// <summary>The time the newest entry was signed. Entry times never go backwards.</summary>
    public long HeadTimestampMs { get; internal set; }

    public UserPublicKey UserKey { get; internal set; }
    public DevicePublicKeys Recovery { get; internal set; }

    /// <summary>Devices that can currently act for the user.</summary>
    public Dictionary<string, UserDeviceInfo> ActiveDevices { get; } = new();

    /// <summary>
    /// Every device from every epoch. Older devices still verify data they
    /// signed before they were revoked or replaced by a reset.
    /// </summary>
    public Dictionary<string, UserDeviceInfo> EverDevices { get; } = new();

    /// <summary>
    /// Public keys for every user key generation. Channel key boxes name the
    /// generation they were sealed to.
    /// </summary>
    public Dictionary<int, UserPublicKey> UserKeyGenerations { get; } = new();

    /// <summary>
    /// For each generation after the first in an epoch, the previous generation
    /// encrypted under it.
    /// </summary>
    public Dictionary<int, byte[]> PreviousUserKeyWrapped { get; } = new();

    /// <summary>The first user key generation of the current epoch.</summary>
    public int EpochFirstGeneration { get; internal set; }

    /// <summary>Length of a <see cref="KeyId"/>.</summary>
    public const int KeyIdSize = 16;

    /// <summary>
    /// For every epoch, an ID of its first user key: the first 16 bytes of a
    /// SHA-256 hash over the user ID, the epoch, and that key's public keys.
    /// Access logs admit members by epoch and key ID, so a different key log
    /// that reuses an epoch number does not count as the admitted member.
    /// </summary>
    public Dictionary<int, byte[]> EpochKeyIds { get; } = new();

    /// <summary>The key ID of the current epoch, or null before the first entry.</summary>
    public byte[] KeyId => EpochKeyIds.GetValueOrDefault(Epoch);

    public List<UserKeyLogRecord> Records { get; } = new();

    public bool HasIdentity => UserKey is not null;

    /// <summary>
    /// Returns the public keys of a signer if it may currently act for the user.
    /// </summary>
    public DevicePublicKeys GetActiveSigner(string signerId)
    {
        if (signerId is null)
            return null;
        if (ActiveDevices.TryGetValue(signerId, out var device))
            return device.Keys;
        if (Recovery is not null && Recovery.DeviceId == signerId)
            return Recovery;
        return null;
    }

    /// <summary>
    /// Returns a device's public keys if it was allowed to sign at the given
    /// time: registered, and not yet revoked or replaced by a reset.
    /// </summary>
    public DevicePublicKeys GetDeviceAt(string deviceId, DateTime time)
    {
        if (deviceId is null || !EverDevices.TryGetValue(deviceId, out var device))
            return null;
        if (device.RevokedAt is not null && device.RevokedAt <= time)
            return null;
        return device.Keys;
    }

    /// <summary>
    /// False when a removed device signed a membership log entry after its
    /// cutoff for that log. Such an entry stays in the log's chain but has no
    /// effect. Devices removed without cutoffs, and logs their removal did not
    /// list, are checked only against the removal time.
    /// </summary>
    public bool CountsAccessLogSignature(string deviceId, AccessLogScope scope, long scopeId, int seq) =>
        deviceId is null || !EverDevices.TryGetValue(deviceId, out var device) ||
        device.AccessLogCutoffs is null ||
        !device.AccessLogCutoffs.TryGetValue((scope, scopeId), out var cutoff) ||
        seq <= cutoff;

    /// <summary>
    /// A stable fingerprint of the user's current identity, shown to people
    /// who want to compare security codes in person.
    /// </summary>
    public string SafetyNumber()
    {
        if (UserKey is null)
            return null;

        // Both public keys are covered, the same ones the membership log's
        // key ID binds, so a matching number means matching keys.
        var firstKey = UserKeyGenerations[EpochFirstGeneration];
        var input = new E2eeWriter()
            .WriteMagic("VSN2")
            .WriteInt64(UserId)
            .WriteInt32(Epoch)
            .WriteFixed(firstKey.SignPublicKey, E2eeCrypto.KeySize)
            .WriteFixed(firstKey.EncryptPublicKey, E2eeCrypto.KeySize)
            .ToArray();
        return DigitGroups.Format(E2eeCrypto.Sha256(input), 5, 6);
    }
}

/// <summary>
/// Replays and verifies a user's key log. The same rules run on the server,
/// which refuses invalid appends, and on every client, which does not trust
/// the server.
/// </summary>
public static class UserKeyLogVerifier
{
    public static UserKeyState Verify(long userId, IReadOnlyList<UserKeyLogEntry> entries)
    {
        var state = new UserKeyState { UserId = userId };
        foreach (var entry in entries)
            Apply(state, entry);
        return state;
    }

    /// <summary>
    /// Verifies one more entry against an existing state and applies it.
    /// </summary>
    public static UserKeyLogRecord Apply(UserKeyState state, UserKeyLogEntry entry)
    {
        if (entry is null || entry.Body is null || entry.Signature is null)
            throw new E2eeFormatException("Missing key log entry.");

        var record = UserKeyLogRecord.Decode(entry.Body);

        if (record.UserId != state.UserId || entry.UserId != state.UserId)
            throw new E2eeVerificationException("Key log entry belongs to another user.");
        if (record.Seq != state.HeadSeq + 1 || entry.Seq != record.Seq)
            throw new E2eeVerificationException("Key log entry is out of order.");
        if (!E2eeCrypto.FixedTimeEquals(record.PreviousHash, state.HeadHash))
            throw new E2eeVerificationException("Key log entry does not follow the previous entry.");

        // Times never go backwards, so a removed device cannot date an entry
        // before its removal.
        if (state.HeadSeq >= 0 && record.TimestampMs < state.HeadTimestampMs)
            throw new E2eeVerificationException("Key log entry is dated before the previous entry.");

        switch (record.Type)
        {
            case UserKeyLogEntryType.Genesis:
            case UserKeyLogEntryType.Reset:
                ApplyStart(state, record, entry);
                break;
            case UserKeyLogEntryType.AddDevice:
                ApplyAddDevice(state, record, entry);
                break;
            case UserKeyLogEntryType.RevokeDevice:
                ApplyRevokeDevice(state, record, entry);
                break;
            case UserKeyLogEntryType.RotateUserKey:
                ApplyRotate(state, record, entry);
                break;
            case UserKeyLogEntryType.SetRecovery:
                RequireSigner(state, record, entry);
                if (record.Recovery is not null)
                    ValidateRecoveryKeys(record.Recovery);
                state.Recovery = record.Recovery;
                break;
            default:
                throw new E2eeFormatException("Unknown key log entry type.");
        }

        state.HeadSeq = record.Seq;
        state.HeadHash = entry.Hash();
        state.HeadTimestampMs = record.TimestampMs;
        state.Records.Add(record);
        return record;
    }

    private static void ApplyStart(UserKeyState state, UserKeyLogRecord record, UserKeyLogEntry entry)
    {
        if (record.Type == UserKeyLogEntryType.Genesis)
        {
            if (record.Seq != 0 || record.Epoch != 0)
                throw new E2eeVerificationException("Only the first entry can be a genesis entry.");
        }
        else
        {
            if (record.Seq == 0 || record.Epoch != state.Epoch + 1)
                throw new E2eeVerificationException("A reset must follow an existing entry and start a new epoch.");
        }

        ValidateDevice(state.UserId, record.Device);

        // A start entry is signed by the device it introduces. Nothing earlier
        // vouches for it, which is exactly why resets are shown to contacts.
        if (record.SignerId != record.Device.Keys.DeviceId ||
            !E2eeCrypto.Verify(record.Device.Keys.SignPublicKey, entry.Body, entry.Signature))
            throw new E2eeVerificationException("Start entry is not signed by its device.");

        var expectedGeneration = state.UserKeyGenerations.Count == 0 ? 1 : state.UserKeyGenerations.Keys.Max() + 1;
        ValidateUserKey(record.NewUserKey, expectedGeneration);
        if (record.Recovery is not null)
            ValidateRecoveryKeys(record.Recovery);

        foreach (var previous in state.ActiveDevices.Values)
        {
            previous.RevokedAt = record.Timestamp;
        }

        state.Epoch = record.Epoch;
        state.ActiveDevices.Clear();
        state.Recovery = record.Recovery;
        state.UserKey = record.NewUserKey;
        state.EpochFirstGeneration = record.NewUserKey.Generation;
        state.UserKeyGenerations[record.NewUserKey.Generation] = record.NewUserKey;
        state.EpochKeyIds[record.Epoch] = ComputeKeyId(state.UserId, record.Epoch, record.NewUserKey);

        AddDevice(state, record);
    }

    /// <summary>See <see cref="UserKeyState.EpochKeyIds"/>.</summary>
    public static byte[] ComputeKeyId(long userId, int epoch, UserPublicKey firstUserKey)
    {
        var input = new E2eeWriter()
            .WriteMagic("VKI1")
            .WriteInt64(userId)
            .WriteInt32(epoch)
            .WriteFixed(firstUserKey.SignPublicKey, E2eeCrypto.KeySize)
            .WriteFixed(firstUserKey.EncryptPublicKey, E2eeCrypto.KeySize)
            .ToArray();
        return E2eeCrypto.Sha256(input)[..UserKeyState.KeyIdSize];
    }

    private static void ApplyAddDevice(UserKeyState state, UserKeyLogRecord record, UserKeyLogEntry entry)
    {
        RequireSigner(state, record, entry);
        ValidateDevice(state.UserId, record.Device);
        if (state.EverDevices.ContainsKey(record.Device.Keys.DeviceId))
            throw new E2eeVerificationException("Device is already registered.");
        AddDevice(state, record);
    }

    private static void ApplyRevokeDevice(UserKeyState state, UserKeyLogRecord record, UserKeyLogEntry entry)
    {
        RequireSigner(state, record, entry);
        if (!state.ActiveDevices.TryGetValue(record.TargetDeviceId ?? string.Empty, out var device))
            throw new E2eeVerificationException("Device is not active.");

        ValidateUserKey(record.NewUserKey, state.UserKey.Generation + 1);
        if (record.PreviousUserKeyWrapped is null || record.PreviousUserKeyWrapped.Length == 0)
            throw new E2eeVerificationException("Revocation must carry the previous user key.");

        device.RevokedAt = record.Timestamp;
        device.AccessLogCutoffs = CutoffsOf(record);
        state.ActiveDevices.Remove(record.TargetDeviceId);
        SetUserKey(state, record);
    }

    private static Dictionary<(AccessLogScope Scope, long ScopeId), int> CutoffsOf(UserKeyLogRecord record) =>
        record.AccessLogCutoffs?.ToDictionary(c => (c.Scope, c.ScopeId), c => c.Seq);

    private static void ApplyRotate(UserKeyState state, UserKeyLogRecord record, UserKeyLogEntry entry)
    {
        RequireSigner(state, record, entry);
        ValidateUserKey(record.NewUserKey, state.UserKey.Generation + 1);
        if (record.PreviousUserKeyWrapped is null || record.PreviousUserKeyWrapped.Length == 0)
            throw new E2eeVerificationException("Rotation must carry the previous user key.");
        SetUserKey(state, record);
    }

    private static void SetUserKey(UserKeyState state, UserKeyLogRecord record)
    {
        state.UserKey = record.NewUserKey;
        state.UserKeyGenerations[record.NewUserKey.Generation] = record.NewUserKey;
        state.PreviousUserKeyWrapped[record.NewUserKey.Generation] = record.PreviousUserKeyWrapped;
    }

    private static void AddDevice(UserKeyState state, UserKeyLogRecord record)
    {
        var info = new UserDeviceInfo
        {
            Keys = record.Device.Keys,
            Name = record.Device.Name,
            AddedAt = record.Timestamp,
            AddedBy = record.SignerId,
            Epoch = record.Epoch
        };
        state.ActiveDevices[info.DeviceId] = info;
        state.EverDevices[info.DeviceId] = info;
    }

    private static void RequireSigner(UserKeyState state, UserKeyLogRecord record, UserKeyLogEntry entry)
    {
        if (state.HeadSeq < 0 || state.UserKey is null)
            throw new E2eeVerificationException("Key log must start with a genesis entry.");
        if (record.Epoch != state.Epoch)
            throw new E2eeVerificationException("Key log entry has the wrong epoch.");

        var signer = state.GetActiveSigner(record.SignerId);
        if (signer is null)
            throw new E2eeVerificationException("Key log entry is signed by a device that is not active.");
        if (!E2eeCrypto.Verify(signer.SignPublicKey, entry.Body, entry.Signature))
            throw new E2eeVerificationException("Key log entry signature is invalid.");
    }

    private static void ValidateDevice(long userId, DeviceDescriptor device)
    {
        if (device?.Keys is null)
            throw new E2eeFormatException("Missing device.");
        device.Keys.Validate();
        if (device.Keys.DeviceId.StartsWith(RecoveryKey.IdPrefix, StringComparison.Ordinal))
            throw new E2eeVerificationException("Device ID is reserved.");
        if ((device.Name?.Length ?? 0) > 64)
            throw new E2eeFormatException("Device name is too long.");
        if (!DeviceJoinProof.Verify(userId, device.Keys, device.JoinProof))
            throw new E2eeVerificationException("Device did not prove it joined this account.");
    }

    private static void ValidateRecoveryKeys(DevicePublicKeys recovery)
    {
        if (recovery.SignPublicKey?.Length != E2eeCrypto.KeySize ||
            recovery.EncryptPublicKey?.Length != E2eeCrypto.KeySize ||
            recovery.DeviceId != RecoveryKey.IdPrefix + DevicePublicKeys.DeriveDeviceId(recovery.SignPublicKey))
            throw new E2eeVerificationException("Invalid recovery key.");
    }

    private static void ValidateUserKey(UserPublicKey key, int expectedGeneration)
    {
        if (key is null || key.SignPublicKey?.Length != E2eeCrypto.KeySize ||
            key.EncryptPublicKey?.Length != E2eeCrypto.KeySize)
            throw new E2eeFormatException("Invalid user key.");
        if (key.Generation != expectedGeneration)
            throw new E2eeVerificationException("User key generation is out of order.");
    }
}

/// <summary>
/// Creates signed key log entries.
/// </summary>
public static class UserKeyLogBuilder
{
    public static UserKeyLogEntry Genesis(long userId, DeviceKeyPair device, string deviceName,
        UserKeyPair userKey, RecoveryKey recovery, long timestampMs) =>
        Start(UserKeyLogEntryType.Genesis, userId, 0, 0, new byte[E2eeCrypto.HashSize], device, deviceName,
            userKey, recovery, timestampMs);

    public static UserKeyLogEntry Reset(UserKeyState state, DeviceKeyPair device, string deviceName,
        UserKeyPair userKey, RecoveryKey recovery, long timestampMs) =>
        Start(UserKeyLogEntryType.Reset, state.UserId, state.HeadSeq + 1, state.Epoch + 1, state.HeadHash, device,
            deviceName, userKey, recovery, Math.Max(timestampMs, state.HeadTimestampMs));

    public static UserKeyLogEntry AddDevice(UserKeyState state, string signerId, Func<byte[], byte[]> sign,
        DeviceDescriptor device, long timestampMs)
    {
        var record = new UserKeyLogRecord
        {
            UserId = state.UserId,
            Seq = state.HeadSeq + 1,
            Epoch = state.Epoch,
            PreviousHash = state.HeadHash,
            Type = UserKeyLogEntryType.AddDevice,
            TimestampMs = Math.Max(timestampMs, state.HeadTimestampMs),
            SignerId = signerId,
            Device = device
        };
        return SignRecord(record, sign);
    }

    public static UserKeyLogEntry RevokeDevice(UserKeyState state, string signerId, Func<byte[], byte[]> sign,
        string targetDeviceId, UserKeyPair newUserKey, UserKeyPair currentUserKey, long timestampMs,
        List<AccessLogCutoff> cutoffs = null)
    {
        var record = new UserKeyLogRecord
        {
            UserId = state.UserId,
            Seq = state.HeadSeq + 1,
            Epoch = state.Epoch,
            PreviousHash = state.HeadHash,
            Type = UserKeyLogEntryType.RevokeDevice,
            TimestampMs = Math.Max(timestampMs, state.HeadTimestampMs),
            SignerId = signerId,
            TargetDeviceId = targetDeviceId,
            NewUserKey = newUserKey.PublicKey,
            PreviousUserKeyWrapped = newUserKey.WrapPrevious(state.UserId, currentUserKey),
            AccessLogCutoffs = cutoffs ?? []
        };
        return SignRecord(record, sign);
    }

    public static UserKeyLogEntry SetRecovery(UserKeyState state, string signerId, Func<byte[], byte[]> sign,
        RecoveryKey recovery, long timestampMs)
    {
        var record = new UserKeyLogRecord
        {
            UserId = state.UserId,
            Seq = state.HeadSeq + 1,
            Epoch = state.Epoch,
            PreviousHash = state.HeadHash,
            Type = UserKeyLogEntryType.SetRecovery,
            TimestampMs = Math.Max(timestampMs, state.HeadTimestampMs),
            SignerId = signerId,
            Recovery = recovery?.PublicKeys
        };
        return SignRecord(record, sign);
    }

    public static DeviceDescriptor Describe(long userId, DeviceKeyPair device, string name) => new()
    {
        Keys = device.PublicKeys,
        Name = TrimName(name),
        JoinProof = device.CreateJoinProof(userId)
    };

    private static UserKeyLogEntry Start(UserKeyLogEntryType type, long userId, int seq, int epoch,
        byte[] previousHash, DeviceKeyPair device, string deviceName, UserKeyPair userKey, RecoveryKey recovery,
        long timestampMs)
    {
        var record = new UserKeyLogRecord
        {
            UserId = userId,
            Seq = seq,
            Epoch = epoch,
            PreviousHash = previousHash,
            Type = type,
            TimestampMs = timestampMs,
            SignerId = device.DeviceId,
            Device = Describe(userId, device, deviceName),
            NewUserKey = userKey.PublicKey,
            Recovery = recovery?.PublicKeys
        };
        return SignRecord(record, device.Sign);
    }

    private static UserKeyLogEntry SignRecord(UserKeyLogRecord record, Func<byte[], byte[]> sign)
    {
        var body = record.Encode();
        return new UserKeyLogEntry
        {
            UserId = record.UserId,
            Seq = record.Seq,
            Body = body,
            Signature = sign(body)
        };
    }

    private static string TrimName(string name)
    {
        name = string.IsNullOrWhiteSpace(name) ? "Unnamed device" : name.Trim();
        return name.Length > 64 ? name[..64] : name;
    }
}

/// <summary>
/// Formats hashes as groups of digits for people to compare.
/// </summary>
public static class DigitGroups
{
    public static string Format(byte[] hash, int groups, int digitsPerGroup)
    {
        var parts = new List<string>(groups);
        var modulus = (ulong)Math.Pow(10, digitsPerGroup);
        for (var i = 0; i < groups; i++)
        {
            // Five bytes per group give far more than the needed range, so the
            // modulo bias is negligible.
            ulong value = 0;
            for (var j = 0; j < 5; j++)
                value = (value << 8) | hash[(i * 5 + j) % hash.Length];
            parts.Add((value % modulus).ToString().PadLeft(digitsPerGroup, '0'));
        }
        return string.Join(' ', parts);
    }
}
