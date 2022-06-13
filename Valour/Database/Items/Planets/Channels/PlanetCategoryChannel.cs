using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web.Mvc;
using Valour.Database.Attributes;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Http;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Channels;
using Valour.Database.Extensions;
using Z.BulkOperations;
using Valour.Database.Nodes;
using Microsoft.Extensions.Logging;

namespace Valour.Database.Items.Planets.Channels;

[Table("PlanetCategoryChannels")]
public class PlanetCategoryChannel : PlanetChannel, ISharedPlanetCategoryChannel
{

    [JsonIgnore]
    public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

    /// <summary>
    /// The type of this item
    /// </summary>
    [NotMapped]
    public override ItemType ItemType => ItemType.PlanetCategoryChannel;

    /// <summary>
    /// Returns if the member has the given permission in this category
    /// </summary>
    public override async Task<bool> HasPermissionAsync(PlanetMember member, Permission permission, ValourDB db)
    {
        Planet planet = await GetPlanetAsync(db);

        if (planet.Owner_Id == member.User_Id)
        {
            return true;
        }

        // If true, we ask the parent
        if (InheritsPerms)
        {
            return await (await GetParentAsync(db)).HasPermissionAsync(member, permission, db);
        }

        var roles = await member.GetRolesAsync(db);

        var do_channel = permission is ChatChannelPermission;

        // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
        // If we get an undecided, we continue to the next role down
        foreach (var role in roles.OrderBy(x => x.Position))
        {
            PermissionsNode node = null;

            if (do_channel)
                node = await role.GetChannelNodeAsync(this, db);
            else
                node = await role.GetCategoryNodeAsync(this, db);

            // If we are dealing with the default role and the behavior is undefined, we fall back to the default permissions
            if (node == null)
            {
                if (role.Id == planet.Default_Role_Id)
                {
                    if (do_channel)
                        return Permission.HasPermission(ChatChannelPermissions.Default, permission);
                    else
                        return Permission.HasPermission(CategoryPermissions.Default, permission);
                }

                continue;
            }

            PermissionState state = PermissionState.Undefined;

            state = node.GetPermissionState(permission);

            if (state == PermissionState.Undefined)
            {
                continue;
            }
            else if (state == PermissionState.True)
            {
                return true;
            }
            else
            {
                return false;
            }

        }

        // No roles ever defined behavior: resort to false.
        return false;
    }

