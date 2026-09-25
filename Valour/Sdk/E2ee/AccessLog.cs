namespace Valour.Sdk.E2ee;

/// <summary>
/// What an access log governs.
/// </summary>
public enum AccessLogScope : byte
{
    /// <summary>A private planet. Admins admit members; members share keys only with admitted users.</summary>
    Planet = 1,

    /// <summary>A group DM. Members add and remove each other.</summary>
    GroupChannel = 2
}

public enum AccessLogEntryType : byte
{
    /// <summary>Starts the log with its owner and first members.</summary>
    Genesis = 0,
    AddMembers = 1,
    RemoveMembers = 2,
    SetAdmin = 3,
    CreateInvite = 4,
    RevokeInvite = 5,

    /// <summary>Joins through an invite, signed with the secret from the invite link.</summary>
    RedeemInvite = 6,
    TransferOwnership = 7,

    /// <summary>
    /// Accepts a member's new key epoch after they reset their keys. Until
    /// then, their reset devices have no authority and receive no keys.
    /// </summary>
    ConfirmEpoch = 8,

    /// <summary>
    /// Starts the log over. Only the owner can do this, with the keys the log
    /// names for them, because it replaces every earlier admission. After an
    /// <see cref="Open"/> entry it makes the planet private again.
    /// </summary>
    Restart = 9,

    /// <summary>
    /// A snapshot of the log's state, signed by the owner or an admin. Devices
    /// that replay the log check that it matches; a new device can start from
    /// the latest checkpoint instead of replaying every entry.
    /// </summary>
    Checkpoint = 10,

    /// <summary>
    /// Approves automod word and command triggers, signed by an admin. In an
    /// invite-only planet, moderators' apps only compute search-key hashes for
    /// approved trigger text, so the server cannot use automod to learn which
    /// words appear in messages.
    /// </summary>
    ApproveAutomodTriggers = 11,

    /// <summary>
    /// Ends the log's authority over a planet that became public, signed by
    /// the log's owner. Members' apps stop enforcing the log only after they
    /// verify this entry, so the server cannot make a private planet public
    /// on its own. Only the owner's <see cref="Restart"/> or
    /// <see cref="TransferOwnership"/> can follow it, so only the owner can
    /// make the planet private again.
    /// </summary>
    Open = 12
}

/// <summary>
/// An admin's approval of one automod trigger's type and text.
/// </summary>
public readonly record struct AutomodApproval(Guid TriggerId, byte[] Hash)
{
    public static byte[] HashTrigger(int type, string words) =>
        E2eeCrypto.Sha256(new E2eeWriter().WriteMagic("VAA1").WriteInt32(type).WriteString(words ?? string.Empty)
            .ToArray());
}

/// <summary>
/// The complete state of an access log in a canonical encoding: owner,
/// members, admins, invites, and the last restart. Checkpoints carry it, and
/// devices store it so they only fetch newer entries.
/// </summary>
public sealed class AccessLogSnapshot
{
    public AccessMember Owner { get; init; }
    public List<AccessMember> Members { get; init; } = new();
    public List<AccessMember> Admins { get; init; } = new();
    public List<AccessInvite> Invites { get; init; } = new();
    public long LastRestartMs { get; init; }

    /// <summary>The sequence number of the latest removal or restart, or -1.</summary>
    public int LastRemovalSeq { get; init; } = -1;

    public List<AutomodApproval> AutomodApprovals { get; init; } = new();

    public static AccessLogSnapshot FromState(AccessLogState state) => new()
    {
        Owner = state.Owner,
        Members = state.Members.Values.OrderBy(x => x.UserId).ToList(),
        Admins = state.Admins.Values.OrderBy(x => x.UserId).ToList(),
        Invites = state.Invites.Values.OrderBy(x => x.InviteId, StringComparer.Ordinal).ToList(),
        LastRestartMs = state.LastRestartAt is { } restart ? new DateTimeOffset(restart).ToUnixTimeMilliseconds() : 0,
        LastRemovalSeq = state.LastRemovalSeq,
        AutomodApprovals = state.AutomodApprovals.Select(x => new AutomodApproval(x.Key, x.Value))
            .OrderBy(x => x.TriggerId).ToList()
    };

    public void ApplyTo(AccessLogState state)
    {
        state.Owner = Owner;
        state.Members.Clear();
        state.Admins.Clear();
        state.Invites.Clear();
        foreach (var member in Members)
            state.Members[member.UserId] = member;
        foreach (var admin in Admins)
            state.Admins[admin.UserId] = admin;
        foreach (var invite in Invites)
        {
            state.Invites[invite.InviteId] = new AccessInvite
            {
                InviteId = invite.InviteId,
                PublicKey = invite.PublicKey,
                ExpiresMs = invite.ExpiresMs,
                MaxUses = invite.MaxUses,
                CreatedBy = invite.CreatedBy,
                Uses = invite.Uses,
                Revoked = invite.Revoked
            };
        }
        state.LastRestartAt = LastRestartMs == 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(LastRestartMs).UtcDateTime;
        state.LastRemovalSeq = LastRemovalSeq;
        state.AutomodApprovals.Clear();
        foreach (var approval in AutomodApprovals)
            state.AutomodApprovals[approval.TriggerId] = approval.Hash;
    }

