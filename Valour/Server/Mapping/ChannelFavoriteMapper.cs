namespace Valour.Server.Mapping;

public static class ChannelFavoriteMapper
{
    public static ChannelFavorite ToModel(this Valour.Database.ChannelFavorite favorite)
    {
        if (favorite is null)
            return null;

        return new ChannelFavorite
        {
            Id = favorite.Id,
            UserId = favorite.UserId,
            ChannelId = favorite.ChannelId,
            PlanetId = favorite.PlanetId,
            Position = favorite.Position
        };
    }

    public static Valour.Database.ChannelFavorite ToDatabase(this ChannelFavorite favorite)
    {
        if (favorite is null)
            return null;

        return new Valour.Database.ChannelFavorite
        {
            Id = favorite.Id,
            UserId = favorite.UserId,
            ChannelId = favorite.ChannelId,
            PlanetId = favorite.PlanetId,
            Position = favorite.Position
        };
    }
}
