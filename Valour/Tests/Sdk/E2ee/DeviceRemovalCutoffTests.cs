using Valour.Sdk.E2ee;

namespace Valour.Tests.Sdk.E2ee;

/// <summary>
/// Removing a device records where its membership log entries end. Entries
/// its keys sign after that point have no effect, even when they are dated
/// before the removal, so a copy of a removed device's keys cannot let anyone
/// into a private planet or group.
/// </summary>
public class DeviceRemovalCutoffTests
{
    private const long OwnerId = 1;
    private const long PlanetId = 900;

    private sealed class Owner
    {
        public DeviceKeyPair Laptop { get; } = DeviceKeyPair.Generate();
        public DeviceKeyPair Phone { get; } = DeviceKeyPair.Generate();
        public UserKeyPair UserKey { get; } = UserKeyPair.Generate(1);
        public List<UserKeyLogEntry> Entries { get; } = new();

        public UserKeyState State => UserKeyLogVerifier.Verify(OwnerId, Entries);
        public AccessMember Member => AccessMember.For(State);

        public Owner()
        {
            Entries.Add(UserKeyLogBuilder.Genesis(OwnerId, Laptop, "Laptop", UserKey, null, 1000));
            Entries.Add(UserKeyLogBuilder.AddDevice(State, Laptop.DeviceId, Laptop.Sign,
                UserKeyLogBuilder.Describe(OwnerId, Phone, "Phone"), 1500));
        }

        public void RemoveLaptop(long timestampMs, List<AccessLogCutoff> cutoffs) =>
            Entries.Add(UserKeyLogBuilder.RevokeDevice(State, Phone.DeviceId, Phone.Sign, Laptop.DeviceId,
                UserKeyPair.Generate(2), UserKey, timestampMs, cutoffs));
    }

    private static AccessLogEntry AddMember(AccessLogState state, DeviceKeyPair device, Person person,
        long timestampMs) =>
        AccessLogBuilder.Create(state, AccessLogEntryType.AddMembers, OwnerId, device, timestampMs,
            r => r.With(members: [person.Member]));

    private static Dictionary<long, UserKeyState> States(Owner owner, params Person[] people)
    {
        var states = new Dictionary<long, UserKeyState> { [OwnerId] = owner.State };
        foreach (var person in people)
            states[person.UserId] = person.State;
        return states;
    }

    /// <summary>
    /// A private planet the owner's laptop started, where the laptop then let
    /// Bob in before it was removed.
    /// </summary>
    private static (AccessLogState State, List<AccessLogEntry> Entries) StartPlanet(Owner owner, Person bob)
    {
        var state = new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = PlanetId };
        var entries = new List<AccessLogEntry>();
        var states = States(owner, bob);