    public void Write(E2eeWriter writer)
    {
        AccessMember.Write(writer, Owner);
        WriteMembers(writer, Members);
        WriteMembers(writer, Admins);
        writer.WriteInt32(Invites.Count);
        foreach (var invite in Invites)
        {
            writer.WriteString(invite.InviteId)
                .WriteFixed(invite.PublicKey, E2eeCrypto.KeySize)
                .WriteInt64(invite.ExpiresMs)
                .WriteInt32(invite.MaxUses)
                .WriteInt64(invite.CreatedBy)
                .WriteInt32(invite.Uses)
                .WriteBool(invite.Revoked);
        }
        writer.WriteInt64(LastRestartMs).WriteInt32(LastRemovalSeq);
        WriteApprovals(writer, AutomodApprovals);
    }

    internal static void WriteApprovals(E2eeWriter writer, List<AutomodApproval> approvals)
    {
        writer.WriteInt32(approvals.Count);
        foreach (var approval in approvals)
            writer.WriteFixed(approval.TriggerId.ToByteArray(), 16).WriteFixed(approval.Hash, E2eeCrypto.HashSize);
    }

    internal static List<AutomodApproval> ReadApprovals(E2eeReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > 10_000)
            throw new E2eeFormatException("Invalid automod approval count.");
        var approvals = new List<AutomodApproval>(count);
        for (var i = 0; i < count; i++)
            approvals.Add(new AutomodApproval(new Guid(reader.ReadFixed(16)), reader.ReadFixed(E2eeCrypto.HashSize)));
        return approvals;
    }

    public static AccessLogSnapshot Read(E2eeReader reader)
    {
        var owner = AccessMember.Read(reader);
        var members = ReadMembers(reader);
        var admins = ReadMembers(reader);
        var inviteCount = reader.ReadInt32();
        if (inviteCount < 0 || inviteCount > 100_000)
            throw new E2eeFormatException("Invalid invite count.");
        var invites = new List<AccessInvite>(inviteCount);
        for (var i = 0; i < inviteCount; i++)
        {
            invites.Add(new AccessInvite
            {
                InviteId = reader.ReadString(),
                PublicKey = reader.ReadFixed(E2eeCrypto.KeySize),
                ExpiresMs = reader.ReadInt64(),
                MaxUses = reader.ReadInt32(),
                CreatedBy = reader.ReadInt64(),
                Uses = reader.ReadInt32(),
                Revoked = reader.ReadBool()
            });
        }
        return new AccessLogSnapshot
        {
            Owner = owner,
            Members = members,
            Admins = admins,
            Invites = invites,
            LastRestartMs = reader.ReadInt64(),
            LastRemovalSeq = reader.ReadInt32(),
            AutomodApprovals = ReadApprovals(reader)
        };
    }

    public byte[] Encode()
    {
        var writer = new E2eeWriter().WriteMagic("VAS3");
        Write(writer);
        return writer.ToArray();
    }

    private static void WriteMembers(E2eeWriter writer, List<AccessMember> members)
    {
        writer.WriteInt32(members.Count);
        foreach (var member in members)
            AccessMember.Write(writer, member);
    }

    private static List<AccessMember> ReadMembers(E2eeReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > 1_000_000)
            throw new E2eeFormatException("Invalid member count.");
        var list = new List<AccessMember>(count);
        for (var i = 0; i < count; i++)
            list.Add(AccessMember.Read(reader));
        return list;
    }
}

/// <summary>
/// A user and the keys they were admitted with: their key epoch and the
/// <see cref="UserKeyState.EpochKeyIds">key ID</see> of that epoch's first
/// user key. The key ID binds the admission to one key history, so a server
/// that shows some devices a different key log with the same epoch number
/// does not gain an admitted member.
/// </summary>
public readonly record struct AccessMember(long UserId, int Epoch, byte[] KeyId)
{
    /// <summary>The user's current keys, as their verified key log shows them.</summary>
    public static AccessMember For(UserKeyState state) => new(state.UserId, state.Epoch, state.KeyId);

    /// <summary>
    /// True when this names the given epoch of the user's verified key log,
    /// with the same first user key.
    /// </summary>
    public bool Matches(UserKeyState state, int epoch) =>
        state is not null && state.UserId == UserId && Epoch == epoch &&
        state.EpochKeyIds.TryGetValue(epoch, out var keyId) && E2eeCrypto.FixedTimeEquals(keyId, KeyId);

    /// <summary>True when this names the user's current keys.</summary>
    public bool Matches(UserKeyState state) => state is not null && Matches(state, state.Epoch);

    /// <summary>True when both name the same user, epoch, and key ID.</summary>
    public bool SameKeys(AccessMember other) =>
        UserId == other.UserId && Epoch == other.Epoch && E2eeCrypto.FixedTimeEquals(KeyId, other.KeyId);

    internal static void Write(E2eeWriter writer, AccessMember member) =>
        writer.WriteInt64(member.UserId).WriteInt32(member.Epoch).WriteFixed(member.KeyId, UserKeyState.KeyIdSize);

    internal static AccessMember Read(E2eeReader reader) =>
        new(reader.ReadInt64(), reader.ReadInt32(), reader.ReadFixed(UserKeyState.KeyIdSize));
}

public sealed class AccessLogEntry
{
    public AccessLogScope Scope { get; set; }
    public long ScopeId { get; set; }
    public int Seq { get; set; }
    public byte[] Body { get; set; }
    public byte[] Signature { get; set; }

    public byte[] Hash() => E2eeCrypto.Sha256(Body, Signature);
}

public sealed class AccessLogRecord
{
    public AccessLogScope Scope { get; init; }
    public long ScopeId { get; init; }
    public int Seq { get; init; }
    public byte[] PreviousHash { get; init; }
    public AccessLogEntryType Type { get; init; }
    public long TimestampMs { get; init; }
    public long SignerUserId { get; init; }
    public string SignerDeviceId { get; init; }

