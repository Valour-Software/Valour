using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Database.Extensions;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Server.Mapping;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class ChannelServiceTests : IAsyncLifetime
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly ValourClient _client;
    private readonly IServiceScope _scope;
    private readonly HostedPlanetService _hostedService;
    private readonly PlanetRoleService _roleService;
    private readonly PlanetService _planetService;
    private readonly ChannelService _channelService;
    private readonly UserService _userService;
    private readonly ValourDb _db;
    
    // This is the ValourCentral planet ID. The "Test User" (_client.Me) is a member here.
    private readonly long _valourCentralId = ISharedPlanet.ValourCentralId;
    
    public ChannelServiceTests(LoginTestFixture fixture)
    {
        _client = fixture.Client;
        _factory = fixture.Factory;

        // Create a new scope for each test class instance
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _roleService = _scope.ServiceProvider.GetRequiredService<PlanetRoleService>();
        _planetService = _scope.ServiceProvider.GetRequiredService<PlanetService>();
        _userService = _scope.ServiceProvider.GetRequiredService<UserService>();
        _hostedService = _scope.ServiceProvider.GetRequiredService<HostedPlanetService>();
        _channelService = _scope.ServiceProvider.GetRequiredService<ChannelService>();
    }
    
    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        
    }

    [Fact]
    public async Task DescendantsOfTests()
    {
        var categories = await _db.Channels.Where(x => x.PlanetId == _valourCentralId &&
                                                      x.ChannelType == ChannelTypeEnum.PlanetCategory)
            .Select(x => x.ToModel())
            .ToListAsync();

        foreach (var category in categories)
        {

            // Manually build descendants via recursion

            var recursiveDescendents = new List<Channel>();
            await AddDescendants(recursiveDescendents, category);

            var extensionDescendants = await _db.Channels.DescendantsOf(category)
                .Select(x => x.ToModel())
                .ToListAsync();

            Assert.Equal(recursiveDescendents.Count, extensionDescendants.Count);

            // Ensure all the children are present
            foreach (var child in recursiveDescendents)
            {
                Assert.True(extensionDescendants.Any(x => x.Id == child.Id));
            }
        }
    }
    
    public async Task AddDescendants(List<Channel> descendants, Channel parent)
    {
        var children = await _db.Channels.DirectChildrenOf(parent)
            .Select(x => x.ToModel())
            .ToListAsync();
        
        descendants.AddRange(children);
        
        foreach (var child in children)
        {
            await AddDescendants(descendants, child);
        }
    }

    [Fact]
    public async Task TestGetNextAvailablePosition()
    {
        // Get a random category in the ValourCentral planet
        var category = await _db.Channels.Where(x => x.PlanetId == _valourCentralId &&
                                                     x.ChannelType == ChannelTypeEnum.PlanetCategory)
            .FirstOrDefaultAsync();

        if (category is null)
            return;
        
        var positionResult = await _channelService.TryGetNextChannelPositionFor(_valourCentralId, category.Id, ChannelTypeEnum.PlanetChat);
        
        Assert.True(positionResult.Success);
        
        // Ensure no other channel has this position
        var otherChannel = await _db.Channels.Where(x => x.PlanetId == _valourCentralId &&
                                                         x.RawPosition == positionResult.Data)
            .FirstOrDefaultAsync();
        
        Assert.Null(otherChannel);

        var position = new ChannelPosition(positionResult.Data);
        
        // Ensure the new position has the correct parent
        Assert.Equal(category.RawPosition, position.GetParentPosition().RawPosition);
    }
}