        entries.Add(AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, OwnerId, owner.Laptop, 2000,
            r => r.With(owner: owner.Member, members: [])));
        AccessLogVerifier.Apply(state, entries[^1], states);
        entries.Add(AddMember(state, owner.Laptop, bob, 2500));
        AccessLogVerifier.Apply(state, entries[^1], states);
        return (state, entries);
    }

    [Fact]
    public void RemovedDevice_BackdatedEntryAfterCutoffHasNoEffect()
    {
        var owner = new Owner();
        var bob = Person.Create(2);
        var mallory = Person.Create(3);
        var carol = Person.Create(4);
        var (state, entries) = StartPlanet(owner, bob);

        owner.RemoveLaptop(3000, [new AccessLogCutoff(AccessLogScope.Planet, PlanetId, state.HeadSeq)]);
        var states = States(owner, bob, mallory, carol);

        // The removed laptop's keys sign an entry dated before the removal.
        // It stays in the chain, so the log keeps working, but Mallory is not let in.
        entries.Add(AddMember(state, owner.Laptop, mallory, 2900));
        AccessLogVerifier.Apply(state, entries[^1], states);
        Assert.Equal(2, state.HeadSeq);
        Assert.False(state.IsMember(mallory.UserId, mallory.State));

        // What the laptop signed before its cutoff still counts, and the
        // phone keeps managing the log.
        Assert.True(state.IsMember(bob.UserId, bob.State));
        entries.Add(AddMember(state, owner.Phone, carol, 3100));
        AccessLogVerifier.Apply(state, entries[^1], states);
        Assert.True(state.IsMember(carol.UserId, carol.State));

        var replayed = AccessLogVerifier.Verify(AccessLogScope.Planet, PlanetId, entries, states);
        Assert.Equal(state.Encode(), replayed.Encode());
    }

    [Fact]
    public void RemovedDevice_WithoutACutoffForTheLog_FallsBackToTheRemovalTime()
    {
        var owner = new Owner();
        var bob = Person.Create(2);
        var mallory = Person.Create(3);
        var (state, _) = StartPlanet(owner, bob);

        // The removal lists another log, so this one is only checked against
        // the removal time, which a backdated entry passes.
        owner.RemoveLaptop(3000, [new AccessLogCutoff(AccessLogScope.GroupChannel, 77, 5)]);
        var states = States(owner, bob, mallory);

        AccessLogVerifier.Apply(state, AddMember(state, owner.Laptop, mallory, 2900), states);
        Assert.True(state.IsMember(mallory.UserId, mallory.State));

        // An entry dated after the removal is refused outright.
        var late = AccessLogState.Decode(state.Encode());
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(late, AddMember(late, owner.Laptop, Person.Create(5), 3500), states));
    }

    [Fact]
    public void StartingOver_NeverUndoesEntriesTheEarlierKeysSigned()
    {
        var owner = new Owner();
        var bob = Person.Create(2);
        var (state, entries) = StartPlanet(owner, bob);

        // Only the new device signs a reset, so the server could forge one.
        // It carries no cutoffs and cannot void what the earlier keys signed.
        var newDevice = DeviceKeyPair.Generate();
        owner.Entries.Add(UserKeyLogBuilder.Reset(owner.State, newDevice, "New phone", UserKeyPair.Generate(2), null,
            3000));
        Assert.Null(UserKeyLogRecord.Decode(owner.Entries[^1].Body).AccessLogCutoffs);

        var replayed = AccessLogVerifier.Verify(AccessLogScope.Planet, PlanetId, entries, States(owner, bob));
        Assert.Equal(state.Encode(), replayed.Encode());
        Assert.True(replayed.IsMember(bob.UserId, bob.State));
    }

    [Fact]
    public void CheckpointSignedAfterCutoff_CannotStartTheLog()
    {
        var owner = new Owner();
        var bob = Person.Create(2);
        var (state, entries) = StartPlanet(owner, bob);
        owner.RemoveLaptop(3000, [new AccessLogCutoff(AccessLogScope.Planet, PlanetId, state.HeadSeq)]);

        var checkpoint = AccessLogBuilder.Create(state, AccessLogEntryType.Checkpoint, OwnerId, owner.Laptop, 2900,
            r => r.With(snapshot: AccessLogSnapshot.FromState(state)));
        var states = States(owner, bob);
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Verify(AccessLogScope.Planet, PlanetId, [checkpoint], states));

        // Readers see it cannot start the log and read the whole log, where
        // the checkpoint has no effect.
        Assert.False(AccessLogVerifier.CanStartFrom([checkpoint], states));
        var whole = AccessLogVerifier.Verify(AccessLogScope.Planet, PlanetId, [.. entries, checkpoint], states);
        Assert.Equal(checkpoint.Seq, whole.HeadSeq);
        Assert.Equal(-1, whole.LastCheckpointSeq);
        Assert.True(whole.IsMember(bob.UserId, bob.State));
    }

    [Fact]
    public void Cutoffs_RoundTripAndOlderRevocationsStillDecode()
    {
        var owner = new Owner();
        var cutoffs = new List<AccessLogCutoff>
        {
            new(AccessLogScope.Planet, PlanetId, 4),
            new(AccessLogScope.GroupChannel, 77, -1)
        };
        owner.RemoveLaptop(3000, cutoffs);

        var revoke = UserKeyLogRecord.Decode(owner.Entries[^1].Body);
        Assert.Equal(cutoffs, revoke.AccessLogCutoffs);
        Assert.Equal(-1, owner.State.EverDevices[owner.Laptop.DeviceId].AccessLogCutoffs[(AccessLogScope.GroupChannel, 77)]);

        // Only removals use the newer format.
        Assert.Equal("VKL1"u8.ToArray(), owner.Entries[1].Body[..4]);
        Assert.Equal("VKL2"u8.ToArray(), owner.Entries[^1].Body[..4]);

        // A revocation written before cutoffs existed has the older magic and
        // no cutoff list.
        var body = owner.Entries[^1].Body;
        var older = body[..^(4 + cutoffs.Count * 13)];
        "VKL1"u8.CopyTo(older);
        Assert.Null(UserKeyLogRecord.Decode(older).AccessLogCutoffs);

        // The same log listed twice is refused.
        var duplicate = new Owner();
        duplicate.RemoveLaptop(3000,
            [new AccessLogCutoff(AccessLogScope.Planet, PlanetId, 1), new AccessLogCutoff(AccessLogScope.Planet, PlanetId, 2)]);
        Assert.Throws<E2eeFormatException>(() => UserKeyLogRecord.Decode(duplicate.Entries[^1].Body));
    }
}
