using Valour.Sdk.Client;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Sdk.Services;
using Valour.Shared.Authorization;

namespace Valour.Tests.Apis;

/// <summary>
/// Who may publish a channel's newest key. Everyone sends with it, so only
/// members who may send messages can publish one.
/// </summary>
[Collection("ApiCollection")]
public class E2eeKeyPublishingTests
{
    private readonly LoginTestFixture _fixture;

    public E2eeKeyPublishingTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    private async Task<ValourClient> CreateUserAsync() =>
        await EncryptedChat.LoginAsync(_fixture, await _fixture.RegisterUser(), new MemoryE2eeKeyStore());

    [Fact]
    public async Task ReadOnlyViewer_CannotPublishANewKey()
    {
        var owner = await CreateUserAsync();
        var member = await CreateUserAsync();

        var create = await new Planet(owner)
        {
            Name = $"Keys {Guid.NewGuid().ToString()[..8]}",
            Description = "Key publishing test planet",
            Public = true,
            Discoverable = true
        }.CreateAsync();
        Assert.True(create.Success, create.Message);
        var planet = await owner.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
        await planet.EnsureReadyAsync();
        var channel = await planet.FetchPrimaryChatChannelAsync();

        var join = await member.PlanetService.JoinPlanetAsync(planet.Id);
        Assert.True(join.Success, join.Message);
        var memberPlanet = await member.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await memberPlanet.EnsureReadyAsync();
        var memberChannel = await memberPlanet.FetchChannelAsync(channel.Id);

        var first = await EncryptedChat.SendAsync(owner, channel, "first", planet.MyMember.Id);
        Assert.True(first.Success, first.Message);
        await member.E2eeService.RequestKeysAsync(memberChannel);
        await owner.E2eeService.ServeChannelAsync(channel);

        // A member who may post can replace the key.
        var allowed = await member.E2eeService.RotateChannelKeyAsync(memberChannel,
            ChannelKeyRotationReason.MemberRemoved);
        Assert.True(allowed.Success, allowed.Message);

        // Without the post permission the member still reads the channel but
        // cannot publish the key everyone else would have to send with.
        await planet.EnsureRolesLoadedAsync();
        var role = planet.DefaultRole;
        role.ChatPermissions = ChatChannelPermissions.Default & ~ChatChannelPermissions.PostMessages.Value;
        var update = await role.UpdateAsync();
        Assert.True(update.Success, update.Message);

        Assert.NotNull(await member.E2eeService.GetKeyRingAsync(memberChannel, refresh: true));
        var refused = await member.E2eeService.RotateChannelKeyAsync(memberChannel,
            ChannelKeyRotationReason.MemberRemoved);
        Assert.False(refused.Success);
        Assert.Contains("cannot send", refused.Message);
    }
}
