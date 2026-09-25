using Valour.Sdk.E2ee;
using Valour.Shared.Utilities;

namespace Valour.Tests.Sdk.E2ee;

/// <summary>
/// A user with one device and a verified key log, for tests that need
/// several distinct users.
/// </summary>
internal sealed class Person
{
    public long UserId { get; init; }
    public DeviceKeyPair Device { get; init; }
    public UserKeyLogEntry Genesis { get; init; }
    public UserKeyState State { get; init; }
    public AccessMember Member => AccessMember.For(State);

    public static Person Create(long userId)
    {
        var device = DeviceKeyPair.Generate();
        var genesis = UserKeyLogBuilder.Genesis(userId, device, "Device", UserKeyPair.Generate(1), null, 1000);
        return new Person
        {
            UserId = userId,
            Device = device,
            Genesis = genesis,
            State = UserKeyLogVerifier.Verify(userId, [genesis])
        };
    }
}

public class E2eeCryptoTests
{
    [Fact]
    public void Seal_RoundTripsOnlyWithMatchingKeyAndContext()
    {
        var (privateKey, publicKey) = E2eeCrypto.GenerateX25519();
        var (otherPrivate, _) = E2eeCrypto.GenerateX25519();
        var plaintext = E2eeCrypto.Utf8("hello");

        var sealedData = E2eeCrypto.Seal(publicKey, plaintext, "context-a");

        Assert.Equal(plaintext, E2eeCrypto.Open(privateKey, sealedData, "context-a"));
        Assert.Throws<E2eeVerificationException>(() => E2eeCrypto.Open(privateKey, sealedData, "context-b"));
        Assert.Throws<E2eeVerificationException>(() => E2eeCrypto.Open(otherPrivate, sealedData, "context-a"));
    }

    [Fact]
    public void Decrypt_RejectsTamperedCiphertextAndAssociatedData()
    {
        var key = E2eeCrypto.RandomBytes(32);
        var encrypted = E2eeCrypto.Encrypt(key, E2eeCrypto.Utf8("secret"), E2eeCrypto.Utf8("aad"));

        var tampered = (byte[])encrypted.Clone();
        tampered[^1] ^= 1;

        Assert.Throws<E2eeVerificationException>(() => E2eeCrypto.Decrypt(key, tampered, E2eeCrypto.Utf8("aad")));
        Assert.Throws<E2eeVerificationException>(() => E2eeCrypto.Decrypt(key, encrypted, E2eeCrypto.Utf8("other")));
    }

    [Fact]
    public void X25519_RejectsLowOrderPublicKey()
    {
        var (privateKey, _) = E2eeCrypto.GenerateX25519();
        Assert.Throws<E2eeVerificationException>(() => E2eeCrypto.X25519Agree(privateKey, new byte[32]));
    }

    [Fact]
    public void RecoveryCode_RoundTripsAndToleratesTyping()
    {
        var key = RecoveryKey.Generate();
        var code = key.ToCode();

        Assert.Matches("^([0-9A-Z]{4}-){7}[0-9A-Z]{4}$", code);
        Assert.True(RecoveryKey.TryParse(code.ToLowerInvariant().Replace("-", " "), out var parsed));
        Assert.Equal(key.Secret, parsed.Secret);
        Assert.Equal(key.RecoveryId, parsed.RecoveryId);

        var confusable = code.Replace('0', 'O').Replace('1', 'l');
        Assert.True(RecoveryKey.TryParse(confusable, out var fromConfusable));
        Assert.Equal(key.Secret, fromConfusable.Secret);

        Assert.False(RecoveryKey.TryParse(code[..^1], out _));
        Assert.False(RecoveryKey.TryParse(code[..^1] + "!", out _));
    }
}

public class UserKeyLogTests
{
    private const long UserId = 42;

    [Fact]
    public void Genesis_AddDevice_Revoke_VerifyAndTrackKeys()
    {
        var first = DeviceKeyPair.Generate();
        var userKey1 = UserKeyPair.Generate(1);
        var recovery = RecoveryKey.Generate();
        var entries = new List<UserKeyLogEntry>
        {
            UserKeyLogBuilder.Genesis(UserId, first, "Laptop", userKey1, recovery, 1000)
        };

        var state = UserKeyLogVerifier.Verify(UserId, entries);
        Assert.Single(state.ActiveDevices);
        Assert.Equal(1, state.UserKey.Generation);
        Assert.Equal(recovery.RecoveryId, state.Recovery.DeviceId);

        var second = DeviceKeyPair.Generate();
        entries.Add(UserKeyLogBuilder.AddDevice(state, first.DeviceId, first.Sign,
            UserKeyLogBuilder.Describe(UserId, second, "Phone"), 2000));
        state = UserKeyLogVerifier.Verify(UserId, entries);
        Assert.Equal(2, state.ActiveDevices.Count);

        var userKey2 = UserKeyPair.Generate(2);
        entries.Add(UserKeyLogBuilder.RevokeDevice(state, second.DeviceId, second.Sign, first.DeviceId, userKey2,
            userKey1, 3000));
        state = UserKeyLogVerifier.Verify(UserId, entries);

        Assert.Single(state.ActiveDevices);
        Assert.Equal(2, state.UserKey.Generation);
        Assert.Equal(first.PublicKeys.SignPublicKey,
            state.GetDeviceAt(first.DeviceId, DateTimeOffset.FromUnixTimeMilliseconds(2500).UtcDateTime)!.SignPublicKey);
        Assert.Null(state.GetDeviceAt(first.DeviceId, DateTimeOffset.FromUnixTimeMilliseconds(3500).UtcDateTime));

        var unwrapped = userKey2.UnwrapPrevious(UserId, state.PreviousUserKeyWrapped[2]);
        Assert.Equal(userKey1.EncryptPrivateKey, unwrapped.EncryptPrivateKey);
    }

    [Fact]
    public void RecoveryKey_CanAddDevice()
    {
        var first = DeviceKeyPair.Generate();
        var recovery = RecoveryKey.Generate();
        var entries = new List<UserKeyLogEntry>
        {
            UserKeyLogBuilder.Genesis(UserId, first, "Laptop", UserKeyPair.Generate(1), recovery, 1000)
        };
        var state = UserKeyLogVerifier.Verify(UserId, entries);

        var restored = DeviceKeyPair.Generate();
        entries.Add(UserKeyLogBuilder.AddDevice(state, recovery.RecoveryId, recovery.Sign,
            UserKeyLogBuilder.Describe(UserId, restored, "New laptop"), 2000));

        state = UserKeyLogVerifier.Verify(UserId, entries);
        Assert.True(state.ActiveDevices.ContainsKey(restored.DeviceId));
        Assert.Equal(recovery.RecoveryId, state.ActiveDevices[restored.DeviceId].AddedBy);
    }

    [Fact]
    public void Verify_RejectsUnauthorizedAndTamperedEntries()
    {
        var first = DeviceKeyPair.Generate();
        var genesis = UserKeyLogBuilder.Genesis(UserId, first, "Laptop", UserKeyPair.Generate(1), null, 1000);
        var state = UserKeyLogVerifier.Verify(UserId, [genesis]);

        var stranger = DeviceKeyPair.Generate();
        var newcomer = DeviceKeyPair.Generate();
        var unauthorized = UserKeyLogBuilder.AddDevice(state, stranger.DeviceId, stranger.Sign,
            UserKeyLogBuilder.Describe(UserId, newcomer, "Ghost"), 2000);
        Assert.Throws<E2eeVerificationException>(() => UserKeyLogVerifier.Verify(UserId, [genesis, unauthorized]));

        var valid = UserKeyLogBuilder.AddDevice(state, first.DeviceId, first.Sign,
            UserKeyLogBuilder.Describe(UserId, newcomer, "Phone"), 2000);
        var tampered = new UserKeyLogEntry
        {
            UserId = valid.UserId, Seq = valid.Seq, Body = (byte[])valid.Body.Clone(), Signature = valid.Signature
        };
        tampered.Body[^70] ^= 1;
        Assert.ThrowsAny<Exception>(() => UserKeyLogVerifier.Verify(UserId, [genesis, tampered]));

        Assert.Throws<E2eeVerificationException>(() => UserKeyLogVerifier.Verify(7, [genesis]));
    }

    [Fact]
    public void Verify_RejectsDeviceWithoutJoinProof()
    {
        var first = DeviceKeyPair.Generate();
        var genesis = UserKeyLogBuilder.Genesis(UserId, first, "Laptop", UserKeyPair.Generate(1), null, 1000);
        var state = UserKeyLogVerifier.Verify(UserId, [genesis]);

        var victim = DeviceKeyPair.Generate();
        var stolenDescriptor = new DeviceDescriptor
        {
            Keys = victim.PublicKeys,
            Name = "Someone else's key",
            JoinProof = victim.CreateJoinProof(UserId + 1)
        };
        var entry = UserKeyLogBuilder.AddDevice(state, first.DeviceId, first.Sign, stolenDescriptor, 2000);

        Assert.Throws<E2eeVerificationException>(() => UserKeyLogVerifier.Verify(UserId, [genesis, entry]));
    }

    [Fact]
    public void Reset_StartsNewEpochAndKeepsOldDevicesForHistory()
    {
        var first = DeviceKeyPair.Generate();
        var genesis = UserKeyLogBuilder.Genesis(UserId, first, "Laptop", UserKeyPair.Generate(1), null, 1000);
        var state = UserKeyLogVerifier.Verify(UserId, [genesis]);
        var safetyBefore = state.SafetyNumber();

        var replacement = DeviceKeyPair.Generate();
        var reset = UserKeyLogBuilder.Reset(state, replacement, "Fresh start", UserKeyPair.Generate(2), null, 5000);
        state = UserKeyLogVerifier.Verify(UserId, [genesis, reset]);

        Assert.Equal(1, state.Epoch);
        Assert.Equal(2, state.UserKey.Generation);
        Assert.Single(state.ActiveDevices);
        Assert.NotNull(state.GetDeviceAt(first.DeviceId, DateTimeOffset.FromUnixTimeMilliseconds(4000).UtcDateTime));
        Assert.Null(state.GetDeviceAt(first.DeviceId, DateTimeOffset.FromUnixTimeMilliseconds(6000).UtcDateTime));
        Assert.NotEqual(safetyBefore, state.SafetyNumber());
    }

