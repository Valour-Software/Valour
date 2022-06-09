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
    /// Tries to delete the category while respecting constraints
    /// </summary>
    public override async Task DeleteAsync(ValourDB db)
    {
        // Remove permission nodes
        db.PermissionsNodes.RemoveRange(
            db.PermissionsNodes.Where(x => x.Target_Id == Id)
        );

        // Remove category
        db.PlanetCategoryChannels.Remove(
            await db.PlanetCategoryChannels.FindAsync(Id)
        );

        // Save changes
        await db.SaveChangesAsync();

        // Notify of update
        PlanetHub.NotifyPlanetItemDelete(this);
    }

    

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

    [ValourRoute(HttpVerbs.Post, "/children/order")]
    [TokenRequired]
    public static async Task<IResult> SetChildOrderRouteAsync(ValourDB db, ulong id, ulong planet_id, ulong child_id, [FromBody] long[] order, [FromHeader] string authorization)
    {
        var auth = await AuthToken.TryAuthorize(authorization, db);
        if (auth is null)
            return ValourResult.NoToken();

        var member = await PlanetMember.FindAsync(auth.User_Id, planet_id, db);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var category = await db.PlanetCategoryChannels.FindAsync(id);
        if (category is null)
            return Results.NotFound();

        if (category.Planet_Id != planet_id)
            return Results.BadRequest("Parent_Id mismatch on category.");

        if (!await category.HasPermissionAsync(member, CategoryPermissions.View, db))
            return ValourResult.LacksPermission(CategoryPermissions.View);

        if (!await category.HasPermissionAsync(member, CategoryPermissions.ManageCategory, db))
            return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);

        // Get all current children
        var children = await 

    }

    [ValourRoute(HttpVerbs.Post, "/children/{child_id}")]
    public static async Task<IResult> AddChildRouteAsync(ValourDB db, ulong id, ulong planet_id, ulong child_id, [FromHeader] string authorization)
    {
        var auth = await AuthToken.TryAuthorize(authorization, db);
        if (auth is null)
            return ValourResult.NoToken();

        var member = await PlanetMember.FindAsync(auth.User_Id, planet_id, db);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var category = await db.PlanetCategoryChannels.FindAsync(id);
        if (category is null)
            return Results.NotFound();

        if (category.Planet_Id != planet_id)
            return Results.BadRequest("Parent_Id mismatch on category.");

        if (!await category.HasPermissionAsync(member, CategoryPermissions.View, db))
            return ValourResult.LacksPermission(CategoryPermissions.View);

        if (!await category.HasPermissionAsync(member, CategoryPermissions.ManageCategory, db))
            return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);

        var child = await db.PlanetChannels.FindAsync(child_id);
        if (child is null)
            return Results.NotFound();

        if (!await child.HasPermissionAsync(member, ChatChannelPermissions.View, db))
            return ValourResult.LacksPermission(ChatChannelPermissions.View);

        if (!await child.HasPermissionAsync(member, ChatChannelPermissions.ManageChannel, db))
            return ValourResult.LacksPermission(ChatChannelPermissions.ManageChannel);

        if (child.Planet_Id != planet_id)
            return Results.BadRequest("Parent_Id mismatch on child.");

        child.Parent_Id = id;
        db.PlanetChannels.Update(child);
        await db.SaveChangesAsync();

        PlanetHub.NotifyPlanetItemChange(child);

        return Results.Ok(child);
    }

    [ValourRoute(HttpVerbs.Get, "/children")]
    public async Task<IResult> GetChildrenRouteAsync(ValourDB db, ulong id, ulong planet_id, [FromHeader] string authorization)
    {
        var auth = await AuthToken.TryAuthorize(authorization, db);
        if (auth is null)
            return ValourResult.NoToken();

        var member = await PlanetMember.FindAsync(auth.User_Id, planet_id, db);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var category = await db.PlanetCategoryChannels.FindAsync(id);
        if (category is null)
            return Results.NotFound();

        if (category.Planet_Id != planet_id)
            return Results.BadRequest("Parent_Id mismatch.");

        if (!await category.HasPermissionAsync(member, CategoryPermissions.View, db))
            return ValourResult.LacksPermission(CategoryPermissions.View);

        // Build child list. We don't have to check permissions for each, because even if the ID is there,
        // it's impossible to get any details on the channels that are hidden.
        var children_ids = await db.PlanetChannels.Where(x => x.Parent_Id == id).Select(x => x.Id).ToListAsync();

        return Results.Json(children_ids);
    }

    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [CategoryChannelPermsRequired("id", CategoryPermissionsEnum.View)]
    public static IResult GetRoute(HttpContext ctx, ulong id) =>
        Results.Json(ctx.GetItem<PlanetCategoryChannel>(id));

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDB]
    [PlanetMembershipRequired("planet_id")]
    [CategoryChannelPermsRequired("id", CategoryPermissionsEnum.View, CategoryPermissionsEnum.ManageCategory)]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, ulong id)
    {
        // Get resources
        var db = ctx.GetDB();
        var category = ctx.GetItem<PlanetCategoryChannel>(id);

        var old = await db.PlanetCategoryChannels.FindAsync(id);

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
        db.PlanetCategoryChannels.Update(category);
        await db.SaveChangesAsync();
        PlanetHub.NotifyPlanetItemChange(category);

        // Response
        return Results.Ok(category);
    }

    #endregion


    public override async Task<TaskResult> CanCreateAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        var canGet = await CanGetAsync(token, member, db);
        if (!canGet.Success)
            return canGet;

        await GetPlanetAsync(db);

        if (!await Planet.HasPermissionAsync(member, PlanetPermissions.ManageCategories, db))
            return new TaskResult(false, "Member lacks Planet Permission " + PlanetPermissions.ManageCategories.Name);

        var nameValid = ValidateName(Name);
        if (!nameValid.Success)
            return nameValid;

        var parentPosValid = await ValidateParentAndPosition(db);
        if (!parentPosValid.Success)
            return parentPosValid;

        return TaskResult.SuccessResult;
    }

    public override async Task<TaskResult> CanDeleteAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        await GetPlanetAsync(db);

        if (!await Planet.HasPermissionAsync(member, PlanetPermissions.ManageCategories, db))
            return new TaskResult(false, "Member lacks planet permission " + PlanetPermissions.ManageCategories.Name);

        if (!await HasPermissionAsync(member, CategoryPermissions.ManageCategory, db))
            return new TaskResult(false, "Member lacks category permission " + CategoryPermissions.ManageCategory.Name);


        if (await db.PlanetCategoryChannels.CountAsync(x => x.Planet_Id == Planet_Id) < 2)
            return new TaskResult(false, "Last category cannot be deleted");

        var childCategoryCount = await db.PlanetCategoryChannels.CountAsync(x => x.Parent_Id == Id);
        var childChannelCount = await db.PlanetChatChannels.CountAsync(x => x.Parent_Id == Id);

        if (childCategoryCount != 0 || childChannelCount != 0)
            return new TaskResult(false, "Category must be empty");

        return new TaskResult(true, "Success");
    }

    #region Validation

    /// <summary>
    /// Validates that a given name is allowable for a server
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
    /// Validates that a given description is allowable for a server
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

    public static async Task<bool> HasUniquePosition(ValourDB db, PlanetCategoryChannel category) =>
        // Ensure position is not already taken
        !(await db.PlanetChannels.AnyAsync(x => x.Parent_Id == category.Parent_Id && // Same parent
                                                x.Position == category.Position && // Same position
                                                x.Id != category.Id)); // Not self

    #endregion
}