    public AccessMember Owner { get; init; }
    public List<AccessMember> Members { get; init; }
    public List<long> UserIds { get; init; }
    public AccessMember Target { get; init; }
    public bool IsAdmin { get; init; }
    public string InviteId { get; init; }
    public byte[] InvitePublicKey { get; init; }
    public long InviteExpiresMs { get; init; }
    public int InviteMaxUses { get; init; }
    public byte[] InviteProof { get; init; }
    public AccessLogSnapshot Snapshot { get; init; }
    public List<AutomodApproval> AutomodApprovals { get; init; }

    public DateTime Timestamp => DateTimeOffset.FromUnixTimeMilliseconds(TimestampMs).UtcDateTime;

    public byte[] Encode()
    {
        var writer = new E2eeWriter()
            .WriteMagic("VAL2")
            .WriteByte((byte)Scope)
            .WriteInt64(ScopeId)
            .WriteInt32(Seq)
            .WriteFixed(PreviousHash, E2eeCrypto.HashSize)
            .WriteByte((byte)Type)
            .WriteInt64(TimestampMs)
            .WriteInt64(SignerUserId)
            .WriteString(SignerDeviceId);

        switch (Type)
        {
            case AccessLogEntryType.Genesis:
            case AccessLogEntryType.Restart:
                WriteMember(writer, Owner);
                WriteMembers(writer, Members);
                break;
            case AccessLogEntryType.AddMembers:
                WriteMembers(writer, Members);
                break;
            case AccessLogEntryType.RemoveMembers:
                writer.WriteInt64List(UserIds);
                break;
            case AccessLogEntryType.SetAdmin:
                WriteMember(writer, Target);
                writer.WriteBool(IsAdmin);
                break;
            case AccessLogEntryType.CreateInvite:
                writer.WriteString(InviteId)
                    .WriteFixed(InvitePublicKey, E2eeCrypto.KeySize)
                    .WriteInt64(InviteExpiresMs)
                    .WriteInt32(InviteMaxUses);
                break;
            case AccessLogEntryType.RevokeInvite:
                writer.WriteString(InviteId);
                break;
            case AccessLogEntryType.RedeemInvite:
                writer.WriteString(InviteId);
                WriteMember(writer, Target);
                writer.WriteFixed(InviteProof, E2eeCrypto.SignatureSize);
                break;
            case AccessLogEntryType.TransferOwnership:
            case AccessLogEntryType.ConfirmEpoch:
                WriteMember(writer, Target);
                break;
            case AccessLogEntryType.Checkpoint:
                Snapshot.Write(writer);
                break;
            case AccessLogEntryType.ApproveAutomodTriggers:
                AccessLogSnapshot.WriteApprovals(writer, AutomodApprovals);
                break;
            case AccessLogEntryType.Open:
                break;
            default:
                throw new E2eeFormatException("Unknown access log entry type.");
        }

        return writer.ToArray();
    }

    public static AccessLogRecord Decode(byte[] body)
    {
        var reader = new E2eeReader(body);
        reader.ReadMagic("VAL2");
        var scope = (AccessLogScope)reader.ReadByte();
        var scopeId = reader.ReadInt64();
        var seq = reader.ReadInt32();
        var previousHash = reader.ReadFixed(E2eeCrypto.HashSize);
        var type = (AccessLogEntryType)reader.ReadByte();
        var timestamp = reader.ReadInt64();
        var signerUserId = reader.ReadInt64();
        var signerDeviceId = reader.ReadString();

        AccessMember owner = default, target = default;
        List<AccessMember> members = null;
        List<long> userIds = null;
        bool isAdmin = false;
        string inviteId = null;
        byte[] invitePublicKey = null, inviteProof = null;
        long inviteExpires = 0;
        int inviteMaxUses = 0;
        AccessLogSnapshot snapshot = null;
        List<AutomodApproval> approvals = null;

        switch (type)
        {
            case AccessLogEntryType.Genesis:
            case AccessLogEntryType.Restart:
                owner = ReadMember(reader);
                members = ReadMembers(reader);
                break;
            case AccessLogEntryType.AddMembers:
                members = ReadMembers(reader);
                break;
            case AccessLogEntryType.RemoveMembers:
                userIds = reader.ReadInt64List();
                break;
            case AccessLogEntryType.SetAdmin:
                target = ReadMember(reader);
                isAdmin = reader.ReadBool();
                break;
            case AccessLogEntryType.CreateInvite:
                inviteId = reader.ReadString();
                invitePublicKey = reader.ReadFixed(E2eeCrypto.KeySize);
                inviteExpires = reader.ReadInt64();
                inviteMaxUses = reader.ReadInt32();
                break;
            case AccessLogEntryType.RevokeInvite:
                inviteId = reader.ReadString();
                break;
            case AccessLogEntryType.RedeemInvite:
                inviteId = reader.ReadString();
                target = ReadMember(reader);
                inviteProof = reader.ReadFixed(E2eeCrypto.SignatureSize);
                break;
            case AccessLogEntryType.TransferOwnership:
            case AccessLogEntryType.ConfirmEpoch:
                target = ReadMember(reader);
                break;
            case AccessLogEntryType.Checkpoint:
                snapshot = AccessLogSnapshot.Read(reader);
                break;
            case AccessLogEntryType.ApproveAutomodTriggers:
                approvals = AccessLogSnapshot.ReadApprovals(reader);
                break;
            case AccessLogEntryType.Open:
                break;
            default:
                throw new E2eeFormatException("Unknown access log entry type.");
        }

        reader.EnsureEnd();

        return new AccessLogRecord
        {
            Scope = scope,
            ScopeId = scopeId,
            Seq = seq,
            PreviousHash = previousHash,
            Type = type,
            TimestampMs = timestamp,
            SignerUserId = signerUserId,
            SignerDeviceId = signerDeviceId,
            Owner = owner,
            Members = members,
            UserIds = userIds,
            Target = target,
            IsAdmin = isAdmin,
            InviteId = inviteId,
            InvitePublicKey = invitePublicKey,
            InviteExpiresMs = inviteExpires,
            InviteMaxUses = inviteMaxUses,
            InviteProof = inviteProof,
            Snapshot = snapshot,
            AutomodApprovals = approvals
        };
    }