    [Fact]
    public void UserKeyBox_OpensOnlyForRecipient()
    {
        var device = DeviceKeyPair.Generate();
        var userKey = UserKeyPair.Generate(3);
        var box = userKey.SealTo(UserId, device.DeviceId, device.EncryptPublicKey);

        var opened = UserKeyPair.OpenBox(UserId, 3, device.DeviceId, device.EncryptPrivateKey, box);
        Assert.Equal(userKey.SignSeed, opened.SignSeed);
        Assert.Throws<E2eeVerificationException>(() =>
            UserKeyPair.OpenBox(UserId + 1, 3, device.DeviceId, device.EncryptPrivateKey, box));
    }
}

public class DeviceLinkTests
{
    [Fact]
    public void LinkCode_RoundTrips()
    {
        var device = DeviceKeyPair.Generate();
        var secret = E2eeCrypto.RandomBytes(DeviceLinkCode.NewDeviceSecretLength);
        var code = new DeviceLinkCode(DeviceLinkMode.NewDeviceShowsCode, 99, "session-abc",
            device.PublicKeys.Fingerprint(), secret);

        Assert.True(DeviceLinkCode.TryParse(code.ToString(), out var parsed));
        Assert.Equal(code.Mode, parsed.Mode);
        Assert.Equal(99, parsed.UserId);
        Assert.Equal("session-abc", parsed.SessionId);
        Assert.Equal(secret, parsed.Secret);
        Assert.True(DeviceLinkVerification.MatchesFingerprint(device.PublicKeys, parsed.Fingerprint));
        Assert.False(DeviceLinkVerification.MatchesFingerprint(DeviceKeyPair.Generate().PublicKeys, parsed.Fingerprint));
        Assert.False(DeviceLinkCode.TryParse("https://valour.gg", out _));

        var shownSecret = E2eeCrypto.RandomBytes(DeviceLinkCode.ExistingDeviceSecretLength);
        var shown = new DeviceLinkCode(DeviceLinkMode.ExistingDeviceShowsCode, 99, "session-xyz", null, shownSecret);
        Assert.True(DeviceLinkCode.TryParse(shown.ToString(), out var parsedShown));
        Assert.Equal(DeviceLinkMode.ExistingDeviceShowsCode, parsedShown.Mode);
        Assert.Null(parsedShown.Fingerprint);
        Assert.Equal(shownSecret, parsedShown.Secret);
    }

    [Fact]
    public void JoinMac_BindsTheDeviceKeys()
    {
        var device = DeviceKeyPair.Generate();
        var secret = E2eeCrypto.RandomBytes(16);
        var mac = DeviceLinkVerification.JoinMac(secret, 5, "s1", device.PublicKeys);
        Assert.True(DeviceLinkVerification.VerifyJoinMac(secret, 5, "s1", device.PublicKeys, mac));
        Assert.False(DeviceLinkVerification.VerifyJoinMac(secret, 5, "s1", DeviceKeyPair.Generate().PublicKeys, mac));
    }
}

public class ChannelAndMessageTests
{
    private const long ChannelId = 1000;
    private const long AuthorId = 7;

    private static (DeviceKeyPair Device, UserKeyState State) CreateAuthor()
    {
        var device = DeviceKeyPair.Generate();
        var genesis = UserKeyLogBuilder.Genesis(AuthorId, device, "Laptop", UserKeyPair.Generate(1), null, 1000);
        return (device, UserKeyLogVerifier.Verify(AuthorId, [genesis]));
    }

    [Fact]
    public void Generation_VerifiesBoxesAndHistoryChain()
    {
        var (device, author) = CreateAuthor();
        var first = ChannelKeySecret.Generate(ChannelId, 1);
        var (entry1, _) = ChannelKeyGenerationBuilder.Create(first, 0, AuthorId, device,
            ChannelKeyRotationReason.Initial, 1, null, 2000, ChannelKeyTerms.Ungoverned(ChannelKeyPolicy.Direct, true));
        var record1 = ChannelKeyGenerationRecord.Verify(entry1, author);
        Assert.True(first.Matches(record1));

        var second = ChannelKeySecret.Generate(ChannelId, 2);
        var (entry2, _) = ChannelKeyGenerationBuilder.Create(second, 0, AuthorId, device,
            ChannelKeyRotationReason.MemberRemoved, 1, first, 3000, ChannelKeyTerms.Ungoverned(ChannelKeyPolicy.Direct, true));
        var record2 = ChannelKeyGenerationRecord.Verify(entry2, author);
        Assert.True(record2.UnlocksPrevious);
        Assert.Equal(first.Secret, second.UnwrapPrevious(record2.PreviousWrapped).Secret);

        var memberKey = UserKeyPair.Generate(1);
        var box = second.SealTo(55, memberKey.PublicKey);
        Assert.True(ChannelKeySecret.OpenBox(ChannelId, 2, 55, memberKey, box).Matches(record2));
        Assert.False(ChannelKeySecret.Generate(ChannelId, 2).Matches(record2));

        var (_, stranger) = CreateAuthor();
        Assert.Throws<E2eeVerificationException>(() => ChannelKeyGenerationRecord.Verify(entry2, stranger));
    }

    [Fact]
    public void Message_SealsOpensAndFranks()
    {
        var (device, author) = CreateAuthor();
        var key = ChannelKeySecret.Generate(ChannelId, 1);
        var nonce = E2eeCrypto.RandomBytes(16);
        var terms = SearchTerms.ForMessage(key.IndexKey, ChannelId, "hello world");

        var (envelope, header) = MessageCrypto.Seal(key, 0, AuthorId, device, nonce, 0, 0, "hello world", null,
            terms, 5000);
        var sentAt = DateTimeOffset.FromUnixTimeMilliseconds(5000).UtcDateTime;
        var opened = MessageCrypto.Open(envelope, key, author, sentAt);

        Assert.Equal("hello world", opened.Payload.Content);
        Assert.Equal(SearchTerms.Hash(terms), header.TermsHash);
        Assert.True(Franking.Verify(opened.Header, opened.Payload.FrankingKey, "hello world", null));
        Assert.False(Franking.Verify(opened.Header, opened.Payload.FrankingKey, "hello w0rld", null));
        Assert.False(Franking.Verify(opened.Header, E2eeCrypto.RandomBytes(32), "hello world", null));
    }

    [Fact]
    public void Message_RejectsTamperingWrongKeyAndForeignSigner()
    {
        var (device, author) = CreateAuthor();
        var key = ChannelKeySecret.Generate(ChannelId, 1);
        var (envelope, _) = MessageCrypto.Seal(key, 0, AuthorId, device, E2eeCrypto.RandomBytes(16), 0, 0, "hi",
            null, [], 5000);
        var sentAt = DateTimeOffset.FromUnixTimeMilliseconds(5000).UtcDateTime;

        var tampered = (byte[])envelope.Clone();
        tampered[^80] ^= 1;
        Assert.ThrowsAny<Exception>(() => MessageCrypto.Open(tampered, key, author, sentAt));

        Assert.Throws<E2eeVerificationException>(() =>
            MessageCrypto.Open(envelope, ChannelKeySecret.Generate(ChannelId + 1, 1), author, sentAt));

        var (_, otherAuthor) = CreateAuthor();
        Assert.Throws<E2eeVerificationException>(() => MessageCrypto.Open(envelope, key, otherAuthor, sentAt));
    }

    [Fact]
    public void ServerSealed_OpensForMembersAndAttests()
    {
        var key = ChannelKeySecret.Generate(ChannelId, 4);
        var (serverSeed, serverPublic) = E2eeCrypto.GenerateEd25519();
        var envelope = ServerSealing.Seal(key.SealPublicKey, ChannelId, 0, 4, ServerSealedKind.Legacy, AuthorId,
            9000, "old text", null, "server-1", data => E2eeCrypto.Sign(serverSeed, data));

        var payload = ServerSealing.Open(envelope, key, id => id == "server-1" ? serverPublic : null);
        Assert.Equal("old text", payload.Content);

        var (header, _, _) = ServerSealing.Read(envelope);
        Assert.True(ServerSealing.VerifyAttestation(header, payload.Salt, "old text", null, serverPublic));
        Assert.False(ServerSealing.VerifyAttestation(header, payload.Salt, "new text", null, serverPublic));
        Assert.Throws<E2eeVerificationException>(() =>
            ServerSealing.Open(envelope, key, _ => E2eeCrypto.GenerateEd25519().PublicKey));
    }

    [Fact]
    public void ServerCreatedKey_VerifiesOnlyWithTheServerKeyAndOnlyAsTheFirstGeneration()
    {
        var (serverSeed, serverPublic) = E2eeCrypto.GenerateEd25519();
        var secret = ChannelKeySecret.Generate(ChannelId, 1);
        var (entry, record) = ChannelKeyGenerationBuilder.CreateByServer(secret, 0, "server-1",
            data => E2eeCrypto.Sign(serverSeed, data), 1000, ChannelKeyTerms.Ungoverned(ChannelKeyPolicy.Direct, true));

        Assert.True(record.IsServerCreated);
        Assert.True(ChannelKeyGenerationRecord.TryReadIsServerCreated(entry.Body));
        var verified = ChannelKeyGenerationRecord.VerifyServer(entry, id => id == "server-1" ? serverPublic : null);
        Assert.True(secret.Matches(verified));

        Assert.Throws<E2eeVerificationException>(() =>
            ChannelKeyGenerationRecord.VerifyServer(entry, _ => E2eeCrypto.GenerateEd25519().PublicKey));
        var (_, author) = CreateAuthor();
        Assert.Throws<E2eeVerificationException>(() => ChannelKeyGenerationRecord.Verify(entry, author));
        Assert.Throws<ArgumentException>(() => ChannelKeyGenerationBuilder.CreateByServer(
            ChannelKeySecret.Generate(ChannelId, 2), 0, "server-1", data => E2eeCrypto.Sign(serverSeed, data), 1000, ChannelKeyTerms.Ungoverned(ChannelKeyPolicy.Direct, true)));

        // A member's key cannot pose as the server's.
        var (device, _) = CreateAuthor();
        var (memberEntry, _) = ChannelKeyGenerationBuilder.Create(ChannelKeySecret.Generate(ChannelId, 1), 0, AuthorId,
            device, ChannelKeyRotationReason.Initial, 1, null, 1000, ChannelKeyTerms.Ungoverned(ChannelKeyPolicy.Direct, true));
        Assert.False(ChannelKeyGenerationRecord.TryReadIsServerCreated(memberEntry.Body));
        Assert.Throws<E2eeVerificationException>(() =>
            ChannelKeyGenerationRecord.VerifyServer(memberEntry, _ => serverPublic));
    }

