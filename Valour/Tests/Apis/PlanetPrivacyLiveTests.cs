using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Sdk.Services;
using Valour.Server.Database;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Apis;

/// <summary>
/// Making planets public and private through the real server and SDK. Whether
/// a planet is public decides its encryption mode, and each change of privacy
/// carries a membership log entry the owner's device signs.
/// </summary>
[Collection("ApiCollection")]
public class PlanetPrivacyLiveTests
{
    private readonly LoginTestFixture _fixture;

    public PlanetPrivacyLiveTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<ValourClient> CreateUserAsync() => (await CreateUserWithStoreAsync()).Client;

    private async Task<(ValourClient Client, RegisterUserRequest Details, MemoryE2eeKeyStore Store)>
        CreateUserWithStoreAsync()
    {
        var details = await _fixture.RegisterUser();
        var store = new MemoryE2eeKeyStore();
        return (await EncryptedChat.LoginAsync(_fixture, details, store), details, store);
    }

    /// <summary>
    /// Turns on app-based two-factor sign-in for a user, which ownership
    /// transfers require, and returns a current code.
    /// </summary>
    private async Task<string> EnableMultiFactorAsync(ValourClient client)
    {
        var secret = $"privacy-test-{client.Me.Id}";
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        db.MultiAuths.Add(new Valour.Database.MultiAuth
        {
            Id = IdManager.Generate(),
            UserId = client.Me.Id,
            Type = "app",
            Secret = secret,
            Verified = true,
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return new Google.Authenticator.TwoFactorAuthenticator()
            .GeneratePINAtInterval(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30);
    }

    private static async Task<PlanetInvite> CreateServerInviteAsync(ValourClient inviter, Planet planet,
        DateTime? expires = null)
    {
        var invite = await planet.Node.PostAsyncWithResponse<PlanetInvite>("api/invites", new PlanetInvite(inviter)
        {
            PlanetId = planet.Id,
            IssuerId = inviter.Me.Id,
            TimeCreated = DateTime.UtcNow,
            TimeExpires = expires
        });
        Assert.True(invite.Success, invite.Message);
        return invite.Data.Sync(inviter);
    }

    private static async Task<(Planet Planet, Channel Channel)> CreatePlanetAsync(ValourClient owner, bool isPublic)
    {
        var create = await new Planet(owner)
        {
            Name = $"Privacy {Guid.NewGuid().ToString()[..8]}",
            Description = "Privacy test planet",
            Public = isPublic,
            Discoverable = true
        }.CreateAsync();
        Assert.True(create.Success, create.Message);

        var planet = await owner.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
        await planet.EnsureReadyAsync();
        return (planet, await planet.FetchPrimaryChatChannelAsync());
    }

    private static async Task<(Planet Planet, Channel Channel)> JoinPublicAsync(ValourClient client, long planetId,
        long channelId)
    {
        var join = await client.PlanetService.JoinPlanetAsync(planetId);
        Assert.True(join.Success, join.Message);
        var planet = await client.PlanetService.FetchPlanetAsync(planetId, skipCache: true);
        await planet.EnsureReadyAsync();
        return (planet, await planet.FetchChannelAsync(channelId));
    }

    /// <summary>
    /// Joins a private planet with an ordinary invite code, as someone
    /// following an invite link would.
    /// </summary>
    private static async Task<Channel> JoinWithInviteAsync(ValourClient inviter, Planet planet, ValourClient joiner,
        long channelId, PlanetInvite invite = null)
    {
        invite ??= await CreateServerInviteAsync(inviter, planet);
        var join = await joiner.PlanetService.JoinPlanetAsync(planet.Id, invite.Id);
        Assert.True(join.Success, join.Message);
        return await EncryptedChat.GetChannelAsync(joiner, planet.Id, channelId);
    }

    private static Task<Valour.Shared.TaskResult<Message>> SendAsync(ValourClient client, Channel channel,
        string content) =>
        EncryptedChat.SendAsync(client, channel, content, channel.Planet.MyMember.Id);

    private static async Task<string> ReadAsync(ValourClient client, Channel channel, long messageId)
    {
        await client.E2eeService.GetKeyRingAsync(channel, refresh: true);
        var message = (await channel.GetMessagesAsync(long.MaxValue, 20)).Single(m => m.Id == messageId);
        Assert.Equal(MessageDecryptionState.Decrypted, message.DecryptionState);
        return message.Content;
    }

    /// <summary>
    /// Checks the stored planet and the type of the newest entry in its
    /// membership log, and that no stored planet is both public and invite-only.
    /// </summary>
    private async Task AssertStoredAsync(long planetId, bool isPublic, PlanetEncryptionMode mode,
        AccessLogEntryType? newestEntry)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        var planet = await db.Planets.AsNoTracking().SingleAsync(x => x.Id == planetId);
        Assert.Equal(isPublic, planet.Public);
        Assert.Equal(mode, planet.EncryptionMode);
        Assert.False(await db.Planets.AnyAsync(x => x.Public && x.EncryptionMode == PlanetEncryptionMode.InviteOnly));

        var newest = await db.E2eeAccessLogEntries.AsNoTracking()
            .Where(x => x.Scope == (int)AccessLogScope.Planet && x.ScopeId == planetId)
            .OrderByDescending(x => x.Seq)
            .FirstOrDefaultAsync();
        Assert.Equal(newestEntry, newest is null ? null : AccessLogRecord.Decode(newest.Body).Type);
    }

    [Fact]
    public async Task Privacy_GoesPrivatePublicAndPrivateAgainUnderANewOwner()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner, isPublic: true);
        var (memberPlanet, memberChannel) = await JoinPublicAsync(member, planet.Id, channel.Id);