    private static void WriteMember(E2eeWriter writer, AccessMember member) => AccessMember.Write(writer, member);

    private static AccessMember ReadMember(E2eeReader reader) => AccessMember.Read(reader);

    private static void WriteMembers(E2eeWriter writer, List<AccessMember> members)
    {
        writer.WriteInt32(members.Count);
        foreach (var member in members)
            WriteMember(writer, member);
    }

    private static List<AccessMember> ReadMembers(E2eeReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0 || count > 100_000)
            throw new E2eeFormatException("Invalid member count.");
        var list = new List<AccessMember>(count);
        for (var i = 0; i < count; i++)
            list.Add(ReadMember(reader));
        return list;
    }

    public static byte[] InviteProofMessage(AccessLogScope scope, long scopeId, string inviteId, long userId,
        string deviceId) =>
        new E2eeWriter()
            .WriteMagic("VIR1")
            .WriteByte((byte)scope)
            .WriteInt64(scopeId)
            .WriteString(inviteId)
            .WriteInt64(userId)
            .WriteString(deviceId)
            .ToArray();
}

public sealed class AccessInvite
{
    public string InviteId { get; init; }
    public byte[] PublicKey { get; init; }
    public long ExpiresMs { get; init; }
    public int MaxUses { get; init; }
    public long CreatedBy { get; init; }
    public int Uses { get; set; }
    public bool Revoked { get; set; }

    public bool IsUsable(long atMs) =>
        !Revoked && (ExpiresMs == 0 || atMs < ExpiresMs) && (MaxUses == 0 || Uses < MaxUses);
}

/// <summary>
/// The verified state of an access log.
/// </summary>
public sealed class AccessLogState
{
    public AccessLogScope Scope { get; init; }
    public long ScopeId { get; init; }
    public int HeadSeq { get; internal set; } = -1;
    public byte[] HeadHash { get; internal set; } = new byte[E2eeCrypto.HashSize];
    public AccessMember Owner { get; internal set; }
    public Dictionary<long, AccessMember> Members { get; } = new();
    public Dictionary<long, AccessMember> Admins { get; } = new();
    public Dictionary<string, AccessInvite> Invites { get; } = new();
    public DateTime? LastRestartAt { get; internal set; }

    /// <summary>The time the newest entry was signed. Entry times never go backwards.</summary>
    public long HeadTimestampMs { get; internal set; }

    /// <summary>
    /// The sequence number of the latest entry that removed members or
    /// restarted the log, or -1. A channel key made before it may be held by
    /// someone who lost access, so members replace it before sending.
    /// </summary>
    public int LastRemovalSeq { get; internal set; } = -1;

    /// <summary>
    /// Automod triggers an admin approved, with a hash of the approved type
    /// and text. See <see cref="AccessLogEntryType.ApproveAutomodTriggers"/>.
    /// </summary>
    public Dictionary<Guid, byte[]> AutomodApprovals { get; } = new();

    /// <summary>True when an admin approved exactly this trigger type and text.</summary>
    public bool IsAutomodTriggerApproved(Guid triggerId, int type, string words) =>
        AutomodApprovals.TryGetValue(triggerId, out var hash) &&
        E2eeCrypto.FixedTimeEquals(hash, AutomodApproval.HashTrigger(type, words));

    /// <summary>The sequence number of the latest checkpoint, or -1 when there is none.</summary>
    public int LastCheckpointSeq { get; internal set; } = -1;

    /// <summary>
    /// The sequence number of the <see cref="AccessLogEntryType.Open"/> entry
    /// that made the planet public, or -1 while the log governs it. A
    /// <see cref="AccessLogEntryType.Restart"/> sets it back to -1.
    /// </summary>
    public int OpenedSeq { get; internal set; } = -1;

    public bool IsStarted => HeadSeq >= 0;

    /// <summary>
    /// True when the owner made the planet public with a signed
    /// <see cref="AccessLogEntryType.Open"/> entry. The log then admits
    /// nobody, and the planet's channels follow its permissions. It still
    /// names its owner, who alone can make the planet private again.
    /// </summary>
    public bool IsOpen => OpenedSeq >= 0;

    /// <summary>True when the log has started and decides who receives keys.</summary>
    public bool IsGoverning => IsStarted && !IsOpen;

    /// <summary>
    /// Encodes the verified state so a device can store it and later fetch
    /// only newer entries.
    /// </summary>
    public byte[] Encode()
    {
        var writer = new E2eeWriter()
            .WriteMagic(StateMagic)
            .WriteByte((byte)Scope)
            .WriteInt64(ScopeId)
            .WriteInt32(HeadSeq)
            .WriteFixed(HeadHash, E2eeCrypto.HashSize)
            .WriteInt64(HeadTimestampMs)
            .WriteInt32(LastCheckpointSeq)
            .WriteInt32(OpenedSeq);
        AccessLogSnapshot.FromState(this).Write(writer);
        return writer.ToArray();
    }

