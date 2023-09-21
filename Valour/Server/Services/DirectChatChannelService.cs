using StackExchange.Redis;
using System.Text.Json;
using Valour.Server.Cdn;
using Valour.Server.Config;
using Valour.Server.Database;
using Valour.Server.Redis;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class DirectChatChannelService
{
    private readonly ValourDB _db;
    private readonly CoreHubService _coreHub;
    private readonly TokenService _tokenService;
    private readonly NodeService _nodeService;
    private readonly NotificationService _notificationService;
    private readonly ILogger<DirectChatChannelService> _logger;
    private readonly CdnDb _cdnDB;
    private readonly IConnectionMultiplexer _redis;
    private readonly HttpClient _httpClient;

    public DirectChatChannelService(
        ValourDB db,
        CoreHubService coreHub,
        TokenService tokenService,
        NotificationService notificationService,
        ILogger<DirectChatChannelService> logger,
        CdnDb cdnDb,
        IConnectionMultiplexer redis,
        NodeService nodeService,
        HttpClient httpClient)
    {
        _db = db;
        _coreHub = coreHub;
        _tokenService = tokenService;
        _logger = logger;
        _cdnDB = cdnDb;
        _redis = redis;
        _httpClient = httpClient;
        _nodeService = nodeService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Returns the direct chat channel with the given id
    /// </summary>
    public async Task<DirectChatChannel> GetAsync(long id) =>
        (await _db.DirectChatChannels.FindAsync(id)).ToModel();

    /// <summary>
    /// Returns the direct chat channel between the two given user ids
    /// </summary>
    public async Task<DirectChatChannel> GetAsync(long userOneId, long userTwoId)
    {
        // Doesn't matter which user is which
        return (await _db.DirectChatChannels.FirstOrDefaultAsync(x =>
            (x.UserOneId == userOneId && x.UserTwoId == userTwoId) ||
            (x.UserOneId == userTwoId && x.UserTwoId == userOneId)
        )).ToModel();
    }
    
    /// <summary>
    /// Returns all of the direct chat channels for the given user id
    /// </summary>
    public async Task<List<DirectChatChannel>> GetChannelsForUserAsync(long userId) =>
        (await _db.DirectChatChannels.Where(x => x.UserOneId == userId || x.UserTwoId == userId)
            .Select(x => x.ToModel()).ToListAsync());

    public async Task<TaskResult<DirectChatChannel>> CreateAsync(long userOneId, long userTwoId)
    {
        Valour.Database.DirectChatChannel channel = null;
        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            // If there is no dm channel yet, we create it
            // TODO: Prevent if one of the users is blocking the other
            channel = new()
            {
                Id = IdManager.Generate(),
                UserOneId = userOneId,
                UserTwoId = userTwoId,
                TimeLastActive = DateTime.UtcNow
            };

            await _db.AddAsync(channel);
            await _db.SaveChangesAsync();
            
            // Add fresh channel state
            var state = new Valour.Database.ChannelState()
            {
                ChannelId = channel.Id,
                PlanetId = null,
                LastUpdateTime = DateTime.UtcNow,
            };

            await _db.ChannelStates.AddAsync(state);
            await _db.SaveChangesAsync();
            
            await tran.CommitAsync();
        }

        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            await tran.RollbackAsync();
            return new(false, "Failed to create channel. An unexpected error occurred.");
        }

        return new(true, "Success", channel.ToModel());
    }

    public async Task<DirectMessage> GetDirectMessageAsync(long msgId)
    {
        var msg = (await _db.DirectMessages.FindAsync(msgId)).ToModel();
        if (msg.ReplyToId is not null)
            msg.ReplyTo = (await _db.DirectMessages.FindAsync(msg.ReplyToId)).ToModel();

        return msg;
    }

    public async Task<List<DirectMessage>> GetDirectMessagesAsync(DirectChatChannel channel, long index, int count) =>
        await _db.DirectMessages.Where(x => x.ChannelId == channel.Id && x.Id <= index)
            .Include(x => x.ReplyToMessage)
            .OrderByDescending(x => x.Id)
            .Take(count)
            .Reverse()
            .Select(x => x.ToModel().AddReplyTo(x.ReplyToMessage.ToModel()))
            .ToListAsync();

    public async Task UpdateUserStateAsync(DirectChatChannel channel, long userId)
    {
        var state = await _db.UserChannelStates.FirstOrDefaultAsync(x => x.UserId == userId && x.ChannelId == channel.Id);
        if (state is null)
        {
            _db.UserChannelStates.Add(new Valour.Database.UserChannelState()
            {
                UserId = userId,
                ChannelId = channel.Id,
                LastViewedTime = DateTime.UtcNow
            });
        }
        else
        {
            state.LastViewedTime = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public static Regex _attachmentRejectRegex = new Regex("(^|.)(<|>|\"|'|\\s)(.|$)");

    public async Task<TaskResult> DeleteMessageAsync(DirectChatChannel channel, DirectMessage message)
    {
        try
        {
            var _old = await _db.DirectMessages.FindAsync(message.Id);
            if (_old is null) return new(false, $"DirectMessage not found");
            _db.DirectMessages.Remove(_old);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError(e.Message);
            return new(false, e.Message);
        }

        // Relay to nodes where sending user or target user is connected
        var rdb = _redis.GetDatabase(RedisDbTypes.Cluster);

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
            if (target.nodeId == NodeConfig.Instance.Name)
            {
                // Just fire event in this node
                _coreHub.NotifyDirectMessageDeletion(message, target.userId);
            }
            else
            {
                // Inter-node communications
                await _httpClient.PostAsJsonAsync($"https://{target.nodeId}.nodes.valour.gg/api/directchatchannels/relaydelete?targetId={target.userId}&auth={NodeConfig.Instance.Key}", message);
            }
        }

        return new(true, "Success");
    }

    public async Task<TaskResult> PostMessageAsync(DirectChatChannel channel, DirectMessage message, long sendingUserId)
    {
        if (message.Content is null)
            message.Content = "";

        message.Id = IdManager.Generate();

        List<Valour.Api.Models.MessageAttachment> attachments = null;
        
        // Handle attachments
        if (message.AttachmentsData is not null)
        {
            attachments = JsonSerializer.Deserialize<List<Valour.Api.Models.MessageAttachment>>(message.AttachmentsData);
            if (attachments is not null)
            {
                foreach (var at in attachments)
                {
                    var result = MediaUriHelper.ScanMediaUri(at);
                    if (!result.Success)
                        return new TaskResult(false, result.Message);
                }
            }
        }

        var inlineChange = false;
        
        // Handle new inline attachments
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            // Prevent markdown bypassing inline, e.g. [](https://example.com)
            // This is because a direct image link is not proxied and can steal ip addresses
            message.Content = message.Content.Replace("[](", "(");
            
            var inlineAttachments = await ProxyHandler.GetUrlAttachmentsFromContent(message.Content, _cdnDB, _httpClient);
            if (inlineAttachments is not null)
            {
                if (attachments is null)
                {
                    attachments = inlineAttachments;
                }
                else
                {
                    attachments.AddRange(inlineAttachments);
                }

                inlineChange = true;
            }
        }
        
        // If there was a change, serialize the new attachments data back to the message
        if (inlineChange)
        {
            message.AttachmentsData = JsonSerializer.Serialize(attachments);
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
                        // Ensure user being mentioned is actually in the channel
                        if (mention.TargetId != channel.UserOneId && mention.TargetId != channel.UserTwoId)
                        {
                            continue;
                        }
                        
                        var mentionTargetUser = await _db.Users.FindAsync(mention.TargetId);
                        var sendingUser = await _db.Users.FindAsync(sendingUserId);

                        var content = message.Content.Replace($"«@u-{mention.TargetId}»", $"@{mentionTargetUser.Name}");

                        Notification notif = new()
                        {
                            Title = sendingUser.Name + " mentioned you in DMs",
                            Body = content,
                            ImageUrl = sendingUser.PfpUrl,
                            ClickUrl = $"/directchannels/{channel.Id}/{message.Id}",
                            ChannelId = channel.Id,
                            Source = NotificationSource.DirectMention,
                            SourceId = message.Id,
                            UserId = mentionTargetUser.Id,
                        };
                        await _notificationService.AddNotificationAsync(notif);
                    }
                }
            }
        }

        var sender = await _db.Users.FindAsync(sendingUserId);
        User targetUser;

        // Get the user that is NOT the token user
        if (channel.UserOneId == sendingUserId)
        {
            targetUser = (await _db.Users.FindAsync(channel.UserTwoId)).ToModel();
        }
        else
        {
            targetUser = (await _db.Users.FindAsync(channel.UserOneId)).ToModel();
        }

        if (targetUser is null)
            return new(false, "Target user not found.");

        // Only notify if there's not already a ping in the message for the target user
        if (string.IsNullOrWhiteSpace(message.MentionsData) || !message.MentionsData.Contains(targetUser.Id.ToString()))
        {
            // Check if a non-mention notification has been sent in this channel in the last minute
            if (!await _db.Notifications.AnyAsync(x =>
                    x.ChannelId == channel.Id && x.UserId == targetUser.Id &&
                    x.Source == NotificationSource.DirectReply && x.TimeSent > DateTime.UtcNow.AddMinutes(-1)))
            {
                
                Notification notif = new()
                {
                    Title = sender.Name + " sent a message in DMs",
                    Body = message.Content,
                    ImageUrl = sender.PfpUrl,
                    ClickUrl = $"/directchannels/{channel.Id}/{message.Id}",
                    ChannelId = channel.Id,
                    Source = NotificationSource.DirectReply,
                    SourceId = message.Id,
                    UserId = targetUser.Id,
                };
                
                await _notificationService.AddNotificationAsync(notif);
            }
        }

        if (message.ReplyToId is not null)
        {
            var replyTo = await _db.DirectMessages.FindAsync(message.ReplyToId);
            if (replyTo is null)
                return new(false, "Reply message not found");

            message.ReplyTo = replyTo.ToModel();
        }


        await UpdateUserStateAsync(channel, sendingUserId);

        await _db.DirectMessages.AddAsync(message.ToDatabase());
        await _db.SaveChangesAsync();

        await _coreHub.RelayDirectMessage(message, _nodeService);

        StatWorker.IncreaseMessageCount();

        return new(true, "Success");
    }
    
    public async Task<TaskResult> EditMessageAsync(DirectMessage message)
    {
        if (message.Content is null)
            message.Content = "";

        List<Valour.Api.Models.MessageAttachment> attachments = null;

        bool inlineChange = false;
        
        // Handle attachments
        if (message.AttachmentsData is not null)
        {
            attachments = JsonSerializer.Deserialize<List<Valour.Api.Models.MessageAttachment>>(message.AttachmentsData);
            if (attachments is not null)
            {
                foreach (var at in attachments)
                {
                    var result = MediaUriHelper.ScanMediaUri(at);
                    if (!result.Success)
                        return new TaskResult(false, result.Message);
                }
                
                // Remove old inline attachments
                var n = attachments.RemoveAll(x => x.Inline);
                if (n > 0)
                    inlineChange = true;
            }
        }
        
        // Handle new inline attachments
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            var inlineAttachments = await ProxyHandler.GetUrlAttachmentsFromContent(message.Content, _cdnDB, _httpClient);
            if (inlineAttachments is not null)
            {
                if (attachments is null)
                {
                    attachments = inlineAttachments;
                }
                else
                {
                    attachments.AddRange(inlineAttachments);
                }
                
                inlineChange = true;
            }
        }

        // If there was a change, serialize the new attachments data back to the message
        if (inlineChange)
        {
            message.AttachmentsData = JsonSerializer.Serialize(attachments);
        }

        var old = await _db.DirectMessages.FindAsync(message.Id);
        if (old is null)
            return new TaskResult(false, "Message not found");
        
        old.Content = message.Content;
        old.AttachmentsData = message.AttachmentsData;
        old.MentionsData = message.MentionsData;
        old.EditedTime = DateTime.UtcNow;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            return new(false, e.Message);
        }

        await _coreHub.RelayDirectMessageEdit(message, _nodeService);

        return new(true, "Success");
    }
}