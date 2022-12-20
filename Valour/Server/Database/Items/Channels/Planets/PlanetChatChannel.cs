using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using Valour.Server.Cdn;
using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Messages;
using Valour.Server.Database.Items.Planets;
using Valour.Server.Database.Items.Planets.Members;
using Valour.Server.Database.Items.Users;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
using Valour.Server.Notifications;
using Valour.Server.Requests;
using Valour.Server.Services;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Authorization;
using Valour.Shared.Items.Channels.Planets;
using Valour.Shared.Items.Messages.Mentions;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

namespace Valour.Server.Database.Items.Channels.Planets;

[Table("planet_chat_channels")]
public class PlanetChatChannel : PlanetChannel, IPlanetItem, ISharedPlanetChatChannel
{
    #region IPlanetItem Implementation

    [JsonIgnore]
    public override string BaseRoute =>
        $"api/planet/{{planetId}}/{nameof(PlanetChatChannel)}";

    #endregion

    [Column("message_count")]
    public long MessageCount { get; set; }

    [NotMapped]
    public override PermissionsTargetType PermissionsTargetType => PermissionsTargetType.PlanetChatChannel;

    /// <summary>
    /// The regex used for name validation
    /// </summary>
    [JsonIgnore]
    public static readonly Regex nameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");

    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(PlanetMember member, ChatChannelPermission permission, PermissionsService permService) =>
        await permService.HasPermissionAsync(member, this, permission);
    