    private const string StateMagic = "VAT4";

    // States devices stored before the log could make a planet public.
    private const string GoverningStateMagic = "VAT3";

    /// <summary>
    /// Decodes a stored state. States stored before planets could be made
    /// public again have no <see cref="OpenedSeq"/> and are read as governing.
    /// </summary>
    public static AccessLogState Decode(byte[] data)
    {
        var reader = new E2eeReader(data);
        var withOpenedSeq = data is { Length: >= 4 } && data.AsSpan(0, 4).SequenceEqual("VAT4"u8);
        reader.ReadMagic(withOpenedSeq ? StateMagic : GoverningStateMagic);
        var state = new AccessLogState
        {
            Scope = (AccessLogScope)reader.ReadByte(),
            ScopeId = reader.ReadInt64(),
            HeadSeq = reader.ReadInt32(),
            HeadHash = reader.ReadFixed(E2eeCrypto.HashSize),
            HeadTimestampMs = reader.ReadInt64(),
            LastCheckpointSeq = reader.ReadInt32(),
            OpenedSeq = withOpenedSeq ? reader.ReadInt32() : -1
        };
        if (state.OpenedSeq < -1 || state.OpenedSeq > state.HeadSeq)
            throw new E2eeFormatException("Invalid stored access log state.");
        AccessLogSnapshot.Read(reader).ApplyTo(state);
        reader.EnsureEnd();
        return state;
    }

    /// <summary>
    /// True when the log admits the user's current keys: the admitted epoch
    /// is the current epoch in the user's verified key log, and its key ID
    /// matches. A member who reset their keys needs a
    /// <see cref="AccessLogEntryType.ConfirmEpoch"/> entry before they count
    /// as admitted again.
    /// </summary>
    public bool IsMember(long userId, UserKeyState keys) =>
        Members.TryGetValue(userId, out var member) && member.Matches(keys);

    /// <summary>
    /// True when a device from <paramref name="deviceEpoch"/> may act for an
    /// admitted member: the log admitted that epoch or a later one, and the
    /// admitted epoch's key ID matches the user's verified key log. Channel
    /// keys a member's device created are trusted on this basis.
    /// </summary>
    public bool AdmitsDevice(long userId, UserKeyState keys, int deviceEpoch) =>
        Members.TryGetValue(userId, out var member) && member.Epoch >= deviceEpoch &&
        member.Matches(keys, member.Epoch);

    /// <summary>
    /// True when the log lists the user with any keys. Use it to decide who
    /// to remove or who is waiting for a confirmation, never to share keys.
    /// </summary>
    public bool IsMemberAnyEpoch(long userId) => Members.ContainsKey(userId);

    /// <summary>True when the user's current keys are the owner's or an admin's.</summary>
    public bool IsAdmin(long userId, UserKeyState keys) =>
        IsOwner(userId, keys) || (Admins.TryGetValue(userId, out var admin) && admin.Matches(keys));

    /// <summary>True when the user's current keys are the owner's.</summary>
    public bool IsOwner(long userId, UserKeyState keys) => Owner.UserId == userId && Owner.Matches(keys);

    // Role checks for a signer, named by the keys of the device that signed.

    internal bool HasMember(AccessMember signer) =>
        Members.TryGetValue(signer.UserId, out var member) && member.SameKeys(signer);

    internal bool HasOwner(AccessMember signer) => Owner.SameKeys(signer);

    internal bool HasAdmin(AccessMember signer) =>
        HasOwner(signer) || (Admins.TryGetValue(signer.UserId, out var admin) && admin.SameKeys(signer));
}

/// <summary>
/// Replays and verifies an access log. Every signer's device, key epoch, and
/// key ID are checked against that signer's verified key log. Keys the signer
/// names for other members cannot be checked here, because the verifier only
/// has the signers' key logs; devices compare them with each member's key log
/// when they decide whom to trust.
/// </summary>
public static class AccessLogVerifier
{
    /// <summary>
    /// Users whose key logs are needed to verify the given entries.
    /// </summary>
    public static HashSet<long> RequiredUsers(IEnumerable<AccessLogEntry> entries)
    {
        var users = new HashSet<long>();
        foreach (var entry in entries)
            users.Add(AccessLogRecord.Decode(entry.Body).SignerUserId);
        return users;
    }

    /// <summary>
    /// Verifies entries from the start of the log, or from a checkpoint when
    /// the first entry is one. Starting from a checkpoint trusts the owner or
    /// admin who signed it for the state before it, the way starting from the
    /// first entry trusts whoever created the log. Devices that already follow
    /// the log check every checkpoint against their own replay.
    /// </summary>
    public static AccessLogState Verify(AccessLogScope scope, long scopeId, IReadOnlyList<AccessLogEntry> entries,
        IReadOnlyDictionary<long, UserKeyState> userStates)
    {
        var state = new AccessLogState { Scope = scope, ScopeId = scopeId };
        var start = 0;
        if (entries.Count > 0 && entries[0].Seq > 0)
        {
            StartFromCheckpoint(state, entries[0], userStates);
            start = 1;
        }

        for (var i = start; i < entries.Count; i++)
            Apply(state, entries[i], userStates);
        return state;
    }