    [Fact]
    public void EmbedUpdate_IsBoundToItsMessageRecipientAndRevision()
    {
        var key = ChannelKeySecret.Generate(ChannelId, 2);
        var encrypted = EmbedUpdateCrypto.Encrypt(key, 50, 7, 3, isFullEmbed: false, "[]");

        var (isFull, content) = EmbedUpdateCrypto.Decrypt(key, 50, 7, 3, encrypted);
        Assert.False(isFull);
        Assert.Equal("[]", content);

        Assert.Throws<E2eeVerificationException>(() => EmbedUpdateCrypto.Decrypt(key, 51, 7, 3, encrypted));
        Assert.Throws<E2eeVerificationException>(() => EmbedUpdateCrypto.Decrypt(key, 50, 8, 3, encrypted));
        Assert.Throws<E2eeVerificationException>(() => EmbedUpdateCrypto.Decrypt(key, 50, 7, 4, encrypted));
        Assert.Throws<E2eeVerificationException>(() =>
            EmbedUpdateCrypto.Decrypt(ChannelKeySecret.Generate(ChannelId, 2), 50, 7, 3, encrypted));
    }
}

public class SearchTermsTests
{
    private static readonly byte[] IndexKey = E2eeCrypto.RandomBytes(32);
    private const long ChannelId = 5;

    [Theory]
    [InlineData("Café", "cafe")]
    [InlineData("CAFÉ", "cafe")]
    [InlineData("b​ad", "bad")]
    [InlineData("b4d", "bad")]
    [InlineData("baaaaad", "baad")]
    [InlineData("\u0412\u0410D", "bad")]
    [InlineData("b\u0430d", "bad")]
    [InlineData("Ｂａｄ", "bad")]
    [InlineData("Łódź", "lodz")]
    public void Tokenize_FoldsEvasionAndAccents(string input, string expected)
    {
        Assert.Equal(expected, Assert.Single(SearchTerms.Tokenize(input)).Text);
    }

    [Fact]
    public void Tokenize_KeepsNumbersAndSplitsCjk()
    {
        Assert.Equal("2024", Assert.Single(SearchTerms.Tokenize("2024")).Text);
        var cjk = SearchTerms.Tokenize("東京都").Select(t => t.Text).ToList();
        Assert.Contains("東京", cjk);
        Assert.Contains("京都", cjk);
    }

    [Fact]
    public void Query_MatchesWholeWordsAndLastPrefix()
    {
        var message = SearchTerms.ForMessage(IndexKey, ChannelId, "The quick brown foxes jumped");

        Assert.True(Contains(message, SearchTerms.ForQuery(IndexKey, ChannelId, "quick fox")));
        Assert.True(Contains(message, SearchTerms.ForQuery(IndexKey, ChannelId, "BROWN")));
        Assert.False(Contains(message, SearchTerms.ForQuery(IndexKey, ChannelId, "fox quick")));
        Assert.False(Contains(message, SearchTerms.ForQuery(IndexKey, ChannelId, "slow")));

        Assert.True(SearchTerms.Matches("The quick brown foxes jumped", "quick fox"));
        Assert.False(SearchTerms.Matches("The quick brown foxes jumped", "fox quick"));
    }

    [Fact]
    public void Terms_DependOnChannelAndKey()
    {
        var a = SearchTerms.ForMessage(IndexKey, ChannelId, "hello");
        Assert.NotEqual(a, SearchTerms.ForMessage(IndexKey, ChannelId + 1, "hello"));
        Assert.NotEqual(a, SearchTerms.ForMessage(E2eeCrypto.RandomBytes(32), ChannelId, "hello"));
        Assert.Equal(a, SearchTerms.ForMessage(IndexKey, ChannelId, "HELLO"));
    }

    [Theory]
    [InlineData("you are a b4dword person", true)]
    [InlineData("badwords everywhere", true)]
    [InlineData("superbadword", true)]
    [InlineData("a bad word", false)]
    [InlineData("nothing to see", false)]
    public void TriggerWord_MatchesWordPrefixOrSuffix(string content, bool expected)
    {
        var message = SearchTerms.ForMessage(IndexKey, ChannelId, content);
        var alternatives = SearchTerms.ForTriggerWord(IndexKey, ChannelId, "badword");
        Assert.Equal(expected, alternatives.Any(alt => Contains(message, alt)));
    }

    [Fact]
    public void TriggerPhraseAndCommand_Match()
    {
        var message = SearchTerms.ForMessage(IndexKey, ChannelId, "/Ban everyone right now");
        Assert.True(SearchTerms.ForTriggerWord(IndexKey, ChannelId, "right now").Any(alt => Contains(message, alt)));
        Assert.Contains(SearchTerms.ForTriggerCommand(IndexKey, ChannelId, "ban"), message);
        Assert.DoesNotContain(SearchTerms.ForTriggerCommand(IndexKey, ChannelId, "kick"), message);
    }

    [Fact]
    public void Terms_AreCapped()
    {
        var content = string.Join(' ', Enumerable.Range(0, 500).Select(i => "word" + i + "abcdefgh"));
        Assert.True(SearchTerms.ForMessage(IndexKey, ChannelId, content).Length <= SearchTerms.MaxTerms);
    }

    private static bool Contains(int[] haystack, int[] needles) => needles.All(haystack.Contains);
}

public class AccessLogTests
{
    private static Dictionary<long, UserKeyState> States(params Person[] people) =>
        people.ToDictionary(p => p.UserId, p => p.State);

    [Fact]
    public void GroupChannel_MembersAddButOnlyOwnerRemovesOthers()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var newcomer = Person.Create(3);
        var states = States(owner, member, newcomer);
        var state = new AccessLogState { Scope = AccessLogScope.GroupChannel, ScopeId = 77 };

        var genesis = AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, owner.UserId, owner.Device, 2000,
            r => r.With(owner: owner.Member, members: [member.Member]));
        AccessLogVerifier.Apply(state, genesis, states);

        var add = AccessLogBuilder.Create(state, AccessLogEntryType.AddMembers, member.UserId, member.Device, 3000,
            r => r.With(members: [newcomer.Member]));
        AccessLogVerifier.Apply(state, add, states);
        Assert.True(state.IsMember(newcomer.UserId, newcomer.State));

        var memberRemoves = AccessLogBuilder.Create(state, AccessLogEntryType.RemoveMembers, member.UserId,
            member.Device, 4000, r => r.With(userIds: [newcomer.UserId]));
        Assert.Throws<E2eeVerificationException>(() => AccessLogVerifier.Apply(state, memberRemoves, states));

        var leave = AccessLogBuilder.Create(state, AccessLogEntryType.RemoveMembers, newcomer.UserId,
            newcomer.Device, 4000, r => r.With(userIds: [newcomer.UserId]));
        AccessLogVerifier.Apply(state, leave, states);
        Assert.False(state.IsMemberAnyEpoch(newcomer.UserId));
    }

    [Fact]
    public void Checkpoint_MustMatchTheReplayedStateAndLetsNewDevicesStartFromIt()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var later = Person.Create(3);
        var states = States(owner, member, later);

        var entries = new List<AccessLogEntry>();
        var state = new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = 700 };
        void Append(AccessLogEntry entry)
        {
            AccessLogVerifier.Apply(state, entry, states);
            entries.Add(entry);
        }

        Append(AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, owner.UserId, owner.Device, 2000,
            r => r.With(owner: owner.Member, members: [])));
        Append(AccessLogBuilder.Create(state, AccessLogEntryType.AddMembers, owner.UserId, owner.Device, 3000,
            r => r.With(members: [member.Member])));

        // A checkpoint claiming a member the log never admitted is rejected.
        var forged = AccessLogSnapshot.FromState(state);
        forged.Members.Add(later.Member);
        var bad = AccessLogBuilder.Create(state, AccessLogEntryType.Checkpoint, owner.UserId, owner.Device, 4000,
            r => r.With(snapshot: forged));
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(AccessLogState.Decode(state.Encode()), bad, states));

        // A member without admin rights cannot sign one.
        var byMember = AccessLogBuilder.Create(state, AccessLogEntryType.Checkpoint, member.UserId, member.Device, 4000,
            r => r.With(snapshot: AccessLogSnapshot.FromState(state)));
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(AccessLogState.Decode(state.Encode()), byMember, states));

        Append(AccessLogBuilder.Create(state, AccessLogEntryType.Checkpoint, owner.UserId, owner.Device, 4000,
            r => r.With(snapshot: AccessLogSnapshot.FromState(state))));
        Append(AccessLogBuilder.Create(state, AccessLogEntryType.AddMembers, owner.UserId, owner.Device, 5000,
            r => r.With(members: [later.Member])));

        // A new device reads only from the checkpoint and reaches the same state.
        var fromCheckpoint = AccessLogVerifier.Verify(AccessLogScope.Planet, 700, entries.Skip(2).ToList(), states);
        Assert.Equal(state.Encode(), fromCheckpoint.Encode());
        Assert.True(fromCheckpoint.IsMember(member.UserId, member.State));
        Assert.True(fromCheckpoint.IsMember(later.UserId, later.State));

        // A partial log must begin at a checkpoint.
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Verify(AccessLogScope.Planet, 700, entries.Skip(3).ToList(), states));
    }

    [Fact]
    public void StoredState_ContinuesWithOnlyNewerEntries()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var states = States(owner, member);
        var state = new AccessLogState { Scope = AccessLogScope.GroupChannel, ScopeId = 800 };
        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, owner.UserId,
            owner.Device, 2000, r => r.With(owner: owner.Member, members: [])), states);

        var stored = AccessLogState.Decode(state.Encode());
        var add = AccessLogBuilder.Create(state, AccessLogEntryType.AddMembers, owner.UserId, owner.Device, 3000,
            r => r.With(members: [member.Member]));
        AccessLogVerifier.Apply(stored, add, states);
        Assert.True(stored.IsMember(member.UserId, member.State));

        // An entry that does not follow the stored head is refused.
        var stale = AccessLogState.Decode(state.Encode());
        var skipped = AccessLogBuilder.Create(stored, AccessLogEntryType.RemoveMembers, owner.UserId, owner.Device,
            4000, r => r.With(userIds: [member.UserId]));
        Assert.Throws<E2eeVerificationException>(() => AccessLogVerifier.Apply(stale, skipped, states));
    }

    [Fact]
    public void Planet_OutsiderCannotAdmitThemselves()
    {
        var owner = Person.Create(1);
        var outsider = Person.Create(99);
        var states = States(owner, outsider);
        var state = new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = 500 };
        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, owner.UserId,
            owner.Device, 2000, r => r.With(owner: owner.Member, members: [])), states);

        var selfAdmit = AccessLogBuilder.Create(state, AccessLogEntryType.AddMembers, outsider.UserId,
            outsider.Device, 3000, r => r.With(members: [outsider.Member]));
        Assert.Throws<E2eeVerificationException>(() => AccessLogVerifier.Apply(state, selfAdmit, states));
    }

    [Fact]
    public void Planet_InviteRedeemsWithinLimits()
    {
        var owner = Person.Create(1);
        var joiner = Person.Create(2);
        var second = Person.Create(3);
        var states = States(owner, joiner, second);
        var state = new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = 500 };
        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, owner.UserId,
            owner.Device, 2000, r => r.With(owner: owner.Member, members: [])), states);

        var secret = E2eeCrypto.RandomBytes(16);
        var (seed, publicKey) = AccessLogBuilder.InviteKey(secret);
        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.CreateInvite, owner.UserId,
            owner.Device, 3000, r => r.With(inviteId: "inv1", invitePublicKey: publicKey, inviteMaxUses: 1)), states);

        AccessLogEntry Redeem(Person person, byte[] inviteSeed) =>
            AccessLogBuilder.Create(state, AccessLogEntryType.RedeemInvite, person.UserId, person.Device, 4000,
                r => r.With(inviteId: "inv1", target: person.Member,
                    inviteProof: E2eeCrypto.Sign(inviteSeed, AccessLogRecord.InviteProofMessage(
                        AccessLogScope.Planet, 500, "inv1", person.UserId, person.Device.DeviceId))));

        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(state, Redeem(joiner, E2eeCrypto.RandomBytes(32)), states));

        AccessLogVerifier.Apply(state, Redeem(joiner, seed), states);
        Assert.True(state.IsMember(joiner.UserId, joiner.State));

        Assert.Throws<E2eeVerificationException>(() => AccessLogVerifier.Apply(state, Redeem(second, seed), states));
    }

    [Fact]
    public void ResetMember_NeedsConfirmationBeforeCountingAgain()
    {
        var owner = Person.Create(1);
        var memberDevice = DeviceKeyPair.Generate();
        var memberGenesis = UserKeyLogBuilder.Genesis(2, memberDevice, "Phone", UserKeyPair.Generate(1), null, 1000);
        var memberState = UserKeyLogVerifier.Verify(2, [memberGenesis]);
        var states = new Dictionary<long, UserKeyState> { [1] = owner.State, [2] = memberState };

        var state = new AccessLogState { Scope = AccessLogScope.GroupChannel, ScopeId = 9 };
        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, owner.UserId,
            owner.Device, 2000, r => r.With(owner: owner.Member, members: [AccessMember.For(memberState)])), states);

        var resetDevice = DeviceKeyPair.Generate();
        var reset = UserKeyLogBuilder.Reset(memberState, resetDevice, "Reset", UserKeyPair.Generate(2), null, 3000);
        states[2] = UserKeyLogVerifier.Verify(2, [memberGenesis, reset]);

        Assert.False(state.IsMember(2, states[2]));
        var addByReset = AccessLogBuilder.Create(state, AccessLogEntryType.AddMembers, 2, resetDevice, 4000,
            r => r.With(members: [new AccessMember(3, 0, new byte[UserKeyState.KeyIdSize])]));
        Assert.Throws<E2eeVerificationException>(() => AccessLogVerifier.Apply(state, addByReset, states));

        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.ConfirmEpoch, owner.UserId,
            owner.Device, 5000, r => r.With(target: AccessMember.For(states[2]))), states);
        Assert.True(state.IsMember(2, states[2]));
    }
}

