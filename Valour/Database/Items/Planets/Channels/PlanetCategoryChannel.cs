using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Channels;

namespace Valour.Database.Items.Planets.Channels;

[Table("PlanetCategoryChannels")]
public class PlanetCategoryChannel : PlanetChannel, ISharedPlanetCategoryChannel, INodeSpecific
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
    /// Validates the parent and position of this category
    /// </summary>
    public async Task<TaskResult> ValidateParentAndPosition(ValourDB db)
    {
        if (Parent_Id != null)
        {
            var parent = await db.PlanetCategoryChannels.FindAsync(Parent_Id);
            if (parent == null) return new TaskResult(false, "Could not find parent");
            if (parent.Planet_Id != Planet_Id) return new TaskResult(false, "Parent category belongs to a different planet");
            if (parent.Id == Id) return new TaskResult(false, "Cannot be own parent");

            // Automatically determine position in this case
            if (Position < 0)
            {
                this.Position = (ushort)(await db.PlanetChannels.CountAsync(x => x.Parent_Id == Parent_Id));
            }
            else
            {
                // Ensure position is not already taken
                if (!await HasUniquePosition(db))
                    return new TaskResult(false, "The position is already taken.");
            }

            // Ensure this category does not contain itself
            var loop_parent = parent;

            while (loop_parent.Parent_Id != null)
            {
                if (loop_parent.Parent_Id == Id)
                {
                    return new TaskResult(false, "Cannot create parent loop.");
                }

                loop_parent = await db.PlanetCategoryChannels.FindAsync(loop_parent.Parent_Id);
            }
        }
        else
        {
            if (Position < 0)
            {
                this.Position = (ushort)(await db.PlanetChannels.CountAsync(x => x.Planet_Id == Planet_Id && x.Parent_Id == null));
            }
            else
            {
                // Ensure position is not already taken
                if (!await HasUniquePosition(db))
                    return new TaskResult(false, "The position is already taken.");
            }
        }

        return TaskResult.SuccessResult;
    }

    public async Task<bool> HasUniquePosition(ValourDB db) =>
        // Ensure position is not already taken
        !(await db.PlanetChannels.AnyAsync(x => x.Parent_Id == Parent_Id && // Same parent
                                                x.Position == Position && // Same position
                                                x.Id != Id)); // Not self

    /// <summary>
    /// Returns if the member has the given permission in this category
    /// </summary>
    public async Task<bool> HasPermission(PlanetMember member, Permission permission, ValourDB db)
    {
        Planet planet = await GetPlanetAsync(db);

        if (planet.Owner_Id == member.User_Id)
        {
            return true;
        }

        // If true, we ask the parent
        if (InheritsPerms)
        {
            return await (await GetParentAsync(db)).HasPermission(member, permission, db);
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

    public override async Task<TaskResult> CanGetAsync(PlanetMember member, ValourDB db)
    {
        if (!await HasPermission(member, CategoryPermissions.View, db))
            return new TaskResult(false, "Member lacks category permission " + CategoryPermissions.View.Name);

        return TaskResult.SuccessResult;
    }

    public override async Task<TaskResult> CanUpdateAsync(PlanetMember member, PlanetItem old, ValourDB db)
    {
        var canCreate = await CanCreateAsync(member, db);
        if (!canCreate.Success)
            return canCreate;

        // Update needs specific perm for this category, everything else is same as create
        if (!await HasPermission(member, CategoryPermissions.ManageCategory, db))
            return new TaskResult(false, "Member lacks category permission " + CategoryPermissions.ManageCategory.Name);

        return TaskResult.SuccessResult;
    }

    public override async Task<TaskResult> CanCreateAsync(PlanetMember member, ValourDB db)
    {
        var canGet = await CanGetAsync(member, db);
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

    public override async Task<TaskResult> CanDeleteAsync(PlanetMember member, ValourDB db)
    {
        await GetPlanetAsync(db);

        if (!await Planet.HasPermissionAsync(member, PlanetPermissions.ManageCategories, db))
            return new TaskResult(false, "Member lacks planet permission " + PlanetPermissions.ManageCategories.Name);

        if (!await HasPermission(member, CategoryPermissions.ManageCategory, db))
            return new TaskResult(false, "Member lacks category permission " + CategoryPermissions.ManageCategory.Name);


        if (await db.PlanetCategoryChannels.CountAsync(x => x.Planet_Id == Planet_Id) < 2)
            return new TaskResult(false, "Last category cannot be deleted");

        var childCategoryCount = await db.PlanetCategoryChannels.CountAsync(x => x.Parent_Id == Id);
        var childChannelCount = await db.PlanetChatChannels.CountAsync(x => x.Parent_Id == Id);

        if (childCategoryCount != 0 || childChannelCount != 0)
            return new TaskResult(false, "Category must be empty");

        return new TaskResult(true, "Success");
    }
}