    private static void StartFromCheckpoint(AccessLogState state, AccessLogEntry entry,
        IReadOnlyDictionary<long, UserKeyState> userStates)
    {
        var record = AccessLogRecord.Decode(entry.Body);
        if (record.Type != AccessLogEntryType.Checkpoint || record.Snapshot is null)
            throw new E2eeVerificationException("A partial access log must start at a checkpoint.");
        if (record.Scope != state.Scope || record.ScopeId != state.ScopeId ||
            entry.Scope != state.Scope || entry.ScopeId != state.ScopeId || entry.Seq != record.Seq)
            throw new E2eeVerificationException("Access log entry belongs to another scope.");

        var signer = VerifySignature(record, entry, userStates);
        if (!CountsSignature(record, userStates))
            throw new E2eeVerificationException("The checkpoint was signed by a device after it was removed.");
        record.Snapshot.ApplyTo(state);
        if (!state.HasAdmin(signer))
            throw new E2eeVerificationException("Only the owner or an admin can sign a checkpoint.");

        state.HeadSeq = record.Seq;
        state.HeadHash = entry.Hash();
        state.HeadTimestampMs = record.TimestampMs;
        state.LastCheckpointSeq = record.Seq;
    }

    /// <summary>
    /// Checks the signature and returns the signer as a member: their user ID
    /// and the epoch and key ID of the device that signed.
    /// </summary>
    private static AccessMember VerifySignature(AccessLogRecord record, AccessLogEntry entry,
        IReadOnlyDictionary<long, UserKeyState> userStates)
    {
        if (!userStates.TryGetValue(record.SignerUserId, out var signerState) || signerState is null)
            throw new E2eeVerificationException("Access log signer's keys are unavailable.");
        var device = signerState.GetDeviceAt(record.SignerDeviceId, record.Timestamp);
        if (device is null || !E2eeCrypto.Verify(device.SignPublicKey, entry.Body, entry.Signature))
            throw new E2eeVerificationException("Access log entry signature is invalid.");
        var epoch = signerState.EverDevices[record.SignerDeviceId].Epoch;
        if (!signerState.EpochKeyIds.TryGetValue(epoch, out var keyId))
            throw new E2eeVerificationException("Access log signer's keys are unavailable.");
        return new AccessMember(record.SignerUserId, epoch, keyId);
    }

    /// <summary>
    /// False when a partial log starts at a checkpoint that a removed device
    /// signed after its cutoff. Such a checkpoint cannot start the log, so the
    /// caller reads the whole log instead.
    /// </summary>
    public static bool CanStartFrom(List<AccessLogEntry> entries, IReadOnlyDictionary<long, UserKeyState> userStates)
    {
        if (entries.Count == 0 || entries[0].Seq == 0)
            return true;

        var record = AccessLogRecord.Decode(entries[0].Body);
        return !userStates.TryGetValue(record.SignerUserId, out var signer) || signer is null ||
               signer.CountsAccessLogSignature(record.SignerDeviceId, record.Scope, record.ScopeId, record.Seq);
    }

    private static bool CountsSignature(AccessLogRecord record, IReadOnlyDictionary<long, UserKeyState> userStates) =>
        userStates[record.SignerUserId].CountsAccessLogSignature(record.SignerDeviceId, record.Scope, record.ScopeId,
            record.Seq);