public class MessageMarkdownSafetyTests
{
    [Theory]
    [InlineData("[](https://example.com/a.png)", "[]\\(https://example.com/a.png)")]
    [InlineData("[hidden]()", "[hidden]\\()")]
    [InlineData("[link](https://valour.gg)", "[link](https://valour.gg)")]
    public void Escape_NeutralizesEmptyLinkTextAndTargets(string input, string expected)
    {
        Assert.Equal(expected, MessageMarkdownSafety.Escape(input));
        Assert.Equal(expected, MessageMarkdownSafety.Escape(MessageMarkdownSafety.Escape(input)));
    }
}

public class ChannelKeyChainTests
{
    private static ChannelKeyChain.Link Unlocks => new(true, ChannelKeyRotationReason.MemberRemoved);

    [Fact]
    public void Heads_IncludeEarlierChainsExceptHistoryHiddenFromNewMembers()
    {
        var links = new Dictionary<int, ChannelKeyChain.Link>
        {
            [1] = new(false, ChannelKeyRotationReason.Initial),
            [2] = Unlocks,
            [3] = new(false, ChannelKeyRotationReason.KeyUnavailable),
            [4] = Unlocks,
            [5] = new(false, ChannelKeyRotationReason.NewMemberWithoutHistory),
        };

        // Generation 2 heads the chain the newcomer's key broke off from.
        // Generation 4 is behind a break that hides history from new members.
        Assert.Equal([5, 2], ChannelKeyChain.Heads(links, 5));
    }

    [Fact]
    public void Reachable_FollowsEachChainDownFromHeldKeys()
    {
        var links = new Dictionary<int, ChannelKeyChain.Link>
        {
            [1] = new(false, ChannelKeyRotationReason.Initial),
            [2] = Unlocks,
            [3] = new(false, ChannelKeyRotationReason.KeyUnavailable),
            [4] = Unlocks,
        };

        Assert.Equal(new HashSet<int> { 3, 4 }, ChannelKeyChain.Reachable(links, [4]));
        Assert.Equal(new HashSet<int> { 1, 2, 3, 4 }, ChannelKeyChain.Reachable(links, [4, 2]));
    }
}

public class ProtocolHardeningTests
{
    private static AccessLogRecord Dated(AccessLogRecord record, long timestampMs) => new()
    {
        Scope = record.Scope,
        ScopeId = record.ScopeId,
        Seq = record.Seq,
        PreviousHash = record.PreviousHash,
        Type = record.Type,
        TimestampMs = timestampMs,
        SignerUserId = record.SignerUserId,
        SignerDeviceId = record.SignerDeviceId,
        Owner = record.Owner,
        Members = record.Members,
        UserIds = record.UserIds
    };

    [Fact]
    public void AccessLog_OnlyTheCurrentOwnerRestartsIt()
    {
        var owner = Person.Create(1);
        var admin = Person.Create(2);
        var states = new Dictionary<long, UserKeyState> { [1] = owner.State, [2] = admin.State };
        var state = new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = 5 };

        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, owner.UserId,
            owner.Device, 2000, r => r.With(owner: owner.Member, members: [admin.Member])), states);
        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.SetAdmin, owner.UserId,
            owner.Device, 3000, r => r.With(target: admin.Member, isAdmin: true)), states);

        // An admin, or an account the server named as the planet's owner,
        // cannot start the log over with members of its choosing.
        var takeover = AccessLogBuilder.Create(state, AccessLogEntryType.Restart, admin.UserId, admin.Device, 4000,
            r => r.With(owner: admin.Member, members: []));
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(AccessLogState.Decode(state.Encode()), takeover, states));

        var restart = AccessLogBuilder.Create(state, AccessLogEntryType.Restart, owner.UserId, owner.Device, 4000,
            r => r.With(owner: owner.Member, members: []));
        AccessLogVerifier.Apply(state, restart, states);
        Assert.Equal(2, state.LastRemovalSeq);
        Assert.False(state.IsMemberAnyEpoch(admin.UserId));
    }

    [Fact]
    public void AccessLog_TimesNeverGoBackwardsAndRemovalsAreTracked()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var states = new Dictionary<long, UserKeyState> { [1] = owner.State, [2] = member.State };
        var state = new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = 6 };

        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, owner.UserId,
            owner.Device, 5000, r => r.With(owner: owner.Member, members: [member.Member])), states);
        Assert.Equal(-1, state.LastRemovalSeq);

        // A backdated entry is refused, and the builder never produces one
        // even when this device's clock is behind.
        var backdated = AccessLogBuilder.Create(state, AccessLogEntryType.RemoveMembers, owner.UserId, owner.Device,
            1000, r => Dated(r.With(userIds: [member.UserId]), 1000));
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(AccessLogState.Decode(state.Encode()), backdated, states));

        var remove = AccessLogBuilder.Create(state, AccessLogEntryType.RemoveMembers, owner.UserId, owner.Device,
            1000, r => r.With(userIds: [member.UserId]));
        Assert.Equal(5000, AccessLogRecord.Decode(remove.Body).TimestampMs);
        AccessLogVerifier.Apply(state, remove, states);
        Assert.Equal(1, state.LastRemovalSeq);

        // Stored state and checkpoints keep the removal, so a device starting
        // from either still replaces keys made before it.
        var stored = AccessLogState.Decode(state.Encode());
        Assert.Equal(1, stored.LastRemovalSeq);
        Assert.Equal(5000, stored.HeadTimestampMs);
        var fromSnapshot = new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = 6 };
        AccessLogSnapshot.FromState(state).ApplyTo(fromSnapshot);
        Assert.Equal(1, fromSnapshot.LastRemovalSeq);
    }

    [Fact]
    public void KeyLog_TimesNeverGoBackwards()
    {
        var device = DeviceKeyPair.Generate();
        var userKey = UserKeyPair.Generate(1);
        var genesis = UserKeyLogBuilder.Genesis(9, device, "Laptop", userKey, null, 5000);
        var state = UserKeyLogVerifier.Verify(9, [genesis]);

        var added = DeviceKeyPair.Generate();
        var entry = UserKeyLogBuilder.AddDevice(state, device.DeviceId, device.Sign,
            UserKeyLogBuilder.Describe(9, added, "Phone"), 1000);
        Assert.Equal(5000, UserKeyLogRecord.Decode(entry.Body).TimestampMs);
        UserKeyLogVerifier.Apply(state, entry);
        Assert.Equal(5000, state.HeadTimestampMs);
    }

    [Fact]
    public void ChannelKeyRecord_SignsItsTerms()
    {
        var device = DeviceKeyPair.Generate();
        var author = UserKeyLogVerifier.Verify(4,
            [UserKeyLogBuilder.Genesis(4, device, "Laptop", UserKeyPair.Generate(1), null, 1000)]);
        var secret = ChannelKeySecret.Generate(88, 1);
        var terms = new ChannelKeyTerms(ChannelKeyPolicy.PlanetInviteOnly, false, 12);
        var (entry, _) = ChannelKeyGenerationBuilder.Create(secret, 3, 4, device, ChannelKeyRotationReason.Initial,
            1, null, 2000, terms);

        var record = ChannelKeyGenerationRecord.Verify(entry, author);
        Assert.Equal(terms, record.Terms);
        Assert.True(record.Terms.IsGoverned);

        // The server cannot relabel a key as open without breaking its signature.
        var tampered = (byte[])entry.Body.Clone();
        tampered[^6] = (byte)ChannelKeyPolicy.PlanetOpen;
        Assert.Throws<E2eeVerificationException>(() => ChannelKeyGenerationRecord.Verify(
            new ChannelKeyGenerationEntry { ChannelId = 88, Generation = 1, Body = tampered, Signature = entry.Signature },
            author));
    }

    [Fact]
    public void AutomodApprovals_OnlyAdminsApproveAndOnlyExactTextCounts()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var states = new Dictionary<long, UserKeyState> { [1] = owner.State, [2] = member.State };
        var state = new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = 7 };
        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, owner.UserId,
            owner.Device, 2000, r => r.With(owner: owner.Member, members: [member.Member])), states);

        var triggerId = Guid.NewGuid();
        var approval = new AutomodApproval(triggerId, AutomodApproval.HashTrigger(0, "badword, other"));

        var byMember = AccessLogBuilder.Create(state, AccessLogEntryType.ApproveAutomodTriggers, member.UserId,
            member.Device, 3000, r => r.With(automodApprovals: [approval]));
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(AccessLogState.Decode(state.Encode()), byMember, states));

        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.ApproveAutomodTriggers,
            owner.UserId, owner.Device, 3000, r => r.With(automodApprovals: [approval])), states);
        Assert.True(state.IsAutomodTriggerApproved(triggerId, 0, "badword, other"));

        // Text the server swapped in, or another trigger, is not approved.
        Assert.False(state.IsAutomodTriggerApproved(triggerId, 0, "badword, other, secretplan"));
        Assert.False(state.IsAutomodTriggerApproved(Guid.NewGuid(), 0, "badword, other"));

        // Approvals survive stored state and checkpoints.
        Assert.True(AccessLogState.Decode(state.Encode()).IsAutomodTriggerApproved(triggerId, 0, "badword, other"));
    }

}

