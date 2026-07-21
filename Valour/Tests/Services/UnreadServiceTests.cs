using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Database;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class UnreadServiceTests : IClassFixture<LoginTestFixture>, IDisposable
{
    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly Valour.Server.Services.UnreadService _unreadService;

    public UnreadServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _unreadService = _scope.ServiceProvider.GetRequiredService<Valour.Server.Services.UnreadService>();
    }

    [Fact]
    public async Task GetUnreadChannels_ExcludesVideoChannels()
    {
        var videoChannel = new Valour.Database.Channel
        {
            Id = IdManager.Generate(),
            PlanetId = ISharedPlanet.ValourCentralId,
            Name = "Unread regression video",
            Description = "Video channels cannot have unread messages",
            ChannelType = ChannelTypeEnum.PlanetVideo,
            LastUpdateTime = DateTime.UtcNow,
            RawPosition = 2_000_000_000,
        };

        _db.Channels.Add(videoChannel);
        await _db.SaveChangesAsync();

        try
        {
            var unreadChannelIds = await _unreadService.GetUnreadChannels(
                ISharedPlanet.ValourCentralId,
                _fixture.Client.Me.Id);

            Assert.DoesNotContain(videoChannel.Id, unreadChannelIds);
        }
        finally
        {
            await _db.Channels.Where(x => x.Id == videoChannel.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task GetUnreadDirectChannels_ExcludesChannelsUserIsNotPartyTo()
    {
        var unrelatedUser = await _fixture.RegisterUser();
        var unrelatedUserId = await _db.Users
            .Where(x => x.Name == unrelatedUser.Username)
            .Select(x => x.Id)
            .SingleAsync();

        // Registration creates a DM between the new account and Victor. The
        // fixture's logged-in user is deliberately not a member of it.
        var unrelatedChannel = await _db.Channels
            .Where(x => x.ChannelType == ChannelTypeEnum.DirectChat &&
                        x.Members.Any(m => m.UserId == unrelatedUserId) &&
                        x.Members.Any(m => m.UserId == ISharedUser.VictorUserId))
            .SingleAsync();
        unrelatedChannel.LastUpdateTime = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        Assert.False(await _db.ChannelMembers.AnyAsync(x =>
            x.ChannelId == unrelatedChannel.Id && x.UserId == _fixture.Client.Me.Id));

        var unreadChannelIds = await _unreadService.GetUnreadChannels(
            planetId: null,
            _fixture.Client.Me.Id);

        Assert.DoesNotContain(unrelatedChannel.Id, unreadChannelIds);

        var ownDirectChannelIds = await _db.Channels
            .Where(x => x.PlanetId == null &&
                        x.Members.Any(m => m.UserId == _fixture.Client.Me.Id))
            .Select(x => x.Id)
            .ToHashSetAsync();
        Assert.All(unreadChannelIds, id => Assert.Contains(id, ownDirectChannelIds));
    }

    public void Dispose() => _scope.Dispose();
}
