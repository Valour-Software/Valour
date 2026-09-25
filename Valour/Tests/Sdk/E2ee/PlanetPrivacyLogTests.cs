using Valour.Sdk.E2ee;

namespace Valour.Tests.Sdk.E2ee;

/// <summary>
/// The membership log entries that make a private planet public and private
/// again. Only the log's owner can open it, an opened log admits nobody, and
/// only the owner's restart or ownership transfer can follow it.
/// </summary>
public class PlanetPrivacyLogTests
{
    private const long PlanetId = 900;

    private sealed record Log(AccessLogState State, List<AccessLogEntry> Entries, Dictionary<long, UserKeyState> States)
    {
        public void Append(AccessLogEntry entry)
        {
            AccessLogVerifier.Apply(State, entry, States);
            Entries.Add(entry);
        }

        /// <summary>Applies an entry to a copy of the state, leaving the log unchanged.</summary>
        public void Refuses(AccessLogEntry entry, Dictionary<long, UserKeyState> states = null) =>
            Assert.Throws<E2eeVerificationException>(() =>
                AccessLogVerifier.Apply(AccessLogState.Decode(State.Encode()), entry, states ?? States));
    }

    private static Log StartPrivate(Person owner, params Person[] others)
    {
        var states = new Dictionary<long, UserKeyState> { [owner.UserId] = owner.State };
        foreach (var person in others)
            states[person.UserId] = person.State;

        var log = new Log(new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = PlanetId }, [], states);
        log.Append(AccessLogBuilder.Create(log.State, AccessLogEntryType.Genesis, owner.UserId, owner.Device, 2000,
            r => r.With(owner: owner.Member, members: others.Select(p => p.Member).ToList())));
        return log;
    }

    private static AccessLogEntry Open(AccessLogState state, long signerUserId, DeviceKeyPair device) =>
        AccessLogBuilder.Create(state, AccessLogEntryType.Open, signerUserId, device, 3000, r => r);

    [Fact]
    public void OwnerSignedOpen_EndsGovernanceAndAdmitsNobody()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var log = StartPrivate(owner, member);
        Assert.True(log.State.IsGoverning);
        Assert.True(log.State.IsMember(member.UserId, member.State));

        log.Append(Open(log.State, owner.UserId, owner.Device));

        Assert.True(log.State.IsOpen);
        Assert.False(log.State.IsGoverning);
        Assert.Equal(1, log.State.OpenedSeq);
        Assert.Empty(log.State.Members);
        Assert.False(log.State.IsMember(member.UserId, member.State));
        Assert.False(log.State.IsMember(owner.UserId, owner.State));

        // A device's stored state, and a device that replays the whole log,
        // reach the same state.
        Assert.Equal(1, AccessLogState.Decode(log.State.Encode()).OpenedSeq);
        var replayed = AccessLogVerifier.Verify(AccessLogScope.Planet, PlanetId, log.Entries, log.States);
        Assert.Equal(log.State.Encode(), replayed.Encode());
    }

    [Fact]
    public void Open_SignedByAnyoneButTheOwnerIsRefused()
    {
        var owner = Person.Create(1);
        var admin = Person.Create(2);
        var member = Person.Create(3);
        var log = StartPrivate(owner, admin, member);
        log.Append(AccessLogBuilder.Create(log.State, AccessLogEntryType.SetAdmin, owner.UserId, owner.Device, 2500,
            r => r.With(target: admin.Member, isAdmin: true)));

        log.Refuses(Open(log.State, admin.UserId, admin.Device));
        log.Refuses(Open(log.State, member.UserId, member.Device));

        // The owner's entry with someone else's signature, as a server
        // forging it would have to send.
        var genuine = Open(log.State, owner.UserId, owner.Device);
        log.Refuses(new AccessLogEntry
        {
            Scope = genuine.Scope,
            ScopeId = genuine.ScopeId,
            Seq = genuine.Seq,
            Body = genuine.Body,
            Signature = member.Device.Sign(genuine.Body)
        });

        // Keys from a reset nobody confirmed, which is what the server would
        // make to act as the owner, cannot open the log either.
        var resetDevice = DeviceKeyPair.Generate();
        var reset = UserKeyLogBuilder.Reset(owner.State, resetDevice, "Reset", UserKeyPair.Generate(2), null, 2600);
        var withReset = new Dictionary<long, UserKeyState>(log.States)
        {
            [owner.UserId] = UserKeyLogVerifier.Verify(owner.UserId, [owner.Genesis, reset])
        };
        log.Refuses(Open(log.State, owner.UserId, resetDevice), withReset);

        // Group chats are never opened.
        var group = new AccessLogState { Scope = AccessLogScope.GroupChannel, ScopeId = 901 };
        AccessLogVerifier.Apply(group, AccessLogBuilder.Create(group, AccessLogEntryType.Genesis, owner.UserId,
            owner.Device, 2000, r => r.With(owner: owner.Member, members: [member.Member])), log.States);
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(group, Open(group, owner.UserId, owner.Device), log.States));

        log.Append(genuine);
        Assert.True(log.State.IsOpen);
    }

    [Fact]
    public void AfterOpen_OnlyTheOwnersRestartMakesItPrivateAgain()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var puppet = Person.Create(3);
        var log = StartPrivate(owner, member);
        log.States[puppet.UserId] = puppet.State;
        log.Append(Open(log.State, owner.UserId, owner.Device));
        Assert.True(log.State.Owner.SameKeys(owner.Member));

        log.Refuses(AccessLogBuilder.Create(log.State, AccessLogEntryType.AddMembers, owner.UserId, owner.Device,
            4000, r => r.With(members: [member.Member])));
        log.Refuses(AccessLogBuilder.Create(log.State, AccessLogEntryType.Checkpoint, owner.UserId, owner.Device,
            4000, r => r.With(snapshot: AccessLogSnapshot.FromState(log.State))));
        log.Refuses(Open(log.State, owner.UserId, owner.Device));

        // An account the server names as the planet's owner cannot make it
        // private again and admit itself to later messages.
        log.Refuses(AccessLogBuilder.Create(log.State, AccessLogEntryType.Restart, puppet.UserId, puppet.Device,
            5000, r => r.With(owner: puppet.Member, members: [puppet.Member])));

        log.Append(AccessLogBuilder.Create(log.State, AccessLogEntryType.Restart, owner.UserId, owner.Device, 5000,
            r => r.With(owner: owner.Member, members: [member.Member])));

        Assert.True(log.State.IsGoverning);
        Assert.Equal(-1, log.State.OpenedSeq);
        Assert.Equal(2, log.State.LastRemovalSeq);
        Assert.True(log.State.IsOwner(owner.UserId, owner.State));
        Assert.True(log.State.IsMember(member.UserId, member.State));
        Assert.False(log.State.IsMember(puppet.UserId, puppet.State));

        var replayed = AccessLogVerifier.Verify(AccessLogScope.Planet, PlanetId, log.Entries, log.States);
        Assert.Equal(log.State.Encode(), replayed.Encode());
    }

    [Fact]
    public void AfterOpen_TheOwnerHandsTheLogToANewOwnerWhoCanMakeItPrivate()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var newOwner = Person.Create(3);
        var log = StartPrivate(owner, member);
        log.States[newOwner.UserId] = newOwner.State;
        log.Append(Open(log.State, owner.UserId, owner.Device));

        // Only the log's owner hands it on. The new owner was never let in,
        // which an opened log does not require, since it has no members.
        log.Refuses(AccessLogBuilder.Create(log.State, AccessLogEntryType.TransferOwnership, member.UserId,
            member.Device, 4000, r => r.With(target: member.Member)));
        log.Append(AccessLogBuilder.Create(log.State, AccessLogEntryType.TransferOwnership, owner.UserId,
            owner.Device, 4000, r => r.With(target: newOwner.Member)));
        Assert.True(log.State.IsOpen);
        Assert.True(log.State.IsOwner(newOwner.UserId, newOwner.State));

        // The earlier owner can no longer make it private.
        log.Refuses(AccessLogBuilder.Create(log.State, AccessLogEntryType.Restart, owner.UserId, owner.Device, 5000,
            r => r.With(owner: owner.Member, members: [])));

        log.Append(AccessLogBuilder.Create(log.State, AccessLogEntryType.Restart, newOwner.UserId, newOwner.Device,
            5000, r => r.With(owner: newOwner.Member, members: [member.Member])));
        Assert.True(log.State.IsGoverning);
        Assert.True(log.State.IsMember(member.UserId, member.State));
        Assert.False(log.State.IsMember(owner.UserId, owner.State));

        // The log continues under its new owner, who alone can open it again.
        log.Refuses(Open(log.State, owner.UserId, owner.Device));
        log.Append(Open(log.State, newOwner.UserId, newOwner.Device));
        Assert.Equal(4, log.State.OpenedSeq);

        var replayed = AccessLogVerifier.Verify(AccessLogScope.Planet, PlanetId, log.Entries, log.States);
        Assert.Equal(log.State.Encode(), replayed.Encode());
    }

    [Fact]
    public void AfterOpen_OwnerKeysFromAResetCannotMakeItPrivate()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var log = StartPrivate(owner, member);
        log.Append(Open(log.State, owner.UserId, owner.Device));

        // The owner started their encryption over while the planet was
        // public. Their new keys are not the ones the log names, and no
        // member remains who could confirm them.
        var resetDevice = DeviceKeyPair.Generate();
        var reset = UserKeyLogBuilder.Reset(owner.State, resetDevice, "Reset", UserKeyPair.Generate(2), null, 3500);
        var resetState = UserKeyLogVerifier.Verify(owner.UserId, [owner.Genesis, reset]);
        var withReset = new Dictionary<long, UserKeyState>(log.States) { [owner.UserId] = resetState };

        log.Refuses(AccessLogBuilder.Create(log.State, AccessLogEntryType.Restart, owner.UserId, resetDevice, 5000,
            r => r.With(owner: AccessMember.For(resetState), members: [member.Member])), withReset);
        log.Refuses(AccessLogBuilder.Create(log.State, AccessLogEntryType.TransferOwnership, owner.UserId,
            resetDevice, 5000, r => r.With(target: member.Member)), withReset);
        Assert.False(log.State.IsOwner(owner.UserId, resetState));
    }

    [Fact]
    public void RestartWhileGoverning_StillNeedsTheOwner()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var log = StartPrivate(owner, member);

        log.Refuses(AccessLogBuilder.Create(log.State, AccessLogEntryType.Restart, member.UserId, member.Device, 3000,
            r => r.With(owner: member.Member, members: [])));
    }

    [Fact]
    public void StoredStatesFromBeforeOpening_StillLoadAsGoverning()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var log = StartPrivate(owner, member);
        var current = log.State.Encode();

        // The earlier format has no opening sequence number after the
        // checkpoint sequence number.
        const int header = 4 + 1 + 8 + 4 + E2eeCrypto.HashSize + 8 + 4;
        var earlier = "VAT3"u8.ToArray().Concat(current[4..header]).Concat(current[(header + 4)..]).ToArray();

        var decoded = AccessLogState.Decode(earlier);
        Assert.Equal(-1, decoded.OpenedSeq);
        Assert.True(decoded.IsGoverning);
        Assert.True(decoded.IsMember(member.UserId, member.State));
        Assert.Equal(current, decoded.Encode());
    }
}