public class E2eeSdkHardeningTests
{
    private const long ChannelId = 1000;
    private static readonly byte[] IndexKey = E2eeCrypto.RandomBytes(32);

    /// <summary>
    /// The term rules written the simple way: every word, prefix, and suffix
    /// is computed, then the first MaxTerms entries are kept.
    /// </summary>
    private static int[] ReferenceTerms(byte[] key, long channelId, string content)
    {
        var words = new List<int>();
        var prefixes = new List<int>();
        var suffixes = new List<int>();
        foreach (var token in SearchTerms.Tokenize(content))
        {
            if (token.IsCjk)
            {
                words.Add(SearchTerms.Term(key, channelId, SearchTerms.Word, token.Text));
                continue;
            }
            if (SearchTerms.IsStopWord(token.Text))
                continue;
            words.Add(SearchTerms.Term(key, channelId, SearchTerms.Word, token.Text));
            for (var length = 2; length <= Math.Min(token.Text.Length, SearchTerms.MaxAffixLength); length++)
                prefixes.Add(SearchTerms.Term(key, channelId, SearchTerms.Prefix, token.Text[..length]));
            for (var length = 3; length <= Math.Min(token.Text.Length, SearchTerms.MaxAffixLength); length++)
                suffixes.Add(SearchTerms.Term(key, channelId, SearchTerms.Suffix, token.Text[^length..]));
        }

        var terms = new List<int>();
        var command = SearchTerms.LeadingCommand(content);
        if (command is not null)
            terms.Add(SearchTerms.Term(key, channelId, SearchTerms.Command, command));
        terms.AddRange(words.Concat(prefixes).Concat(suffixes).Take(SearchTerms.MaxTerms - terms.Count));
        return SearchTerms.Normalize(terms);
    }

    [Fact]
    public void ForMessage_MatchesTheTermRulesForShortAndLongText()
    {
        var random = new Random(4);
        string[] vocabulary = ["hello", "world", "encryption", "the", "valour", "東京都", "b4aaad", "x", "internationalization"];
        var inputs = new List<string>
        {
            "",
            "hello world",
            "/Ban everyone right now",
            "東京都 and 日本語 text",
            "the the the a an",
            string.Join(' ', Enumerable.Repeat("repeated", 500))
        };
        for (var i = 0; i < 20; i++)
        {
            var text = string.Join(' ', Enumerable.Range(0, random.Next(1, 400))
                .Select(_ => vocabulary[random.Next(vocabulary.Length)] + random.Next(100)));
            inputs.Add(text.Length > MessageCrypto.MaxContentLength ? text[..MessageCrypto.MaxContentLength] : text);
        }

        foreach (var input in inputs)
        {
            var terms = SearchTerms.ForMessage(IndexKey, ChannelId, input);
            Assert.Equal(ReferenceTerms(IndexKey, ChannelId, input), terms);
            Assert.True(terms.Length <= SearchTerms.MaxTerms);
        }
    }

    [Fact]
    public void Seal_RefusesTextOverTheLimit()
    {
        var device = DeviceKeyPair.Generate();
        var key = ChannelKeySecret.Generate(ChannelId, 1);
        var limit = new string('a', MessageCrypto.MaxContentLength);

        MessageCrypto.Seal(key, 0, 7, device, E2eeCrypto.RandomBytes(16), 0, 0, limit, null, [], 1000);
        Assert.Throws<E2eeFormatException>(() => MessageCrypto.Seal(key, 0, 7, device, E2eeCrypto.RandomBytes(16), 0,
            0, limit + "a", null, [], 1000));
    }

    [Fact]
    public void PreviewUrls_FollowTheServersLinkRules()
    {
        Assert.Equal(["https://a.example/page"], Valour.Sdk.Services.MessagePreviewUrls.Extract("see https://a.example/page"));

        // <url> links are not previewed, and neither are links in code.
        Assert.Empty(Valour.Sdk.Services.MessagePreviewUrls.Extract("<https://a.example/page>"));
        Assert.Empty(Valour.Sdk.Services.MessagePreviewUrls.Extract("`https://a.example/page`"));
        Assert.Empty(Valour.Sdk.Services.MessagePreviewUrls.Extract("```\nhttps://a.example/page\n```"));
        Assert.Empty(Valour.Sdk.Services.MessagePreviewUrls.Extract("![](https://a.example/image.png)"));

        // Links in spoilers are marked, and markers inside code do not count.
        Assert.Equal(["||https://a.example/secret||", "https://b.example/"],
            Valour.Sdk.Services.MessagePreviewUrls.Extract("||look https://a.example/secret|| and https://b.example/"));
        Assert.Equal(["https://b.example/"], Valour.Sdk.Services.MessagePreviewUrls.Extract("`||` https://b.example/"));

        // The fragment can hold a secret, such as an encrypted invite's key.
        Assert.Equal(["https://app.valour.gg/i/abc"],
            Valour.Sdk.Services.MessagePreviewUrls.Extract("join https://app.valour.gg/i/abc#e2ee=1.secret"));

        var many = string.Join(' ', Enumerable.Range(0, 8).Select(i => $"https://site{i}.example/"));
        Assert.Equal(Valour.Sdk.Services.MessagePreviewUrls.MaxUrls, Valour.Sdk.Services.MessagePreviewUrls.Extract(many).Count);
    }

    [Fact]
    public void Mentions_OnlyThoseInTheTextAreKept()
    {
        var mentions = new List<Valour.Shared.Models.Mention>
        {
            new() { TargetId = 111111111111111111, Type = Valour.Shared.Models.MentionType.User },
            new() { TargetId = 222222222222222222, Type = Valour.Shared.Models.MentionType.User },
            new() { TargetId = 111111111111111111, Type = Valour.Shared.Models.MentionType.Role }
        };

        var kept = Valour.Sdk.Services.E2eeService.MentionsInText(mentions, "hi «@u-111111111111111111»");
        var mention = Assert.Single(kept);
        Assert.Equal(111111111111111111, mention.TargetId);
        Assert.Equal(Valour.Shared.Models.MentionType.User, mention.Type);

        Assert.Empty(Valour.Sdk.Services.E2eeService.MentionsInText(mentions, "no mentions"));
    }

    [Fact]
    public void SealedMessages_CannotPostAsOtherPeople()
    {
        const long victor = Valour.Shared.Models.ISharedUser.VictorUserId;
        var sent = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var sentMs = new DateTimeOffset(sent).ToUnixTimeMilliseconds();

        ServerSealedHeader Header(ServerSealedKind kind, long author, long timeMs) => new()
        {
            ChannelId = ChannelId, Generation = 1, Nonce = new byte[16], Kind = kind, AuthorUserId = author,
            TimeSentMs = timeMs, ServerKeyId = "k", Attestation = new byte[64]
        };

        ChannelKeyGenerationRecord Record(long timestampMs, bool server) => new()
        {
            ChannelId = ChannelId, Generation = 1, CreatorUserId = server ? ChannelKeyGenerationRecord.ServerCreatorId : 5,
            TimestampMs = timestampMs,
            Reason = server ? ChannelKeyRotationReason.ServerSealing : ChannelKeyRotationReason.Initial
        };

        string Check(ServerSealedHeader header, long author, DateTime time, ChannelKeyGenerationRecord record) =>
            Valour.Sdk.Services.E2eeService.SealedHeaderViolation(header, author, time, header.Generation, record);

        // Webhook and system messages are posted as the system account only.
        Assert.Null(Check(Header(ServerSealedKind.Webhook, victor, sentMs), victor, sent, null));
        Assert.Null(Check(Header(ServerSealedKind.System, victor, sentMs), victor, sent, null));
        Assert.NotNull(Check(Header(ServerSealedKind.Webhook, 5, sentMs), 5, sent, null));
        Assert.NotNull(Check(Header(ServerSealedKind.System, 5, sentMs), 5, sent, null));

        // The author and time shown must be the ones the server attested.
        Assert.NotNull(Check(Header(ServerSealedKind.System, victor, sentMs), 5, sent, null));
        Assert.Null(Check(Header(ServerSealedKind.System, victor, sentMs + 1500), victor, sent, null));
        Assert.NotNull(Check(Header(ServerSealedKind.System, victor, sentMs + 5000), victor, sent, null));
        Assert.Null(Check(Header(ServerSealedKind.System, victor, sentMs),
            victor, DateTime.SpecifyKind(sent, DateTimeKind.Unspecified), null));

        // History from before encryption must be older than the first key a
        // member created.
        var legacy = Header(ServerSealedKind.Legacy, 5, sentMs);
        Assert.Null(Check(legacy, 5, sent, Record(sentMs + 1, server: false)));
        // An hour's allowance covers slow clocks and text an older server
        // wrote during an upgrade; anything later is refused.
        Assert.Null(Check(legacy, 5, sent, Record(sentMs - 60_000, server: false)));
        Assert.NotNull(Check(legacy, 5, sent, Record(sentMs - 60 * 60 * 1000, server: false)));
        Assert.NotNull(Check(legacy, 5, sent, Record(sentMs - 2 * 60 * 60 * 1000, server: false)));
        // Before any member created a key, nothing dates the start of encryption.
        Assert.Null(Check(legacy, 5, sent, null));
    }

