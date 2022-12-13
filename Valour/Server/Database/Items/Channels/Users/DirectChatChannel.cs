using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using StackExchange.Redis;
using Valour.Server.API;
using Valour.Server.Cdn;
using Valour.Server.Config;
using Valour.Server.Database.Items.Messages;
using Valour.Server.Database.Items.Users;
using Valour.Server.EndpointFilters;
using Valour.Server.EndpointFilters.Attributes;
using Valour.Server.Hubs;
using Valour.Server.Notifications;
using Valour.Server.Redis;
using Valour.Server.Services;
using Valour.Server.Workers;
using Valour.Shared.Authorization;
using Valour.Shared.Items.Channels.Users;
using Valour.Shared.Items.Messages.Mentions;

namespace Valour.Server.Database.Items.Channels.Users;

/*  Valour - A free and secure chat client
 *  Copyright (C) 2021 Vooper Media LLC
 *  This program is subject to the GNU Affero General Public license
 *  A copy of the license should be included - if not, see <http://www.gnu.org/licenses/>
 */

[Table("direct_chat_channels")]
public class DirectChatChannel : Channel, ISharedDirectChatChannel
{
    /// <summary>
    /// One of the users in the DM channel
    /// </summary>
    [ForeignKey("UserOneId")]
    public virtual User UserOne { get; set; }

    /// <summary>
    /// One of the users in the DM channel
    /// </summary>
    [ForeignKey("UserTwoId")]
    public virtual User UserTwo { get; set; }

    /// <summary>
    /// The id of one of the users in the DM channel
    /// </summary>
    [Column("user_one_id")]
    public long UserOneId { get; set; }

    /// <summary>
    /// The id of one of the users in the DM channel
    /// </summary>
    [Column("user_two_id")]
    public long UserTwoId { get; set; }

    [Column("message_count")]
    public long MessageCount { get; set; }

    /// <summary>
    /// Returns the direct chat channel with the given id
    /// </summary>
    public static async Task<DirectChatChannel> FindAsync(long id, ValourDB db)
        => await db.DirectChatChannels.FindAsync(id);

    /// <summary>
    /// Returns the direct chat channel between the two given user ids
    /// </summary>
    public static async Task<DirectChatChannel> FindAsync(long userOneId, long userTwoId, ValourDB db)
    {
        // Doesn't matter which user is which
        return await db.DirectChatChannels.FirstOrDefaultAsync(x =>
            (x.UserOneId == userOneId && x.UserTwoId == userTwoId) ||
            (x.UserOneId == userTwoId && x.UserTwoId == userOneId)
        );
    }


    #region Routes