    public static AccessLogRecord Apply(AccessLogState state, AccessLogEntry entry,
        IReadOnlyDictionary<long, UserKeyState> userStates)
    {
        if (entry?.Body is null || entry.Signature is null)
            throw new E2eeFormatException("Missing access log entry.");

        var record = AccessLogRecord.Decode(entry.Body);
        if (record.Scope != state.Scope || record.ScopeId != state.ScopeId ||
            entry.Scope != state.Scope || entry.ScopeId != state.ScopeId)
            throw new E2eeVerificationException("Access log entry belongs to another scope.");
        if (record.Seq != state.HeadSeq + 1 || entry.Seq != record.Seq)
            throw new E2eeVerificationException("Access log entry is out of order.");
        if (!E2eeCrypto.FixedTimeEquals(record.PreviousHash, state.HeadHash))
            throw new E2eeVerificationException("Access log entry does not follow the previous entry.");

        // Times never go backwards, so an entry cannot be dated before the
        // one it follows, for example to redeem an expired invite.
        if (state.IsStarted && record.TimestampMs < state.HeadTimestampMs)
            throw new E2eeVerificationException("Access log entry is dated before the previous entry.");

        var signerMember = VerifySignature(record, entry, userStates);
        var signer = record.SignerUserId;

        // An entry a removed device signed after the point its account
        // recorded for this log stays in the chain but has no effect, so a
        // copy of the device's keys cannot let anyone in by dating an entry
        // before the removal.
        if (!CountsSignature(record, userStates))
        {
            state.HeadSeq = record.Seq;
            state.HeadHash = entry.Hash();
            state.HeadTimestampMs = record.TimestampMs;
            return record;
        }

        if (!state.IsStarted && record.Type != AccessLogEntryType.Genesis)
            throw new E2eeVerificationException("Access log must start with a genesis entry.");

        // A log the owner opened admits nobody. It changes no further until
        // its owner makes the planet private again or hands the log to a new
        // owner along with the planet.
        if (state.IsOpen && record.Type is not (AccessLogEntryType.Restart or AccessLogEntryType.TransferOwnership))
            throw new E2eeVerificationException("The planet is public, so only its owner can change its membership log.");

        switch (record.Type)
        {
            case AccessLogEntryType.Genesis:
            case AccessLogEntryType.Restart:
                if (record.Type == AccessLogEntryType.Genesis && record.Seq != 0)
                    throw new E2eeVerificationException("Only the first entry can be a genesis entry.");
                if (record.Type == AccessLogEntryType.Restart && !state.IsStarted)
                    throw new E2eeVerificationException("A restart must follow an existing entry.");
                if (!record.Owner.SameKeys(signerMember))
                    throw new E2eeVerificationException("The log owner must sign its first entry.");

                // Only the current owner may start over, including after the
                // planet was made public. Otherwise the server could name an
                // account it controls as the planet's owner and have it
                // restart the log, admitting itself to later messages.
                if (record.Type == AccessLogEntryType.Restart && !state.HasOwner(signerMember))
                    throw new E2eeVerificationException("Only the log's owner can restart it.");
                state.Owner = record.Owner;
                state.Members.Clear();
                state.Admins.Clear();
                state.Invites.Clear();
                foreach (var member in record.Members)
                    state.Members[member.UserId] = member;
                state.Members[record.Owner.UserId] = record.Owner;
                state.OpenedSeq = -1;
                if (record.Type == AccessLogEntryType.Restart)
                {
                    state.LastRestartAt = record.Timestamp;
                    state.LastRemovalSeq = record.Seq;
                }
                break;

            case AccessLogEntryType.Open:
                RequirePlanet(state);
                if (!state.HasOwner(signerMember))
                    throw new E2eeVerificationException("Only the log's owner can make the planet public.");
                state.Members.Clear();
                state.Admins.Clear();
                state.Invites.Clear();
                state.OpenedSeq = record.Seq;
                break;

            case AccessLogEntryType.AddMembers:
                RequireAddAuthority(state, signerMember);
                foreach (var member in record.Members)
                    state.Members[member.UserId] = member;
                break;

            case AccessLogEntryType.RemoveMembers:
                var removingSelfOnly = record.UserIds.Count == 1 && record.UserIds[0] == signer;
                if (!removingSelfOnly)
                    RequireRemoveAuthority(state, signerMember);
                if (record.UserIds.Contains(state.Owner.UserId))
                    throw new E2eeVerificationException("The owner cannot be removed.");
                foreach (var userId in record.UserIds)
                {
                    state.Members.Remove(userId);
                    state.Admins.Remove(userId);
                }
                state.LastRemovalSeq = record.Seq;
                break;

            case AccessLogEntryType.SetAdmin:
                RequirePlanet(state);
                if (!state.HasOwner(signerMember))
                    throw new E2eeVerificationException("Only the owner can change admins.");
                if (record.IsAdmin)
                {
                    if (!state.Members.TryGetValue(record.Target.UserId, out var adminMember) ||
                        !adminMember.SameKeys(record.Target))
                        throw new E2eeVerificationException("Admins must be members with the keys they were admitted with.");
                    state.Admins[record.Target.UserId] = record.Target;
                }
                else
                {
                    state.Admins.Remove(record.Target.UserId);
                }
                break;

            case AccessLogEntryType.CreateInvite:
                RequirePlanet(state);
                if (!state.HasAdmin(signerMember))
                    throw new E2eeVerificationException("Only admins can create invites.");
                if (string.IsNullOrWhiteSpace(record.InviteId) || record.InviteId.Length > 64 ||
                    state.Invites.ContainsKey(record.InviteId))
                    throw new E2eeVerificationException("Invalid invite.");
                state.Invites[record.InviteId] = new AccessInvite
                {
                    InviteId = record.InviteId,
                    PublicKey = record.InvitePublicKey,
                    ExpiresMs = record.InviteExpiresMs,
                    MaxUses = record.InviteMaxUses,
                    CreatedBy = signer
                };
                break;

            case AccessLogEntryType.RevokeInvite:
                RequirePlanet(state);
                if (!state.HasAdmin(signerMember))
                    throw new E2eeVerificationException("Only admins can revoke invites.");
                if (!state.Invites.TryGetValue(record.InviteId ?? string.Empty, out var revoked))
                    throw new E2eeVerificationException("Unknown invite.");
                revoked.Revoked = true;
                break;

            case AccessLogEntryType.RedeemInvite:
                RequirePlanet(state);
                if (!state.Invites.TryGetValue(record.InviteId ?? string.Empty, out var invite) ||
                    !invite.IsUsable(record.TimestampMs))
                    throw new E2eeVerificationException("Invite is not usable.");
                if (!record.Target.SameKeys(signerMember))
                    throw new E2eeVerificationException("An invite must be redeemed by the joining user.");
                var proofMessage = AccessLogRecord.InviteProofMessage(state.Scope, state.ScopeId, record.InviteId,
                    signer, record.SignerDeviceId);
                if (!E2eeCrypto.Verify(invite.PublicKey, proofMessage, record.InviteProof))
                    throw new E2eeVerificationException("Invite proof is invalid.");
                invite.Uses++;
                state.Members[signer] = signerMember;
                break;

            case AccessLogEntryType.TransferOwnership:
                if (!state.HasOwner(signerMember))
                    throw new E2eeVerificationException("Only the owner can transfer ownership.");

                // An opened log has no members; the entry names the new
                // owner's keys, which devices check against their key log.
                if (!state.IsOpen && !state.HasMember(record.Target))
                    throw new E2eeVerificationException("The new owner must be a member with the keys they were admitted with.");
                state.Owner = record.Target;
                state.Admins.Remove(record.Target.UserId);
                break;

            case AccessLogEntryType.Checkpoint:
                if (!state.HasAdmin(signerMember))
                    throw new E2eeVerificationException("Only the owner or an admin can sign a checkpoint.");
                if (record.Snapshot is null ||
                    !record.Snapshot.Encode().AsSpan().SequenceEqual(AccessLogSnapshot.FromState(state).Encode()))
                    throw new E2eeVerificationException("The checkpoint does not match the log.");
                state.LastCheckpointSeq = record.Seq;
                break;

            case AccessLogEntryType.ApproveAutomodTriggers:
                RequirePlanet(state);
                if (!state.HasAdmin(signerMember))
                    throw new E2eeVerificationException("Only admins can approve automod triggers.");
                foreach (var approval in record.AutomodApprovals)
                {
                    if (approval.Hash?.Length != E2eeCrypto.HashSize)
                        throw new E2eeFormatException("Invalid automod approval.");
                    state.AutomodApprovals[approval.TriggerId] = approval.Hash;
                }
                break;

            case AccessLogEntryType.ConfirmEpoch:
                if (record.Target.UserId == signer)
                    throw new E2eeVerificationException("A member cannot confirm their own new keys.");
                RequireAddAuthority(state, signerMember);
                if (!state.Members.ContainsKey(record.Target.UserId))
                    throw new E2eeVerificationException("Only members can be confirmed.");
                state.Members[record.Target.UserId] = record.Target;
                if (state.Admins.ContainsKey(record.Target.UserId))
                    state.Admins[record.Target.UserId] = record.Target;
                if (state.Owner.UserId == record.Target.UserId)
                    state.Owner = record.Target;
                break;

            default:
                throw new E2eeFormatException("Unknown access log entry type.");
        }

        state.HeadSeq = record.Seq;
        state.HeadHash = entry.Hash();
        state.HeadTimestampMs = record.TimestampMs;
        return record;
    }

