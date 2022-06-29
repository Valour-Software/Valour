using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Messages;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Http;
using Valour.Shared.Items;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Planets.Channels;
using Valour.Shared.MPS;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Planets.Channels;

[Table("PlanetChatChannels")]
public class PlanetChatChannel : PlanetChannel, ISharedPlanetChatChannel
{
    public ulong MessageCount { get; set; }

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

        if (Planet.OwnerId == member.UserId)
            return true;

        // If true, we just ask the category
        if (InheritsPerms)
        {
            return await (await GetParentAsync(db)).HasPermissionAsync(member, permission, db);
        }


        // Load permission data
        await db.Entry(member).Collection(x => x.RoleMembership)
                              .Query()
                              .Where(x => x.PlanetId == Planet.Id)
                              .Include(x => x.Role)
                              .ThenInclude(x => x.PermissionNodes.Where(x => x.TargetId == Id))
                              .OrderBy(x => x.Role.Position)
                              .LoadAsync();

        // Starting from the most important role, we stop once we hit the first clear "TRUE/FALSE".
        // If we get an undecided, we continue to the next role down
        foreach (var roleMembership in member.RoleMembership)
        {
            var role = roleMembership.Role;
            // For some reason, we need to make sure we get the node that has the same targetId as this channel
            // When loading I suppose it grabs all the nodes even if the target is not the same?
            PermissionsNode node = role.PermissionNodes.FirstOrDefault(x => x.TargetId == Id && x.TargetType == PermissionsTarget.PlanetChatChannel);

            // If we are dealing with the default role and the behavior is undefined, we fall back to the default permissions
            if (node == null)
            {
                if (role.Id == Planet.DefaultRoleId)
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

    public async Task DeleteAsync(ValourDB db)
    {
        // Remove permission nodes
        await db.PermissionsNodes.BulkDeleteAsync(
            db.PermissionsNodes.Where(x => x.TargetId == Id)
        );

        // Remove messages
        await db.PlanetMessages.BulkDeleteAsync(
            db.PlanetMessages.Where(x => x.ChannelId == Id)
        );

        // Remove channel
        db.PlanetChatChannels.Remove(this);
    }

    /// <summary>
    /// Returns all members who can see this channel
    /// </summary>
    public async Task<List<PlanetMember>> GetChannelMembersAsync(ValourDB db)
    {
        List<PlanetMember> members = new List<PlanetMember>();

        var planetMembers = db.PlanetMembers.Include(x => x.RoleMembership).Where(x => x.PlanetId == PlanetId);

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

    [ValourRoute(HttpVerbs.Get), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired, ChatChannelPermsRequired(ChatChannelPermissionsEnum.View)]
    public static IResult GetRoute(HttpContext ctx, ulong id) =>
        Results.Json(ctx.GetItem<PlanetChatChannel>(id));

    [ValourRoute(HttpVerbs.Post), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired, PlanetPermsRequired(PlanetPermissionsEnum.ManageChannels)]
    public static async Task<IResult> PostRouteAsync(HttpContext ctx, ulong planetId, [FromBody] PlanetChatChannel channel,
        ILogger<PlanetChatChannel> logger)
    {
        // Get resources
        var db = ctx.GetDb();
        var member = ctx.GetMember();

        if (channel.PlanetId != planetId)
            return Results.BadRequest("PlanetId mismatch.");

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
        if (channel.ParentId is not null)
        {
            var parent_cat = await db.PlanetCategoryChannels.FindAsync(channel.ParentId);
            if (!await parent_cat.HasPermissionAsync(member, CategoryPermissions.ManageCategory, db))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }

        try
        {
            await db.PlanetChatChannels.AddAsync(channel);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        PlanetHub.NotifyPlanetItemChange(channel);

        return Results.Created(channel.GetUri(), channel);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired]
    [PlanetPermsRequired(PlanetPermissionsEnum.ManageChannels),
     ChatChannelPermsRequired(ChatChannelPermissionsEnum.ManageChannel)]
    public static async Task<IResult> PutRouteAsync(HttpContext ctx, ulong id, [FromBody] PlanetChatChannel channel,
        ILogger<PlanetChatChannel> logger)
    {
        // Get resources
        var db = ctx.GetDb();
        var old = ctx.GetItem<PlanetChatChannel>(id);

        // Validation
        if (old.Id != channel.Id)
            return Results.BadRequest("Cannot change Id.");
        if (old.PlanetId != channel.PlanetId)
            return Results.BadRequest("Cannot change PlanetId.");

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

    [ValourRoute(HttpVerbs.Delete), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired]
    [PlanetPermsRequired(PlanetPermissionsEnum.ManageChannels),
     ChatChannelPermsRequired(ChatChannelPermissionsEnum.ManageChannel)]
    public static async Task<IResult> DeleteRouteAsync(HttpContext ctx, ulong id, ulong planetId,
        ILogger<PlanetChatChannel> logger)
    {
        var db = ctx.GetDb();
        var channel = ctx.GetItem<PlanetChatChannel>(id);

        // Always use transaction for multi-step DB operations
        using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            await channel.DeleteAsync(db);
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

    [ValourRoute(HttpVerbs.Get, "/{id}/checkperm/{memberId}/{value}"), TokenRequired, InjectDb]
    [PlanetMembershipRequired]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.View)]
    public static async Task<IResult> HasPermissionRouteAsync(HttpContext ctx, ulong id, ulong memberId, ulong value)
    {
        var db = ctx.GetDb();
        var channel = ctx.GetItem<PlanetChatChannel>(id);

        var targetMember = await FindAsync<PlanetMember>(memberId, db);
        if (targetMember is null)
            return ValourResult.NotFound<PlanetMember>();

        var hasPerm = await channel.HasPermissionAsync(targetMember, new Permission(value, "", ""), db);

        return Results.Json(hasPerm);
    }

    // Message routes

    [ValourRoute(HttpVerbs.Get, "/{id}/messages"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]
    [PlanetMembershipRequired]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.ViewMessages)]
    public static async Task<IResult> GetMessagesRouteAsync(HttpContext ctx, ulong id, ulong index = ulong.MaxValue, int count = 10)
    {
        if (count > 64)
            return Results.BadRequest("Maximum count is 64.");

        var channel = ctx.GetItem<PlanetChatChannel>(id);
        var db = ctx.GetDb();

        List<PlanetMessage> staged = PlanetMessageWorker.GetStagedMessages(id, count);

        count = count - staged.Count;

        if (count > 0)
        {
            var messages = await db.PlanetMessages.Where(x => x.ChannelId == id && x.MessageIndex <= index)
                                                  .OrderByDescending(x => x.MessageIndex)
                                                  .Take(count)
                                                  .Reverse()
                                                  .ToListAsync();

            messages.AddRange(staged);

            return Results.Json(messages);
        }
        else
        {
            return Results.Json(staged);
        }
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/messages"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]
    [PlanetMembershipRequired]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.ViewMessages,
                              ChatChannelPermissionsEnum.PostMessages)]
    public static async Task<IResult> PostMessageRouteAsync(HttpContext ctx, [FromBody] PlanetMessage message)
    {
        var member = ctx.GetMember();

        if (message is null)
            return Results.BadRequest("Include message in body.");

        if (string.IsNullOrEmpty(message.Content) && string.IsNullOrEmpty(message.EmbedData))
            return Results.BadRequest("Message content cannot be null");

        if (message.Fingerprint is null)
            return Results.BadRequest("Please include a Fingerprint.");

        if (message.AuthorId != member.UserId)
            return Results.BadRequest("UserId must match sender.");

        if (message.MemberId != member.Id)
            return Results.BadRequest("MemberId must match sender.");

        if (message.Content != null && message.Content.Length > 2048)
            return Results.BadRequest("Content must be under 2048 chars");


        if (message.EmbedData != null && message.EmbedData.Length > 65535)
            return Results.BadRequest("EmbedData must be under 65535 chars");

        // Handle URL content
        message.Content = await MPSUtils.HandleUrls(message.Content);
        message.Id = IdManager.Generate();

        PlanetMessageWorker.AddToQueue(message);

        StatWorker.IncreaseMessageCount();

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/messages/{message_id}"), TokenRequired, InjectDb]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]
    [PlanetMembershipRequired]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.ViewMessages)]
    public static async Task<IResult> DeleteMessageRouteAsync(HttpContext ctx, ulong id, ulong message_id,
        ILogger<PlanetChatChannel> logger)
    {
        var db = ctx.GetDb();
        var member = ctx.GetMember();
        var channel = ctx.GetItem<PlanetChatChannel>(id);

        var message = await FindAsync<PlanetMessage>(message_id, db);

        var inDb = true;

        if (message is null)
        {
            inDb = false;

            // Try to find in staged
            message = PlanetMessageWorker.GetStagedMessage(message_id);
            if (message is null)
                return ValourResult.NotFound<PlanetMessage>();
        }

        if (message.ChannelId != id)
            return ValourResult.NotFound<PlanetMessage>();

        if (member.Id != message.MemberId)
        {
            if (!await channel.HasPermissionAsync(member, ChatChannelPermissions.ManageMessages, db))
                return ValourResult.LacksPermission(ChatChannelPermissions.ManageMessages);
        }

        // Remove from staging
        PlanetMessageWorker.RemoveFromQueue(message);

        // If in db, remove from db
        if (inDb)
        {
            try
            {
                db.PlanetMessages.Remove(message);
                await db.SaveChangesAsync();
            }
            catch (System.Exception e)
            {
                logger.LogError(e.Message);
                return Results.Problem(e.Message);
            }
        }

        PlanetHub.NotifyMessageDeletion(message);

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
        if (channel.ParentId is not null)
        {
            var parent = await db.PlanetCategoryChannels.FirstOrDefaultAsync
                (x => x.Id == channel.ParentId
                && x.PlanetId == channel.PlanetId); // This ensures the result has the same planet id

            if (parent is null)
                return new TaskResult(false, "Parent ID is not valid");
        }

        if (!await HasUniquePosition(db, channel))
            return new TaskResult(false, "The position is already taken.");

        return new TaskResult(true, "Valid");
    }

    #endregion
}

