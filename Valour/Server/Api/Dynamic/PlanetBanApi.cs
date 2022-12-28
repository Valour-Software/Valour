namespace Valour.Server.Api.Dynamic;

public class PlanetBanApi
{
    [ValourRoute(HttpVerbs.Get), TokenRequired]
    [PlanetMembershipRequired]
    //[PlanetPermsRequired(PlanetPermissionsEnum.Ban)] (There is an exception to this!)
    public static async Task<IResult> GetRoute(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
        var ban = await FindAsync<PlanetBan>(id, db);
        var member = ctx.GetMember();

        if (ban is null)
            return ValourResult.NotFound<PlanetBan>();

        // You can retrieve your own ban
        if (ban.TargetId != member.Id)
        {
            if (!await member.HasPermissionAsync(PlanetPermissions.Ban, db))
                return ValourResult.LacksPermission(PlanetPermissions.Ban);
        }

        return Results.Json(ban);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> PostRoute(
        [FromBody] PlanetBan ban, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetBan> logger)
    {
        var member = ctx.GetMember();

        if (ban is null)
            return Results.BadRequest("Include ban in body.");

        if (ban.PlanetId != planetId)
            return Results.BadRequest("PlanetId mismatch.");

        if (ban.IssuerId != member.UserId)
            return Results.BadRequest("IssuerId should match user Id.");

        if (ban.TargetId == member.Id)
            return Results.BadRequest("You cannot ban yourself.");

        // Ensure it doesn't already exist
        if (await db.PlanetBans.AnyAsync(x => x.PlanetId == ban.PlanetId && x.TargetId == ban.TargetId))
            return Results.BadRequest("Ban already exists for user.");

        // Ensure user has more authority than the user being banned
        var target = await PlanetMember.FindAsyncByUser(ban.TargetId, ban.PlanetId, db);

        if (target is null)
            return ValourResult.NotFound<PlanetMember>();

        if (await target.GetAuthorityAsync(db) >= await member.GetAuthorityAsync(db))
            return ValourResult.Forbid("The target has a higher authority than you.");

        await using var tran = await db.Database.BeginTransactionAsync();

        try
        {
            ban.Id = IdManager.Generate();

            // Add ban
            await db.PlanetBans.AddAsync(ban);

            // Save changes
            await db.SaveChangesAsync();

            // Delete target member
            await target.DeleteAsync(db);

            // Save changes
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            await tran.RollbackAsync();
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        // Notify of changes
        hubService.NotifyPlanetItemChange(ban);
        hubService.NotifyPlanetItemDelete(target);

        return Results.Created(ban.GetUri(), ban);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> PutRoute(
        [FromBody] PlanetBan ban, 
        long id, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetBan> logger)
    {
        var member = ctx.GetMember();

        if (ban is null)
            return Results.BadRequest("Include updated ban in body.");

        var old = await FindAsync<PlanetBan>(id, db);

        if (old is null)
            return ValourResult.NotFound<PlanetBan>();

        if (ban.PlanetId != old.PlanetId)
            return Results.BadRequest("You cannot change the PlanetId.");

        if (ban.TargetId != old.TargetId)
            return Results.BadRequest("You cannot change who was banned.");

        if (ban.IssuerId != old.IssuerId)
            return Results.BadRequest("You cannot change who banned the user.");

        if (ban.TimeCreated != old.TimeCreated)
            return Results.BadRequest("You cannot change the creation time");

        try
        {
            db.Entry(old).State = EntityState.Detached;
            db.PlanetBans.Update(ban);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        // Notify of changes
        hubService.NotifyPlanetItemChange(ban);

        return Results.Ok(ban);
    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.Ban)]
    public static async Task<IResult> DeleteRoute(
        long id, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetBan> logger)
    {
        var member = ctx.GetMember();

        var ban = await FindAsync<PlanetBan>(id, db);

        // Ensure the user unbanning is either the user that made the ban, or someone
        // with equal or higher authority to them

        if (ban.IssuerId != member.Id)
        {
            var banner = await FindAsync<PlanetMember>(ban.IssuerId, db);

            if (await banner.GetAuthorityAsync(db) > await member.GetAuthorityAsync(db))
                return ValourResult.Forbid("The banner of this user has higher authority than you.");
        }

        try
        {
            db.PlanetBans.Remove(ban);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }


        // Notify of changes
        hubService.NotifyPlanetItemDelete(ban);

        return Results.NoContent();
    }
}