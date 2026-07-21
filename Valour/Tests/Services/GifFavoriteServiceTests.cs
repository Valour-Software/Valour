using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Valour.Database.Context;
using Valour.Server;
using Valour.Server.Models;
using Valour.Server.Services;

namespace Valour.Tests.Services;

[Collection("ApiCollection")]
public class GifFavoriteServiceTests : IClassFixture<LoginTestFixture>, IDisposable
{
    private readonly LoginTestFixture _fixture;
    private readonly IServiceScope _scope;
    private readonly ValourDb _db;
    private readonly GifFavoriteService _gifFavorites;
    private readonly TenorFavoriteService _tenorFavorites;

    public GifFavoriteServiceTests(LoginTestFixture fixture)
    {
        _fixture = fixture;
        _scope = fixture.Factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<ValourDb>();
        _gifFavorites = _scope.ServiceProvider.GetRequiredService<GifFavoriteService>();
        _tenorFavorites = _scope.ServiceProvider.GetRequiredService<TenorFavoriteService>();
    }

    [Fact]
    public async Task DeleteGifFavorite_AfterLoadingIt_DoesNotCreateATrackingConflict()
    {
        var favorite = new GifFavorite
        {
            UserId = _fixture.Client.Me.Id,
            Provider = "klipy",
            ProviderId = $"test-{Guid.NewGuid():N}",
            Title = "Regression favorite",
            PreviewUrl = "https://static.klipy.com/regression-preview.gif",
            GifUrl = "https://static.klipy.com/regression.gif",
            Width = 320,
            Height = 240
        };

        var create = await _gifFavorites.CreateAsync(favorite);
        Assert.True(create.Success, create.Message);

        var loaded = await _gifFavorites.GetAsync(create.Data.Id);
        Assert.NotNull(loaded);

        var delete = await _gifFavorites.DeleteAsync(loaded);

        Assert.True(delete.Success, delete.Message);
        Assert.False(await _db.GifFavorites.AnyAsync(x => x.Id == create.Data.Id));
    }

    [Fact]
    public async Task DeleteLegacyTenorFavorite_AfterLoadingIt_DoesNotCreateATrackingConflict()
    {
        var favorite = new TenorFavorite
        {
            UserId = _fixture.Client.Me.Id,
            TenorId = $"test-{Guid.NewGuid():N}"
        };

        var create = await _tenorFavorites.CreateAsync(favorite);
        Assert.True(create.Success, create.Message);

        var loaded = await _tenorFavorites.GetAsync(create.Data.Id);
        Assert.NotNull(loaded);

        var delete = await _tenorFavorites.DeleteAsync(loaded);

        Assert.True(delete.Success, delete.Message);
        Assert.False(await _db.TenorFavorites.AnyAsync(x => x.Id == create.Data.Id));
    }

    public void Dispose() => _scope.Dispose();
}
