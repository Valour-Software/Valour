using Microsoft.AspNetCore.Mvc;
using Valour.Database.Items;
using Valour.Shared.Authorization;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
using Valour.Shared.Items.Users;

namespace Valour.Database;

/// <summary>
/// Represents a favorite gif or media from Tenor
/// </summary>
[Table("tenor_favorites")]
public class TenorFavorite : Item, ISharedTenorFavorite
{
    [Column("id")]
    public long Id { get; set; }

    [Column("user_id")]
    public long UserId { get; set; }
    
    [Column("tenor_id")]
    public string TenorId { get; set; }

    #region Routes

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
    
    #endregion
}