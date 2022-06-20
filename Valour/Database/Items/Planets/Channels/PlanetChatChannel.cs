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
using Valour.Database.Attributes;
using System.Web.Mvc;
using Valour.Database.Extensions;
using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Http;
using Microsoft.Extensions.Logging;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Database.Items.Planets.Channels;

[Table("PlanetChatChannels")]
public class PlanetChatChannel : PlanetChannel, ISharedPlanetChatChannel
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
    /// Returns all members who can see this channel
    /// </summary>
    public async Task<List<PlanetMember>> GetChannelMembersAsync(ValourDB db)
    {
        List<PlanetMember> members = new List<PlanetMember>();

        var planetMembers = db.PlanetMembers.Include(x => x.RoleMembership).Where(x => x.Planet_Id == Planet_Id);

        foreach (var member in planetMembers)
        {
            if (await HasPermissionAsync(member, ChatChannelPermissions.View, db))
            {
                members.Add(member);
            }
        }

        return members;
    }

    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDB]
    [PlanetMembershipRequired, ChatChannelPermsRequired(ChatChannelPermissionsEnum.View)]
    public static IResult GetRoute(HttpContext ctx, ulong id) =>
        Results.Json(ctx.GetItem<PlanetChatChannel>(id));

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDB]
    [PlanetMembershipRequired, PlanetPermsRequired(PlanetPermissionsEnum.ManageChannels)]
    public static async Task<IResult> PostRouteAsync(HttpContext ctx, ulong planet_id, [FromBody] PlanetChatChannel channel,
        ILogger<PlanetChatChannel> logger)
    {
        // Get resources
        var db = ctx.GetDB();
        var member = ctx.GetMember();

        if (channel.Planet_Id != planet_id)
            return Results.BadRequest("Planet_Id mismatch.");

        var nameValid = ValidateName(channel.Name);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        var descValid = ValidateDescription(channel.Description);
        if (!descValid.Success)
            return Results.BadRequest(descValid.Message);

        var positionValid = await ValidateParentAndPosition(db, channel);
        if (!positionValid.Success)
            return Results.BadRequest(positionValid.Message);

        // Ensure user has permission for parent category management
        if (channel.Parent_Id is not null)
        {
            var parent_cat = await db.PlanetCategoryChannels.FindAsync(channel.Parent_Id);
            if (!await parent_cat.HasPermissionAsync(member, CategoryPermissions.ManageCategory, db))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }

        try
        {
            await db.PlanetChatChannels.AddAsync(channel);
            await db.SaveChangesAsync();
        }
        catch(System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(channel);

        return Results.Created(channel.GetUri(), channel);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    [PlanetPermsRequired(PlanetPermissionsEnum.ManageChannels),
     ChatChannelPermsRequired(ChatChannelPermissionsEnum.View,
                              ChatChannelPermissionsEnum.ManageChannel)]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, ulong id, [FromBody] PlanetChatChannel channel,
        ILogger<PlanetChatChannel> logger)
    {
        // Get resources
        var db = ctx.GetDB();
        var old = ctx.GetItem<PlanetChatChannel>(id);

        // Validation
        if (old.Id != channel.Id)
            return Results.BadRequest("Cannot change Id.");
        if (old.Planet_Id != channel.Planet_Id)
            return Results.BadRequest("Cannot change Planet_Id.");

        var nameValid = ValidateName(channel.Name);
        if (!nameValid.Success)
            return Results.BadRequest(nameValid.Message);

        var descValid = ValidateDescription(channel.Description);
        if (!descValid.Success)
            return Results.BadRequest(descValid.Message);

        var positionValid = await ValidateParentAndPosition(db, channel);
        if (!positionValid.Success)
            return Results.BadRequest(positionValid.Message);

        // Update
        try
        {
            db.PlanetChatChannels.Update(channel);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(channel);

        // Response
        return Results.Ok(channel);
    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDB]
    [PlanetMembershipRequired]
    [PlanetPermsRequired(PlanetPermissionsEnum.ManageChannels),
     ChatChannelPermsRequired(ChatChannelPermissionsEnum.View,
                              ChatChannelPermissionsEnum.ManageChannel)]
    public static async Task<IResult> DeleteRouteAsync(HttpContext ctx, ulong id, ulong planet_id,
        ILogger<PlanetChatChannel> logger)
    {
        var db = ctx.GetDB();
        var channel = ctx.GetItem<PlanetChatChannel>(id);

        // Always use transaction for multi-step DB operations
        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            // Remove permission nodes
            await db.PermissionsNodes.BulkDeleteAsync(
                db.PermissionsNodes.Where(x => x.Target_Id == id)
            );

            // Remove messages
            await db.PlanetMessages.BulkDeleteAsync(
                db.PlanetMessages.Where(x => x.Channel_Id == id)
            );

            // Remove channel
            db.PlanetChatChannels.Remove(channel);

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

        PlanetHub.NotifyPlanetItemDelete(channel);

        return Results.NoContent();
    }

    #endregion

    #region Validation

    /// <summary>
    /// Validates that a given name is allowable
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

    public static async Task<TaskResult> ValidateParentAndPosition(ValourDB db, PlanetChatChannel channel)
    {
        // Logic to check if parent is legitimate
        if (channel.Parent_Id is not null)
        {
            var parent = await db.PlanetCategoryChannels.FirstOrDefaultAsync
                (x => x.Id == channel.Parent_Id
                && x.Planet_Id == channel.Planet_Id); // This ensures the result has the same planet id

            if (parent is null)
                return new TaskResult(false, "Parent ID is not valid");
        }

        if (!await HasUniquePosition(db, channel))
            return new TaskResult(false, "The position is already taken.");

        return new TaskResult(true, "Valid");
    }

    #endregion
}

