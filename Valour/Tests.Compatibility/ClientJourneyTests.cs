using Valour.Sdk.Models;
using static Valour.Tests.Compatibility.CompatibilityServer;

namespace Valour.Tests.Compatibility;

/// <summary>
/// What a person does in the app on an ordinary day, done through the SDK
/// against a running server. Built against a released SDK package, these show
/// that the server still works for people who installed that release.
/// </summary>
public class ClientJourneyTests
{
    [Fact]
    public async Task ServerAcceptsThisClientVersion()
    {
        Require();
        var client = await CreateUserAsync();

        var manifest = await client.FetchInstanceManifestAsync();
        Assert.NotNull(manifest);
        Assert.False(client.UpdateRequired,
            $"The server requires client protocol {manifest.MinimumClientProtocol}, so this release would be asked to update.");
    }

    [Fact]
    public async Task PlanetChat_IsEncryptedAndArrivesLive()
    {
        Require();
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var (planet, ownerChannel) = await CreatePlanetAsync(owner);

        // The first message creates the channel's key.
        var welcome = await SendAsync(owner, ownerChannel, "welcome", planet.MyMember.Id);
        Assert.True(welcome.Success, $"Sending failed: {welcome.Message}");

        var (_, memberChannel) = await JoinAndOpenAsync(owner, ownerChannel, member);

        var text = $"hello {Guid.NewGuid():N}";
        Message sent = null;
        var received = await NextAsync<Message>(
            h => member.MessageService.MessageReceived += h, h => member.MessageService.MessageReceived -= h,
            m => m.Content == text,
            async () =>
            {
                var result = await SendAsync(owner, ownerChannel, text, planet.MyMember.Id);
                Assert.True(result.Success, $"Sending failed: {result.Message}");
                sent = result.Data;
            },
            "the member receives the new message, decrypted");
        Assert.Equal(sent.Id, received.Id);

        var edited = $"edited {Guid.NewGuid():N}";
        await NextAsync<Message>(
            h => member.MessageService.MessageEdited += h, h => member.MessageService.MessageEdited -= h,
            m => m.Id == sent.Id && m.Content == edited,
            async () =>
            {
                sent.Content = edited;
                var result = await sent.UpdateAsync();
                Assert.True(result.Success, $"Editing failed: {result.Message}");
            },
            "the member receives the edit, decrypted");

        await NextAsync<MessageReaction>(
            h => received.ReactionAdded += h, h => received.ReactionAdded -= h,
            r => r.Emoji == "👍",
            () => sent.AddReactionAsync("👍"),
            "the member sees the reaction");

        await NextAsync<Message>(
            h => member.MessageService.MessageDeleted += h, h => member.MessageService.MessageDeleted -= h,
            m => m.Id == sent.Id,
            async () =>
            {
                var result = await sent.DeleteAsync();
                Assert.True(result.Success, $"Deleting failed: {result.Message}");
            },
            "the member is told the message was deleted");

        var history = await memberChannel.GetMessagesAsync(long.MaxValue, 20);
        var first = Assert.Single(history, m => m.Id == welcome.Data.Id);
        Assert.Equal("welcome", first.Content);
        Assert.DoesNotContain(history, m => m.Id == sent.Id);
    }

    [Fact]
    public async Task MemberReplies_AreReadableByTheOwner()
    {
        Require();
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var (planet, ownerChannel) = await CreatePlanetAsync(owner);
        Assert.True((await SendAsync(owner, ownerChannel, "first", planet.MyMember.Id)).Success);

        var (memberPlanet, memberChannel) = await JoinAndOpenAsync(owner, ownerChannel, member);
        var opened = await ownerChannel.OpenWithResult("compatibility");
        Assert.True(opened.Success, $"Opening the channel failed: {opened.Message}");

        var text = $"reply {Guid.NewGuid():N}";
        await NextAsync<Message>(
            h => owner.MessageService.MessageReceived += h, h => owner.MessageService.MessageReceived -= h,
            m => m.Content == text,
            async () =>
            {
                var result = await SendAsync(member, memberChannel, text, memberPlanet.MyMember.Id);
                Assert.True(result.Success, $"Sending failed: {result.Message}");
            },
            "the owner receives the member's message, decrypted");
    }

    [Fact]
    public async Task ChannelChanges_ReachMembersLive()
    {
        Require();
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();
        var (planet, ownerChannel) = await CreatePlanetAsync(owner);
        Assert.True((await SendAsync(owner, ownerChannel, "first", planet.MyMember.Id)).Success);
        var (_, memberChannel) = await JoinAndOpenAsync(owner, ownerChannel, member);

        var name = $"renamed-{Guid.NewGuid().ToString("N")[..8]}";
        await NextAsync<Valour.Sdk.ModelLogic.ModelUpdatedEvent<Channel>>(
            h => memberChannel.Updated += h, h => memberChannel.Updated -= h,
            e => e.Model.Name == name,
            async () =>
            {
                ownerChannel.Name = name;
                var result = await ownerChannel.UpdateAsync();
                Assert.True(result.Success, $"Renaming failed: {result.Message}");
            },
            "the member's copy of the channel is renamed");
    }

    [Fact]
    public async Task DirectMessages_AreEncryptedAndArriveLive()
    {
        Require();
        var alice = await CreateUserAsync();
        var bob = await CreateUserAsync();

        var aliceDm = await alice.ChannelService.FetchDmChannelAsync(bob.Me.Id, create: true);
        Assert.NotNull(aliceDm);
        var first = await SendAsync(alice, aliceDm, "hi bob");
        Assert.True(first.Success, $"Sending failed: {first.Message}");

        var bobDm = await bob.ChannelService.FetchDmChannelAsync(alice.Me.Id, skipCache: true);
        Assert.NotNull(bobDm);
        var history = await bobDm.GetMessagesAsync(long.MaxValue, 20);
        Assert.Equal("hi bob", Assert.Single(history, m => m.Id == first.Data.Id).Content);

        var text = $"are you there {Guid.NewGuid():N}";
        await NextAsync<Message>(
            h => bob.MessageService.MessageReceived += h, h => bob.MessageService.MessageReceived -= h,
            m => m.Content == text,
            async () =>
            {
                var result = await SendAsync(alice, aliceDm, text);
                Assert.True(result.Success, $"Sending failed: {result.Message}");
            },
            "bob receives the direct message, decrypted");
    }

    [Fact]
    public async Task FriendRequests_ArriveLive()
    {
        Require();
        var alice = await CreateUserAsync();
        var bob = await CreateUserAsync();

        await NextAsync<FriendEventData>(
            h => bob.FriendService.FriendsChanged += h, h => bob.FriendService.FriendsChanged -= h,
            e => e.User?.Id == alice.Me.Id,
            async () =>
            {
                var result = await alice.FriendService.AddFriendAsync(bob.Me.NameAndTag);
                Assert.True(result.Success, $"Adding a friend failed: {result.Message}");
            },
            "bob is told about alice's friend request");
    }
}
