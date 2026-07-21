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
    
    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        
    }

    [Fact]
    public async Task GetDirectChannel_SelfDm_DoesNotReturnAnotherUsersDm()
    {
        // Regression for #1499: looking up a DM with yourself matched ANY dm
        // channel you were in, opening a random other person's DM
        var myId = _client.Me.Id;

        // Seed a DM with a different user so the old buggy query has something
        // to wrongly match
        var otherUser = await _db.Users.FirstAsync(x => x.Id != myId);
        var otherDm = await _channelService.GetDirectChannelByUsersAsync(myId, otherUser.Id, create: true);
        Assert.NotNull(otherDm);

        // A self-DM must not resolve to the DM with the other user
        var selfDm = await _channelService.GetDirectChannelByUsersAsync(myId, myId, create: true);
        Assert.NotNull(selfDm);
        Assert.NotEqual(otherDm.Id, selfDm.Id);

        // The self-DM must contain only me
        var members = await _channelService.GetDirectChannelMembersAsync(selfDm.Id);
        Assert.NotEmpty(members);
        Assert.All(members, m => Assert.Equal(myId, m.Id));

        // A second lookup returns the same channel instead of creating another
        var selfDmAgain = await _channelService.GetDirectChannelByUsersAsync(myId, myId, create: true);
        Assert.Equal(selfDm.Id, selfDmAgain.Id);

        // The two-user lookup still returns the pair channel, not the self channel
        var otherDmAgain = await _channelService.GetDirectChannelByUsersAsync(myId, otherUser.Id, create: true);
        Assert.Equal(otherDm.Id, otherDmAgain.Id);
    }

    [Fact]
    public async Task PlanetChannels_AreRegisteredInGlobalClientCache()
    {
        // Regression for #1501: channels loaded through a planet were only put
        // in the per-planet store, never the global Client.Cache.Channels,
        // breaking SDK consumers (bots) that look up channels client-wide
        var planet = await _client.PlanetService.FetchPlanetAsync(_valourCentralId, skipCache: true);
        Assert.NotNull(planet);

        await planet.FetchChannelsAsync();
        Assert.NotEmpty(planet.Channels);

        foreach (var channel in planet.Channels)
        {
            Assert.True(_client.Cache.Channels.TryGet(channel.Id, out var cached),
                $"Planet channel {channel.Id} missing from Client.Cache.Channels");
            Assert.Same(channel, cached);
        }
    }

    [Fact]
    public async Task UpdateRootCategory_NameAndDescriptionOnly_Succeeds()
    {
        // Regression for #1486/#1468 (fixed in v0.6.2): HasUniquePosition wasn't
        // scoped to the planet, so editing any ROOT channel (categories have a
        // null ParentId and share the same small RawPosition slots across
        // planets) collided with another planet's root channel and always
        // failed with "The position is already taken."
        var owner = await _userService.GetAsync(_client.Me.Id);

        // Seed a second planet so a cross-planet root-position collision
        // deterministically exists
        var otherPlanetResult = await _planetService.CreateAsync(new Planet
        {
            Name = "Collision Planet",
            Description = "Planet whose root channels share position slots",
            OwnerId = owner.Id
        }, owner);
        Assert.True(otherPlanetResult.Success, otherPlanetResult.Message);

        try
        {
            var category = await _db.Channels
                .Where(x => x.PlanetId == _valourCentralId &&
                            x.ChannelType == ChannelTypeEnum.PlanetCategory &&
                            x.ParentId == null)
                .Select(x => x.ToModel())
                .FirstOrDefaultAsync();

            Assert.NotNull(category);

            var oldName = category.Name;
            var oldDesc = category.Description;
            category.Name = $"Renamed {Guid.NewGuid():N}"[..16];
            category.Description = "Updated description";

            var result = await _channelService.UpdateAsync(category);
            Assert.True(result.Success, result.Message);

            var fromDb = await _db.Channels.AsNoTracking().FirstAsync(x => x.Id == category.Id);
            Assert.Equal(category.Name, fromDb.Name);
            Assert.Equal("Updated description", fromDb.Description);

            // Restore
            category.Name = oldName;
            category.Description = oldDesc;
            var restore = await _channelService.UpdateAsync(category);
            Assert.True(restore.Success, restore.Message);
        }
        finally
        {
            await _planetService.DeleteAsync(otherPlanetResult.Data.Id);
        }
    }

    [Fact]
    public async Task DefaultChannel_CanRename_CannotDelete_CannotFlipIsDefault()
    {
        // Regression for #1431: renaming the default channel must work, deleting
        // it is intentionally blocked with a clear message, and IsDefault can't
        // be flipped through a plain update (that would break the single-default
        // invariant maintained by SetPrimaryChannelAsync)
        var defaultChannel = await _db.Channels
            .AsNoTracking()
            .Where(x => x.PlanetId == _valourCentralId && x.IsDefault && !x.IsDeleted)
            .Select(x => x.ToModel())
            .FirstAsync();

        // Rename works
        var oldName = defaultChannel.Name;
        defaultChannel.Name = $"Renamed {Guid.NewGuid():N}"[..16];
        var rename = await _channelService.UpdateAsync(defaultChannel);
        Assert.True(rename.Success, rename.Message);

        var fromDb = await _db.Channels.AsNoTracking().FirstAsync(x => x.Id == defaultChannel.Id);
        Assert.Equal(defaultChannel.Name, fromDb.Name);
        Assert.True(fromDb.IsDefault);

        // Restore name
        defaultChannel.Name = oldName;
        Assert.True((await _channelService.UpdateAsync(defaultChannel)).Success);

        // Delete is blocked with a clear message
        var del = await _channelService.DeletePlanetChannelAsync(_valourCentralId, defaultChannel.Id);
        Assert.False(del.Success);
        Assert.Contains("default", del.Message, StringComparison.OrdinalIgnoreCase);

        // IsDefault cannot be changed via a plain update
        defaultChannel.IsDefault = false;
        var flip = await _channelService.UpdateAsync(defaultChannel);
        Assert.False(flip.Success);
    }

    [Fact]
    public async Task MoveChannel_IntoEmptyCategory_PersistsParentAndPosition()
    {
        var owner = await _userService.GetAsync(_client.Me.Id);
        var planetResult = await _planetService.CreateAsync(new Planet
        {
            Name = $"Move Test {Guid.NewGuid():N}"[..24],
            Description = "Isolated channel movement regression planet",
            OwnerId = owner.Id
        }, owner);
        Assert.True(planetResult.Success, planetResult.Message);

        try
        {
            var categoryResult = await _channelService.CreateAsync(new Channel
            {
                PlanetId = planetResult.Data.Id,
                Name = "Empty Category",
                Description = "Empty Category",
                ChannelType = ChannelTypeEnum.PlanetCategory
            });
            Assert.True(categoryResult.Success, categoryResult.Message);

            var channelResult = await _channelService.CreateAsync(new Channel
            {
                PlanetId = planetResult.Data.Id,
                Name = "Move Me",
                Description = "Move Me",
                ChannelType = ChannelTypeEnum.PlanetChat
            });
            Assert.True(channelResult.Success, channelResult.Message);

            var moveResult = await _channelService.MoveChannelAsync(
                planetResult.Data.Id,
                channelResult.Data.Id,
                categoryResult.Data.Id,
                insertBefore: false,
                insideCategory: true);
            Assert.True(moveResult.Success, moveResult.Message);

            var moved = await _db.Channels.AsNoTracking()
                .FirstAsync(x => x.Id == channelResult.Data.Id);
            Assert.Equal(categoryResult.Data.Id, moved.ParentId);
            Assert.Equal(
                categoryResult.Data.RawPosition,
                new ChannelPosition(moved.RawPosition).GetParentPosition().RawPosition);
            Assert.Equal(1u, ChannelPosition.GetLocalPosition(moved.RawPosition));

            var hosted = await _hostedService.GetRequiredAsync(planetResult.Data.Id);
            var cached = hosted.GetChannel(channelResult.Data.Id);
            Assert.NotNull(cached);
            Assert.Equal(categoryResult.Data.Id, cached.ParentId);
            Assert.Equal(moved.RawPosition, cached.RawPosition);
        }
        finally
        {
            await _planetService.DeleteAsync(planetResult.Data.Id);
        }
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
