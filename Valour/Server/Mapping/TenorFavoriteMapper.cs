namespace Valour.Server.Mapping;

public static class TenorFavoriteMapper
{
    public static TenorFavorite ToModel(this Valour.Database.TenorFavorite fav)
    {
        if (fav is null)
            return null;
        
        return new TenorFavorite()
        {
            Id = fav.Id,
            UserId = fav.UserId,
            TenorId = fav.TenorId
        };
    }
    
    public static Valour.Database.TenorFavorite ToDatabase(this TenorFavorite fav)
    {
        if (fav is null)
            return null;
        
        return new Valour.Database.TenorFavorite()
        {
            Id = fav.Id,
            UserId = fav.UserId,
            TenorId = fav.TenorId
        };
    }
}