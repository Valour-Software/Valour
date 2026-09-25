using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.E2ee;
using Valour.Sdk.Models;
using Valour.Sdk.Services;

namespace Valour.Tests.Apis;

/// <summary>
/// Opening a channel gets its keys ready, so a member's first message does
/// not wait for someone to share the channel's key.
/// </summary>
[Collection("ApiCollection")]
public class E2eeKeyPreparationTests
{
    private readonly LoginTestFixture _fixture;

    public E2eeKeyPreparationTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task OpeningAChannel_RequestsTheKeyBeforeTheFirstSend()
    {
        var owner = await EncryptedChat.LoginAsync(_fixture, await _fixture.RegisterUser(), new MemoryE2eeKeyStore());
        var create = await new Planet(owner)
        {
            Name = $"Prepare {Guid.NewGuid().ToString()[..8]}",
            Description = "Key preparation test planet",
            Public = true,
            Discoverable = true
        }.CreateAsync();
        Assert.True(create.Success, create.Message);
        var planet = await owner.PlanetService.FetchPlanetAsync(create.Data.Id, skipCache: true);
        await planet.EnsureReadyAsync();
        var channel = await planet.FetchPrimaryChatChannelAsync();
        Assert.True((await EncryptedChat.SendAsync(owner, channel, "the channel's first key")).Success);

        // The member joins after the key was made, so no copy was sealed to them.
        var member = await EncryptedChat.LoginAsync(_fixture, await _fixture.RegisterUser(), new MemoryE2eeKeyStore());
        Assert.True((await member.PlanetService.JoinPlanetAsync(planet.Id)).Success);
        var memberPlanet = await member.PlanetService.FetchPlanetAsync(planet.Id, skipCache: true);
        await memberPlanet.EnsureReadyAsync();
        var memberChannel = await memberPlanet.FetchChannelAsync(channel.Id);
        Assert.Null((await member.E2eeService.GetKeyRingAsync(memberChannel, refresh: true))?.Latest);

        await member.E2eeService.PrepareToSendAsync(memberChannel);

        ChannelKeySecret received = null;
        for (var i = 0; i < 50 && received is null; i++)
        {
            await Task.Delay(100);
            received = (await member.E2eeService.GetKeyRingAsync(memberChannel, refresh: true))?.Latest;
        }
        Assert.NotNull(received);
        Assert.Equal(1, received.Generation);

        // The first message uses the shared key rather than starting a new one.
        var sent = await EncryptedChat.SendAsync(member, memberChannel, "no waiting");
        Assert.True(sent.Success, sent.Message);
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ValourDb>();
        Assert.Equal(1, await db.E2eeChannelKeyGenerations.CountAsync(x => x.ChannelId == channel.Id));
    }
}