    [Fact]
    public void RevokedDevicesAreToldApartFromDevicesReplacedByAReset()
    {
        const long userId = 42;
        var first = DeviceKeyPair.Generate();
        var second = DeviceKeyPair.Generate();
        var userKey1 = UserKeyPair.Generate(1);
        var entries = new List<UserKeyLogEntry>
        {
            UserKeyLogBuilder.Genesis(userId, first, "Laptop", userKey1, null, 1000)
        };
        var state = UserKeyLogVerifier.Verify(userId, entries);
        entries.Add(UserKeyLogBuilder.AddDevice(state, first.DeviceId, first.Sign,
            UserKeyLogBuilder.Describe(userId, second, "Phone"), 2000));
        state = UserKeyLogVerifier.Verify(userId, entries);
        entries.Add(UserKeyLogBuilder.RevokeDevice(state, first.DeviceId, first.Sign, second.DeviceId,
            UserKeyPair.Generate(2), userKey1, 3000));
        state = UserKeyLogVerifier.Verify(userId, entries);

        Assert.True(Valour.Sdk.Services.E2eeService.WasRevokedDirectly(state, second.DeviceId));
        Assert.False(Valour.Sdk.Services.E2eeService.WasRemovedByReset(state, second.DeviceId));
        Assert.False(Valour.Sdk.Services.E2eeService.WasRemovedByReset(state, first.DeviceId));

        entries.Add(UserKeyLogBuilder.Reset(state, DeviceKeyPair.Generate(), "Fresh start", UserKeyPair.Generate(3),
            null, 4000));
        state = UserKeyLogVerifier.Verify(userId, entries);

        Assert.True(Valour.Sdk.Services.E2eeService.WasRemovedByReset(state, first.DeviceId));
        Assert.False(Valour.Sdk.Services.E2eeService.WasRevokedDirectly(state, first.DeviceId));
    }

    [Fact]
    public async Task FileKeyStore_KeepsKeysPrivateAndWritesWhole()
    {
        var directory = Path.Combine(Path.GetTempPath(), "valour-e2ee-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new FileE2eeKeyStore(directory);
            Assert.True(store.CreatedDirectory);
            Assert.False(new FileE2eeKeyStore(directory).CreatedDirectory);

            await Task.WhenAll(Enumerable.Range(0, 20).Select(i => store.SetAsync("key", [(byte)i, 1, 2, 3])));
            var value = await store.GetAsync("key");
            Assert.Equal(4, value.Length);
            Assert.Equal([1, 2, 3], value[1..]);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));