        var first = await SendAsync(owner, channel, "hello while public");
        Assert.True(first.Success, first.Message);
        await member.E2eeService.RequestKeysAsync(memberChannel);
        await owner.E2eeService.ServeChannelAsync(channel);
        Assert.Equal("hello while public", await ReadAsync(member, memberChannel, first.Data.Id));

        // Private: the owner's device signs the log, letting in everyone here.
        var makePrivate = await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, sharesHistory: true);
        Assert.True(makePrivate.Success, makePrivate.Message);
        await AssertStoredAsync(planet.Id, false, PlanetEncryptionMode.InviteOnly, AccessLogEntryType.Genesis);

        var membersOnly = await SendAsync(owner, channel, "members only");
        Assert.True(membersOnly.Success, membersOnly.Message);
        Assert.Equal("members only", await ReadAsync(member, memberChannel, membersOnly.Data.Id));

        // The key made while the planet was public may be held by people the
        // log never admitted, so the first message after going private uses a new one.
        Assert.True(membersOnly.Data.KeyGeneration > first.Data.KeyGeneration);
        Assert.Equal(ChannelKeyPolicy.PlanetInviteOnly,
            (await member.E2eeService.GetKeyRingAsync(memberChannel, refresh: true)).Policy);

        // Public: the owner's device signs an open entry. The member's app
        // saw the log before and follows it only after checking that entry.
        var makePublic = await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: true, sharesHistory: true);
        Assert.True(makePublic.Success, makePublic.Message);
        await AssertStoredAsync(planet.Id, true, PlanetEncryptionMode.Open, AccessLogEntryType.Open);

        await member.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        Assert.Equal(ChannelKeyPolicy.PlanetOpen,
            (await member.E2eeService.GetKeyRingAsync(memberChannel, refresh: true)).Policy);
        Assert.Equal(ChannelKeyPolicy.PlanetOpen,
            (await owner.E2eeService.GetKeyRingAsync(channel, refresh: true)).Policy);

        // Anyone can join a public planet, and members share keys with them.
        var outsider = await CreateUserAsync();
        var (_, outsiderChannel) = await JoinPublicAsync(outsider, planet.Id, channel.Id);
        var open = await SendAsync(member, memberChannel, "open to everyone");
        Assert.True(open.Success, open.Message);
        await outsider.E2eeService.RequestKeysAsync(outsiderChannel);
        await member.E2eeService.ServeChannelAsync(memberChannel);
        Assert.Equal("open to everyone", await ReadAsync(outsider, outsiderChannel, open.Data.Id));

        // Ownership moves while the planet is public. Only the log's owner can
        // make the planet private again, so the server refuses a transfer
        // without the owner's signed entry handing the log on.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var unsigned = await scope.ServiceProvider.GetRequiredService<Valour.Server.Services.PlanetService>()
                .TransferOwnershipAsync(planet.Id, owner.Me.Id, member.Me.Id);
            Assert.False(unsigned.Success);
        }

        var transfer = await owner.E2eeService.TransferPlanetOwnershipAsync(planet, member.Me.Id,
            await EnableMultiFactorAsync(owner));
        Assert.True(transfer.Success, transfer.Message);
        await AssertStoredAsync(planet.Id, true, PlanetEncryptionMode.Open, AccessLogEntryType.TransferOwnership);

        memberPlanet = await member.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        Assert.Equal(member.Me.Id, memberPlanet.OwnerId);

        // The new owner makes it private again with a restart, which lets in
        // everyone here now.
        var again = await member.E2eeService.SetPlanetPrivacyAsync(memberPlanet, isPublic: false, sharesHistory: true);
        Assert.True(again.Success, again.Message);
        await AssertStoredAsync(planet.Id, false, PlanetEncryptionMode.InviteOnly, AccessLogEntryType.Restart);

        var privateAgain = await SendAsync(member, memberChannel, "private again");
        Assert.True(privateAgain.Success, privateAgain.Message);
        Assert.Equal("private again", await ReadAsync(outsider, outsiderChannel, privateAgain.Data.Id));

        // The earlier owner's app, which saw the log opened, follows the restart.
        Assert.Equal("private again", await ReadAsync(owner, channel, privateAgain.Data.Id));
        Assert.Equal(ChannelKeyPolicy.PlanetInviteOnly,
            (await owner.E2eeService.GetKeyRingAsync(channel, refresh: true)).Policy);

        // Someone who joins afterwards with an ordinary invite waits to be let in.
        var latecomer = await CreateUserAsync();
        var latecomerChannel = await JoinWithInviteAsync(member, memberPlanet, latecomer, channel.Id);
        await latecomer.E2eeService.RequestKeysAsync(latecomerChannel);
        await member.E2eeService.ServeChannelAsync(memberChannel);
        Assert.Null((await latecomer.E2eeService.GetKeyRingAsync(latecomerChannel, refresh: true)).Latest);
    }

    [Fact]
    public async Task PrivatePlanet_AdminInviteLinkLetsPeopleInRightAway()
    {
        var owner = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner, isPublic: false);
        Assert.True((await owner.E2eeService.FinishPrivatePlanetAsync(planet)).Success);
        var sent = await SendAsync(owner, channel, "welcome in");
        Assert.True(sent.Success, sent.Message);

        var expires = DateTime.UtcNow.AddDays(1);
        var invite = await CreateServerInviteAsync(owner, planet, expires);
        Assert.True(await owner.E2eeService.CanSignPlanetInvitesAsync(planet));
        var signed = await owner.E2eeService.SignPlanetInviteAsync(planet, invite);
        Assert.True(signed.Success, signed.Message);
        Assert.Equal(invite.Id, signed.Data.InviteId);

        // The signed invite is named after the code and expires with it.
        // Codes have no use limit, so neither does the signed invite.
        var log = await owner.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            refresh: true);
        var logInvite = log.Invites[invite.Id];
        Assert.Equal(0, logInvite.MaxUses);
        Assert.Equal(new DateTimeOffset(invite.TimeExpires!.Value, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            logInvite.ExpiresMs);
        Assert.InRange(logInvite.ExpiresMs, new DateTimeOffset(expires).ToUnixTimeMilliseconds() - 1000,
            new DateTimeOffset(expires).ToUnixTimeMilliseconds() + 1000);

        // The key travels only in the link's fragment; nothing the server stored holds it.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            var bodies = await db.E2eeAccessLogEntries.AsNoTracking()
                .Where(x => x.Scope == (int)AccessLogScope.Planet && x.ScopeId == planet.Id)
                .Select(x => x.Body)
                .ToListAsync();
            Assert.DoesNotContain(bodies, b => b.AsSpan().IndexOf(signed.Data.Secret) >= 0);
        }

        var joiner = await CreateUserAsync();
        var joinerChannel = await JoinWithInviteAsync(owner, planet, joiner, channel.Id, invite);
        Assert.True(EncryptedInvite.TryParseFragment(signed.Data.Fragment, out var parsed));
        var redeem = await joiner.E2eeService.RedeemPlanetInviteAsync(joinerChannel.Planet, parsed);
        Assert.True(redeem.Success, redeem.Message);

        Assert.DoesNotContain(await owner.E2eeService.GetPendingAdmissionsAsync(planet),
            p => p.UserId == joiner.Me.Id);
        await joiner.E2eeService.RequestKeysAsync(joinerChannel);
        await owner.E2eeService.ServeChannelAsync(channel);
        Assert.Equal("welcome in", await ReadAsync(joiner, joinerChannel, sent.Data.Id));
    }

    [Fact]
    public async Task PrivatePlanet_SignedInviteLinkCopiesAgainOnlyOnTheCreatingDevice()
    {
        var (owner, ownerDetails, ownerStore) = await CreateUserWithStoreAsync();
        var admin = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner, isPublic: true);
        var (adminPlanet, _) = await JoinPublicAsync(admin, planet.Id, channel.Id);
        Assert.True((await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, true)).Success);
        Assert.True((await owner.E2eeService.SetPlanetAccessAdminAsync(planet, admin.Me.Id, true)).Success);

        var invite = await CreateServerInviteAsync(owner, planet, DateTime.UtcNow.AddDays(1));
        var signed = await owner.E2eeService.SignPlanetInviteAsync(planet, invite);
        Assert.True(signed.Success, signed.Message);

        // The creating device keeps the key and copies the same link again.
        Assert.Equal(signed.Data.Fragment, await owner.E2eeService.GetSavedInviteFragmentAsync(planet, invite.Id));

        // Another admin, and another device of the same account, copy the plain link.
        await admin.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, adminPlanet.Node,
            refresh: true);
        Assert.Null(await admin.E2eeService.GetSavedInviteFragmentAsync(adminPlanet, invite.Id));
        var otherDevice = await EncryptedChat.LoginAsync(_fixture, ownerDetails, new MemoryE2eeKeyStore(),
            requireReady: false);
        var otherDevicePlanet = await otherDevice.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        Assert.Null(await otherDevice.E2eeService.GetSavedInviteFragmentAsync(otherDevicePlanet, invite.Id));

        // Deleting the code forgets the key on the creating device.
        var storeKey = $"valour-e2ee/u{owner.Me.Id}/invite-secrets";
        Assert.NotNull(await ownerStore.GetAsync(storeKey));
        Assert.True((await owner.E2eeService.DeletePlanetInviteAsync(planet, invite)).Success);
        Assert.Null(await ownerStore.GetAsync(storeKey));
        Assert.Null(await owner.E2eeService.GetSavedInviteFragmentAsync(planet, invite.Id));
    }

    [Fact]
    public async Task PublicPlanet_OwnerWhoStartedOverHandsItOnButNobodyCanMakeItPrivate()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner, isPublic: true);
        var (memberPlanet, _) = await JoinPublicAsync(member, planet.Id, channel.Id);
        Assert.True((await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, true)).Success);
        Assert.True((await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: true, true)).Success);

        // The owner starts their encryption over while the planet is public,
        // so their keys are no longer the ones the opened log names.
        var reset = await owner.E2eeService.ResetKeysAsync();
        Assert.True(reset.Success, reset.Message);
        Assert.Equal(E2eeService.OpenedLogOwnerKeysChangedMessage,
            await owner.E2eeService.GetMakePrivateBlockerAsync(planet));
        var makePrivate = await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, true);
        Assert.False(makePrivate.Success);
        Assert.Contains("started over", makePrivate.Message);

        // Nobody can sign for the log, so the planet moves without an entry
        // and the log stays with the keys it names.
        var transfer = await owner.E2eeService.TransferPlanetOwnershipAsync(planet, member.Me.Id,
            await EnableMultiFactorAsync(owner));
        Assert.True(transfer.Success, transfer.Message);
        await AssertStoredAsync(planet.Id, true, PlanetEncryptionMode.Open, AccessLogEntryType.Open);

        // The new owner is told why the planet cannot be made private, and
        // the server refuses a restart from them.
        memberPlanet = await member.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        Assert.Equal(member.Me.Id, memberPlanet.OwnerId);
        Assert.Equal(E2eeService.OpenedLogOwnedByEarlierKeysMessage,
            await member.E2eeService.GetMakePrivateBlockerAsync(memberPlanet));
        Assert.False((await member.E2eeService.SetPlanetPrivacyAsync(memberPlanet, isPublic: false, true)).Success);

        var log = await member.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id,
            memberPlanet.Node, refresh: true);
        var restart = AccessLogBuilder.Create(log, AccessLogEntryType.Restart, member.Me.Id, DeviceKeyPair.Generate(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            r => r.With(owner: AccessMember.For(member.E2eeService.MyKeyState), members: []));
        var byServerPuppet = await memberPlanet.Node.PutAsyncWithResponse<Planet>($"api/planets/{planet.Id}/privacy",
            new SetPlanetPrivacyRequest { Public = false, SharesHistory = true, Entry = restart });
        Assert.False(byServerPuppet.Success);
        await AssertStoredAsync(planet.Id, true, PlanetEncryptionMode.Open, AccessLogEntryType.Open);
    }

    [Fact]
    public async Task PrivatePlanet_DeletingASignedInviteRevokesIt()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner, isPublic: true);
        var (memberPlanet, _) = await JoinPublicAsync(member, planet.Id, channel.Id);
        Assert.True((await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, true)).Success);

        var invite = await CreateServerInviteAsync(owner, planet);
        var signed = await owner.E2eeService.SignPlanetInviteAsync(planet, invite);
        Assert.True(signed.Success, signed.Message);

        // A member who is not an admin cannot revoke it, so they cannot delete it.
        var memberInvite = (await memberPlanet.Node.GetJsonAsync<PlanetInvite>($"api/invites/{invite.Id}")).Data
            .Sync(member);
        Assert.False((await member.E2eeService.DeletePlanetInviteAsync(memberPlanet, memberInvite)).Success);

        var deleted = await owner.E2eeService.DeletePlanetInviteAsync(planet, invite);
        Assert.True(deleted.Success, deleted.Message);
        var log = await owner.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            refresh: true);
        Assert.True(log.Invites[invite.Id].Revoked);
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            Assert.False(await db.PlanetInvites.AnyAsync(x => x.Id == invite.Id));
        }

        // Someone who got in with another link cannot use the revoked key.
        var joiner = await CreateUserAsync();
        var joinerChannel = await JoinWithInviteAsync(owner, planet, joiner, channel.Id);
        Assert.True(EncryptedInvite.TryParseFragment(signed.Data.Fragment, out var parsed));
        Assert.False((await joiner.E2eeService.RedeemPlanetInviteAsync(joinerChannel.Planet, parsed)).Success);
        Assert.Contains(await owner.E2eeService.GetPendingAdmissionsAsync(planet), p => p.UserId == joiner.Me.Id);
    }

    [Fact]
    public async Task PrivatePlanet_PlainInviteLinkWaitsForAnAdmin()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner, isPublic: true);
        var (memberPlanet, _) = await JoinPublicAsync(member, planet.Id, channel.Id);
        Assert.True((await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, true)).Success);
        var sent = await SendAsync(owner, channel, "for people who are let in");
        Assert.True(sent.Success, sent.Message);

        // A member who is not an admin cannot sign invites, so their links are plain.
        Assert.False(await member.E2eeService.CanSignPlanetInvitesAsync(memberPlanet));
        var invite = await CreateServerInviteAsync(owner, planet);
        Assert.False((await member.E2eeService.SignPlanetInviteAsync(memberPlanet, invite)).Success);

        // Someone who joins with a plain link waits for an admin.
        var joiner = await CreateUserAsync();
        var joinerChannel = await JoinWithInviteAsync(owner, planet, joiner, channel.Id, invite);
        await joiner.E2eeService.RequestKeysAsync(joinerChannel);
        await owner.E2eeService.ServeChannelAsync(channel);
        Assert.Null((await joiner.E2eeService.GetKeyRingAsync(joinerChannel, refresh: true)).Latest);
        Assert.Contains(await owner.E2eeService.GetPendingAdmissionsAsync(planet), p => p.UserId == joiner.Me.Id);
    }

    [Fact]
    public async Task Privacy_OnlyTheOwnerChangesItWithASignedEntry()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner, isPublic: true);
        var (memberPlanet, _) = await JoinPublicAsync(member, planet.Id, channel.Id);

        // Nobody changes it through a general planet update, the owner included.
        planet.Public = false;
        var update = await planet.UpdateAsync();
        planet.Public = true;
        Assert.False(update.Success);
        Assert.Contains("privacy settings", update.Message);

        // A member cannot change it.
        Assert.False((await member.E2eeService.SetPlanetPrivacyAsync(memberPlanet, isPublic: false, true)).Success);
        var byMember = await memberPlanet.Node.PutAsyncWithResponse<Planet>($"api/planets/{planet.Id}/privacy",
            new SetPlanetPrivacyRequest { Public = false, SharesHistory = true });
        Assert.False(byMember.Success);

        // The owner cannot make it private without signing the log.
        var unsigned = await planet.Node.PutAsyncWithResponse<Planet>($"api/planets/{planet.Id}/privacy",
            new SetPlanetPrivacyRequest { Public = false, SharesHistory = true });
        Assert.False(unsigned.Success);
        await AssertStoredAsync(planet.Id, true, PlanetEncryptionMode.Open, null);

        Assert.True((await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, true)).Success);
        await AssertStoredAsync(planet.Id, false, PlanetEncryptionMode.InviteOnly, AccessLogEntryType.Genesis);

        // Making it public needs an open entry from the owner's device: none,
        // the wrong kind, or one signed by a device the owner does not have
        // is refused, and the planet stays private.
        var log = await owner.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            refresh: true);
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var strangerDevice = DeviceKeyPair.Generate();
        var attempts = new[]
        {
            new SetPlanetPrivacyRequest { Public = true, SharesHistory = true },
            new SetPlanetPrivacyRequest
            {
                Public = true, SharesHistory = true,
                Entry = AccessLogBuilder.Create(log, AccessLogEntryType.Restart, owner.Me.Id, strangerDevice, now,
                    r => r.With(owner: log.Owner, members: []))
            },
            new SetPlanetPrivacyRequest
            {
                Public = true, SharesHistory = true,
                Entry = AccessLogBuilder.Create(log, AccessLogEntryType.Open, owner.Me.Id, strangerDevice, now, r => r)
            }
        };
        foreach (var attempt in attempts)
        {
            var result = await planet.Node.PutAsyncWithResponse<Planet>($"api/planets/{planet.Id}/privacy", attempt);
            Assert.False(result.Success);
        }

        await AssertStoredAsync(planet.Id, false, PlanetEncryptionMode.InviteOnly, AccessLogEntryType.Genesis);

        // A member cannot open the log even with their own signed entry.
        var memberLog = await member.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id,
            memberPlanet.Node, refresh: true);
        Assert.NotNull(memberLog);
        Assert.False((await member.E2eeService.SetPlanetPrivacyAsync(memberPlanet, isPublic: true, true)).Success);
        await AssertStoredAsync(planet.Id, false, PlanetEncryptionMode.InviteOnly, AccessLogEntryType.Genesis);
    }

    [Fact]
    public async Task PendingPrivatePlanet_OwnerFinishesAndMembersKeepAccess()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner, isPublic: true);
        var (_, memberChannel) = await JoinPublicAsync(member, planet.Id, channel.Id);

        var before = await SendAsync(owner, channel, "before finishing");
        Assert.True(before.Success, before.Message);
        await member.E2eeService.RequestKeysAsync(memberChannel);
        await owner.E2eeService.ServeChannelAsync(channel);
        Assert.Equal("before finishing", await ReadAsync(member, memberChannel, before.Data.Id));

        // As after upgrading: the planet is private, but its keys still follow
        // its permissions, because the server cannot sign for the owner.
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
            await db.Planets.Where(x => x.Id == planet.Id).ExecuteUpdateAsync(x => x.SetProperty(p => p.Public, false));
            var hosted = await scope.ServiceProvider.GetRequiredService<HostedPlanetService>().GetRequiredAsync(planet.Id);
            hosted.Planet.Public = false;
        }

        planet = await owner.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        Assert.True(E2eeService.IsPrivacyPending(planet));

        var finish = await owner.E2eeService.FinishPrivatePlanetAsync(planet);
        Assert.True(finish.Success, finish.Message);
        Assert.False(E2eeService.IsPrivacyPending(planet));
        await AssertStoredAsync(planet.Id, false, PlanetEncryptionMode.InviteOnly, AccessLogEntryType.Genesis);

        // The member was let in with the keys they had, so they keep reading
        // and sending.
        var after = await SendAsync(owner, channel, "after finishing");
        Assert.True(after.Success, after.Message);
        Assert.Equal("before finishing", await ReadAsync(member, memberChannel, before.Data.Id));
        Assert.Equal("after finishing", await ReadAsync(member, memberChannel, after.Data.Id));
        var reply = await SendAsync(member, memberChannel, "still here");
        Assert.True(reply.Success, reply.Message);

        // Someone who joins now with an ordinary invite waits to be let in.
        var newcomer = await CreateUserAsync();
        var newcomerChannel = await JoinWithInviteAsync(owner, planet, newcomer, channel.Id);
        await newcomer.E2eeService.RequestKeysAsync(newcomerChannel);
        await owner.E2eeService.ServeChannelAsync(channel);
        Assert.Null((await newcomer.E2eeService.GetKeyRingAsync(newcomerChannel, refresh: true)).Latest);
    }

    [Fact]
    public async Task NewPrivatePlanet_IsPendingUntilTheOwnersDeviceSigns()
    {
        var owner = await CreateUserAsync();
        var (planet, channel) = await CreatePlanetAsync(owner, isPublic: false);
        Assert.True(E2eeService.IsPrivacyPending(planet));
        await AssertStoredAsync(planet.Id, false, PlanetEncryptionMode.Open, null);

        var finish = await owner.E2eeService.FinishPrivatePlanetAsync(planet);
        Assert.True(finish.Success, finish.Message);
        await AssertStoredAsync(planet.Id, false, PlanetEncryptionMode.InviteOnly, AccessLogEntryType.Genesis);

        var sent = await SendAsync(owner, channel, "first words");
        Assert.True(sent.Success, sent.Message);
        Assert.Equal(ChannelKeyPolicy.PlanetInviteOnly,
            (await owner.E2eeService.GetKeyRingAsync(channel, refresh: true)).Policy);
    }

    [Fact]
    public async Task LinkedDevice_ReceivesThePrivatePlanetsLogPin()
    {
        var (owner, ownerDetails, _) = await CreateUserWithStoreAsync();
        var (planet, _) = await CreatePlanetAsync(owner, isPublic: true);
        var makePrivate = await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, sharesHistory: true);
        Assert.True(makePrivate.Success, makePrivate.Message);

        var laptop = await EncryptedChat.LoginAsync(_fixture, ownerDetails, new MemoryE2eeKeyStore(),
            autoSetUp: false, requireReady: false);
        var shown = await owner.E2eeService.ShowLinkCodeAsync();
        Assert.True(shown.Success, shown.Message);
        var joined = await laptop.E2eeService.JoinLinkAsync(shown.Data.TypedCode);
        Assert.True(joined.Success, joined.Message);
        var session = (await owner.E2eeService.GetPendingLinkRequestsAsync()).Single(s => s.Id == shown.Data.Session.Id);
        var approve = await owner.E2eeService.ApproveLinkAsync(session);
        Assert.True(approve.Success, approve.Message);

        // The approving device's pin for the planet's log comes with the link.
        var complete = await laptop.E2eeService.CompleteLinkAsync();
        Assert.True(complete.Success, complete.Message);
        var laptopPlanet = await laptop.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        var log = await laptop.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, laptopPlanet.Node);
        Assert.NotNull(log);
        Assert.True(log.IsGoverning);
    }

    [Fact]
    public async Task RemovingADevice_EndsItsMembershipLogEntries()
    {
        var (owner, ownerDetails, _) = await CreateUserWithStoreAsync();
        var recoveryCode = owner.E2eeService.PendingRecoveryCode;
        Assert.NotNull(recoveryCode);
        var (planet, channel) = await CreatePlanetAsync(owner, isPublic: true);
        var makePrivate = await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, sharesHistory: true);
        Assert.True(makePrivate.Success, makePrivate.Message);

        // The owner's second device lets someone in before it is removed.
        var laptop = await EncryptedChat.LoginAsync(_fixture, ownerDetails, new MemoryE2eeKeyStore(),
            autoSetUp: false, requireReady: false);
        var restore = await laptop.E2eeService.RestoreWithRecoveryCodeAsync(recoveryCode);
        Assert.True(restore.Success, restore.Message);
        var member = await CreateUserAsync();
        await JoinWithInviteAsync(owner, planet, member, channel.Id);
        var laptopPlanet = await laptop.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        var admit = await laptop.E2eeService.AdmitPlanetMembersAsync(laptopPlanet, [member.Me.Id]);
        Assert.True(admit.Success, admit.Message);

        var laptopDevice = laptop.E2eeService.Device;
        var removal = await owner.E2eeService.RevokeDeviceAsync(laptopDevice.DeviceId);
        Assert.True(removal.Success, removal.Message);

        // The removal ends the laptop's entries at the log's current end, so
        // the member it let in stays in.
        var log = await owner.E2eeService.GetAccessLogStateAsync(AccessLogScope.Planet, planet.Id, planet.Node,
            refresh: true);
        var ownerKeys = await owner.E2eeService.GetUserStateAsync(owner.Me.Id, refresh: true);
        Assert.Equal(log.HeadSeq,
            ownerKeys.EverDevices[laptopDevice.DeviceId].AccessLogCutoffs[(AccessLogScope.Planet, planet.Id)]);
        Assert.True(log.IsMember(member.Me.Id, await owner.E2eeService.GetUserStateAsync(member.Me.Id)));

        // The server takes nothing more signed with the laptop's keys, even
        // an entry dated before the removal.
        var outsider = await CreateUserAsync();
        var outsiderKeys = await owner.E2eeService.GetUserStateAsync(outsider.Me.Id);
        var backdated = AccessLogBuilder.Create(AccessLogState.Decode(log.Encode()), AccessLogEntryType.AddMembers,
            owner.Me.Id, laptopDevice, log.HeadTimestampMs, r => r.With(members: [AccessMember.For(outsiderKeys)]));
        var refused = await owner.PrimaryNode.PostAsync($"api/e2ee/access-logs/{(int)AccessLogScope.Planet}/{planet.Id}",
            backdated);
        Assert.False(refused.Success);
        Assert.Contains("removed", refused.Message);
    }

    [Fact]
    public async Task LoggingOut_EndsTheDevicesEntriesInLogsItNeverOpened()
    {
        var (owner, ownerDetails, _) = await CreateUserWithStoreAsync();
        var recoveryCode = owner.E2eeService.PendingRecoveryCode;
        var (planet, _) = await CreatePlanetAsync(owner, isPublic: true);
        var makePrivate = await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, sharesHistory: true);
        Assert.True(makePrivate.Success, makePrivate.Message);

        // A second device that never opens the planet's log logs out.
        var laptop = await EncryptedChat.LoginAsync(_fixture, ownerDetails, new MemoryE2eeKeyStore(),
            autoSetUp: false, requireReady: false);
        var restore = await laptop.E2eeService.RestoreWithRecoveryCodeAsync(recoveryCode);
        Assert.True(restore.Success, restore.Message);
        var laptopDeviceId = laptop.E2eeService.Device.DeviceId;
        await laptop.E2eeService.SignOutDeviceAsync();

        // It signed nothing in the planet's log, so nothing its keys sign
        // there counts any more.
        var ownerKeys = await owner.E2eeService.GetUserStateAsync(owner.Me.Id, refresh: true);
        var removed = ownerKeys.EverDevices[laptopDeviceId];
        Assert.NotNull(removed.RevokedAt);
        Assert.Equal(-1, removed.AccessLogCutoffs[(AccessLogScope.Planet, planet.Id)]);
    }

    [Fact]
    public async Task PlanetImport_NeverStoresAPublicInviteOnlyPlanet()
    {
        var owner = await CreateUserAsync();
        var (planet, _) = await CreatePlanetAsync(owner, isPublic: true);
        Assert.True((await owner.E2eeService.SetPlanetPrivacyAsync(planet, isPublic: false, true)).Success);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var snapshots = scope.ServiceProvider.GetRequiredService<PlanetSnapshotService>();
            var export = await snapshots.ExportAsync(planet.Id);
            Assert.True(export.Success, export.Message);

            // A snapshot claiming both, for example from a server that still
            // allowed it, arrives private.
            export.Data.Planet.Public = true;
            Assert.True((await snapshots.DeletePlanetDataAsync(planet.Id)).Success);
            scope.ServiceProvider.GetRequiredService<HostedPlanetService>().Remove(planet.Id);
            var import = await snapshots.ImportAsync(export.Data);
            Assert.True(import.Success, import.Message);
        }

        await AssertStoredAsync(planet.Id, false, PlanetEncryptionMode.InviteOnly, AccessLogEntryType.Genesis);
    }
}