    private static void RequirePlanet(AccessLogState state)
    {
        if (state.Scope != AccessLogScope.Planet)
            throw new E2eeVerificationException("This entry is only valid for planets.");
    }

    /// <summary>
    /// In a group DM any member can add people. In a planet only admins can.
    /// </summary>
    private static void RequireAddAuthority(AccessLogState state, AccessMember signer)
    {
        var allowed = state.Scope == AccessLogScope.GroupChannel
            ? state.HasMember(signer)
            : state.HasAdmin(signer);
        if (!allowed)
            throw new E2eeVerificationException("Signer cannot admit members.");
    }

    /// <summary>
    /// In a group DM only the owner removes others. In a planet admins can.
    /// </summary>
    private static void RequireRemoveAuthority(AccessLogState state, AccessMember signer)
    {
        var allowed = state.Scope == AccessLogScope.GroupChannel
            ? state.HasOwner(signer)
            : state.HasAdmin(signer);
        if (!allowed)
            throw new E2eeVerificationException("Signer cannot remove members.");
    }
}

/// <summary>
/// Creates signed access log entries.
/// </summary>
public static class AccessLogBuilder
{
    public static AccessLogEntry Create(AccessLogState state, AccessLogEntryType type, long signerUserId,
        DeviceKeyPair device, long timestampMs, Func<AccessLogRecord, AccessLogRecord> fill)
    {
        var record = fill(new AccessLogRecord
        {
            Scope = state.Scope,
            ScopeId = state.ScopeId,
            Seq = state.HeadSeq + 1,
            PreviousHash = state.HeadHash,
            Type = type,
            // Entry times never go backwards, even when this device's clock
            // is behind the one that signed the previous entry.
            TimestampMs = Math.Max(timestampMs, state.HeadTimestampMs),
            SignerUserId = signerUserId,
            SignerDeviceId = device.DeviceId
        });

        var body = record.Encode();
        return new AccessLogEntry
        {
            Scope = state.Scope,
            ScopeId = state.ScopeId,
            Seq = record.Seq,
            Body = body,
            Signature = device.Sign(body)
        };
    }

    public static AccessLogRecord With(this AccessLogRecord record, AccessMember? owner = null,
        List<AccessMember> members = null, List<long> userIds = null, AccessMember? target = null,
        bool isAdmin = false, string inviteId = null, byte[] invitePublicKey = null, long inviteExpiresMs = 0,
        int inviteMaxUses = 0, byte[] inviteProof = null, AccessLogSnapshot snapshot = null,
        List<AutomodApproval> automodApprovals = null) => new()
    {
        Scope = record.Scope,
        ScopeId = record.ScopeId,
        Seq = record.Seq,
        PreviousHash = record.PreviousHash,
        Type = record.Type,
        TimestampMs = record.TimestampMs,
        SignerUserId = record.SignerUserId,
        SignerDeviceId = record.SignerDeviceId,
        Owner = owner ?? record.Owner,
        Members = members ?? record.Members,
        UserIds = userIds ?? record.UserIds,
        Target = target ?? record.Target,
        IsAdmin = isAdmin,
        InviteId = inviteId ?? record.InviteId,
        InvitePublicKey = invitePublicKey ?? record.InvitePublicKey,
        InviteExpiresMs = inviteExpiresMs,
        InviteMaxUses = inviteMaxUses,
        InviteProof = inviteProof ?? record.InviteProof,
        Snapshot = snapshot ?? record.Snapshot,
        AutomodApprovals = automodApprovals ?? record.AutomodApprovals
    };

    /// <summary>
    /// Derives an invite's signing key from the secret carried in the invite
    /// link's fragment, which browsers never send to the server.
    /// </summary>
    public static (byte[] Seed, byte[] PublicKey) InviteKey(byte[] secret)
    {
        var seed = E2eeCrypto.Hkdf(secret, null, "valour-e2ee/invite/v1");
        return (seed, E2eeCrypto.Ed25519PublicKey(seed));
    }
}