            if (!OperatingSystem.IsWindows())
            {
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                    File.GetUnixFileMode(directory));
                var file = Assert.Single(Directory.GetFiles(directory));
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            }

            await store.RemoveAsync("key");
            Assert.Null(await store.GetAsync("key"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

public class DeviceLinkApprovalTests
{
    private const long UserId = 42;
    private const string SessionId = "session-1";

    private sealed record Approval(DeviceKeyPair NewDevice, UserKeyLogEntry Entry, UserKeyBoxDto Box, byte[] Pins,
        byte[] Mac, byte[] Secret);

    /// <summary>
    /// An existing device adds a new one and proves the approval with the
    /// link secret, as ApproveLinkAsync does.
    /// </summary>
    private static Approval Approve(byte[] secret)
    {
        var existing = DeviceKeyPair.Generate();
        var userKey = UserKeyPair.Generate(1);
        var state = UserKeyLogVerifier.Verify(UserId,
            [UserKeyLogBuilder.Genesis(UserId, existing, "Laptop", userKey, null, 1000)]);

        var newDevice = DeviceKeyPair.Generate();
        var entry = UserKeyLogBuilder.AddDevice(state, existing.DeviceId, existing.Sign,
            UserKeyLogBuilder.Describe(UserId, newDevice, "Phone"), 2000);
        var box = new UserKeyBoxDto
        {
            UserId = UserId,
            Generation = userKey.Generation,
            RecipientId = newDevice.DeviceId,
            Box = userKey.SealTo(UserId, newDevice.DeviceId, newDevice.EncryptPublicKey)
        };
        var pins = E2eeCrypto.Seal(newDevice.EncryptPublicKey, E2eeCrypto.Utf8("{}"),
            DeviceLinkVerification.PinsContext(UserId, SessionId, newDevice.DeviceId));
        var mac = DeviceLinkVerification.ApprovalMac(secret, UserId, SessionId, newDevice.DeviceId, entry.Body,
            box.Generation, box.Box, pins);
        return new Approval(newDevice, entry, box, pins, mac, secret);
    }

    private static bool Verify(Approval a, byte[] secret = null, byte[] entryBody = null, byte[] box = null,
        byte[] pins = null, int? generation = null, string sessionId = SessionId, long userId = UserId) =>
        DeviceLinkVerification.VerifyApprovalMac(secret ?? a.Secret, userId, sessionId, a.NewDevice.DeviceId,
            entryBody ?? a.Entry.Body, generation ?? a.Box.Generation, box ?? a.Box.Box, pins ?? a.Pins, a.Mac);

    [Theory]
    [InlineData(DeviceLinkCode.NewDeviceSecretLength)]
    [InlineData(DeviceLinkCode.ExistingDeviceSecretLength)]
    public void ApprovalMac_AcceptsTheApprovalMadeWithTheSecret(int secretLength)
    {
        var approval = Approve(E2eeCrypto.RandomBytes(secretLength));
        Assert.True(Verify(approval));
    }

    [Fact]
    public void ApprovalMac_RejectsAWrongSecret()
    {
        var approval = Approve(E2eeCrypto.RandomBytes(DeviceLinkCode.ExistingDeviceSecretLength));
        Assert.False(Verify(approval, secret: E2eeCrypto.RandomBytes(DeviceLinkCode.ExistingDeviceSecretLength)));
    }

    [Fact]
    public void ApprovalMac_RejectsAnApprovalTheServerMadeWithoutTheSecret()
    {
        // After a forged reset the server holds a device that can sign an
        // AddDevice entry and seal a user key it knows, but it does not have
        // the secret from the new device's screen.
        var real = Approve(E2eeCrypto.RandomBytes(DeviceLinkCode.NewDeviceSecretLength));
        var forged = Approve(E2eeCrypto.RandomBytes(DeviceLinkCode.NewDeviceSecretLength));

        Assert.False(DeviceLinkVerification.VerifyApprovalMac(real.Secret, UserId, SessionId,
            forged.NewDevice.DeviceId, forged.Entry.Body, forged.Box.Generation, forged.Box.Box, forged.Pins,
            forged.Mac));
        Assert.False(DeviceLinkVerification.VerifyApprovalMac(real.Secret, UserId, SessionId,
            forged.NewDevice.DeviceId, forged.Entry.Body, forged.Box.Generation, forged.Box.Box, forged.Pins, null));
    }

    [Fact]
    public void ApprovalMac_RejectsATamperedEntryBoxOrPins()
    {
        var approval = Approve(E2eeCrypto.RandomBytes(DeviceLinkCode.NewDeviceSecretLength));

        var otherEntry = Approve(approval.Secret).Entry.Body;
        Assert.False(Verify(approval, entryBody: otherEntry));

        var flippedEntry = (byte[])approval.Entry.Body.Clone();
        flippedEntry[^1] ^= 1;
        Assert.False(Verify(approval, entryBody: flippedEntry));

        var otherBox = UserKeyPair.Generate(1).SealTo(UserId, approval.NewDevice.DeviceId,
            approval.NewDevice.EncryptPublicKey);
        Assert.False(Verify(approval, box: otherBox));
        Assert.False(Verify(approval, generation: approval.Box.Generation + 1));

        var otherPins = E2eeCrypto.Seal(approval.NewDevice.EncryptPublicKey, E2eeCrypto.Utf8("{}"), "other");
        Assert.False(Verify(approval, pins: otherPins));
        Assert.False(Verify(approval, pins: []));
    }

    [Fact]
    public void ApprovalMac_IsBoundToTheSessionAndAccount()
    {
        var approval = Approve(E2eeCrypto.RandomBytes(DeviceLinkCode.NewDeviceSecretLength));
        Assert.False(Verify(approval, sessionId: "session-2"));
        Assert.False(Verify(approval, userId: UserId + 1));
    }

    [Fact]
    public void TypedCode_RoundTripsAndFindsTheSession()
    {
        var secret = E2eeCrypto.RandomBytes(DeviceLinkCode.ExistingDeviceSecretLength);
        var typed = DeviceLinkTypedCode.Format(secret);

        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$", typed);
        Assert.True(DeviceLinkTypedCode.TryParse(typed.ToLowerInvariant().Replace("-", " "), out var parsed));
        Assert.Equal(secret, parsed);
        Assert.False(DeviceLinkTypedCode.TryParse(typed[..^1], out _));
        Assert.False(DeviceLinkTypedCode.TryParse(typed + "U", out _));

        var sessionId = DeviceLinkVerification.SessionIdFor(secret, UserId);
        Assert.Equal(22, sessionId.Length);
        Assert.Equal(sessionId, DeviceLinkVerification.SessionIdFor(parsed, UserId));
        Assert.NotEqual(sessionId, DeviceLinkVerification.SessionIdFor(secret, UserId + 1));
    }

    [Fact]
    public void TypedCode_MapsLettersThatLookLikeDigits()
    {
        var secret = new byte[DeviceLinkCode.ExistingDeviceSecretLength];
        var typed = DeviceLinkTypedCode.Format(secret);
        Assert.Equal("0000-0000-0000-0000", typed);
        Assert.True(DeviceLinkTypedCode.TryParse("oooo-OOOO-0000-0000", out var parsed));
        Assert.Equal(secret, parsed);
    }
}

public class E2eeContentBindingTests
{
    private const long ChannelId = 1000;
    private const long AuthorId = 7;

    private static Valour.Sdk.Models.MessageAttachment File(string location, bool spoiler = false) =>
        new(Valour.Shared.Models.MessageAttachmentType.Image)
        {
            Location = location, MimeType = "image/png", FileName = "cat.png", Width = 10, Height = 20,
            IsSpoiler = spoiler
        };

    [Fact]
    public void SealedIndexKey_KeepsSealedTermsApartFromMembersTerms()
    {
        var key = ChannelKeySecret.Generate(ChannelId, 3);
        Assert.NotEqual(key.IndexKey, key.SealedIndexKey);
        Assert.Equal(key.SealedIndexKey, new ChannelKeySecret(ChannelId, 3, key.Secret).SealedIndexKey);
        Assert.NotEqual(key.SealedIndexKey, new ChannelKeySecret(ChannelId + 1, 3, key.Secret).SealedIndexKey);

        // Terms of text the server sealed never equal the terms members'
        // messages carry for the same words, so asking a member to index
        // sealed text teaches the server nothing about members' messages.
        var sealedTerms = SearchTerms.ForMessage(key.SealedIndexKey, ChannelId, "password hunter2");
        var memberTerms = SearchTerms.ForMessage(key.IndexKey, ChannelId, "password hunter2");
        Assert.Empty(sealedTerms.Intersect(memberTerms));

        // A search hashes the query with both keys and finds each kind.
        Assert.True(SearchTerms.ForQuery(key.SealedIndexKey, ChannelId, "password").All(sealedTerms.Contains));
        Assert.True(SearchTerms.ForQuery(key.IndexKey, ChannelId, "password").All(memberTerms.Contains));
        Assert.False(SearchTerms.ForQuery(key.IndexKey, ChannelId, "password").All(sealedTerms.Contains));
    }

    [Fact]
    public void SearchLimits_AreSharedByTheSdkAndServer()
    {
        Assert.Equal(E2eeLimits.MaxSearchIndexGenerations,
            Valour.Server.Services.E2eeChannelKeyService.MaxSearchIndexGenerations);
        Assert.Equal(2 * E2eeLimits.MaxSearchIndexGenerations, E2eeLimits.MaxSearchTermSets);
    }

    [Fact]
    public void Payload_BindsTheAttachmentListAndRefusesOldFormats()
    {
        var device = DeviceKeyPair.Generate();
        var author = UserKeyLogVerifier.Verify(AuthorId,
            [UserKeyLogBuilder.Genesis(AuthorId, device, "Laptop", UserKeyPair.Generate(1), null, 1000)]);
        var key = ChannelKeySecret.Generate(ChannelId, 1);
        var digests = AttachmentBinding.Digests([File("https://cdn.example/a.png"), File("https://cdn.example/b.png")]);

        var (envelope, _) = MessageCrypto.Seal(key, 0, AuthorId, device, E2eeCrypto.RandomBytes(16), 0, 0, "hi", null,
            [], 5000, digests);
        var opened = MessageCrypto.Open(envelope, key, author, DateTimeOffset.FromUnixTimeMilliseconds(5000).UtcDateTime);
        Assert.Equal(digests, opened.Payload.AttachmentDigests);

        var payload = new MessagePayload { Content = "hi", FrankingKey = new byte[32], AttachmentDigests = digests };
        var encoded = payload.Encode();
        Assert.Equal(digests, MessagePayload.Decode(encoded).AttachmentDigests);
        encoded[3] = (byte)'1';
        Assert.Throws<E2eeFormatException>(() => MessagePayload.Decode(encoded));
        Assert.Throws<E2eeFormatException>(() => new MessagePayload
        {
            FrankingKey = new byte[32],
            AttachmentDigests = Enumerable.Range(0, AttachmentBinding.MaxAttachments + 1).Select(_ => new byte[32]).ToList()
        }.Encode());
    }

    [Fact]
    public void AttachmentFilter_ShowsOnlyWhatTheAuthorSent()
    {
        var sent = new List<Valour.Sdk.Models.MessageAttachment>
        {
            File("https://cdn.example/a.png"), File("https://cdn.example/b.png", spoiler: true)
        };
        var digests = AttachmentBinding.Digests(sent);

        // The server's copies of the same files are kept.
        var (kept, removed) = AttachmentBinding.Filter(
            [File("https://cdn.example/a.png"), File("https://cdn.example/b.png", spoiler: true)], digests);
        Assert.Equal(2, kept.Count);
        Assert.False(removed);

        // An added file, a changed location, and a spoiler turned off are not.
        (kept, removed) = AttachmentBinding.Filter(
            [File("https://cdn.example/a.png"), File("https://cdn.example/b.png"), File("https://evil.example/x.png")],
            digests);
        Assert.Equal("https://cdn.example/a.png", Assert.Single(kept).Location);
        Assert.True(removed);

        // Each listed file is shown once.
        (kept, removed) = AttachmentBinding.Filter(
            [File("https://cdn.example/a.png"), File("https://cdn.example/a.png")], digests);
        Assert.Single(kept);
        Assert.True(removed);

        // A deleted file's placeholder stands in for a listed file, but only
        // as many placeholders as listed files remain.
        var missing = Valour.Sdk.Models.MessageAttachment.CreateMissing("anything");
        (kept, removed) = AttachmentBinding.Filter(
            [File("https://cdn.example/a.png"), missing, Valour.Sdk.Models.MessageAttachment.CreateMissing()], digests);
        Assert.Equal(2, kept.Count);
        Assert.True(kept[1].Missing);
        Assert.Equal(Valour.Sdk.Models.MessageAttachment.CreateMissing().FileName, kept[1].FileName);
        Assert.True(removed);

        // Embeds and link previews are left for their own checks.
        var embed = new Valour.Sdk.Models.MessageAttachment(Valour.Shared.Models.MessageAttachmentType.Embed);
        var preview = File("https://a.example/p.png");
        preview.Inline = true;
        (kept, removed) = AttachmentBinding.Filter([embed, preview], []);
        Assert.Equal(2, kept.Count);
        Assert.False(removed);
    }

    [Fact]
    public void LinkPreviews_MustComeFromALinkInTheText()
    {
        Valour.Sdk.Models.MessageAttachment Preview(Valour.Shared.Models.MessageAttachmentType type, string location) =>
            new(type) { Location = location, Inline = true };

        var direct = Preview(Valour.Shared.Models.MessageAttachmentType.Image, "https://i.imgur.com/cat.png");
        Assert.Equal("https://i.imgur.com/cat.png",
            InlinePreviewBinding.SourceOf(direct, ["https://i.imgur.com/cat.png"]));

        // Media copied to the content CDN is stored under a hash of the link.
        var link = "https://images.example/photos/dog.JPG";
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(new Uri(link).AbsoluteUri))).ToLowerInvariant();
        var proxied = Preview(Valour.Shared.Models.MessageAttachmentType.Image, $"https://cdn.example/proxy/{hash}.jpg");
        Assert.Equal(link, InlinePreviewBinding.SourceOf(proxied, [link]));
        Assert.Null(InlinePreviewBinding.SourceOf(proxied, ["https://images.example/photos/cat.jpg"]));

        // A player needs a link to the site its type names.
        var player = Preview(Valour.Shared.Models.MessageAttachmentType.YouTube, "https://www.youtube.com/embed/abc");
        Assert.Equal("||https://youtu.be/abc||", InlinePreviewBinding.SourceOf(player, ["||https://youtu.be/abc||"]));
        Assert.True(InlinePreviewBinding.IsSpoiler("||https://youtu.be/abc||"));
        Assert.Null(InlinePreviewBinding.SourceOf(player, ["https://vimeo.com/1"]));

        // A preview the text gives no reason for is refused, as is a file.
        Assert.Null(InlinePreviewBinding.SourceOf(
            Preview(Valour.Shared.Models.MessageAttachmentType.Image, "https://evil.example/x.png"),
            ["https://a.example/page"]));
        Assert.Null(InlinePreviewBinding.SourceOf(direct, []));
        Assert.Null(InlinePreviewBinding.SourceOf(File("https://i.imgur.com/cat.png"), ["https://i.imgur.com/cat.png"]));
    }

    [Fact]
    public void LegacyMessages_UseTheFirstKeyAndPredateTheFirstMemberKey()
    {
        var sent = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var sentMs = new DateTimeOffset(sent).ToUnixTimeMilliseconds();

        ServerSealedHeader Header(int generation) => new()
        {
            ChannelId = ChannelId, Generation = generation, Nonce = new byte[16], Kind = ServerSealedKind.Legacy,
            AuthorUserId = 5, TimeSentMs = sentMs, ServerKeyId = "k", Attestation = new byte[64]
        };

        var memberRecord = new ChannelKeyGenerationRecord
        {
            ChannelId = ChannelId, Generation = 2, CreatorUserId = 5, TimestampMs = sentMs - 2 * 60 * 60 * 1000,
            Reason = ChannelKeyRotationReason.Initial
        };

        string Check(int headerGeneration, int keyGeneration, ChannelKeyGenerationRecord record) =>
            Valour.Sdk.Services.E2eeService.SealedHeaderViolation(Header(headerGeneration), 5, sent, keyGeneration,
                record);

        // Sealed with a later key, even the server-created first key's
        // successor, a message cannot claim to be from before encryption.
        Assert.NotNull(Check(2, 2, null));
        Assert.NotNull(Check(1, 2, null));
        Assert.NotNull(Check(2, 1, null));

        // With the first key it is accepted until a member's key dates the
        // start of encryption, and refused when it is newer than that.
        Assert.Null(Check(1, 1, null));
        Assert.NotNull(Check(1, 1, memberRecord));
        Assert.Null(Check(1, 1, new ChannelKeyGenerationRecord
        {
            ChannelId = ChannelId, Generation = 2, CreatorUserId = 5, TimestampMs = sentMs + 1,
            Reason = ChannelKeyRotationReason.Initial
        }));
    }

    [Fact]
    public void EmbedUpdates_UseATrustedMemberKeyNoOlderThanTheMessage()
    {
        ChannelKeyGenerationRecord Record(int generation, bool server) => new()
        {
            ChannelId = ChannelId, Generation = generation,
            CreatorUserId = server ? ChannelKeyGenerationRecord.ServerCreatorId : AuthorId,
            Reason = server ? ChannelKeyRotationReason.ServerSealing : ChannelKeyRotationReason.Manual
        };

        string Check(int updateGeneration, int messageGeneration, ChannelKeyGenerationRecord record, bool trusted) =>
            Valour.Sdk.Services.E2eeService.EmbedUpdateViolation(updateGeneration, messageGeneration, record, trusted);

        Assert.Null(Check(3, 3, Record(3, server: false), trusted: true));
        Assert.Null(Check(4, 3, Record(4, server: false), trusted: true));
        Assert.NotNull(Check(2, 3, Record(2, server: false), trusted: true));
        Assert.NotNull(Check(3, 3, Record(3, server: false), trusted: false));
        Assert.NotNull(Check(1, 1, Record(1, server: true), trusted: true));
        Assert.NotNull(Check(3, 3, null, trusted: true));
    }
}

