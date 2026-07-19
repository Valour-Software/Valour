namespace Valour.Server.Mapping;

public static class GifFavoriteMapper
{
    public static GifFavorite ToModel(this Valour.Database.GifFavorite favorite)
    {
        if (favorite is null)
            return null;

        return new GifFavorite
        {
            Id = favorite.Id,
            UserId = favorite.UserId,
            Provider = favorite.Provider,
            ProviderId = favorite.ProviderId,
            Title = favorite.Title,
            PreviewUrl = favorite.PreviewUrl,
            GifUrl = favorite.GifUrl,
            Width = favorite.Width,
            Height = favorite.Height
        };
    }

    public static Valour.Database.GifFavorite ToDatabase(this GifFavorite favorite)
    {
        if (favorite is null)
            return null;

        return new Valour.Database.GifFavorite
        {
            Id = favorite.Id,
            UserId = favorite.UserId,
            Provider = favorite.Provider,
            ProviderId = favorite.ProviderId,
            Title = favorite.Title,
            PreviewUrl = favorite.PreviewUrl,
            GifUrl = favorite.GifUrl,
            Width = favorite.Width,
            Height = favorite.Height
        };
    }
}
