using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Valour.Server;
using Valour.Server.Services;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

public class PlanetPermissionsServiceTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public PlanetPermissionsServiceTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetValourCentralTest()
    {
        var scope = _factory.Services.CreateScope();
        var planetService = scope.ServiceProvider.GetRequiredService<PlanetService>();
        var valourCentral = await planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(valourCentral);
    }

    [Fact]
    public async Task OwnerPermissions()
    {
        var scope = _factory.Services.CreateScope();
        var planetService = scope.ServiceProvider.GetRequiredService<PlanetService>();
        var planetMemberService = scope.ServiceProvider.GetRequiredService<PlanetMemberService>();
        var channelService = scope.ServiceProvider.GetRequiredService<ChannelService>();
        
        var valourCentral = await planetService.GetAsync(ISharedPlanet.ValourCentralId);
        Assert.NotNull(valourCentral);

        var ownerMember = await planetMemberService.GetByUserAsync(valourCentral.OwnerId, ISharedPlanet.ValourCentralId);
        Assert.NotNull(ownerMember);
        
        // Ensure owner has access to all channels
        var allChannels = await planetService.GetAllChannelsAsync(ISharedPlanet.ValourCentralId);
        Assert.NotEmpty(allChannels);

        var channelAccess = await planetService.GetMemberChannelsAsync(ownerMember);

        foreach (var channel in allChannels)
        {
            var canAccess = channelAccess.Contains(channel.Id);
            Assert.True(canAccess);

            var permissions = await channelService.GetPermissionsAsync(channel, ownerMember, channel.ChannelType);
            Assert.Equal(permissions, Permission.FULL_CONTROL);
        }
    }
}