    [ValourRoute(HttpVerbs.Get), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetRoute(
        long id, 
        ValourDB db)
    {
        // id is the id of the channel
        var channel = await FindAsync(id, db);

        return channel is null ? ValourResult.NotFound<DirectChatChannel>() : 
            Results.Json(channel);
    }

    [ValourRoute(HttpVerbs.Get, "/byuser/{id}", $"api/{nameof(DirectChatChannel)}"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetViaTargetRoute(
        long id, 
        HttpContext ctx, 
        ValourDB db)
    {
        // id is the id of the target user, not the channel!
        var token = ctx.GetToken();

        // Ensure target user exists
        if (!await db.Users.AnyAsync(x => x.Id == id))
            return ValourResult.NotFound("Target user not found");

        var channel = await FindAsync(token.UserId, id, db);
        
        if (channel is not null) return Results.Json(channel);
        
        // If there is no dm channel yet, we create it
        // TODO: Prevent if one of the users is blocking the other
        channel = new()
        {
            Id = IdManager.Generate(),
            UserOneId = token.UserId,
            UserTwoId = id,
            TimeLastActive = DateTime.UtcNow,
            MessageCount = 0
        };

        await db.AddAsync(channel);
        await db.SaveChangesAsync();

        return Results.Json(channel);
    }

    // Message routes

    [ValourRoute(HttpVerbs.Get, "/{id}/message/{messageId}"), TokenRequired,]
    [UserPermissionsRequired(UserPermissionsEnum.Messages, UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetMessagesRouteAsync(
        long id, 
        long messageId, 
        HttpContext ctx,
        ValourDB db)
    {
        var token = ctx.GetToken();

        var channel = await FindAsync(id, db);

        if (channel is null)
            return ValourResult.NotFound("Direct chat channel not found");

        if ((channel.UserOneId != token.UserId) &&
            (channel.UserTwoId != token.UserId))
            return ValourResult.Forbid("You do not have access to this direct chat channel");

        return Results.Json(await db.DirectMessages.FindAsync(messageId));
    }

    [ValourRoute(HttpVerbs.Get, "/{id}/messages"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages, UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> GetMessagesRouteAsync(
        long id, 
        HttpContext ctx,
        ValourDB db,
        [FromQuery] long index = long.MaxValue, 
        [FromQuery] int count = 10)
    {
        if (count > 64)
            return Results.BadRequest("Maximum count is 64.");
        
        var token = ctx.GetToken();
        var channel = await FindAsync(id, db);

        if (channel is null)
            return ValourResult.NotFound("Direct chat channel not found");

        if ((channel.UserOneId != token.UserId) &&
            (channel.UserTwoId != token.UserId))
            return ValourResult.Forbid("You do not have access to this direct chat channel");


        var messages = await db.DirectMessages.Where(x => x.ChannelId == id && x.Id <= index)
                                              .OrderByDescending(x => x.Id)
                                              .Take(count)
                                              .Reverse()
                                              .ToListAsync();
        var state = await db.UserChannelStates.FirstOrDefaultAsync(x => x.UserId == token.UserId && x.ChannelId == channel.Id);
        if (state is null)
        {
            db.UserChannelStates.Add(new UserChannelState()
            {
                UserId = token.UserId,
                ChannelId = channel.Id,
                LastViewedState = channel.State
            });

            await db.SaveChangesAsync();
        }

        else
        {

            state.LastViewedState = channel.State;

            await db.SaveChangesAsync();
        }

        return Results.Json(messages);
    }

    public static Regex _attachmentRejectRegex = new Regex("(^|.)(<|>|\"|'|\\s)(.|$)");

    [ValourRoute(HttpVerbs.Post, "/{id}/messages"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages, UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> PostMessageRouteAsync(
        [FromBody] DirectMessage message, 
        HttpContext ctx, 
        HttpClient client, 
        ValourDB valourDb, 
        CdnDb db,
        CoreHubService hubService,
        ChannelStateService channelService,
        IConnectionMultiplexer redis)
    {
        var token = ctx.GetToken();

        if (message is null)
            return Results.BadRequest("Include message in body.");

        if (string.IsNullOrEmpty(message.Content) &&
            string.IsNullOrEmpty(message.EmbedData) &&
            string.IsNullOrEmpty(message.AttachmentsData))
            return Results.BadRequest("Message content cannot be null");

        if (message.Fingerprint is null)
            return Results.BadRequest("Please include a Fingerprint.");

        if (message.AuthorUserId != token.UserId)
            return Results.BadRequest("UserId must match sender.");

        if (message.Content != null && message.Content.Length > 2048)
            return Results.BadRequest("Content must be under 2048 chars");


        if (message.EmbedData != null && message.EmbedData.Length > 65535)
            return Results.BadRequest("EmbedData must be under 65535 chars");

        var channel = await DirectChatChannel.FindAsync(message.ChannelId, valourDb);

        if (channel is null)
            return ValourResult.NotFound("Direct chat channel not found");

        if ((channel.UserOneId != token.UserId) &&
            (channel.UserTwoId != token.UserId))
            return ValourResult.Forbid("You do not have access to this direct chat channel");

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
                    if (!at.Location.StartsWith("https://cdn.valour.gg"))
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
                    if (mention.Type == MentionType.User)
                    {
                        var mentionTargetUser = await Item.FindAsync<User>(mention.TargetId, valourDb);
                        var sendingUser = await Item.FindAsync<User>(token.UserId, valourDb);

                        var content = message.Content.Replace($"«@u-{mention.TargetId}»", $"@{mentionTargetUser.Name}");

                        await NotificationManager.SendNotificationAsync(valourDb, mentionTargetUser.Id, sendingUser.PfpUrl, sendingUser.Name + " in DMs", content);
                    }
                }
            }
        }

        User targetUser;

        // Get the user that is NOT the token user
        if (channel.UserOneId == token.UserId)
        {
            targetUser = await Item.FindAsync<User>(channel.UserTwoId, valourDb);
        }
        else
        {
            targetUser = await Item.FindAsync<User>(channel.UserOneId, valourDb);
        }

        if (targetUser is null)
            return ValourResult.NotFound("Target user not found.");

        // Add message to database
        await channelService.SetMessageState(channel, message.Id);
        var newChannelState = await channelService.GetState(channel.Id);
        
        var state = await valourDb.UserChannelStates.FirstOrDefaultAsync(x => x.UserId == token.UserId && x.ChannelId == channel.Id);
        if (state is null)
        {
            valourDb.UserChannelStates.Add(new UserChannelState()
            {
                UserId = token.UserId,
                ChannelId = channel.Id,
                LastViewedState = newChannelState
            });
        }

        else
        {
            state.LastViewedState = newChannelState;
        }

        await valourDb.DirectMessages.AddAsync(message);
        await valourDb.SaveChangesAsync();

        // Relay to nodes where sending user or target user is connected
        var rdb = redis.GetDatabase(RedisDbTypes.Connections);

        var nodeTargets = new List<(string nodeId, long userId)>();
        await foreach (var conn in rdb.SetScanAsync($"user:{channel.UserOneId}"))
        {
            var split = conn.ToString().Split(':');
            var nodeId = split[0];
            nodeTargets.Add((nodeId, channel.UserOneId));
        }
        await foreach (var conn in rdb.SetScanAsync($"user:{channel.UserTwoId}"))
        {
            var split = conn.ToString().Split(':');
            var nodeId = split[0];
            nodeTargets.Add((nodeId, channel.UserTwoId));
        }

        foreach (var target in nodeTargets.Distinct())
        {
            // Case for same name
            if (target.nodeId == NodeAPI.Node.Name)
            {
                // Just fire event in this node
                hubService.RelayDirectMessage(message, target.userId);
            }
            else
            {
                // Inter-node communications
                await client.PostAsJsonAsync($"https://{target.nodeId}.nodes.valour.gg/api/{nameof(DirectChatChannel)}/relay?targetId={target.userId}&auth={NodeConfig.Instance.Key}", message);
            }
        }

        StatWorker.IncreaseMessageCount();

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Delete, "/{id}/messages/{message_id}"), TokenRequired]
    [UserPermissionsRequired(UserPermissionsEnum.Messages, UserPermissionsEnum.DirectMessages)]
    public static async Task<IResult> DeleteMessageRouteAsync(
        long id, 
        long message_id, 
        HttpContext ctx, 
        HttpClient client,
        ValourDB db,
        CoreHubService hubService,
        UserOnlineService onlineService,
        ILogger<DirectChatChannel> logger,
        IConnectionMultiplexer redis)
    {
        var token = ctx.GetToken();

        var channel = await DirectChatChannel.FindAsync(id, db);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");

        var message = await FindAsync<DirectMessage>(message_id, db);

        if (message.ChannelId != id)
            return ValourResult.NotFound<PlanetMessage>();

        if (token.UserId != message.AuthorUserId)
        {
            return ValourResult.Forbid("You cannot delete another user's direct messages");
        }

        try
        {
            db.DirectMessages.Remove(message);
            await db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            logger.LogError(e.Message);
            return Results.Problem(e.Message);
        }
        
        // Relay to nodes where sending user or target user is connected
        var rdb = redis.GetDatabase(RedisDbTypes.Connections);

        var nodeTargets = new List<(string nodeId, long userId)>();
        await foreach (var conn in rdb.SetScanAsync($"user:{channel.UserOneId}"))
        {
            var split = conn.ToString().Split(':');
            var nodeId = split[0];
            nodeTargets.Add((nodeId, channel.UserOneId));
        }
        await foreach (var conn in rdb.SetScanAsync($"user:{channel.UserTwoId}"))
        {
            var split = conn.ToString().Split(':');
            var nodeId = split[0];
            nodeTargets.Add((nodeId, channel.UserTwoId));
        }

        foreach (var target in nodeTargets.Distinct())
        {
            // Case for same name
            if (target.nodeId == NodeAPI.Node.Name)
            {
                // Just fire event in this node
                hubService.NotifyDirectMessageDeletion(message, target.userId);
            }
            else
            {
                // Inter-node communications
                await client.PostAsJsonAsync($"https://{target.nodeId}.nodes.valour.gg/api/{nameof(DirectChatChannel)}/relaydelete?targetId={target.userId}&auth={NodeConfig.Instance.Key}", message);
            }
        }

        await onlineService.UpdateOnlineState(token.UserId);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "/relay", $"api/{nameof(DirectChatChannel)}")]
    public static async Task<IResult> RelayDirectMessageAsync(
        [FromBody] DirectMessage message, 
        [FromQuery] string auth, 
        [FromQuery] long targetId,
        CoreHubService hubService)
    {
        if (auth != NodeConfig.Instance.Key)
            return ValourResult.Forbid("Invalid inter-node key.");

        hubService.RelayDirectMessage(message, targetId);

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Post, "/relaydelete", $"api/{nameof(DirectChatChannel)}")]
    public static async Task<IResult> RelayDeleteDirectMessageAsync(
        [FromBody] DirectMessage message, 
        [FromQuery] string auth, 
        [FromQuery] long targetId,
        CoreHubService hubService)
    {
        if (auth != NodeConfig.Instance.Key)
            return ValourResult.Forbid("Invalid inter-node key.");

        hubService.NotifyDirectMessageDeletion(message, targetId);

        return Results.Ok();
    }

    #endregion
}
