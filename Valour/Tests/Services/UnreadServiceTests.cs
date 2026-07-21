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

    public void Dispose() => _scope.Dispose();
}
