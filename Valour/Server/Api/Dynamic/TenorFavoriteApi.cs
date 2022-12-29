namespace Valour.Server.Api.Dynamic;

public class TenorFavoriteApi
{
    [ValourRoute(HttpVerbs.Post), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostAsync(
        [FromBody] TenorFavorite favorite, 
        HttpContext ctx, 
        ValourDB db)
    {
        var token = ctx.GetToken();
        var user = await User.FindAsync(token.UserId, db);

        favorite.UserId = user.Id;
        favorite.Id = IdManager.Generate();
        
        db.TenorFavorites.Add(favorite);
        await db.SaveChangesAsync();

        return Results.Json(favorite);
    }
    
    [ValourRoute(HttpVerbs.Delete), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostAsync(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
        var token = ctx.GetToken();
        var user = await User.FindAsync(token.UserId, db);

        var favorite = await FindAsync<TenorFavorite>(id, db);

        if (favorite is null)
            return ValourResult.NotFound("Tenor favorite not found.");

        if (favorite.UserId != user.Id)
            return ValourResult.Forbid("You do not own this resource.");

        db.TenorFavorites.Remove(favorite);
        await db.SaveChangesAsync();

        return ValourResult.Ok("Deleted");
    }
}