namespace Valour.Server.Api.Dynamic;

public class PlanetCategoryApi
{
    [ValourRoute(HttpVerbs.Get), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired, CategoryChannelPermsRequired(CategoryPermissionsEnum.View)]
    public static IResult GetRoute(long id, HttpContext ctx) =>
        Results.Json(ctx.GetItem<PlanetCategoryChannel>(id));

    [ValourRoute(HttpVerbs.Put), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageCategories)]
    [CategoryChannelPermsRequired(CategoryPermissionsEnum.ManageCategory)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetCategoryChannel category, 
        long id, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetCategoryChannel> logger)
    {
        // Get resources
        var old = ctx.GetItem<PlanetCategoryChannel>(id);

        // Validation
        if (old.Id != category.Id)
            return Results.BadRequest("Cannot change Id.");
        if (old.PlanetId != category.PlanetId)
            return Results.BadRequest("Cannot change PlanetId.");

        var nameValid = ValidateName(category.Name);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        var descValid = ValidateDescription(category.Description);
        if (!descValid.Success)
            return Results.BadRequest(descValid.Message);

        var positionValid = await ValidateParentAndPosition(db, category);
        if (!positionValid.Success)
            return Results.BadRequest(positionValid.Message);

        // Update
        try
        {
            db.Entry(old).State = EntityState.Detached;
            db.PlanetCategoryChannels.Update(category);
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(category);

        // Response
        return Results.Ok(category);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageCategories)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetCategoryChannel category, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PermissionsService permService,
        ILogger<PlanetCategoryChannel> logger)
    {
        // Get resources
        var member = ctx.GetMember();

        if (category.PlanetId != planetId)
            return Results.BadRequest("PlanetId mismatch.");

        var nameValid = ValidateName(category.Name);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        var descValid = ValidateDescription(category.Description);
        if (!descValid.Success)
            return Results.BadRequest(descValid.Message);

        var positionValid = await ValidateParentAndPosition(db, category);
        if (!positionValid.Success)
            return Results.BadRequest(positionValid.Message);

        // Ensure user has permission for parent category management
        if (category.ParentId is not null)
        {
            var parent = await db.PlanetCategoryChannels.FindAsync(category.ParentId);
            if (!await parent.HasPermissionAsync(member, CategoryPermissions.ManageCategory, permService))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }

        category.Id = IdManager.Generate();

        try
        {
            await db.PlanetCategoryChannels.AddAsync(category);
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(category);

        return Results.Created(category.GetUri(), category);
    }

    [ValourRoute(HttpVerbs.Post, "/detailed"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageCategories)]
    public static async Task<IResult> PostRouteWithDetailsAsync(
        [FromBody] CreatePlanetCategoryChannelRequest request,
        long planetId,
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PermissionsService permService,
        PlanetMemberService memberService,
         ILogger<PlanetCategoryChannel> logger)
    {
        // Get resources
        var member = ctx.GetMember();

        var category = request.Category;

        if (category.PlanetId != planetId)
            return Results.BadRequest("PlanetId mismatch.");

        var nameValid = ValidateName(category.Name);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        var descValid = ValidateDescription(category.Description);
        if (!descValid.Success)
            return Results.BadRequest(descValid.Message);

        var positionValid = await ValidateParentAndPosition(db, category);
        if (!positionValid.Success)
            return Results.BadRequest(positionValid.Message);

        // Ensure user has permission for parent category management
        if (category.ParentId is not null)
        {
            var parent = await db.PlanetCategoryChannels.FindAsync(category.ParentId);
            if (!await parent.HasPermissionAsync(member, CategoryPermissions.ManageCategory, permService))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }

        category.Id = IdManager.Generate();

        List<PermissionsNode> nodes = new();

        // Create nodes
        foreach (var nodeReq in request.Nodes)
        {
            var node = nodeReq;
            node.TargetId = category.Id;
            node.PlanetId = planetId;

            var role = await FindAsync<PlanetRole>(node.RoleId, db);
            if (role.GetAuthority() > await member.GetAuthorityAsync(memberService))
                return ValourResult.Forbid("A permission node's role has higher authority than you.");

            node.Id = IdManager.Generate();

            nodes.Add(node);
        }

        var tran = await db.Database.BeginTransactionAsync();

        try
        {
            await db.PlanetCategoryChannels.AddAsync(category);
            await db.SaveChangesAsync();

            await db.PermissionsNodes.AddRangeAsync(nodes);
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        hubService.NotifyPlanetItemChange(category);

        return Results.Created(category.GetUri(), category);
    }


    [ValourRoute(HttpVerbs.Delete), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageCategories),
     CategoryChannelPermsRequired(CategoryPermissionsEnum.ManageCategory)]
    public static async Task<IResult> DeleteRouteAsync(
        long id,
        long planetId,
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PlanetCategoryService categoryService)
    {
        var category = ctx.GetItem<PlanetCategoryChannel>(id);

        if (await db.PlanetCategoryChannels.CountAsync(x => x.PlanetId == planetId) < 2)
            return Results.BadRequest("Last category cannot be deleted.");

        var childCount = await db.PlanetChannels.CountAsync(x => x.ParentId == id);

        if (childCount > 0)
            return Results.BadRequest("Category must be empty.");
        
        await categoryService.DeleteAsync(category);
        hubService.NotifyPlanetItemDelete(category);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/children"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired, CategoryChannelPermsRequired(CategoryPermissionsEnum.View)]
    public static async Task<IResult> GetChildrenRouteAsync(
        long id,
        ValourDB db)
    {
        // Build child list. We don't have to check permissions for each, because even if the ID is there,
        // it's impossible to get any details on the channels that are hidden.
        var children_ids = await db.PlanetChannels.Where(x => x.ParentId == id).Select(x => x.Id).ToListAsync();

        return Results.Json(children_ids);
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/children/order"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageCategories),
     CategoryChannelPermsRequired(CategoryPermissionsEnum.ManageCategory)]
    public static async Task<IResult> SetChildOrderRouteAsync(
        [FromBody] long[] order, 
        long id, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetCategoryChannel> logger)
    {
        var category = ctx.GetItem<PlanetCategoryChannel>(id);

        if (category.PlanetId != planetId)
            return Results.BadRequest("ParentId mismatch.");

        order = order.Distinct().ToArray();

        var totalChildren = await db.PlanetChannels.CountAsync(x => x.ParentId == id);

        if (totalChildren != order.Length)
            return Results.BadRequest("Your order does not contain all the children.");

        // Use transaction so we can stop at any failure
        await using var tran = await db.Database.BeginTransactionAsync();

        List<PlanetChannel> children = new();

        try
        {
            var pos = 0;
            foreach (var child_id in order)
            {
                var child = await FindAsync<PlanetChannel>(child_id, db);
                if (child is null)
                {
                    return ValourResult.NotFound<PlanetChannel>();
                }

                if (child.ParentId != category.Id)
                    return Results.BadRequest($"Category {child_id} is not a child of {category.Id}.");

                child.Position = pos;

                // child.TimeLastActive = DateTime.SpecifyKind(child.TimeLastActive, DateTimeKind.Utc);

                db.PlanetChannels.Update(child);

                children.Add(child);

                pos++;
            }

            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        foreach (var child in children)
        {
            hubService.NotifyPlanetItemChange(child);
        }

        return Results.NoContent();

    }
}