    public void Delete(PlanetChatChannelService service) =>
        service.Delete(this);
    
    
    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Membership)]
    [PlanetMembershipRequired, ChatChannelPermsRequired(ChatChannelPermissionsEnum.View)]
    public static IResult GetRoute(HttpContext ctx, long id) =>
        Results.Json(ctx.GetItem<PlanetChatChannel>(id));

    [ValourRoute(HttpVerbs.Post), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageChannels)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetChatChannel channel, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PermissionsService permService,
        ILogger<PlanetChatChannel> logger)
    {
        // Get resources
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
            var parent = await db.PlanetCategoryChannels.FindAsync(channel.ParentId);
            if (!await parent.HasPermissionAsync(member, CategoryPermissions.ManageCategory, permService))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }

        channel.Id = IdManager.Generate();

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

        hubService.NotifyPlanetItemChange(channel);

        return Results.Created(channel.GetUri(), channel);
    }

    [ValourRoute(HttpVerbs.Post, "/detailed"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageChannels)]
    public static async Task<IResult> PostRouteWithDetailsAsync(
        [FromBody] CreatePlanetChatChannelRequest request, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PermissionsService permService,
        ILogger<PlanetChatChannel> logger)
    {
        // Get resources
        var member = ctx.GetMember();

        var channel = request.Channel;

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
            var parent = await db.PlanetCategoryChannels.FindAsync(channel.ParentId);
            if (!await parent.HasPermissionAsync(member, CategoryPermissions.ManageCategory, permService))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }

        channel.Id = IdManager.Generate();
        
        List<PermissionsNode> nodes = new();

        // Create nodes
        foreach (var nodeReq in request.Nodes)
        {
            var node = nodeReq;
            node.TargetId = channel.Id;
            node.PlanetId = planetId;

            var role = await FindAsync<PlanetRole>(node.RoleId, db);
            if (role.GetAuthority() > await member.GetAuthorityAsync(db))
                return ValourResult.Forbid("A permission node's role has higher authority than you.");

            node.Id = IdManager.Generate();

            nodes.Add(node);
        }

        var tran = await db.Database.BeginTransactionAsync();

        try
        {
            await db.PlanetChatChannels.AddAsync(channel);
            await db.SaveChangesAsync();

            await db.PermissionsNodes.AddRangeAsync(nodes);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            await tran.RollbackAsync();
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        await tran.CommitAsync();

        hubService.NotifyPlanetItemChange(channel);

        return Results.Created(channel.GetUri(), channel);
    }

    [ValourRoute(HttpVerbs.Put), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageChannels)]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.ManageChannel)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetChatChannel channel, 
        long id, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        ILogger<PlanetChatChannel> logger)
    {
        // Get resources
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
            db.Entry(old).State = EntityState.Detached;
            db.PlanetChatChannels.Update(channel);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemChange(channel);

        // Response
        return Results.Ok(channel);
    }

    [ValourRoute(HttpVerbs.Delete), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.PlanetManagement)]
    [PlanetMembershipRequired(permissions: PlanetPermissionsEnum.ManageChannels)]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.ManageChannel)]
    public static async Task<IResult> DeleteRouteAsync(
        long id, 
        long planetId, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PlanetChatChannelService channelService,
        ILogger<PlanetChatChannel> logger)
    {
        var channel = ctx.GetItem<PlanetChatChannel>(id);

        // Always use transaction for multi-step DB operations
        await using var transaction = await db.Database.BeginTransactionAsync();

        try
        {
            channel.Delete(channelService);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            await transaction.RollbackAsync();
            return Results.Problem(e.Message);
        }

        hubService.NotifyPlanetItemDelete(channel);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/checkperm/{memberId}/{value}"), TokenRequired]
    [PlanetMembershipRequired]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.View)]
    public static async Task<IResult> HasPermissionRouteAsync(
        long id, 
        long memberId, 
        long value,
        PermissionsService permService,
        HttpContext ctx,
        ValourDB db)
    {
        var channel = ctx.GetItem<PlanetChatChannel>(id);

        var targetMember = await FindAsync<PlanetMember>(memberId, db);
        if (targetMember is null)
            return ValourResult.NotFound<PlanetMember>();

        var hasPerm = await channel.HasPermissionAsync(targetMember, new ChatChannelPermission(value, "", ""), permService);

        return Results.Json(hasPerm);
    }

    // Message routes

    [ValourRoute(HttpVerbs.Get, "/{id}/message/{messageId}"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]
    [PlanetMembershipRequired]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.ViewMessages)]
    public static async Task<IResult> GetMessageRouteAsync(
        long id, 
        long messageId, 
        HttpContext ctx,
        ValourDB db)
    {
        var message = await db.PlanetMessages.FindAsync(messageId);
        if (message is null)
            message = PlanetMessageWorker.GetStagedMessage(messageId);

        if (message is null)
            return ValourResult.NotFound("Message not found.");

        return Results.Json(message);
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/messages"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]
    [PlanetMembershipRequired]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.ViewMessages)]
    public static async Task<IResult> GetMessagesRouteAsync(
        long id,
        ValourDB db, 
        [FromQuery] long index = long.MaxValue, 
        [FromQuery] int count = 10)
    {
        if (count > 64)
            return Results.BadRequest("Maximum count is 64.");
        
        List<PlanetMessage> staged = PlanetMessageWorker.GetStagedMessages(id);
        
        if (count > 0)
        {
            var messages = await db.PlanetMessages.Where(x => x.ChannelId == id && x.Id < index)
                                                  .OrderByDescending(x => x.TimeSent)
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

    public static Regex _attachmentRejectRegex = new Regex("(^|.)(<|>|\"|'|\\s)(.|$)");

    [ValourRoute(HttpVerbs.Post, "/{id}/messages"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]
    [PlanetMembershipRequired]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.ViewMessages,
                              ChatChannelPermissionsEnum.PostMessages)]
    public static async Task<IResult> PostMessageRouteAsync(
        [FromBody] PlanetMessage message,
        long id,
        HttpContext ctx, 
        HttpClient client, 
        ValourDB valourDb, 
        CdnDb db,
        UserOnlineService onlineService)
    {
        var member = ctx.GetMember();
        var channel = ctx.GetItem<PlanetChatChannel>(id);

        if (message is null)
            return Results.BadRequest("Include message in body.");

        if (string.IsNullOrEmpty(message.Content) &&
            string.IsNullOrEmpty(message.EmbedData) &&
            string.IsNullOrEmpty(message.AttachmentsData))
            return Results.BadRequest("Message content cannot be null");

        if (message.Fingerprint is null)
            return Results.BadRequest("Please include a Fingerprint.");

        if (message.AuthorUserId != member.UserId)
            return Results.BadRequest("UserId must match sender.");

        if (message.AuthorMemberId != member.Id)
            return Results.BadRequest("MemberId must match sender.");

        if (message.Content != null && message.Content.Length > 2048)
            return Results.BadRequest("Content must be under 2048 chars");


        if (message.EmbedData != null && message.EmbedData.Length > 65535)
            return Results.BadRequest("EmbedData must be under 65535 chars");

        if (message.Content is null)
            message.Content = "";

        // Handle URL content
        if (!string.IsNullOrWhiteSpace(message.Content))
            message.Content = await ProxyHandler.HandleUrls(message.Content, client, db);

        message.Id = IdManager.Generate();

        // Handle attachments
        if (message.AttachmentsData is not null)
        {
            var attachments = JsonSerializer.Deserialize<List<MessageAttachment>>(message.AttachmentsData);
            if (attachments is not null)
            {
                foreach (var at in attachments)
                {
                    if (!at.Location.StartsWith("https://cdn.valour.gg") && 
                        !at.Location.StartsWith("https://media.tenor.com"))
                    {
                        return Results.BadRequest("Attachments must be from https://cdn.valour.gg...");
                    }
                    if (_attachmentRejectRegex.IsMatch(at.Location))
                    {
                        return Results.BadRequest("Attachment location contains invalid characters");
                    }
                }
            }
        }

        if (message.MentionsData is not null)
        {
            var mentions = JsonSerializer.Deserialize<List<Mention>>(message.MentionsData);
            if (mentions is not null)
            {
                foreach (var mention in mentions)
                {
                    if (mention.Type == MentionType.Member)
                    {
                        var targetMember = await Item.FindAsync<PlanetMember>(mention.TargetId, valourDb);
                        var sendingUser = await Item.FindAsync<User>(member.UserId, valourDb);
                        var planet = await Item.FindAsync<Planet>(message.PlanetId, valourDb);

                        var content = message.Content.Replace($"«@m-{mention.TargetId}»", $"@{targetMember.Nickname}");

                        await NotificationManager.SendNotificationAsync(valourDb, targetMember.UserId, sendingUser.PfpUrl, member.Nickname + " in " + planet.Name, content);
                    }
                }
            }
        }

        PlanetMessageWorker.AddToQueue(message);

        StatWorker.IncreaseMessageCount();

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Delete, "/{id}/messages/{message_id}"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]
    [PlanetMembershipRequired]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.ViewMessages)]
    public static async Task<IResult> DeleteMessageRouteAsync(
        long id, 
        long message_id, 
        HttpContext ctx,
        ValourDB db,
        CoreHubService hubService,
        PermissionsService permService,
        ILogger<PlanetChatChannel> logger)
    {
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

        if (member.Id != message.AuthorMemberId)
        {
            if (!await channel.HasPermissionAsync(member, ChatChannelPermissions.ManageMessages, permService))
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

        hubService.NotifyMessageDeletion(message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "/{id}/typing"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages)]
    [PlanetMembershipRequired]
    [ChatChannelPermsRequired(ChatChannelPermissionsEnum.ViewMessages,
                              ChatChannelPermissionsEnum.PostMessages)]
    public static async Task<IResult> PostTypingAsync(
        long id, 
        HttpContext ctx,
        CurrentlyTypingService typingService)
    {
        var member = ctx.GetMember();
        if (member is null)
            return ValourResult.Forbid("Member not found");
        
        typingService.AddCurrentlyTyping(id, member.UserId);
        
        return Results.Ok();
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

        // Auto determine position
        if (channel.Position < 0)
        {
            channel.Position = (ushort)(await db.PlanetChannels.CountAsync(x => x.ParentId == channel.ParentId));
        }
        else
        {
            if (!await HasUniquePosition(db, channel))
                return new TaskResult(false, "The position is already taken.");
        }

        return new TaskResult(true, "Valid");
    }

    #endregion
}

