using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Sdk.Client;
using Valour.Server;
using Valour.Shared.Models;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class UserBlockServiceTests : IAsyncLifetime
{
    private readonly LoginTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;

    private long _secondaryUserId;

    public UserBlockServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _factory = fixture.Factory;
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
    }

    public async ValueTask InitializeAsync()
    {
        var details = await _fixture.RegisterUser();
        _secondaryUserId = await _db.Users
            .AsNoTracking()
            .Where(x => x.Name == details.Username)
            .Select(x => x.Id)
            .FirstAsync();
    }

    public ValueTask DisposeAsync()
    {
        _scope.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Block_Persists_And_RefreshReloads()
    {
        var me = _fixture.Client.Me;
        var blockResult = await _fixture.Client.BlockService.BlockUserAsync(_secondaryUserId, BlockType.OneWay);

        Assert.True(blockResult.Success, blockResult.Message);

        var persisted = await _db.UserBlocks
            .AsNoTracking()
            .AnyAsync(x => x.UserId == me.Id && x.BlockedUserId == _secondaryUserId);
        Assert.True(persisted);

        // Simulate a hard refresh with a brand new client instance
        var refreshedClient = new ValourClient("https://localhost:5001/", httpProvider: new TestHttpProvider(_factory));
        var refreshedHttp = _factory.CreateClient();
        refreshedHttp.BaseAddress = new Uri(refreshedClient.BaseAddress);
        refreshedClient.SetHttpClient(refreshedHttp);

        var init = await refreshedClient.InitializeUser(_fixture.Client.AuthService.Token);
        Assert.True(init.Success, init.Message);
        Assert.True(refreshedClient.BlockService.Blocks.Any(x => x.BlockedUserId == _secondaryUserId));
    }

    [Fact]
    public async Task Block_AlreadyBlocked_ReturnsFailure_And_DoesNotDuplicate()
    {
        var me = _fixture.Client.Me;

        var first = await _fixture.Client.BlockService.BlockUserAsync(_secondaryUserId, BlockType.OneWay);
        Assert.True(first.Success, first.Message);

        var second = await _fixture.Client.BlockService.BlockUserAsync(_secondaryUserId, BlockType.TwoWay);
        Assert.False(second.Success);
        Assert.Equal("User is already blocked.", second.Message);

        var rows = await _db.UserBlocks
            .AsNoTracking()
            .Where(x => x.UserId == me.Id && x.BlockedUserId == _secondaryUserId)
            .ToListAsync();

        Assert.Single(rows);
        Assert.Equal(BlockType.OneWay, rows[0].BlockType);
    }
}