public class AccessMemberKeyBindingTests
{
    private static AccessLogState StartLog(AccessLogScope scope, Person owner, params Person[] members)
    {
        var states = members.Append(owner).ToDictionary(p => p.UserId, p => p.State);
        var state = new AccessLogState { Scope = scope, ScopeId = 40 };
        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, owner.UserId,
            owner.Device, 2000, r => r.With(owner: owner.Member, members: members.Select(m => m.Member).ToList())),
            states);
        return state;
    }

    [Fact]
    public void KeyId_IsPerEpochAndDependsOnTheFirstUserKey()
    {
        var person = Person.Create(2);
        var other = Person.Create(2);
        Assert.Equal(UserKeyState.KeyIdSize, person.State.KeyId.Length);
        Assert.NotEqual(person.State.KeyId, other.State.KeyId);

        var resetState = UserKeyLogVerifier.Verify(2, [person.Genesis,
            UserKeyLogBuilder.Reset(person.State, DeviceKeyPair.Generate(), "Reset", UserKeyPair.Generate(2), null, 3000)]);
        Assert.Equal(person.State.KeyId, resetState.EpochKeyIds[0]);
        Assert.NotEqual(resetState.EpochKeyIds[0], resetState.KeyId);
    }

    [Fact]
    public void ForgedKeyLogWithTheSameEpoch_IsNotTheAdmittedMember()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var state = StartLog(AccessLogScope.GroupChannel, owner, member);

        // A key log the server shows some devices, with the admitted epoch
        // number but a first key it controls.
        var forged = Person.Create(2);
        Assert.Equal(member.State.Epoch, forged.State.Epoch);

        Assert.True(state.IsMember(2, member.State));
        Assert.False(state.IsMember(2, forged.State));
        Assert.True(state.AdmitsDevice(2, member.State, 0));
        Assert.False(state.AdmitsDevice(2, forged.State, 0));

        // The forged keys have no authority in the log either.
        var forgedStates = new Dictionary<long, UserKeyState> { [1] = owner.State, [2] = forged.State };
        var add = AccessLogBuilder.Create(state, AccessLogEntryType.AddMembers, 2, forged.Device, 3000,
            r => r.With(members: [Person.Create(3).Member]));
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(AccessLogState.Decode(state.Encode()), add, forgedStates));

        // Stored state and checkpoints keep the key IDs.
        var stored = AccessLogState.Decode(state.Encode());
        Assert.True(stored.IsMember(2, member.State));
        Assert.False(stored.IsMember(2, forged.State));
        var fromSnapshot = new AccessLogState { Scope = AccessLogScope.GroupChannel, ScopeId = 40 };
        AccessLogSnapshot.FromState(state).ApplyTo(fromSnapshot);
        Assert.False(fromSnapshot.IsMember(2, forged.State));
    }

    [Fact]
    public void OwnerAndInviteEntries_MustNameTheSignersOwnKeys()
    {
        var owner = Person.Create(1);
        var states = new Dictionary<long, UserKeyState> { [1] = owner.State };
        var state = new AccessLogState { Scope = AccessLogScope.Planet, ScopeId = 40 };

        var wrongKeys = new AccessMember(1, 0, new byte[UserKeyState.KeyIdSize]);
        var genesis = AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, 1, owner.Device, 2000,
            r => r.With(owner: wrongKeys, members: []));
        Assert.Throws<E2eeVerificationException>(() => AccessLogVerifier.Apply(state, genesis, states));

        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.Genesis, 1, owner.Device,
            2000, r => r.With(owner: owner.Member, members: [])), states);
        Assert.True(state.IsOwner(1, owner.State));
        Assert.True(state.IsAdmin(1, owner.State));

        var secret = E2eeCrypto.RandomBytes(16);
        var (seed, publicKey) = AccessLogBuilder.InviteKey(secret);
        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.CreateInvite, 1,
            owner.Device, 3000, r => r.With(inviteId: "inv", invitePublicKey: publicKey)), states);

        var joiner = Person.Create(5);
        states[5] = joiner.State;
        var proof = E2eeCrypto.Sign(seed, AccessLogRecord.InviteProofMessage(AccessLogScope.Planet, 40, "inv", 5,
            joiner.Device.DeviceId));
        var redeemWithOtherKeys = AccessLogBuilder.Create(state, AccessLogEntryType.RedeemInvite, 5, joiner.Device,
            4000, r => r.With(inviteId: "inv", target: Person.Create(5).Member, inviteProof: proof));
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(AccessLogState.Decode(state.Encode()), redeemWithOtherKeys, states));
    }

    [Fact]
    public void MemberWithoutKeys_CannotBeAdmitted()
    {
        // An admission must name a key ID, so there is no placeholder for
        // someone who has no keys yet.
        var record = new AccessLogRecord
        {
            Scope = AccessLogScope.Planet,
            ScopeId = 40,
            Seq = 1,
            PreviousHash = new byte[E2eeCrypto.HashSize],
            Type = AccessLogEntryType.AddMembers,
            SignerDeviceId = "device",
            Members = [new AccessMember(7, 0, null)]
        };
        Assert.Throws<E2eeFormatException>(() => record.Encode());

        // Whatever keys an admission names, a key log created later by someone
        // else does not match them.
        var owner = Person.Create(1);
        var state = StartLog(AccessLogScope.Planet, owner);
        var states = new Dictionary<long, UserKeyState> { [1] = owner.State };
        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.AddMembers, 1, owner.Device,
            3000, r => r.With(members: [new AccessMember(7, 0, new byte[UserKeyState.KeyIdSize])])), states);
        Assert.True(state.IsMemberAnyEpoch(7));
        Assert.False(state.IsMember(7, Person.Create(7).State));
    }

    [Fact]
    public void ResetMember_OldDevicesStayTrustedButNewOnesNeedConfirmation()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var state = StartLog(AccessLogScope.GroupChannel, owner, member);
        var states = new Dictionary<long, UserKeyState> { [1] = owner.State };

        var resetState = UserKeyLogVerifier.Verify(2, [member.Genesis,
            UserKeyLogBuilder.Reset(member.State, DeviceKeyPair.Generate(), "Reset", UserKeyPair.Generate(2), null, 3000)]);
        Assert.False(state.IsMember(2, resetState));
        Assert.True(state.AdmitsDevice(2, resetState, 0));
        Assert.False(state.AdmitsDevice(2, resetState, 1));

        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.ConfirmEpoch, 1,
            owner.Device, 4000, r => r.With(target: AccessMember.For(resetState))), states);
        Assert.True(state.IsMember(2, resetState));
        Assert.True(state.AdmitsDevice(2, resetState, 1));
    }

    [Fact]
    public void TransferOwnership_NeedsTheNewOwnersAdmittedKeys()
    {
        var owner = Person.Create(1);
        var member = Person.Create(2);
        var state = StartLog(AccessLogScope.Planet, owner, member);
        var states = new Dictionary<long, UserKeyState> { [1] = owner.State, [2] = member.State };

        var toOtherKeys = AccessLogBuilder.Create(state, AccessLogEntryType.TransferOwnership, 1, owner.Device, 3000,
            r => r.With(target: Person.Create(2).Member));
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(AccessLogState.Decode(state.Encode()), toOtherKeys, states));

        var byMember = AccessLogBuilder.Create(state, AccessLogEntryType.TransferOwnership, 2, member.Device, 3000,
            r => r.With(target: member.Member));
        Assert.Throws<E2eeVerificationException>(() =>
            AccessLogVerifier.Apply(AccessLogState.Decode(state.Encode()), byMember, states));

        AccessLogVerifier.Apply(state, AccessLogBuilder.Create(state, AccessLogEntryType.TransferOwnership, 1,
            owner.Device, 3000, r => r.With(target: member.Member)), states);
        Assert.True(state.IsOwner(2, member.State));
        Assert.False(state.IsOwner(1, owner.State));
        Assert.True(state.IsMember(1, owner.State));
    }
}

public class KeyPinMergeTests
{
    private static Valour.Sdk.Services.E2eeService.KeyPin Pin(int epoch, int count, bool verified = false,
        long verifiedAt = 0, int? accepted = null, long acceptedAt = 0) => new()
    {
        Epoch = epoch,
        Count = count,
        HeadHash = $"head-{count}",
        Verified = verified,
        VerifiedChangedAt = verifiedAt,
        AcceptedEpoch = accepted ?? epoch,
        AcceptedChangedAt = acceptedAt
    };

    private static Valour.Sdk.Services.E2eeService.KeyPin Merge(Valour.Sdk.Services.E2eeService.KeyPin mine,
        Valour.Sdk.Services.E2eeService.KeyPin stored) =>
        Valour.Sdk.Services.E2eeService.MergePins(mine, stored);

    [Fact]
    public void RemovingAVerificationSticks()
    {
        var merged = Merge(Pin(0, 1, verified: false, verifiedAt: 200), Pin(0, 1, verified: true, verifiedAt: 100));
        Assert.False(merged.Verified);

        merged = Merge(Pin(0, 1, verified: false, verifiedAt: 100), Pin(0, 1, verified: true, verifiedAt: 200));
        Assert.True(merged.Verified);
    }

    [Fact]
    public void LongerLogWinsAndEarlierEpochVerificationDoesNotCarryOver()
    {
        // Another tab marked the old keys verified after this tab saw a reset.
        var merged = Merge(Pin(1, 2, verified: false, verifiedAt: 200), Pin(0, 1, verified: true, verifiedAt: 300));
        Assert.Equal(1, merged.Epoch);
        Assert.Equal(2, merged.Count);
        Assert.Equal("head-2", merged.HeadHash);
        Assert.False(merged.Verified);
    }

    [Fact]
    public void AcceptedEpochFollowsTheLatestAcceptance()
    {
        var merged = Merge(Pin(1, 2, accepted: 0), Pin(1, 2, accepted: 1, acceptedAt: 500));
        Assert.Equal(1, merged.AcceptedEpoch);

        // A tab that only saw the old epoch never raises the accepted epoch
        // past the keys the merged pin names.
        merged = Merge(Pin(0, 1, accepted: 0), Pin(0, 1, accepted: 3, acceptedAt: 500));
        Assert.Equal(0, merged.AcceptedEpoch);
    }
}
