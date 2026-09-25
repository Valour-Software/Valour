using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Sdk.Requests;
using Valour.Sdk.Services;
using Valour.Server.Mapping;
using Valour.Server.Services;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Utilities;

namespace Valour.Tests.Apis;

/// <summary>
/// End-to-end encryption through the real server and SDK: several clients
/// with their own keys, checking that the server stores no readable text and
/// that every path members use still works.
/// </summary>
[Collection("ApiCollection")]
public class E2eeLiveTests
{
    private readonly LoginTestFixture _fixture;

    public E2eeLiveTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<(ValourClient Client, RegisterUserRequest Details)> CreateUserAsync(bool setUp = true)
    {
        var details = await _fixture.RegisterUser();
        var client = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: setUp);
        return (client, details);
    }

    private Task<ValourClient> LoginAsync(RegisterUserRequest details, IE2eeKeyStore store, bool autoSetUp) =>
        EncryptedChat.LoginAsync(_fixture, details, store, autoSetUp, requireReady: false);

    private Task<Valour.Database.Message> WaitForStoredAsync(long messageId) =>
        EncryptedChat.WaitForStoredAsync(_fixture, messageId);

    private static Task<Valour.Shared.TaskResult<Message>> SendAsync(ValourClient client, Channel channel,
        string content, long? memberId = null, long? replyToId = null) =>
        EncryptedChat.SendAsync(client, channel, content, memberId, replyToId);

    private static bool ContainsUtf8(byte[] haystack, string needle)
    {
        var bytes = Encoding.UTF8.GetBytes(needle);
        return haystack.AsSpan().IndexOf(bytes) >= 0;
    }

    [Fact]
    public async Task DirectMessages_AreEncryptedAndReadableOnlyByMembers()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        Assert.Equal(E2eeStatus.Ready, alice.E2eeService.Status);
        Assert.Equal(E2eeStatus.Ready, bob.E2eeService.Status);

        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var sent = await SendAsync(alice, dm, "meet at the usual place");
        Assert.True(sent.Success, sent.Message);
        Assert.Equal("meet at the usual place", sent.Data.Content);

        var stored = await WaitForStoredAsync(sent.Data.Id);
        Assert.Equal(MessageEncryption.EndToEnd, stored.EncryptionVersion);
        Assert.Equal(string.Empty, stored.Content);
        Assert.False(ContainsUtf8(stored.Envelope, "usual place"));
        Assert.NotNull(stored.SearchTerms);

        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        Assert.True(bobDm.IsEncrypted);
        var history = await bobDm.GetMessagesAsync(long.MaxValue, 20);
        var received = Assert.Single(history, m => m.Id == sent.Data.Id);
        Assert.Equal(MessageDecryptionState.Decrypted, received.DecryptionState);
        Assert.Equal("meet at the usual place", received.Content);

        var reply = await SendAsync(bob, bobDm, "see you there", replyToId: sent.Data.Id);
        Assert.True(reply.Success, reply.Message);

        var aliceHistory = await dm.GetMessagesAsync(long.MaxValue, 20);
        var aliceReply = Assert.Single(aliceHistory, m => m.Id == reply.Data.Id);
        Assert.Equal("see you there", aliceReply.Content);
        Assert.Equal("meet at the usual place", aliceReply.ReplyTo?.Content);
    }

    [Fact]
    public async Task EncryptedChannel_RefusesPlainText()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        Assert.True((await SendAsync(alice, dm, "first")).Success);

        var plain = await alice.PrimaryNode.PostAsyncWithResponse<Message>("api/messages", new Message(alice)
        {
            Content = "trying plain text",
            ChannelId = dm.Id,
            AuthorUserId = alice.Me.Id,
            Fingerprint = Guid.NewGuid().ToString()
        });
        Assert.False(plain.Success);
        Assert.Contains("end-to-end encrypted", plain.Message);
    }

    [Fact]
    public async Task Edits_KeepEncryptionAndProveEarlierRevisions()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var sent = await SendAsync(alice, dm, "original words");
        Assert.True(sent.Success, sent.Message);
        await WaitForStoredAsync(sent.Data.Id);

        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        var seen = (await bobDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == sent.Data.Id);
        var evidence = E2eeService.BuildEvidence(seen);

        var mine = (await dm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == sent.Data.Id);
        mine.Content = "edited words";
        var edit = await mine.UpdateAsync();
        Assert.True(edit.Success, edit.Message);
        Assert.Equal("edited words", edit.Data.Content);

        var afterEdit = (await bobDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == sent.Data.Id);
        Assert.Equal("edited words", afterEdit.Content);
        Assert.Equal(1, afterEdit.EncryptedRevision);

        // The earlier revision is still provable after the edit.
        var report = await bob.SafetyService.PostReportAsync(new Report()
        {
            ReportingUserId = bob.Me.Id,
            MessageId = sent.Data.Id,
            ChannelId = dm.Id,
            ReasonCode = ReportReasonCode.IsTargetedHarassment,
            LongReason = "test",
            Evidence = [evidence]
        });
        Assert.True(report.Success, report.Message);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var stored = await db.ReportEvidenceEntries.AsNoTracking().FirstAsync(x => x.MessageId == sent.Data.Id);
        Assert.Equal((int)ReportEvidenceVerification.AuthorSigned, stored.Verification);
        Assert.Equal("original words", stored.Content);
    }

    [Fact]
    public async Task Reports_ProveDeletedMessagesAndRejectForgedText()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var sent = await SendAsync(alice, dm, "something awful");
        Assert.True(sent.Success, sent.Message);
        await WaitForStoredAsync(sent.Data.Id);

        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        var seen = (await bobDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == sent.Data.Id);
        var evidence = E2eeService.BuildEvidence(seen);

        // The author deletes it; only a proof without text remains.
        var delete = await alice.PrimaryNode.DeleteAsync($"api/messages/{sent.Data.Id}");
        Assert.True(delete.Success, delete.Message);

        var forged = E2eeService.BuildEvidence(seen);
        forged.Content = "something nice";

        var genuine = await bob.SafetyService.PostReportAsync(new Report()
        {
            ReportingUserId = bob.Me.Id,
            MessageId = sent.Data.Id,
            ChannelId = dm.Id,
            ReasonCode = ReportReasonCode.IsTargetedHarassment,
            LongReason = "genuine",
            Evidence = [evidence]
        });
        Assert.True(genuine.Success, genuine.Message);

        var fake = await bob.SafetyService.PostReportAsync(new Report()
        {
            ReportingUserId = bob.Me.Id,
            MessageId = sent.Data.Id,
            ChannelId = dm.Id,
            ReasonCode = ReportReasonCode.IsTargetedHarassment,
            LongReason = "forged",
            Evidence = [forged]
        });
        Assert.True(fake.Success, fake.Message);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var rows = await db.ReportEvidenceEntries.AsNoTracking().Where(x => x.MessageId == sent.Data.Id).ToListAsync();
        Assert.Contains(rows, r => r.Content == "something awful" && r.Verification == (int)ReportEvidenceVerification.AuthorSigned);
        Assert.Contains(rows, r => r.Content == "something nice" && r.Verification == (int)ReportEvidenceVerification.Unverified);

        var reports = await db.Reports.AsNoTracking().Where(x => x.MessageId == sent.Data.Id).ToListAsync();
        Assert.All(reports, r => Assert.Equal(alice.Me.Id, r.ReportedUserId));
    }

    [Fact]
    public async Task DecryptedText_EscapesUnsafeMarkdownButReportsTheSignedText()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        const string text = "look [](https://example.com/track.png)";
        var sent = await SendAsync(alice, dm, text);
        Assert.True(sent.Success, sent.Message);
        await WaitForStoredAsync(sent.Data.Id);

        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        var seen = (await bobDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == sent.Data.Id);
        Assert.Equal(MessageMarkdownSafety.Escape(text), seen.Content);
        Assert.Equal(text, seen.SignedContent);

        var report = await bob.SafetyService.PostReportAsync(new Report()
        {
            ReportingUserId = bob.Me.Id,
            MessageId = sent.Data.Id,
            ChannelId = dm.Id,
            ReasonCode = ReportReasonCode.IsTargetedHarassment,
            LongReason = "tracking image",
            Evidence = [E2eeService.BuildEvidence(seen)]
        });
        Assert.True(report.Success, report.Message);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var stored = await db.ReportEvidenceEntries.AsNoTracking().FirstAsync(x => x.MessageId == sent.Data.Id);
        Assert.Equal(text, stored.Content);
        Assert.Equal((int)ReportEvidenceVerification.AuthorSigned, stored.Verification);
    }

    [Fact]
    public async Task Search_FindsWordsWithoutServerReadingThem()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var target = await SendAsync(alice, dm, "The package arrives Thursday afternoon");
        Assert.True(target.Success, target.Message);
        Assert.True((await SendAsync(alice, dm, "unrelated chatter")).Success);
        await WaitForStoredAsync(target.Data.Id);

        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        var results = await bobDm.SearchMessagesAsync("thurs");
        var hit = Assert.Single(results);
        Assert.Equal(target.Data.Id, hit.Id);
        Assert.Equal("The package arrives Thursday afternoon", hit.Content);

        Assert.Empty(await bobDm.SearchMessagesAsync("friday"));
    }

    [Fact]
    public async Task NewDevice_LinksByScanningItsCodeAndReadsHistory()
    {
        var details = await _fixture.RegisterUser();
        var laptop = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: false);
        Assert.Equal(E2eeStatus.NotSetUp, laptop.E2eeService.Status);
        var recoveryCode = await laptop.E2eeService.SetUpAsync();
        Assert.True(recoveryCode.Success, recoveryCode.Message);

        var (friend, _) = await CreateUserAsync();
        var dm = await laptop.ChannelService.FetchDmChannelAsync(friend.Me.Id, create: true);
        var sent = await SendAsync(laptop, dm, "sent before the phone existed");
        Assert.True(sent.Success, sent.Message);
        await WaitForStoredAsync(sent.Data.Id);

        var phone = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: false);
        Assert.Equal(E2eeStatus.NeedsVerification, phone.E2eeService.Status);

        var start = await phone.E2eeService.StartLinkingAsync();
        Assert.True(start.Success, start.Message);

        var pending = await laptop.E2eeService.GetPendingLinkRequestsAsync();
        var request = Assert.Single(pending, p => p.Id == start.Data.Session.Id);

        // Approving needs the secret from the new device's screen.
        var unscanned = await laptop.E2eeService.ApproveLinkAsync(request);
        Assert.False(unscanned.Success);

        var scanned = await laptop.E2eeService.MatchScannedLinkCodeAsync(start.Data.QrCode);
        Assert.True(scanned.Success, scanned.Message);

        var approve = await laptop.E2eeService.ApproveLinkAsync(scanned.Data);
        Assert.True(approve.Success, approve.Message);

        var complete = await phone.E2eeService.CompleteLinkAsync();
        Assert.True(complete.Success, complete.Message);
        Assert.Equal(E2eeStatus.Ready, phone.E2eeService.Status);

        var phoneDm = await phone.ChannelService.FetchDmChannelAsync(friend.Me.Id, skipCache: true);
        var history = await phoneDm.GetMessagesAsync(long.MaxValue, 10);
        Assert.Equal("sent before the phone existed", history.Single(m => m.Id == sent.Data.Id).Content);

        // Removing the phone replaces the user key; the laptop keeps working.
        var revoke = await laptop.E2eeService.RevokeDeviceAsync(phone.E2eeService.Device.DeviceId);
        Assert.True(revoke.Success, revoke.Message);
        var afterRevoke = await SendAsync(laptop, dm, "after removing the phone");
        Assert.True(afterRevoke.Success, afterRevoke.Message);
    }

    [Fact]
    public async Task NewDevice_LinksByScanningExistingDevice()
    {
        var details = await _fixture.RegisterUser();
        var desktop = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: true);
        var tablet = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: false);
        Assert.Equal(E2eeStatus.NeedsVerification, tablet.E2eeService.Status);

        var shown = await desktop.E2eeService.ShowLinkCodeAsync();
        Assert.True(shown.Success, shown.Message);

        var joined = await tablet.E2eeService.JoinLinkAsync(shown.Data.QrCode);
        Assert.True(joined.Success, joined.Message);

        var session = (await desktop.E2eeService.GetPendingLinkRequestsAsync()).Single(s => s.Id == shown.Data.Session.Id);
        var approve = await desktop.E2eeService.ApproveLinkAsync(session);
        Assert.True(approve.Success, approve.Message);

        Assert.True((await tablet.E2eeService.CompleteLinkAsync()).Success);
        Assert.Equal(E2eeStatus.Ready, tablet.E2eeService.Status);
    }

    [Fact]
    public async Task NewDevice_LinksByTypingTheCodeAndReceivesPins()
    {
        var details = await _fixture.RegisterUser();
        var desktop = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: true);

        // The desktop pins a contact and marks them verified.
        var (friend, _) = await CreateUserAsync();
        Assert.True(await desktop.E2eeService.HasEncryptionAsync(friend.Me.Id));
        Assert.True((await desktop.E2eeService.SetVerifiedAsync(friend.Me.Id, true,
            await desktop.E2eeService.GetSafetyNumberAsync(friend.Me.Id))).Success);
        Assert.True(await desktop.E2eeService.IsVerifiedAsync(friend.Me.Id));

        var laptop = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: false);
        Assert.Equal(E2eeStatus.NeedsVerification, laptop.E2eeService.Status);

        var shown = await desktop.E2eeService.ShowLinkCodeAsync();
        Assert.True(shown.Success, shown.Message);
        Assert.Matches("^[0-9A-Z]{4}(-[0-9A-Z]{4}){3}$", shown.Data.TypedCode);

        var joined = await laptop.E2eeService.JoinLinkAsync(shown.Data.TypedCode.ToLowerInvariant());
        Assert.True(joined.Success, joined.Message);
        Assert.Equal(shown.Data.Session.Id, joined.Data.Id);

        var session = (await desktop.E2eeService.GetPendingLinkRequestsAsync()).Single(s => s.Id == shown.Data.Session.Id);
        var approve = await desktop.E2eeService.ApproveLinkAsync(session);
        Assert.True(approve.Success, approve.Message);

        var complete = await laptop.E2eeService.CompleteLinkAsync();
        Assert.True(complete.Success, complete.Message);
        Assert.Equal(E2eeStatus.Ready, laptop.E2eeService.Status);

        // The approving device's verification mark came with the link.
        Assert.True(await laptop.E2eeService.IsVerifiedAsync(friend.Me.Id));
    }

    [Fact]
    public async Task NewDevice_RefusesAnApprovalWithoutTheLinkSecret()
    {
        var details = await _fixture.RegisterUser();
        var desktop = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: true);
        var phone = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: false);

        var start = await phone.E2eeService.StartLinkingAsync();
        Assert.True(start.Success, start.Message);

        // A device that signs a valid entry and seals the real user key, but
        // never saw the phone's code, stands in for a server-controlled one.
        var session = (await desktop.E2eeService.GetPendingLinkRequestsAsync()).Single(s => s.Id == start.Data.Session.Id);
        var descriptor = session.Device.ToDescriptor();
        var log = (await desktop.PrimaryNode.GetJsonAsync<List<UserKeyLogEntry>>(
            $"api/e2ee/users/{desktop.Me.Id}/log", cacheDurationMs: null)).Data;
        var state = UserKeyLogVerifier.Verify(desktop.Me.Id, log);
        var signer = desktop.E2eeService.Device;
        var userKey = desktop.E2eeService.CurrentUserKey;
        var entry = UserKeyLogBuilder.AddDevice(state, signer.DeviceId, signer.Sign, descriptor,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var box = new UserKeyBoxDto
        {
            UserId = desktop.Me.Id,
            Generation = userKey.Generation,
            RecipientId = descriptor.Keys.DeviceId,
            Box = userKey.SealTo(desktop.Me.Id, descriptor.Keys.DeviceId, descriptor.Keys.EncryptPublicKey)
        };
        var wrongSecret = E2eeCrypto.RandomBytes(DeviceLinkCode.NewDeviceSecretLength);
        var forged = await desktop.PrimaryNode.PostAsyncWithResponse<DeviceLinkSessionDto>(
            $"api/e2ee/link-sessions/{session.Id}/approve", new ApproveDeviceLinkRequest
            {
                Entry = entry,
                Box = box,
                ApprovalMac = DeviceLinkVerification.ApprovalMac(wrongSecret, desktop.Me.Id, session.Id,
                    descriptor.Keys.DeviceId, entry.Body, box.Generation, box.Box, null)
            });
        Assert.True(forged.Success, forged.Message);

        var complete = await phone.E2eeService.CompleteLinkAsync();
        Assert.False(complete.Success);
        Assert.NotEqual(E2eeStatus.Ready, phone.E2eeService.Status);
        Assert.Null(phone.E2eeService.Device);
    }

    [Fact]
    public async Task RecoveryCode_RestoresAccessOnFreshDevice()
    {
        var details = await _fixture.RegisterUser();
        var original = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: false);
        var code = await original.E2eeService.SetUpAsync();
        Assert.True(code.Success, code.Message);

        var (friend, _) = await CreateUserAsync();
        var dm = await friend.ChannelService.FetchDmChannelAsync(original.Me.Id, create: true);
        var sent = await SendAsync(friend, dm, "do you still have your keys");
        Assert.True(sent.Success, sent.Message);
        await WaitForStoredAsync(sent.Data.Id);

        var fresh = await LoginAsync(details, new MemoryE2eeKeyStore(), autoSetUp: false);
        Assert.Equal(E2eeStatus.NeedsVerification, fresh.E2eeService.Status);
        Assert.False((await fresh.E2eeService.RestoreWithRecoveryCodeAsync("AAAA-BBBB-CCCC-DDDD-EEEE-FFFF-GGGG-HHHH")).Success);

        var restore = await fresh.E2eeService.RestoreWithRecoveryCodeAsync(code.Data);
        Assert.True(restore.Success, restore.Message);

        var freshDm = await fresh.ChannelService.FetchDmChannelAsync(friend.Me.Id, skipCache: true);
        var history = await freshDm.GetMessagesAsync(long.MaxValue, 10);
        Assert.Equal("do you still have your keys", history.Single(m => m.Id == sent.Data.Id).Content);
    }

    /// <summary>
    /// Stores a plain-text message the way messages were stored before
    /// encryption became mandatory.
    /// </summary>
    private async Task<long> InsertLegacyMessageAsync(Channel channel, long authorUserId, string content)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var id = Valour.Server.Database.IdManager.Generate();
        db.Messages.Add(new Valour.Database.Message
        {
            Id = id,
            ChannelId = channel.Id,
            PlanetId = channel.PlanetId,
            AuthorUserId = authorUserId,
            Content = content,
            TimeSent = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return id;
    }

    /// <summary>
    /// Runs the sealing worker until the given messages are sealed. The
    /// shared test database holds plain text from other tests, so this can
    /// take several batches.
    /// </summary>
    private async Task<bool> SealLegacyAsync(params long[] messageIds)
    {
        for (var pass = 0; pass < 500; pass++)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<E2eeMaintenanceService>().SealLegacyBatchAsync(5000);
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            if (!await db.Messages.AnyAsync(x => messageIds.Contains(x.Id) && x.EncryptionVersion == MessageEncryption.None))
                return true;
        }

        return false;
    }

    [Fact]
    public async Task LegacyHistory_IsSealedWithAServerKeyThatMembersReplace()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var legacyId = await InsertLegacyMessageAsync(dm, alice.Me.Id, "written before encryption");

        Assert.True(await SealLegacyAsync(legacyId));

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var stored = await db.Messages.AsNoTracking().FirstAsync(x => x.Id == legacyId);
            Assert.Equal(MessageEncryption.ServerSealed, stored.EncryptionVersion);
            Assert.Equal(string.Empty, stored.Content);
            Assert.False(ContainsUtf8(stored.Envelope, "before encryption"));

            // The server created the channel's first key and gave it to both
            // members, so it no longer holds it.
            var generation = await db.E2eeChannelKeyGenerations.AsNoTracking()
                .SingleAsync(x => x.ChannelId == dm.Id);
            Assert.Equal(ChannelKeyGenerationRecord.ServerCreatorId, generation.CreatorUserId);
            Assert.Null(generation.HeldSecretProtected);
            Assert.Equal(2, await db.E2eeChannelKeyBoxes.CountAsync(x => x.ChannelId == dm.Id && x.Generation == 1));
        }

        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        var sealedMessage = (await bobDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == legacyId);
        Assert.Equal(MessageDecryptionState.Decrypted, sealedMessage.DecryptionState);
        Assert.Equal("written before encryption", sealedMessage.Content);
        Assert.Equal(ServerSealedKind.Legacy, sealedMessage.SealedKind);

        // Members replace the server's key, including its search key, before sending.
        var aliceDm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, skipCache: true);
        var sent = await SendAsync(alice, aliceDm, "first encrypted message");
        Assert.True(sent.Success, sent.Message);
        var sentStored = await WaitForStoredAsync(sent.Data.Id);
        Assert.Equal(2, sentStored.KeyGeneration);
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var second = await db.E2eeChannelKeyGenerations.AsNoTracking()
                .SingleAsync(x => x.ChannelId == dm.Id && x.Generation == 2);
            Assert.Equal(2, second.IndexGeneration);
        }

        // A report on sealed history is checked against the server's attestation.
        var report = await bob.SafetyService.PostReportAsync(new Report()
        {
            ReportingUserId = bob.Me.Id,
            MessageId = legacyId,
            ChannelId = dm.Id,
            ReasonCode = ReportReasonCode.IsTargetedHarassment,
            LongReason = "sealed",
            Evidence = [E2eeService.BuildEvidence(sealedMessage)]
        });
        Assert.True(report.Success, report.Message);
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var evidence = await db.ReportEvidenceEntries.AsNoTracking().FirstAsync(x => x.MessageId == legacyId);
            Assert.Equal((int)ReportEvidenceVerification.ServerAttested, evidence.Verification);
        }

        // The author can still edit their own message from before encryption.
        var mine = (await aliceDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == legacyId);
        mine.Content = "edited after encryption";
        var edit = await mine.UpdateAsync();
        Assert.True(edit.Success, edit.Message);
        var edited = (await bobDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == legacyId);
        Assert.Equal("edited after encryption", edited.Content);
        Assert.Equal(MessageEncryption.EndToEnd, edited.EncryptionVersion);
    }

    [Fact]
    public async Task ServerKey_IsHeldUntilEnoughMembersHoldIt()
    {
        var (carol, carolDetails) = await CreateUserAsync(setUp: false);
        var (dave, daveDetails) = await CreateUserAsync(setUp: false);
        var dm = await carol.ChannelService.FetchDmChannelAsync(dave.Me.Id, create: true);
        var legacyId = await InsertLegacyMessageAsync(dm, carol.Me.Id, "sent while nobody had keys");

        Assert.True(await SealLegacyAsync(legacyId));
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var generation = await db.E2eeChannelKeyGenerations.AsNoTracking().SingleAsync(x => x.ChannelId == dm.Id);
            Assert.NotNull(generation.HeldSecretProtected);
        }

        // Carol's first login sets up her keys; loading the channel delivers the held key.
        carol = await LoginAsync(carolDetails, new MemoryE2eeKeyStore(), autoSetUp: true);
        var carolDm = await carol.ChannelService.FetchDmChannelAsync(dave.Me.Id, skipCache: true);
        var readable = (await carolDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == legacyId);
        Assert.Equal("sent while nobody had keys", readable.Content);

        // One member alone could lose the key by resetting, so the server
        // keeps its copy until both members of the chat hold it.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var generation = await db.E2eeChannelKeyGenerations.AsNoTracking().SingleAsync(x => x.ChannelId == dm.Id);
            Assert.NotNull(generation.HeldSecretProtected);
        }

        dave = await LoginAsync(daveDetails, new MemoryE2eeKeyStore(), autoSetUp: true);
        var daveDm = await dave.ChannelService.FetchDmChannelAsync(carol.Me.Id, skipCache: true);
        var served = (await daveDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == legacyId);
        Assert.Equal("sent while nobody had keys", served.Content);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var generation = await db.E2eeChannelKeyGenerations.AsNoTracking().SingleAsync(x => x.ChannelId == dm.Id);
            Assert.Null(generation.HeldSecretProtected);
            Assert.Equal(2, await db.E2eeChannelKeyBoxes.CountAsync(x => x.ChannelId == dm.Id && x.Generation == 1));
        }
    }

    [Fact]
    public async Task GroupChat_RemovedMemberCannotReadNewMessages()
    {
        var (owner, _) = await CreateUserAsync();
        var (member, _) = await CreateUserAsync();
        var (leaver, _) = await CreateUserAsync();

        var create = await owner.ChannelService.CreateGroupDmAsync("Secret group", [member.Me.Id, leaver.Me.Id]);
        Assert.True(create.Success, create.Message);
        owner.Cache.Channels.TryGet(create.Data.Id, out var group);

        var first = await SendAsync(owner, group, "hello everyone");
        Assert.True(first.Success, first.Message);
        await WaitForStoredAsync(first.Data.Id);

        var leaverGroup = await leaver.ChannelService.FetchDirectChannelAsync(group.Id, skipCache: true);
        var leaverView = await leaverGroup.GetMessagesAsync(long.MaxValue, 10);
        Assert.Equal("hello everyone", leaverView.Single(m => m.Id == first.Data.Id).Content);

        var removed = await owner.ChannelService.RemoveGroupDmMemberAsync(group.Id, leaver.Me.Id);
        Assert.True(removed.Success, removed.Message);

        var afterRemoval = await SendAsync(owner, group, "just us now");
        Assert.True(afterRemoval.Success, afterRemoval.Message);
        var stored = await WaitForStoredAsync(afterRemoval.Data.Id);
        Assert.True(stored.KeyGeneration > 1);

        var memberGroup = await member.ChannelService.FetchDirectChannelAsync(group.Id, skipCache: true);
        var memberView = await memberGroup.GetMessagesAsync(long.MaxValue, 10);
        Assert.Equal("just us now", memberView.Single(m => m.Id == afterRemoval.Data.Id).Content);

        // The removed member's old keys cannot open the new generation.
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        Assert.False(await db.E2eeChannelKeyBoxes.AnyAsync(x =>
            x.ChannelId == group.Id && x.UserId == leaver.Me.Id && x.Generation == stored.KeyGeneration));
    }

    /// <summary>
    /// Closes a client's realtime connections without reconnecting, so it
    /// stops answering key requests the way an offline member would. Its
    /// HTTP calls keep working.
    /// </summary>
    private static async Task GoOfflineAsync(ValourClient client)
    {
        foreach (var node in client.NodeService.Nodes.ToList())
            await node.DisposeFailedRealtimeConnectionAsync();
    }

    private async Task<(Planet Planet, Channel Channel)> CreatePlanetAsync(ValourClient owner, bool isPublic = true)
    {
        var create = await new Planet(owner)
        {
            Name = $"E2EE {Guid.NewGuid().ToString()[..8]}",
            Description = "Encryption test planet",
            Public = isPublic,
            Discoverable = true
        }.CreateAsync();
        Assert.True(create.Success, create.Message);

        var planet = await owner.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
        await planet.EnsureReadyAsync();
        var channel = await planet.FetchPrimaryChatChannelAsync();
        return (planet, channel);
    }

    [Fact]
    public async Task OpenPlanet_MembersReceiveKeysFromOnlineHolders()
    {
        var (owner, _) = await CreateUserAsync();
        var (member, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);

        var enable = await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.Open, sharesHistory: true);
        Assert.True(enable.Success, enable.Message);

        var first = await SendAsync(owner, channel, "welcome to the encrypted planet", planet.MyMember.Id);
        Assert.True(first.Success, first.Message);
        var stored = await WaitForStoredAsync(first.Data.Id);
        Assert.Equal(MessageEncryption.EndToEnd, stored.EncryptionVersion);

        var join = await member.PlanetService.JoinPlanetAsync(planet.Id);
        Assert.True(join.Success, join.Message);
        var memberPlanet = await member.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await memberPlanet.EnsureReadyAsync();
        var memberChannel = await memberPlanet.FetchChannelAsync(channel.Id);

        // Without the key the message waits, and a key request is recorded.
        var waiting = (await memberChannel.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == first.Data.Id);
        Assert.Equal(MessageDecryptionState.WaitingForKey, waiting.DecryptionState);

        // The owner, who holds the key, serves the request.
        var ownerChannel = await planet.FetchChannelAsync(channel.Id);
        await owner.E2eeService.ServeChannelAsync(ownerChannel);

        await member.E2eeService.GetKeyRingAsync(memberChannel, refresh: true);
        var readable = (await memberChannel.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == first.Data.Id);
        Assert.Equal("welcome to the encrypted planet", readable.Content);

        var answer = await SendAsync(member, memberChannel, "glad to be here", memberPlanet.MyMember.Id);
        Assert.True(answer.Success, answer.Message);
    }

    [Fact]
    public async Task EncryptedAutomod_BlocksTriggerWordsFromTerms()
    {
        var (owner, _) = await CreateUserAsync();
        var (member, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.Open, true)).Success);

        var trigger = await owner.AutomodService.CreateTriggerAsync(new CreateAutomodTriggerRequest
        {
            Trigger = new AutomodTrigger(owner)
            {
                PlanetId = planet.Id,
                Name = "No secrets",
                Type = AutomodTriggerType.Blacklist,
                TriggerWords = "forbiddenword"
            },
            Actions =
            [
                new AutomodAction(owner) { PlanetId = planet.Id, ActionType = AutomodActionType.BlockMessage, Strikes = 1 }
            ]
        });
        Assert.True(trigger.Success, trigger.Message);

        Assert.True((await member.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        var memberPlanet = await member.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await memberPlanet.EnsureReadyAsync();
        var memberChannel = await memberPlanet.FetchChannelAsync(channel.Id);
        var ownerChannel = await planet.FetchChannelAsync(channel.Id);
        Assert.True((await owner.E2eeService.EnableChannelEncryptionAsync(ownerChannel)).Success);
        await member.E2eeService.RequestKeysAsync(memberChannel);
        await owner.E2eeService.ServeChannelAsync(ownerChannel);
        var ring = await member.E2eeService.GetKeyRingAsync(memberChannel, refresh: true);
        Assert.NotNull(ring.Latest);

        var sync = await owner.E2eeService.SyncAutomodTermsAsync(planet);
        Assert.True(sync.Success, sync.Message);

        var blocked = await SendAsync(member, memberChannel, "this has a F0RBIDDENWORD in it", memberPlanet.MyMember.Id);
        Assert.False(blocked.Success);
        Assert.True(blocked.Message?.Contains("automod", StringComparison.OrdinalIgnoreCase) == true, blocked.Message);

        var allowed = await SendAsync(member, memberChannel, "this is fine", memberPlanet.MyMember.Id);
        Assert.True(allowed.Success, allowed.Message);
    }

    [Fact]
    public async Task InviteOnlyPlanet_KeysGoOnlyToAdmittedMembers()
    {
        var (owner, _) = await CreateUserAsync();
        var (outsider, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);

        var enable = await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.InviteOnly, true);
        Assert.True(enable.Success, enable.Message);
        Assert.True((await SendAsync(owner, channel, "admitted eyes only", planet.MyMember.Id)).Success);

        Assert.True((await outsider.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        var outsiderPlanet = await outsider.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await outsiderPlanet.EnsureReadyAsync();
        var outsiderChannel = await outsiderPlanet.FetchChannelAsync(channel.Id);
        await outsider.E2eeService.RequestKeysAsync(outsiderChannel);

        // The server lets them join, but no one shares keys until an admin admits them.
        var ownerChannel = await planet.FetchChannelAsync(channel.Id);
        await owner.E2eeService.ServeChannelAsync(ownerChannel);
        var ring = await outsider.E2eeService.GetKeyRingAsync(outsiderChannel, refresh: true);
        Assert.Null(ring.Latest);

        var pending = await owner.E2eeService.GetPendingAdmissionsAsync(planet);
        Assert.Contains(pending, p => p.UserId == outsider.Me.Id);

        Assert.True((await owner.E2eeService.AdmitPlanetMembersAsync(planet, [outsider.Me.Id])).Success);
        await owner.E2eeService.ServeChannelAsync(ownerChannel);
        ring = await outsider.E2eeService.GetKeyRingAsync(outsiderChannel, refresh: true);
        Assert.NotNull(ring.Latest);
    }

    [Fact]
    public async Task InviteOnlyPlanet_SignedInviteAdmitsWithoutAnAdmin()
    {
        var (owner, _) = await CreateUserAsync();
        var (guest, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.InviteOnly, true)).Success);

        var invite = await owner.E2eeService.CreatePlanetInviteAsync(planet, DateTime.UtcNow.AddDays(1), maxUses: 1);
        Assert.True(invite.Success, invite.Message);
        Assert.True(EncryptedInvite.TryParseFragment(invite.Data.Fragment, out var parsed));

        Assert.True((await guest.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        var guestPlanet = await guest.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        var redeem = await guest.E2eeService.RedeemPlanetInviteAsync(guestPlanet, parsed);
        Assert.True(redeem.Success, redeem.Message);

        var pending = await owner.E2eeService.GetPendingAdmissionsAsync(planet);
        Assert.DoesNotContain(pending, p => p.UserId == guest.Me.Id);

        // The single-use invite cannot admit anyone else.
        var (second, _) = await CreateUserAsync();
        Assert.True((await second.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        var secondPlanet = await second.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        Assert.False((await second.E2eeService.RedeemPlanetInviteAsync(secondPlanet, parsed)).Success);
    }

    [Fact]
    public async Task InviteOnlyPlanet_DevicesKeepEnforcingTheLogWhenTheServerClaimsItIsOpen()
    {
        var (owner, _) = await CreateUserAsync();
        var (outsider, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.InviteOnly, true)).Success);
        Assert.True((await SendAsync(owner, channel, "members only", planet.MyMember.Id)).Success);

        var reopen = await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.Open, true);
        Assert.False(reopen.Success);

        // A compromised server relabels the planet as open without the owner.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            await db.Planets.Where(x => x.Id == planet.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(p => p.EncryptionMode, PlanetEncryptionMode.Open));
            var hosted = await scope.ServiceProvider.GetRequiredService<HostedPlanetService>().GetRequiredAsync(planet.Id);
            hosted.Planet.EncryptionMode = PlanetEncryptionMode.Open;
        }

        Assert.True((await outsider.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        var outsiderPlanet = await outsider.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await outsiderPlanet.EnsureReadyAsync();
        var outsiderChannel = await outsiderPlanet.FetchChannelAsync(channel.Id);
        await outsider.E2eeService.RequestKeysAsync(outsiderChannel);

        var reported = await planet.Node.GetJsonAsync<ChannelKeyStateDto>(
            $"api/e2ee/channels/{channel.Id}/keys?planetId={planet.Id}", cacheDurationMs: null);
        Assert.Equal(ChannelKeyPolicy.PlanetOpen, reported.Data.Policy);

        var ownerChannel = await planet.FetchChannelAsync(channel.Id);
        var ownerRing = await owner.E2eeService.GetKeyRingAsync(ownerChannel, refresh: true);
        Assert.Equal(ChannelKeyPolicy.PlanetInviteOnly, ownerRing.Policy);
        await owner.E2eeService.ServeChannelAsync(ownerChannel);

        var ring = await outsider.E2eeService.GetKeyRingAsync(outsiderChannel, refresh: true);
        Assert.Null(ring.Latest);
    }

    [Fact]
    public async Task EncryptedPlanet_MovesBetweenNodesWithItsKeysAndHistory()
    {
        var (owner, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        var sent = await SendAsync(owner, channel, "before the move", planet.MyMember.Id);
        Assert.True(sent.Success, sent.Message);
        await WaitForStoredAsync(sent.Data.Id);
        var legacyId = await InsertLegacyMessageAsync(channel, owner.Me.Id, "sealed before the move");
        Assert.True(await SealLegacyAsync(legacyId));

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var snapshots = scope.ServiceProvider.GetRequiredService<PlanetSnapshotService>();
            var export = await snapshots.ExportAsync(planet.Id);
            Assert.True(export.Success, export.Message);
            var snapshot = export.Data;
            Assert.Contains(snapshot.ChannelKeyGenerations, g => g.ChannelId == channel.Id);
            Assert.Contains(snapshot.ChannelKeyBoxes, b => b.ChannelId == channel.Id && b.UserId == owner.Me.Id);
            Assert.All(snapshot.Messages, m => Assert.NotEqual(MessageEncryption.None, m.EncryptionVersion));
            Assert.NotEmpty(snapshot.ServerPublicKeys);

            // Arrive as if from another node: every node-local id except
            // channel ids is replaced.
            snapshot.SourceDomain = "community.example";
            Assert.True((await snapshots.DeletePlanetDataAsync(planet.Id)).Success);
            scope.ServiceProvider.GetRequiredService<HostedPlanetService>().Remove(planet.Id);
            var import = await snapshots.ImportAsync(snapshot);
            Assert.True(import.Success, import.Message);
        }

        var (reader, _) = await CreateUserAsync();
        Assert.True((await reader.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        var moved = await owner.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await moved.EnsureReadyAsync();
        var movedChannel = await moved.FetchChannelAsync(channel.Id);
        Assert.NotNull(movedChannel);

        var history = await movedChannel.GetMessagesAsync(long.MaxValue, 10);
        Assert.Contains(history, m => m.Content == "before the move" && m.DecryptionState == MessageDecryptionState.Decrypted);
        Assert.Contains(history, m => m.Content == "sealed before the move" && m.SealedKind == ServerSealedKind.Legacy);
        Assert.DoesNotContain(history, m => m.Id == sent.Data.Id);

        // Keys held before the move still serve new members afterwards.
        var readerPlanet = await reader.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await readerPlanet.EnsureReadyAsync();
        var readerChannel = await readerPlanet.FetchChannelAsync(channel.Id);
        await reader.E2eeService.RequestKeysAsync(readerChannel);
        await owner.E2eeService.ServeChannelAsync(movedChannel);
        await reader.E2eeService.GetKeyRingAsync(readerChannel, refresh: true);
        var readerHistory = await readerChannel.GetMessagesAsync(long.MaxValue, 10);
        Assert.Contains(readerHistory, m => m.Content == "before the move");
    }

    [Fact]
    public async Task LargeOpenPlanet_ReplacesKeysAfterDeparturesOnASchedule()
    {
        var (owner, _) = await CreateUserAsync();
        var (member, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await member.PlanetService.JoinPlanetAsync(planet.Id)).Success);

        var memberPlanet = await member.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await memberPlanet.EnsureReadyAsync();
        var memberChannel = await memberPlanet.FetchChannelAsync(channel.Id);
        Assert.True((await SendAsync(owner, channel, "hello all", planet.MyMember.Id)).Success);
        await member.E2eeService.RequestKeysAsync(memberChannel);
        await owner.E2eeService.ServeChannelAsync(await planet.FetchChannelAsync(channel.Id));
        Assert.NotNull((await member.E2eeService.GetKeyRingAsync(memberChannel, refresh: true)).Latest);

        var config = Valour.Config.Configs.E2eeConfig.Current ?? new Valour.Config.Configs.E2eeConfig();
        var previousThreshold = config.LargePlanetMembers;
        config.LargePlanetMembers = 1;
        try
        {
            Assert.True((await memberPlanet.MyMember.DeleteAsync()).Success);

            using var scope = _fixture.Factory.Services.CreateScope();
            var keys = scope.ServiceProvider.GetRequiredService<E2eeChannelKeyService>();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            E2eeChannelKeyService.ForgetRotationCheck(channel.Id);
            var serverChannel = (await db.Channels.AsNoTracking().FirstAsync(x => x.Id == channel.Id)).ToModel();
            var latest = serverChannel.EncryptionGeneration;

            // The key someone who left holds stays in use until it is due.
            Assert.False(await keys.IsRotationRequiredAsync(serverChannel, latest));

            await db.E2eeChannelKeyGenerations
                .Where(x => x.ChannelId == channel.Id && x.Generation == latest)
                .ExecuteUpdateAsync(x => x.SetProperty(g => g.CreatedAt, DateTime.UtcNow.AddDays(-8)));
            E2eeChannelKeyService.ForgetRotationCheck(channel.Id);
            Assert.True(await keys.IsRotationRequiredAsync(serverChannel, latest));
        }
        finally
        {
            config.LargePlanetMembers = previousThreshold;
        }
    }

    private async Task<(Planet Planet, Channel Channel)> JoinPlanetAsync(ValourClient client, long planetId, long channelId)
    {
        Assert.True((await client.PlanetService.JoinPlanetAsync(planetId)).Success);
        var planet = await client.PlanetService.FetchPlanetAsync(planetId, skipCache: true);
        await planet.EnsureReadyAsync();
        return (planet, await planet.FetchChannelAsync(channelId));
    }

    [Fact]
    public async Task NewKeys_AreSealedToExistingMembersRightAway()
    {
        var (owner, _) = await CreateUserAsync();
        var (member, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        var (_, memberChannel) = await JoinPlanetAsync(member, planet.Id, channel.Id);

        // The member joined before the channel had a key, so the first key is
        // sealed to them when it is created. Nobody has to share it later.
        var sent = await SendAsync(owner, channel, "welcome, everyone", planet.MyMember.Id);
        Assert.True(sent.Success, sent.Message);

        var seen = (await memberChannel.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == sent.Data.Id);
        Assert.Equal(MessageDecryptionState.Decrypted, seen.DecryptionState);
        Assert.Equal("welcome, everyone", seen.Content);
    }

    [Fact]
    public async Task Newcomer_SendsWithoutWaitingAndReadsHistoryOnceAHolderIsOnline()
    {
        var (owner, _) = await CreateUserAsync();
        var (newcomer, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        var before = await SendAsync(owner, channel, "said before you joined", planet.MyMember.Id);
        Assert.True(before.Success, before.Message);
        await GoOfflineAsync(owner);

        // Nobody online shares the key, so the newcomer starts a new one
        // sealed to the owner and can send right away.
        var (newcomerPlanet, newcomerChannel) = await JoinPlanetAsync(newcomer, planet.Id, channel.Id);
        var hello = await SendAsync(newcomer, newcomerChannel, "hello, I just arrived", newcomerPlanet.MyMember.Id);
        Assert.True(hello.Success, hello.Message);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var started = await db.E2eeChannelKeyGenerations.AsNoTracking()
                .SingleAsync(x => x.ChannelId == channel.Id && x.Generation == 2);
            var record = ChannelKeyGenerationRecord.Decode(started.Body);
            Assert.Equal(ChannelKeyRotationReason.KeyUnavailable, record.Reason);
            Assert.False(record.UnlocksPrevious);
            Assert.Equal(2, record.IndexGeneration);
        }

        var ownerChannel = await planet.FetchChannelAsync(channel.Id);
        var ownerView = (await ownerChannel.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == hello.Data.Id);
        Assert.Equal("hello, I just arrived", ownerView.Content);

        // Earlier history waits for a member who holds the older key.
        var waiting = (await newcomerChannel.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == before.Data.Id);
        Assert.Equal(MessageDecryptionState.WaitingForKey, waiting.DecryptionState);

        await owner.E2eeService.ServeChannelAsync(ownerChannel);
        await newcomer.E2eeService.GetKeyRingAsync(newcomerChannel, refresh: true);
        var history = (await newcomerChannel.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == before.Data.Id);
        Assert.Equal("said before you joined", history.Content);

        // Everything is shared, so the request is done.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            Assert.False(await db.E2eeKeyRequests.AnyAsync(x => x.ChannelId == channel.Id && x.UserId == newcomer.Me.Id));
        }
    }

    [Fact]
    public async Task Newcomer_WaitsForAHolderWhenThePlanetFiltersWords()
    {
        var (owner, _) = await CreateUserAsync();
        var (newcomer, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await SendAsync(owner, channel, "hi", planet.MyMember.Id)).Success);

        var trigger = await owner.AutomodService.CreateTriggerAsync(new CreateAutomodTriggerRequest
        {
            Trigger = new AutomodTrigger(owner)
            {
                PlanetId = planet.Id,
                Name = "Filter",
                Type = AutomodTriggerType.Blacklist,
                TriggerWords = "forbiddenword"
            },
            Actions = [new AutomodAction(owner) { PlanetId = planet.Id, ActionType = AutomodActionType.BlockMessage, Strikes = 1 }]
        });
        Assert.True(trigger.Success, trigger.Message);
        await GoOfflineAsync(owner);

        // A new search key would have no automod hashes until a moderator is
        // online, so the newcomer waits for the current key instead.
        var (newcomerPlanet, newcomerChannel) = await JoinPlanetAsync(newcomer, planet.Id, channel.Id);
        var blocked = await SendAsync(newcomer, newcomerChannel, "hello", newcomerPlanet.MyMember.Id);
        Assert.False(blocked.Success);
        Assert.Contains("automod", blocked.Message, StringComparison.OrdinalIgnoreCase);

        await owner.E2eeService.ServeChannelAsync(await planet.FetchChannelAsync(channel.Id));
        var sent = await SendAsync(newcomer, newcomerChannel, "hello", newcomerPlanet.MyMember.Id);
        Assert.True(sent.Success, sent.Message);
    }

    [Fact]
    public async Task PlanetHidingHistory_NewMembersMessagesStayReadable()
    {
        var (owner, _) = await CreateUserAsync();
        var (newcomer, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.Open, sharesHistory: false)).Success);
        var before = await SendAsync(owner, channel, "private history", planet.MyMember.Id);
        Assert.True(before.Success, before.Message);

        var (newcomerPlanet, newcomerChannel) = await JoinPlanetAsync(newcomer, planet.Id, channel.Id);
        await newcomer.E2eeService.RequestKeysAsync(newcomerChannel);
        var ownerChannel = await planet.FetchChannelAsync(channel.Id);
        await owner.E2eeService.ServeChannelAsync(ownerChannel);
        await newcomer.E2eeService.GetKeyRingAsync(newcomerChannel, refresh: true);

        var hidden = (await newcomerChannel.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == before.Data.Id);
        Assert.NotEqual(MessageDecryptionState.Decrypted, hidden.DecryptionState);

        // The key started for the newcomer has its own search key, so the
        // terms the newcomer computes match what the owner recomputes.
        var hello = await SendAsync(newcomer, newcomerChannel, "hello there", newcomerPlanet.MyMember.Id);
        Assert.True(hello.Success, hello.Message);
        var ownerView = (await ownerChannel.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == hello.Data.Id);
        Assert.Equal(MessageDecryptionState.Decrypted, ownerView.DecryptionState);
        Assert.Equal("hello there", ownerView.Content);
    }

    [Fact]
    public async Task OldKeys_ArePrunedAndLoadedOnDemand()
    {
        var (alice, _) = await CreateUserAsync();
        var bobStore = new MemoryE2eeKeyStore();
        var bobDetails = await _fixture.RegisterUser();
        var bob = await LoginAsync(bobDetails, bobStore, autoSetUp: true);

        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var first = await SendAsync(alice, dm, "the very first message");
        Assert.True(first.Success, first.Message);

        const int rotations = 40;
        for (var i = 0; i < rotations; i++)
            Assert.True((await alice.E2eeService.RotateChannelKeyAsync(dm, ChannelKeyRotationReason.Manual)).Success);

        // Each new key unlocks the previous one, so once Bob's device has
        // opened the chain it asks the server to keep only the newest box.
        var bobDmBefore = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        await bob.E2eeService.GetKeyRingAsync(bobDmBefore, refresh: true);
        List<int> bobBoxes = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            bobBoxes = await db.E2eeChannelKeyBoxes.AsNoTracking()
                .Where(x => x.ChannelId == dm.Id && x.UserId == bob.Me.Id)
                .Select(x => x.Generation)
                .ToListAsync();
            if (bobBoxes.Count == 1)
                break;
            await Task.Delay(200);
        }
        Assert.Equal([rotations + 1], bobBoxes);

        // A fresh session receives only the recent records, and loads older
        // ones when it reads the first message.
        var bobAgain = await LoginAsync(bobDetails, bobStore, autoSetUp: true);
        var state = await bobAgain.PrimaryNode.GetJsonAsync<ChannelKeyStateDto>(
            $"api/e2ee/channels/{dm.Id}/keys", cacheDurationMs: null);
        Assert.True(state.Success, state.Message);
        Assert.True(state.Data.Generations.Count <= E2eeChannelKeyService.RecentGenerations + 1);

        var bobDm = await bobAgain.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        var read = (await bobDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == first.Data.Id);
        Assert.Equal("the very first message", read.Content);
    }

    [Fact]
    public async Task PlanetHidingHistory_NewcomersShareAnUnusedKey()
    {
        var (owner, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.Open, sharesHistory: false)).Success);
        Assert.True((await SendAsync(owner, channel, "before anyone joined", planet.MyMember.Id)).Success);
        var ownerChannel = await planet.FetchChannelAsync(channel.Id);

        async Task<(ValourClient Client, Planet Planet, Channel Channel)> JoinAndReceiveAsync()
        {
            var (client, _) = await CreateUserAsync();
            var (joined, joinedChannel) = await JoinPlanetAsync(client, planet.Id, channel.Id);
            await client.E2eeService.RequestKeysAsync(joinedChannel);
            await owner.E2eeService.ServeChannelAsync(ownerChannel);
            Assert.NotNull((await client.E2eeService.GetKeyRingAsync(joinedChannel, refresh: true)).Latest);
            return (client, joined, joinedChannel);
        }

        async Task<int> LatestAsync()
        {
            using var scope = _fixture.Factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<E2eeChannelKeyService>().GetLatestGenerationAsync(channel.Id);
        }

        await JoinAndReceiveAsync();
        Assert.Equal(2, await LatestAsync());

        // Nobody has used the key started for the first newcomer, so the second
        // receives it too instead of another new key.
        var (second, secondPlanet, secondChannel) = await JoinAndReceiveAsync();
        Assert.Equal(2, await LatestAsync());

        Assert.True((await SendAsync(second, secondChannel, "hi all", secondPlanet.MyMember.Id)).Success);
        await JoinAndReceiveAsync();
        Assert.Equal(3, await LatestAsync());
    }

    [Fact]
    public async Task MembershipLog_NewDevicesStartFromTheLatestCheckpoint()
    {
        var (owner, _) = await CreateUserAsync();
        var (planet, _) = await CreatePlanetAsync(owner);
        Assert.True((await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.InviteOnly, true)).Success);

        owner.E2eeService.AccessLogCheckpointInterval = E2eeAccessLogService.MinCheckpointInterval;
        for (var i = 0; i <= E2eeAccessLogService.MinCheckpointInterval; i++)
            Assert.True((await owner.E2eeService.CreatePlanetInviteAsync(planet, DateTime.UtcNow.AddDays(1), 1)).Success);

        // The owner's device signs a checkpoint once the log is long enough.
        List<AccessLogEntry> fromCheckpoint = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var result = await owner.PrimaryNode.GetJsonAsync<List<AccessLogEntry>>(
                $"api/e2ee/access-logs/{(int)AccessLogScope.Planet}/{planet.Id}?known=0", cacheDurationMs: null);
            if (result.Data is { Count: > 0 } && result.Data[0].Seq > 0)
            {
                fromCheckpoint = result.Data;
                break;
            }

            await owner.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
            await Task.Delay(200);
        }

        Assert.NotNull(fromCheckpoint);
        Assert.Equal(AccessLogEntryType.Checkpoint, AccessLogRecord.Decode(fromCheckpoint[0].Body).Type);

        // A member who never saw the log starts from the checkpoint and reaches
        // the same state as the owner.
        var (member, _) = await CreateUserAsync();
        Assert.True((await member.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        Assert.True((await owner.E2eeService.AdmitPlanetMembersAsync(planet, [member.Me.Id])).Success);

        var ownerState = await owner.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node, refresh: true);
        var memberPlanet = await member.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        var memberState = await member.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, memberPlanet.Node);
        Assert.NotNull(memberState);
        Assert.Equal(ownerState.Encode(), memberState.Encode());
        Assert.True(memberState.IsMember(member.Me.Id, member.E2eeService.MyKeyState));
    }

    [Fact]
    public async Task ServerRefusesKeyBoxesForUnadmittedUsers()
    {
        var (owner, _) = await CreateUserAsync();
        var (outsider, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.InviteOnly, true)).Success);
        Assert.True((await outsider.PlanetService.JoinPlanetAsync(planet.Id)).Success);

        // A modified client tries to hand the key to someone the log does not admit.
        var ownerChannel = await planet.FetchChannelAsync(channel.Id);
        Assert.True((await owner.E2eeService.EnableChannelEncryptionAsync(ownerChannel)).Success);
        var ring = await owner.E2eeService.GetKeyRingAsync(ownerChannel, refresh: true);
        var outsiderState = await owner.E2eeService.GetUserStateAsync(outsider.Me.Id);
        var box = new ChannelKeyBoxDto
        {
            ChannelId = channel.Id,
            Generation = ring.LatestGeneration,
            UserId = outsider.Me.Id,
            UserKeyGeneration = outsiderState.UserKey.Generation,
            Box = ring.Latest.SealTo(outsider.Me.Id, outsiderState.UserKey)
        };
        var result = await planet.Node.PostAsync($"api/e2ee/channels/{channel.Id}/boxes?planetId={planet.Id}",
            new List<ChannelKeyBoxDto> { box });
        Assert.False(result.Success);
        Assert.Contains("admitted", result.Message);
    }

    [Fact]
    public async Task UnencryptedPost_IsRefusedWithMachineReadableCode()
    {
        // A raw HTTP bot posts plain text the way clients did before encryption.
        var (owner, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        var result = await planet.Node.PostAsyncWithResponse<Message>("api/messages", new Message(owner)
        {
            Content = "plain text from a raw HTTP bot",
            ChannelId = channel.Id,
            PlanetId = planet.Id,
            AuthorUserId = owner.Me.Id,
            AuthorMemberId = planet.MyMember.Id,
            Fingerprint = Guid.NewGuid().ToString()
        });

        Assert.False(result.Success);
        Assert.StartsWith(E2eeErrorCodes.EncryptionRequired + ":", result.Message);
        Assert.Contains("BOT_GUIDE", result.Message);
    }

    [Fact]
    public async Task EncryptedMessage_WithPlainEmbedAttachment_IsRefused()
    {
        var (owner, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);

        var embed = new Valour.Sdk.Models.MessageAttachment(MessageAttachmentType.Embed);
        embed.SetEmbedPayload(Valour.Sdk.Models.Embeds.EmbedParser.Serialize(
            new Valour.Sdk.Models.Embeds.EmbedBuilder().AddPage("Injected").AddText("x").Build()));

        using var scope = _fixture.Factory.Services.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<Valour.Server.Services.MessageService>()
            .PostMessageAsync(new Valour.Server.Models.Message
            {
                ChannelId = channel.Id,
                PlanetId = planet.Id,
                AuthorUserId = owner.Me.Id,
                AuthorMemberId = planet.MyMember.Id,
                EncryptionVersion = MessageEncryption.EndToEnd,
                Envelope = new byte[64],
                Fingerprint = Guid.NewGuid().ToString(),
                Attachments = [embed]
            });

        Assert.False(result.Success);
        Assert.Contains("inside the envelope", result.Message);
    }

    [Fact]
    public async Task CustomEmojis_InEncryptedMessages_AreChecked()
    {
        var (owner, _) = await CreateUserAsync();
        var (friend, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        var unknownEmoji = PlanetEmojiText.BuildToken("unknown", 123456789);

        // The SDK lists the emoji IDs, so the server can reject ones that do
        // not belong to the planet even though it cannot read the text.
        var inPlanet = await SendAsync(owner, channel, "look " + unknownEmoji, planet.MyMember.Id);
        Assert.False(inPlanet.Success);
        Assert.Contains("invalid custom emoji", inPlanet.Message);

        var dm = await owner.ChannelService.FetchDmChannelAsync(friend.Me.Id, create: true);
        var inDm = await SendAsync(owner, dm, "look " + unknownEmoji);
        Assert.False(inDm.Success);
        Assert.Contains("planet channels", inDm.Message);

        var plain = await SendAsync(owner, channel, "no emoji here", planet.MyMember.Id);
        Assert.True(plain.Success, plain.Message);
    }

    [Fact]
    public async Task EncryptedSearch_HidesRepliedMessagesFromBlockedUsers()
    {
        var (owner, _) = await CreateUserAsync();
        var (blocked, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await owner.E2eeService.SetPlanetEncryptionAsync(planet, PlanetEncryptionMode.Open, true)).Success);

        var first = await SendAsync(owner, channel, "channel opened", planet.MyMember.Id);
        Assert.True(first.Success, first.Message);

        Assert.True((await blocked.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        var blockedPlanet = await blocked.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await blockedPlanet.EnsureReadyAsync();
        var blockedChannel = await blockedPlanet.FetchChannelAsync(channel.Id);
        await blocked.E2eeService.RequestKeysAsync(blockedChannel);
        await owner.E2eeService.ServeChannelAsync(await planet.FetchChannelAsync(channel.Id));
        await blocked.E2eeService.GetKeyRingAsync(blockedChannel, refresh: true);

        var original = await SendAsync(blocked, blockedChannel, "something from the blocked member",
            blockedPlanet.MyMember.Id);
        Assert.True(original.Success, original.Message);
        var reply = await SendAsync(owner, channel, "a searchable reply", planet.MyMember.Id, original.Data.Id);
        Assert.True(reply.Success, reply.Message);
        await WaitForStoredAsync(reply.Data.Id);

        var block = await owner.BlockService.BlockUserAsync(blocked.Me.Id, BlockType.OneWay);
        Assert.True(block.Success, block.Message);

        var results = await owner.E2eeService.SearchAsync(channel, "searchable");
        var found = Assert.Single(results, m => m.Id == reply.Data.Id);
        Assert.Null(found.ReplyTo);
    }

    [Fact]
    public async Task LegacyHistory_SealsEveryEmbed()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var legacyId = await InsertLegacyMessageAsync(dm, alice.Me.Id, "two embeds before encryption");

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            for (var i = 0; i < 2; i++)
            {
                db.MessageAttachments.Add(new Valour.Database.MessageAttachment
                {
                    Id = Valour.Server.Database.IdManager.Generate(),
                    MessageId = legacyId,
                    SortOrder = i,
                    Type = MessageAttachmentType.Embed,
                    Location = Valour.Sdk.Models.MessageAttachment.EmbedLocation,
                    MimeType = "application/vnd.valour.embed+json",
                    FileName = "Embed",
                    Data = Valour.Sdk.Models.Embeds.EmbedParser.Serialize(
                        new Valour.Sdk.Models.Embeds.EmbedBuilder().AddPage("Legacy " + i).AddText("x").Build())
                });
            }

            await db.SaveChangesAsync();
        }

        Assert.True(await SealLegacyAsync(legacyId));

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            Assert.False(await db.MessageAttachments.AnyAsync(a => a.MessageId == legacyId));
        }

        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        var sealedMessage = (await bobDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == legacyId);
        Assert.Equal(MessageDecryptionState.Decrypted, sealedMessage.DecryptionState);
        var titles = sealedMessage.Attachments!
            .Where(a => a.Type == MessageAttachmentType.Embed)
            .Select(a => a.Embed!.Pages[0].Title)
            .ToList();
        Assert.Equal(["Legacy 0", "Legacy 1"], titles);
    }

    [Fact]
    public async Task LegacyHistory_IsSealedToTheOldestKey()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        Assert.True((await SendAsync(alice, dm, "encrypted from the start")).Success);
        for (var i = 0; i < 2; i++)
            Assert.True((await alice.E2eeService.RotateChannelKeyAsync(dm, ChannelKeyRotationReason.Manual)).Success);

        var legacyId = await InsertLegacyMessageAsync(dm, alice.Me.Id, "older than every key");
        Assert.True(await SealLegacyAsync(legacyId));

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var stored = await db.Messages.AsNoTracking().FirstAsync(x => x.Id == legacyId);
            Assert.Equal(MessageEncryption.ServerSealed, stored.EncryptionVersion);
            Assert.Equal(1, stored.KeyGeneration);
            Assert.True(await db.E2eeChannelKeyGenerations.AnyAsync(x =>
                x.ChannelId == dm.Id && x.Generation == 1 && x.HasMessages));
        }

        // Bob holds only the newest key and reaches generation 1 through it.
        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        var read = (await bobDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == legacyId);
        Assert.Equal(MessageDecryptionState.Decrypted, read.DecryptionState);
        Assert.Equal("older than every key", read.Content);
    }

    [Fact]
    public async Task LegacyHistory_InDeletedChannelsLosesItsText()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var legacyId = await InsertLegacyMessageAsync(dm, alice.Me.Id, "in a channel nobody can open");

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            await db.Channels.IgnoreQueryFilters().Where(x => x.Id == dm.Id)
                .ExecuteUpdateAsync(x => x.SetProperty(c => c.IsDeleted, true));
        }

        Assert.True(await SealLegacyAsync(legacyId));

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var stored = await db.Messages.AsNoTracking().FirstAsync(x => x.Id == legacyId);
            Assert.Equal(string.Empty, stored.Content);
            Assert.Null(stored.Envelope);
            Assert.NotEqual(MessageEncryption.None, stored.EncryptionVersion);

            // No key was created for a channel nobody can open.
            Assert.False(await db.E2eeChannelKeyGenerations.AnyAsync(x => x.ChannelId == dm.Id));
        }
    }

    [Fact]
    public async Task LegacyHistory_KeepsReportedTextAsEvidence()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var legacyId = await InsertLegacyMessageAsync(dm, alice.Me.Id, "reported before encryption");
        var reportId = Guid.NewGuid().ToString();

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            db.Reports.Add(new Valour.Database.Report
            {
                Id = reportId,
                TimeCreated = DateTime.UtcNow,
                ReportingUserId = bob.Me.Id,
                MessageId = legacyId,
                ChannelId = dm.Id,
                ReportedUserId = alice.Me.Id,
                ReasonCode = ReportReasonCode.IsTargetedHarassment,
                LongReason = "reported before sealing"
            });
            await db.SaveChangesAsync();
        }

        Assert.True(await SealLegacyAsync(legacyId));

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var evidence = await db.ReportEvidenceEntries.AsNoTracking()
                .SingleAsync(x => x.ReportId == reportId && x.MessageId == legacyId);
            Assert.Equal("reported before encryption", evidence.Content);
            Assert.Equal((int)ReportEvidenceVerification.ServerAttested, evidence.Verification);

            var stored = await db.Messages.AsNoTracking().FirstAsync(x => x.Id == legacyId);
            Assert.Equal(MessageEncryption.ServerSealed, stored.EncryptionVersion);
            Assert.Equal(string.Empty, stored.Content);
        }
    }

    [Fact]
    public async Task KeyGeneration_RecordsDatedAheadOrBeforeThePreviousKeyAreRefused()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        Assert.True((await SendAsync(alice, dm, "the first key")).Success);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var keys = scope.ServiceProvider.GetRequiredService<E2eeChannelKeyService>();
        var serverChannel = (await db.Channels.AsNoTracking().FirstAsync(x => x.Id == dm.Id)).ToModel();
        var latest = serverChannel.EncryptionGeneration;
        var previous = await keys.GetGenerationAsync(dm.Id, latest);
        var previousTime = ChannelKeyGenerationRecord.Decode(previous.Body).TimestampMs;

        Task<Valour.Shared.TaskResult<ChannelKeyStateDto>> CreateAsync(long timestampMs)
        {
            var (entry, _) = ChannelKeyGenerationBuilder.Create(ChannelKeySecret.Generate(dm.Id, latest + 1), 0,
                alice.Me.Id, alice.E2eeService.Device, ChannelKeyRotationReason.Manual, previous.IndexGeneration,
                null, timestampMs, ChannelKeyTerms.Ungoverned(ChannelKeyPolicy.Direct, true));
            return keys.CreateGenerationAsync(serverChannel, alice.Me.Id,
                new CreateChannelKeyGenerationRequest { Entry = entry });
        }

        var ahead = await CreateAsync(DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds());
        Assert.False(ahead.Success);
        Assert.Contains("clock", ahead.Message);

        var behind = await CreateAsync(previousTime - 60_000);
        Assert.False(behind.Success);
        Assert.Contains("dated before", behind.Message);
    }

    [Fact]
    public async Task ShareBoxes_SkipsBoxesSealedToReplacedUserKeysAndNamesThoseUsers()
    {
        var (owner, _) = await CreateUserAsync();
        var (member, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await SendAsync(owner, channel, "held by the owner", planet.MyMember.Id)).Success);
        Assert.True((await member.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        await GoOfflineAsync(owner);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var keys = scope.ServiceProvider.GetRequiredService<E2eeChannelKeyService>();
        var identity = scope.ServiceProvider.GetRequiredService<E2eeIdentityService>();
        var serverChannel = (await db.Channels.AsNoTracking().FirstAsync(x => x.Id == channel.Id)).ToModel();
        var latest = serverChannel.EncryptionGeneration;
        var memberState = await identity.GetStateAsync(member.Me.Id);

        // Sealed to the key the member had before signing out elsewhere.
        var result = await keys.ShareBoxesAsync(serverChannel, owner.Me.Id,
        [
            new ChannelKeyBoxDto
            {
                ChannelId = channel.Id,
                Generation = latest,
                UserId = member.Me.Id,
                UserKeyGeneration = memberState.UserKey.Generation - 1,
                Box = new byte[80]
            }
        ]);

        Assert.True(result.Success, result.Message);
        Assert.Equal([member.Me.Id], result.Data.StaleRecipients);
        Assert.False(await db.E2eeChannelKeyBoxes.AnyAsync(x =>
            x.ChannelId == channel.Id && x.UserId == member.Me.Id && x.Generation == latest &&
            x.UserKeyGeneration == memberState.UserKey.Generation - 1));
    }

    [Fact]
    public async Task KeyRecipients_IgnoreBoxesFromBeforeAKeyReset()
    {
        var (owner, _) = await CreateUserAsync();
        var (member, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await SendAsync(owner, channel, "before the reset", planet.MyMember.Id)).Success);
        Assert.True((await member.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        await GoOfflineAsync(owner);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var keys = scope.ServiceProvider.GetRequiredService<E2eeChannelKeyService>();
        var identity = scope.ServiceProvider.GetRequiredService<E2eeIdentityService>();
        var serverChannel = (await db.Channels.AsNoTracking().FirstAsync(x => x.Id == channel.Id)).ToModel();
        var latest = serverChannel.EncryptionGeneration;
        var memberState = await identity.GetStateAsync(member.Me.Id);

        // A box sealed to a user key from an earlier epoch can never be opened.
        await db.E2eeChannelKeyBoxes
            .Where(x => x.ChannelId == channel.Id && x.UserId == member.Me.Id)
            .ExecuteDeleteAsync();
        db.E2eeChannelKeyBoxes.Add(new Valour.Database.E2eeChannelKeyBox
        {
            ChannelId = channel.Id,
            Generation = latest,
            UserId = member.Me.Id,
            UserKeyGeneration = memberState.EpochFirstGeneration - 1,
            Box = new byte[80],
            SharedByUserId = owner.Me.Id,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        Assert.True((await keys.RequestKeysAsync(serverChannel, member.Me.Id)).Success);
        var recipients = await keys.GetRecipientsAsync(serverChannel);
        var pending = Assert.Single(recipients.Pending, p => p.UserId == member.Me.Id);
        Assert.Contains(latest, pending.Generations);
        Assert.True(pending.HasEarlierKeys);
    }

    [Fact]
    public async Task KeyRequests_AreNotAnnouncedAgainWithinThirtySeconds()
    {
        var (owner, _) = await CreateUserAsync();
        var (member, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await SendAsync(owner, channel, "someone asks twice", planet.MyMember.Id)).Success);
        Assert.True((await member.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        await GoOfflineAsync(owner);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var keys = scope.ServiceProvider.GetRequiredService<E2eeChannelKeyService>();
        var serverChannel = (await db.Channels.AsNoTracking().FirstAsync(x => x.Id == channel.Id)).ToModel();

        Assert.True((await keys.RequestKeysAsync(serverChannel, member.Me.Id)).Success);
        var first = await db.E2eeKeyRequests.AsNoTracking()
            .SingleAsync(x => x.ChannelId == channel.Id && x.UserId == member.Me.Id);

        Assert.True((await keys.RequestKeysAsync(serverChannel, member.Me.Id)).Success);
        var second = await db.E2eeKeyRequests.AsNoTracking()
            .SingleAsync(x => x.ChannelId == channel.Id && x.UserId == member.Me.Id);
        Assert.Equal(first.RequestedAt, second.RequestedAt);
    }

    [Fact]
    public async Task KeyRecipients_ListAtMostTheNewestHoldersWithACount()
    {
        var (owner, _) = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner);
        Assert.True((await SendAsync(owner, channel, "one holder", planet.MyMember.Id)).Success);

        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var keys = scope.ServiceProvider.GetRequiredService<E2eeChannelKeyService>();
        var serverChannel = (await db.Channels.AsNoTracking().FirstAsync(x => x.Id == channel.Id)).ToModel();

        var recipients = await keys.GetRecipientsAsync(serverChannel);
        Assert.Contains(owner.Me.Id, recipients.Holders);
        Assert.Equal(recipients.Holders.Count, recipients.HolderCount);
        Assert.True(recipients.Holders.Count <= ChannelKeyRecipientsDto.MaxListedHolders);
    }

    [Fact]
    public async Task LegacyHistory_KeepsTheFirstEmbedsOfAMessageWithTooMany()
    {
        var (alice, _) = await CreateUserAsync();
        var (bob, _) = await CreateUserAsync();
        var dm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        var legacyId = await InsertLegacyMessageAsync(dm, alice.Me.Id, "seven embeds before encryption");

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            for (var i = 0; i < ServerSealedPayload.MaxEmbeds + 2; i++)
            {
                db.MessageAttachments.Add(new Valour.Database.MessageAttachment
                {
                    Id = Valour.Server.Database.IdManager.Generate(),
                    MessageId = legacyId,
                    SortOrder = i,
                    Type = MessageAttachmentType.Embed,
                    Location = Valour.Sdk.Models.MessageAttachment.EmbedLocation,
                    MimeType = "application/vnd.valour.embed+json",
                    FileName = "Embed",
                    Data = Valour.Sdk.Models.Embeds.EmbedParser.Serialize(
                        new Valour.Sdk.Models.Embeds.EmbedBuilder().AddPage("Legacy " + i).AddText("x").Build())
                });
            }

            await db.SaveChangesAsync();
        }

        Assert.True(await SealLegacyAsync(legacyId));

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var stored = await db.Messages.AsNoTracking().FirstAsync(x => x.Id == legacyId);
            Assert.Equal(MessageEncryption.ServerSealed, stored.EncryptionVersion);
            Assert.Equal(string.Empty, stored.Content);
            Assert.False(await db.MessageAttachments.AnyAsync(a => a.MessageId == legacyId));
        }

        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        var sealedMessage = (await bobDm.GetMessagesAsync(long.MaxValue, 10)).Single(m => m.Id == legacyId);
        Assert.Equal(MessageDecryptionState.Decrypted, sealedMessage.DecryptionState);
        var titles = sealedMessage.Attachments!
            .Where(a => a.Type == MessageAttachmentType.Embed)
            .Select(a => a.Embed!.Pages[0].Title)
            .ToList();
        Assert.Equal(Enumerable.Range(0, ServerSealedPayload.MaxEmbeds).Select(i => "Legacy " + i), titles);
    }
}
