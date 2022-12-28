namespace Valour.Server.Api.Dynamic;

public class PlanetRoleApi
{
    [ValourRoute(HttpVerbs.Get), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired]
    public static async Task<IResult> GetRouteAsync(
        long id, 
        ValourDB db)
    {
        var role = await FindAsync<PlanetRole>(id, db);

        if (role is null)
            return ValourResult.NotFound<PlanetRole>();

        return Results.Json(role);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageRoles)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetRole role, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PlanetMemberService memberService,
        ILogger<PlanetRole> logger)
    {
        var authMember = ctx.GetMember();

        role.Position = await db.PlanetRoles.CountAsync(x => x.PlanetId == role.PlanetId);
        role.Id = IdManager.Generate();

        if (role.GetAuthority() > await authMember.GetAuthorityAsync(memberService))
            return ValourResult.Forbid("You cannot create roles with higher authority than your own.");

        try
        {
            await db.AddAsync(role);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(role);

        return Results.Created(role.GetUri(), role);

    }

    [ValourRoute(HttpVerbs.Put), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageRoles)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetRole role, 
        long id,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetRole> logger)
    {
        var oldRole = await FindAsync<PlanetRole>(id, db);

        if (role.PlanetId != oldRole.PlanetId)
            return Results.BadRequest("You cannot change what planet.");

        if (role.Position != oldRole.Position)
            return Results.BadRequest("Position cannot be changed directly.");
        try
        {
            db.Entry(oldRole).State = EntityState.Detached;
            db.PlanetRoles.Update(role);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(role);

        return Results.Json(role);

    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageRoles)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetRole> logger)
    {
        var role = await FindAsync<PlanetRole>(id, db);

        try
        {
            await role.DeleteAsync(db);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemDelete(role);

        return Results.NoContent();

    }
}