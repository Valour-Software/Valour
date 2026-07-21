using Valour.Client.Components.Menus.Upload;
using Valour.Sdk.Client;
using Valour.Sdk.Models;

namespace Valour.Tests.Client;

public class KlipyMenuComponentTests
{
    [Fact]
    public void BuildFavoriteResults_ProducesRenderableMedia()
    {
        var client = new ValourClient("https://api.valour.example/");
        var favorite = new GifFavorite(client)
        {
            Provider = "klipy",
            ProviderId = "favorite-slug",
            Title = "Favorite GIF",
            PreviewUrl = "https://cdn.example/preview.gif",
            GifUrl = "https://cdn.example/full.gif",
            Width = 320,
            Height = 180,
        };

        var results = KlipyMenuComponent.BuildFavoriteResults([favorite]);

        var gif = Assert.Single(results.Results);
        Assert.Equal(favorite.ProviderId, gif.Slug);
        Assert.Equal(favorite.PreviewUrl, gif.Preview?.Url);
        Assert.Equal(favorite.GifUrl, gif.Gif?.Url);
    }

    [Fact]
    public void BuildFavoriteResults_IgnoresOtherProviders()
    {
        var client = new ValourClient("https://api.valour.example/");
        var legacy = new GifFavorite(client)
        {
            Provider = "other",
            ProviderId = "legacy",
        };

        var results = KlipyMenuComponent.BuildFavoriteResults([legacy]);

        Assert.Empty(results.Results);
    }
}