    /// <summary>
    /// Returns the children for this category
    /// </summary>
    public async Task<List<PlanetChannel>> GetChildrenAsync(ValourDB db)
        => await db.PlanetChannels.Where(x => x.Parent_Id == Id).ToListAsync();

    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [CategoryChannelPermsRequired("id", CategoryPermissionsEnum.View)]
    public static IResult GetRoute(HttpContext ctx, ulong id) =>
        Results.Json(ctx.GetItem<PlanetCategoryChannel>(id));

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [PlanetPermsRequired("planet_id", PlanetPermissionsEnum.ManageCategories)]
    [CategoryChannelPermsRequired("id", CategoryPermissionsEnum.View, 
                                        CategoryPermissionsEnum.ManageCategory)]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, ulong id, [FromBody] PlanetCategoryChannel category, 
        ILogger<PlanetCategoryChannel> logger)
    {
        // Get resources
        var db = ctx.GetDB();
        var old = ctx.GetItem<PlanetCategoryChannel>(id);

        // Validation
        if (old.Id != category.Id)
            return Results.BadRequest("Cannot change Id.");
        if (old.Planet_Id != category.Planet_Id)
            return Results.BadRequest("Cannot change Planet_Id.");

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
            db.PlanetCategoryChannels.Update(category);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(category);

        // Response
        return Results.Ok(category);
    }

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [PlanetPermsRequired("planet_id", PlanetPermissionsEnum.ManageCategories)]
    public static async Task<IResult> PostRouteAsync(HttpContext ctx, ulong planet_id, [FromBody] PlanetCategoryChannel category,
        ILogger<PlanetCategoryChannel> logger)
    {
        // Get resources
        var db = ctx.GetDB();
        var member = ctx.GetMember();

        if (category.Planet_Id != planet_id)
            return Results.BadRequest("Planet_Id mismatch.");

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
        if (category.Parent_Id is not null)
        {
            var parent_cat = await db.PlanetCategoryChannels.FindAsync(category.Parent_Id);
            if (!await parent_cat.HasPermissionAsync(member, CategoryPermissions.ManageCategory, db))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }

        try
        {
            await db.PlanetCategoryChannels.AddAsync(category);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(category);

        return Results.Created(category.GetUri(), category);
    }


    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [PlanetPermsRequired("planet_id", PlanetPermissionsEnum.ManageCategories),
     CategoryChannelPermsRequired("id", CategoryPermissionsEnum.View,
                                      CategoryPermissionsEnum.ManageCategory)]
    public static async Task<IResult> DeleteRouteAsync(HttpContext ctx, ulong id, ulong planet_id,
        ILogger<PlanetCategoryChannel> logger)
    {
        var db = ctx.GetDB();
        var category = ctx.GetItem<PlanetCategoryChannel>(id);

        if (await db.PlanetCategoryChannels.CountAsync(x => x.Planet_Id == planet_id) < 2)
            return Results.BadRequest("Last category cannot be deleted.");

        var childCount = await db.PlanetChannels.CountAsync(x => x.Parent_Id == id);

        if (childCount > 0)
            return Results.BadRequest("Category must be empty.");

        // Always use transaction for multi-step DB operations
        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Remove permission nodes
            await db.BulkDeleteAsync(
                db.PermissionsNodes.Where(x => x.Target_Id == id)
            );

            // Remove category
            db.PlanetCategoryChannels.Remove(
                category
            );

            // Save changes
            await db.SaveChangesAsync();

            await transaction.CommitAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            await transaction.RollbackAsync();
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemDelete(category);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/children"), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [CategoryChannelPermsRequired("id", CategoryPermissionsEnum.View)]
    public async Task<IResult> GetChildrenRouteAsync(HttpContext ctx, ulong id)
    {
        var category = ctx.GetItem<PlanetCategoryChannel>(id);
        var db = ctx.GetDB();

        // Build child list. We don't have to check permissions for each, because even if the ID is there,
        // it's impossible to get any details on the channels that are hidden.
        var children_ids = await db.PlanetChannels.Where(x => x.Parent_Id == id).Select(x => x.Id).ToListAsync();

        return Results.Json(children_ids);
    }

    [ValourRoute(HttpVerbs.Post, "/children/order"), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [PlanetPermsRequired("planet_id", PlanetPermissionsEnum.ManageCategories),
    CategoryChannelPermsRequired("id", CategoryPermissionsEnum.ManageCategory)]
    public static async Task<IResult> SetChildOrderRouteAsync(HttpContext ctx, ulong id, ulong planet_id, [FromBody] ulong[] order,
        ILogger<PlanetCategoryChannel> logger)
    {
        var db = ctx.GetDB();
        var category = ctx.GetItem<PlanetCategoryChannel>(id);

        if (category.Planet_Id != planet_id)
            return Results.BadRequest("Parent_Id mismatch.");

        // Use transaction so we can stop at any failure
        var tran = await db.Database.BeginTransactionAsync();

        List<PlanetCategoryChannel> children = new();

        try
        {
            var pos = 0;
            foreach (var child_id in order)
            {
                var child = await FindAsync<PlanetCategoryChannel>(child_id, db);
                if (child is null)
                {
                    return Results.NotFound($"Child {child_id} was not found.");
                }

                if (child.Parent_Id != category.Id)
                    return Results.BadRequest($"Category {child_id} is not a child of {category.Id}.");

                child.Position = pos;

                db.Update(child);

                children.Add(child);

                pos++;
            }

            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            await tran.RollbackAsync();
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        foreach (var child in children)
        {
            PlanetHub.NotifyPlanetItemChange(child);
        }

        return Results.NoContent();

    }

    /* This route isn't actually needed because you can PUT a new parent_id onto a category.
     * But it's still a good reference.
    
    [ValourRoute(HttpVerbs.Post, "/children/{child_id}"), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [PlanetPermsRequired("planet_id", PlanetPermissionsEnum.ManageCategories),
     CategoryChannelPermsRequired("id", CategoryPermissionsEnum.ManageCategory), // Need permission for parent
     CategoryChannelPermsRequired("child_id", CategoryPermissionsEnum.ManageCategory)] // Need permission for child
        public static async Task<IResult> AddChildRouteAsync(HttpContext ctx, ulong id, ulong planet_id, ulong child_id)
        {
            var db = ctx.GetDB();

            var parent = ctx.GetItem<PlanetCategoryChannel>(id);
            var child = ctx.GetItem<PlanetCategoryChannel>(child_id);

            if (parent.Planet_Id != planet_id || child.Planet_Id != planet_id)
                return Results.BadRequest("Planet_Id mismatch.");

            child.Parent_Id = parent.Id;

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
        if (category.Parent_Id != null)
        {
            var parent = await db.PlanetCategoryChannels.FindAsync(category.Parent_Id);
            if (parent == null) return new TaskResult(false, "Could not find parent");
            if (parent.Planet_Id != category.Planet_Id) return new TaskResult(false, "Parent category belongs to a different planet");
            if (parent.Id == category.Id) return new TaskResult(false, "Cannot be own parent");

            // Automatically determine position in this case
            if (category.Position < 0)
            {
                category.Position = (ushort)(await db.PlanetChannels.CountAsync(x => x.Parent_Id == category.Parent_Id));
            }
            else
            {
                // Ensure position is not already taken
                if (!await HasUniquePosition(db, category))
                    return new TaskResult(false, "The position is already taken.");
            }

            // Ensure this category does not contain itself
            var loop_parent = parent;

            while (loop_parent.Parent_Id != null)
            {
                if (loop_parent.Parent_Id == category.Id)
                {
                    return new TaskResult(false, "Cannot create parent loop.");
                }

                loop_parent = await db.PlanetCategoryChannels.FindAsync(loop_parent.Parent_Id);
            }
        }
        else
        {
            if (category.Position < 0)
            {
                category.Position = (ushort)(await db.PlanetChannels.CountAsync(x => x.Planet_Id == category.Planet_Id && x.Parent_Id == null));
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

