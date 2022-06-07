using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Valour.Shared.Items.Authorization;
using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;
using Valour.Shared;
using Valour.Database.Items.Authorization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Planets.Channels;
using Valour.Shared.Items;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Channels;

[Table("PlanetChatChannels")]
public class PlanetChatChannel : PlanetChannel, ISharedPlanetChatChannel, INodeSpecific
{
    public ulong MessageCount { get; set; }

    /// <summary>
    /// The type of this item
    /// </summary>
    [NotMapped]
    public override ItemType ItemType => ItemType.PlanetChatChannel;

    /// <summary>
    /// The regex used for name validation
    /// </summary>
    public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public override async Task<bool> HasPermissionAsync(PlanetMember member, Permission permission, ValourDB db)
    {
        await GetPlanetAsync(db);

        if (Planet.Owner_Id == member.User_Id)
            return true;

        // If true, we just ask the category
        if (InheritsPerms)
        {
            return await (await GetParentAsync(db)).HasPermissionAsync(member, permission, db);
        }


        // Load permission data
        await db.Entry(member).Collection(x => x.RoleMembership)
                              .Query()
                              .Where(x => x.Planet_Id == Planet.Id)
                              .Include(x => x.Role)
                              .ThenInclude(x => x.PermissionNodes.Where(x => x.Target_Id == Id))
                              .OrderBy(x => x.Role.Position)
                              .LoadAsync();

        // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
        // If we get an undecided, we continue to the next role down
        foreach (var roleMembership in member.RoleMembership)
        {
            var role = roleMembership.Role;
            // For some reason, we need to make sure we get the node that has the same target_id as this channel
            // When loading I suppose it grabs all the nodes even if the target is not the same?
            PermissionsNode node = role.PermissionNodes.FirstOrDefault(x => x.Target_Id == Id && x.Target_Type == ItemType);

            // If we are dealing with the default role and the behavior is undefined, we fall back to the default permissions
            if (node == null)
            {
                if (role.Id == Planet.Default_Role_Id)
                {
                    return Permission.HasPermission(ChatChannelPermissions.Default, permission);
                }

                continue;
            }

            PermissionState state = node.GetPermissionState(permission);

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
    /// Validates that a given name is allowable for a channel
    /// </summary>
    public static TaskResult ValidateName(string name)
    {
        if (name.Length > 32)
            return new TaskResult(false, "Channel names must be 32 characters or less.");

        if (!nameRegex.IsMatch(name))
            return new TaskResult(false, "Channel names may only include letters, numbers, dashes, and underscores.");

        return new TaskResult(true, "The given name is valid.");
    }

    /// <summary>
    /// Returns all members who can see this channel
    /// </summary>
    public async Task<List<PlanetMember>> GetChannelMembersAsync(ValourDB db = null)
    {
        List<PlanetMember> members = new List<PlanetMember>();

        bool createdb = false;
        if (db == null) { db = new ValourDB(ValourDB.DBOptions); createdb = true; }

        var planetMembers = db.PlanetMembers.Include(x => x.RoleMembership).Where(x => x.Planet_Id == Planet_Id);

        foreach (var member in planetMembers)
        {
            if (await HasPermissionAsync(member, ChatChannelPermissions.View, db))
            {
                members.Add(member);
            }
        }

        if (createdb) { await db.DisposeAsync(); }

        return members;
    }

    #region API Methods

    public override async Task<TaskResult> CanGetAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        if (member is null)
            return new TaskResult(false, "User is not a member of the target planet");

        if (!await HasPermissionAsync(member, ChatChannelPermissions.View, db))
            return new TaskResult(false, "Member lacks channel permission " + ChatChannelPermissions.View.Name);

        return new TaskResult(true, "Success");
    }

    public override async Task<TaskResult> CanDeleteAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        // Needs to be able to GET in order to do anything else
        var canGet = await CanGetAsync(token, member, db);
        if (!canGet.Success)
            return canGet;

        await GetPlanetAsync(db);

        if (!await Planet.HasPermissionAsync(member, PlanetPermissions.ManageChannels, db))
            return new TaskResult(false, "Member lacks planet permission " + PlanetPermissions.ManageChannels.Name);

        if (!await HasPermissionAsync(member, ChatChannelPermissions.ManageChannel, db))
            return new TaskResult(false, "Member lacks channel permission " + ChatChannelPermissions.ManageChannel.Name);


        return new TaskResult(true, "Success");
    }

    public override async Task<TaskResult> CanUpdateAsync(AuthToken token, PlanetMember member, PlanetItem old, ValourDB db)
    {
        // Similar to Create but also needs specific channel perms
        var canCreate = await CanCreateAsync(token, member, db);
        if (!canCreate.Success)
            return canCreate;

        if (!await HasPermissionAsync(member, ChatChannelPermissions.ManageChannel, db))
            return new TaskResult(false, "Member lacks channel permission " + ChatChannelPermissions.ManageChannel.Name);

        return new TaskResult(true, "Success");
    }

    public override async Task<TaskResult> CanCreateAsync(AuthToken token, PlanetMember member, ValourDB db)
    {
        await GetPlanetAsync(db);

        if (!await Planet.HasPermissionAsync(member, PlanetPermissions.ManageChannels, db))
            return new TaskResult(false, "Member lacks planet permission " + PlanetPermissions.ManageChannels.Name);

        var valid = await ValidateAsync(db);
        if (!valid.Success)
            return valid;

        return new TaskResult(true, "Success");
    }

    public override async Task DeleteAsync(ValourDB db)
    {
        // Remove permission nodes
        db.PermissionsNodes.RemoveRange(
            db.PermissionsNodes.Where(x => x.Target_Id == Id)
        );

        // Remove messages
        db.PlanetMessages.RemoveRange(
            db.PlanetMessages.Where(x => x.Channel_Id == Id)
        );

        // Remove channel
        db.PlanetChatChannels.Remove(
            await db.PlanetChatChannels.FirstOrDefaultAsync(x => x.Id == Id)
        );

        // Save changes
        await db.SaveChangesAsync();

        // Notify channel deletion
        PlanetHub.NotifyPlanetItemChange(this);
    }

    public async Task<TaskResult> ValidateAsync(ValourDB db)
    {
        var nameValid = ValidateName(Name);
        if (!nameValid.Success)
            return nameValid;

        if (Description.Length > 128)
            return new TaskResult(false, "Description must be at or under 128 characters");

        // Logic to check if parent is legitimate
        if (Parent_Id is not null)
        {
            var parent = await db.PlanetCategoryChannels.FirstOrDefaultAsync
                (x => x.Id == Parent_Id
                && x.Planet_Id == Planet_Id); // This ensures the result has the same planet id

            if (parent is null)
                return new TaskResult(false, "Parent ID is not valid");
        }

        return new TaskResult(true, "Valid");
    }

    #endregion
}

