using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
using Valour.Server.Requests;
using Valour.Server.Services;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Channels.Planets;

namespace Valour.Database.Items.Channels.Planets;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

[Table("planet_category_channels")]
public class PlanetCategoryChannel : PlanetChannel, IPlanetItem, ISharedPlanetCategoryChannel
{
    #region IPlanetItem Implementation

    [JsonIgnore]
    public override string BaseRoute =>
        $"api/planet/{{planetId}}/{nameof(PlanetCategoryChannel)}";

    #endregion

    [JsonIgnore]
    public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

    public override PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetCategoryChannel;

    /// <summary>
    /// Returns if the member has the given permission in this category
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetMember member, CategoryPermission permission, PermissionsService service) =>
        await service.HasPermissionAsync(member, this, permission);
    
    /// <summary>
    /// Returns if the member has the given chat permission in this category
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetMember member, ChatChannelPermission permission, PermissionsService service) =>
        await service.HasPermissionAsync(member, this, permission);

    /// <summary>
    /// Deletes this category
    /// </summary>
    public void DeleteAsync(PlanetCategoryService service) =>
        service.DeleteAsync(this);

    /// <summary>
    /// Returns the children for this category
    /// </summary>
    public async Task<List<PlanetChannel>> GetChildrenAsync(PlanetCategoryService service) =>
        await service.GetChildrenAsync(this);

    #region Routes

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

    /* This route isn't actually needed because you can PUT a new parentId onto a category.
     * But it's still a good reference.
    
    [ValourRoute(HttpVerbs.Post, "/children/{child_id}"), TokenRequired, InjectDB]
    [PlanetMembershipRequired, PlanetPermsRequired(PlanetPermissionsEnum.ManageCategories),
     CategoryChannelPermsRequired("id", CategoryPermissionsEnum.ManageCategory), // Need permission for parent
     CategoryChannelPermsRequired("child_id", CategoryPermissionsEnum.ManageCategory)] // Need permission for child
        public static async Task<IResult> AddChildRouteAsync(HttpContext ctx, ulong id, long planetId, ulong child_id)
        {
            var db = ctx.GetDB();

            var parent = ctx.GetItem<PlanetCategoryChannel>(id);
            var child = ctx.GetItem<PlanetCategoryChannel>(child_id);

            if (parent.PlanetId != planetId || child.PlanetId != planetId)
                return Results.BadRequest("PlanetId mismatch.");

            child.ParentId = parent.Id;

            db.PlanetChannels.Update(child);
            await db.SaveChangesAsync();

            PlanetHub.NotifyPlanetItemChange(child);

            return Results.Ok(child);
        }
        */

    #endregion

    #region Validation

    /// <summary>
    /// Validates that a given name is allowable
    /// </summary>
    public static TaskResult ValidateName(string name)
    {
        if (name.Length > 32)
        {
            return new TaskResult(false, "Planet names must be 32 characters or less.");
        }

        if (!nameRegex.IsMatch(name))
        {
            return new TaskResult(false, "Planet names may only include letters, numbers, dashes, and underscores.");
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates that a given description is allowable
    /// </summary>
    public static TaskResult ValidateDescription(string desc)
    {
        if (desc.Length > 500)
        {
            return new TaskResult(false, "Planet descriptions must be 500 characters or less.");
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates the parent and position of this category
    /// </summary>
    public static async Task<TaskResult> ValidateParentAndPosition(ValourDB db, PlanetCategoryChannel category)
    {
        if (category.ParentId != null)
        {
            var parent = await db.PlanetCategoryChannels.FindAsync(category.ParentId);
            if (parent == null) return new TaskResult(false, "Could not find parent");
            if (parent.PlanetId != category.PlanetId) return new TaskResult(false, "Parent category belongs to a different planet");
            if (parent.Id == category.Id) return new TaskResult(false, "Cannot be own parent");

            // Automatically determine position in this case
            if (category.Position < 0)
            {
                category.Position = (ushort)await db.PlanetChannels.CountAsync(x => x.ParentId == category.ParentId);
            }
            else
            {
                // Ensure position is not already taken
                if (!await HasUniquePosition(db, category))
                    return new TaskResult(false, "The position is already taken.");
            }

            // Ensure this category does not contain itself
            var loop_parent = parent;

            while (loop_parent.ParentId != null)
            {
                if (loop_parent.ParentId == category.Id)
                {
                    return new TaskResult(false, "Cannot create parent loop.");
                }

                loop_parent = await db.PlanetCategoryChannels.FindAsync(loop_parent.ParentId);
            }
        }
        else
        {
            if (category.Position < 0)
            {
                category.Position = (ushort)await db.PlanetChannels.CountAsync(x => x.PlanetId == category.PlanetId && x.ParentId == null);
            }
            else
            {
                // Ensure position is not already taken
                if (!await HasUniquePosition(db, category))
                    return new TaskResult(false, "The position is already taken.");
            }
        }

        return TaskResult.SuccessResult;
    }

    #endregion
}

