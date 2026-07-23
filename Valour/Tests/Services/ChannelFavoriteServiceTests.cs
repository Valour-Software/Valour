using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server.Services;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class ChannelFavoriteServiceTests : IClassFixture<LoginTestFixture>, IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;
    private readonly ValourClient _client;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly ChannelFavoriteService _favorites;

    private readonly long _valourCentralId = ISharedPlanet.ValourCentralId;

    public ChannelFavoriteServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _favorites = _scope.ServiceProvider.GetRequiredService<ChannelFavoriteService>();
    }

    // Favorites are unique per (user, channel) and the fixture user is shared
    // across the collection, so start each test from a clean slate.
    public async ValueTask InitializeAsync() =>
        await _db.ChannelFavorites
            .Where(x => x.UserId == _client.Me.Id)
            .ExecuteDeleteAsync();

    public ValueTask DisposeAsync()
    {
        _scope.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<Valour.Database.Channel> GetCentralChatChannelAsync() =>
        await _db.Channels.AsNoTracking()
            .FirstAsync(x => x.PlanetId == _valourCentralId &&
                             x.ChannelType == ChannelTypeEnum.PlanetChat);

    [Fact]
    public async Task Create_AssignsSequentialPositions_AndRejectsDuplicates()
    {
        var channel = await GetCentralChatChannelAsync();

        var first = await _favorites.CreateAsync(_client.Me.Id, channel.Id, _valourCentralId);
        Assert.True(first.Success, first.Message);
        Assert.Equal(0, first.Data.Position);
        Assert.Equal(_valourCentralId, first.Data.PlanetId);

        // Channels on federated planets aren't in the local db, so unknown ids
        // are accepted - which also gives us a second favorite here.
        var second = await _favorites.CreateAsync(_client.Me.Id, long.MaxValue - 1, _valourCentralId);
        Assert.True(second.Success, second.Message);
        Assert.Equal(1, second.Data.Position);

        var duplicate = await _favorites.CreateAsync(_client.Me.Id, channel.Id, _valourCentralId);
        Assert.False(duplicate.Success);
    }

    [Fact]
    public async Task Create_RejectsCategories()
    {
        var category = await _db.Channels.AsNoTracking()
            .FirstAsync(x => x.PlanetId == _valourCentralId &&
                             x.ChannelType == ChannelTypeEnum.PlanetCategory);

        var result = await _favorites.CreateAsync(_client.Me.Id, category.Id, _valourCentralId);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Reorder_AppliesOrder_AndKeepsUnlistedAfter()
    {
        long[] channelIds = [9_000_000_001, 9_000_000_002, 9_000_000_003];
        foreach (var channelId in channelIds)
        {
            var create = await _favorites.CreateAsync(_client.Me.Id, channelId, _valourCentralId);
            Assert.True(create.Success, create.Message);
        }

        // Move the last channel first; leave the middle one unlisted.
        var reorder = await _favorites.ReorderAsync(
            _client.Me.Id, _valourCentralId, [channelIds[2], channelIds[0]]);
        Assert.True(reorder.Success, reorder.Message);

        var ordered = (await _favorites.GetForUserAsync(_client.Me.Id))
            .Where(x => x.PlanetId == _valourCentralId)
            .OrderBy(x => x.Position)
            .Select(x => x.ChannelId)
            .ToList();

        Assert.Equal([channelIds[2], channelIds[0], channelIds[1]], ordered);
    }

    [Fact]
    public async Task Delete_RemovesFavorite()
    {
        var channel = await GetCentralChatChannelAsync();

        var create = await _favorites.CreateAsync(_client.Me.Id, channel.Id, _valourCentralId);
        Assert.True(create.Success, create.Message);

        var delete = await _favorites.DeleteAsync(_client.Me.Id, channel.Id);
        Assert.True(delete.Success, delete.Message);
        Assert.False(await _db.ChannelFavorites.AnyAsync(
            x => x.UserId == _client.Me.Id && x.ChannelId == channel.Id));

        var missing = await _favorites.DeleteAsync(_client.Me.Id, channel.Id);
        Assert.False(missing.Success);
    }

    [Fact]
    public async Task HttpRoutes_CreateListReorderDelete_RoundTrip()
    {
        var channel = await GetCentralChatChannelAsync();

        // Create
        var createResponse = await _client.Http.PostAsJsonAsync(
            "api/channelfavorites",
            new { channelId = channel.Id, planetId = _valourCentralId });
        createResponse.EnsureSuccessStatusCode();

        var otherResponse = await _client.Http.PostAsJsonAsync(
            "api/channelfavorites",
            new { channelId = 9_100_000_001, planetId = _valourCentralId });
        otherResponse.EnsureSuccessStatusCode();

        // List
        var favorites = await _client.Http.GetFromJsonAsync<List<Valour.Sdk.Models.ChannelFavorite>>(
            "api/users/me/channelfavorites");
        Assert.NotNull(favorites);
        Assert.Equal(2, favorites.Count(x => x.PlanetId == _valourCentralId));

        // Reorder
        var reorderResponse = await _client.Http.PostAsJsonAsync(
            "api/channelfavorites/order",
            new ReorderChannelFavoritesRequest
            {
                PlanetId = _valourCentralId,
                ChannelIds = [9_100_000_001, channel.Id]
            });
        reorderResponse.EnsureSuccessStatusCode();

        favorites = await _client.Http.GetFromJsonAsync<List<Valour.Sdk.Models.ChannelFavorite>>(
            "api/users/me/channelfavorites");
        Assert.Equal(9_100_000_001, favorites!
            .Where(x => x.PlanetId == _valourCentralId)
            .OrderBy(x => x.Position)
            .First().ChannelId);

        // Delete
        var deleteResponse = await _client.Http.DeleteAsync(
            $"api/channelfavorites/by-channel/{channel.Id}");
        deleteResponse.EnsureSuccessStatusCode();

        favorites = await _client.Http.GetFromJsonAsync<List<Valour.Sdk.Models.ChannelFavorite>>(
            "api/users/me/channelfavorites");
        Assert.DoesNotContain(favorites!, x => x.ChannelId == channel.Id);
    }
}
