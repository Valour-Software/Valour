#nullable enable

using System.Text.Json;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Sdk.Models.Messages.Embeds.Items;
using Valour.Server.Cdn;
using Valour.Server.Utilities;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class MessageService
{
    private readonly ILogger<MessageService> _logger;
    private readonly ValourDb _db;
    private readonly NodeLifecycleService _nodeLifecycleService;
    private readonly ChannelService _channelService;
    private readonly NotificationService _notificationService;
    private readonly CoreHubService _coreHubService;
    private readonly ChatCacheService _chatCacheService;
    private readonly HostedPlanetService _hostedPlanetService;
    private readonly AutomodService _automodService;
    private readonly ProxyHandler _proxyHandler;

    public MessageService(
        ILogger<MessageService> logger,
        ValourDb db,
        NodeLifecycleService nodeLifecycleService,
        NotificationService notificationService,
        CoreHubService coreHubService,
        ChannelService channelService,
        ChatCacheService chatCacheService,
        HostedPlanetService hostedPlanetService,
        AutomodService automodService,
        ProxyHandler proxyHandler)
    {
        _logger = logger;
        _db = db;
        _nodeLifecycleService = nodeLifecycleService;
        _notificationService = notificationService;
        _coreHubService = coreHubService;
        _channelService = channelService;
        _chatCacheService = chatCacheService;
        _hostedPlanetService = hostedPlanetService;
        _automodService = automodService;
        _proxyHandler = proxyHandler;
    }
    
    /// <summary>
    /// Returns the message with the given id
    /// Will include the reply to message if it exists
    /// </summary>
    public async Task<Message?> GetMessageAsync(long id)
    {
        // Check staged first
        var staged = PlanetMessageWorker.GetStagedMessage(id);
        if (staged is not null)
        {
            return staged;
        }
        
        var message = await _db.Messages.AsNoTracking()
            .Include(x => x.ReplyToMessage)
            .Include(x => x.Reactions)
            .FirstOrDefaultAsync(x => x.Id == id);
        
        return message?.ToModel();
    }
    
    /// <summary>
    /// Returns the message with the given id (no reply!)
    /// </summary>
    public async Task<Message?> GetMessageNoReplyAsync(long id) =>
        (await _db.Messages.Include(x => x.Reactions).FirstOrDefaultAsync(x => x.Id == id)).ToModel();
    
    /// <summary>
    /// Used to post a message 
    /// </summary>
    public async Task<TaskResult<Message>> PostMessageAsync(Message message)
    {
        var user = await _db.Users.FindAsync(message.AuthorUserId);
        if (user is null)
            return TaskResult<Message>.FromFailure("Author user not found.");

        var channel = await _channelService.GetChannelAsync(message.PlanetId, message.ChannelId);
        if (channel is null)
            return TaskResult<Message>.FromFailure("Channel not found.");

        if (channel.PlanetId != message.PlanetId)
            return TaskResult<Message>.FromFailure("Invalid planet id. Must match channel's planet id.");
        
        if (!ISharedChannel.ChatChannelTypes.Contains(channel.ChannelType))
            return TaskResult<Message>.FromFailure("Channel is not a message channel.");
        
        // The notification to send out after processing the message
        NotificationSource? notification = null;

        HostedPlanet? hostedPlanet = null;
        Planet? planet = null;
        Valour.Database.PlanetMember? member = null;
        
        // Validation specifically for planet messages
        if (channel.PlanetId is not null)
        {
            if (channel.PlanetId != message.PlanetId)
                return TaskResult<Message>.FromFailure("Invalid planet id. Must match channel's planet id.");
            
            hostedPlanet = await _hostedPlanetService.GetRequiredAsync(channel.PlanetId.Value);
            planet = hostedPlanet.Planet;

            if (!ISharedChannel.PlanetChannelTypes.Contains(channel.ChannelType))
                return TaskResult<Message>.FromFailure("Only planet channel messages can have a planet id.");

            if (message.AuthorUserId != ISharedUser.VictorUserId) // Always allow system messages
            {
                if (message.AuthorMemberId is null)
                    return TaskResult<Message>.FromFailure("AuthorMemberId is required for planet channel messages.");

                member = await _db.PlanetMembers.FindAsync(message.AuthorMemberId);
                if (member is null)
                    return TaskResult<Message>.FromFailure("Member id does not exist or is invalid for this planet.");

                if (member.UserId != message.AuthorUserId)
                    return TaskResult<Message>.FromFailure(
                        "Mismatch between member's user id and message author user id.");
            }
        }
        else
        {
            // DMs always send a notification
            notification = NotificationSource.DirectMessage;
        }
        
        // The message this message is replying to
        Message? replyTo = null;

        // Handle replies
        if (message.ReplyToId is not null)
        {
            replyTo = (await _db.Messages.FindAsync(message.ReplyToId)).ToModel();
            if (replyTo is null)
            {
                // Try to get from cache if it has not yet posted
                replyTo = PlanetMessageWorker.GetStagedMessage(message.ReplyToId.Value);
                
                if (replyTo is null)
                    return TaskResult<Message>.FromFailure("ReplyToId does not exist.");
            }

            // TODO: Technically we could support this in the future
            if (replyTo.ChannelId != channel.Id)
                return TaskResult<Message>.FromFailure("Cannot reply to a message from another channel.");
            
            message.ReplyTo = replyTo;
            
            // Flag notification to be sent
            notification = planet is null ? NotificationSource.DirectReply : NotificationSource.PlanetMemberReply;
        }
        
        if (string.IsNullOrEmpty(message.Content) &&
            string.IsNullOrEmpty(message.EmbedData) &&
            string.IsNullOrEmpty(message.AttachmentsData))
            return TaskResult<Message>.FromFailure("Message must contain content, embed data, or attachments.");
        
        if (message.Fingerprint is null)
            return TaskResult<Message>.FromFailure("Fingerprint is required. Generating a random UUID is suggested.");
        
        if (message.Content != null && message.Content.Length > 2048)
            return TaskResult<Message>.FromFailure("Content must be under 2048 chars");


        if (!string.IsNullOrWhiteSpace(message.EmbedData))
        {
            if (message.EmbedData.Length > 65535)
            {
                return TaskResult<Message>.FromFailure("EmbedData must be under 65535 chars");
            }
            
            // load embed to check for anti-valour propaganda (incorrect media URIs)
            var embed = JsonSerializer.Deserialize<Embed>(message.EmbedData);
            foreach (var page in embed.Pages)
            {
                foreach (var item in page.GetAllItems())
                {
                    if (item.ItemType == EmbedItemType.Media)
                    {
                        var at = ((EmbedMediaItem)item).Attachment;
                        var result = MediaUriHelper.ScanMediaUri(at);
                        if (!result.Success)
                            return TaskResult<Message>.FromFailure($"Error scanning media URI in embed | Page {page.Id} | ServerModel {item.Id}) | URI {at.Location}");
                    }
                }
            }
        }

        if (message.Content is null)
            message.Content = "";

        message.Id = Valour.Server.Database.IdManager.Generate();
        message.TimeSent = DateTime.UtcNow;
        
        List<Valour.Sdk.Models.MessageAttachment> attachments = null;
        // Handle attachments
        if (!string.IsNullOrWhiteSpace(message.AttachmentsData))
        {
            attachments = JsonSerializer.Deserialize<List<Valour.Sdk.Models.MessageAttachment>>(message.AttachmentsData);
            if (attachments is not null)
            {
                foreach (var at in attachments)
                {
                    var result = MediaUriHelper.ScanMediaUri(at);
                    if (!result.Success)
                        return TaskResult<Message>.FromFailure($"Error scanning media URI in message attachments | {at.Location}");
                }
            }
        }

        // True if the scanning process makes changes to inline attachments
        var inlineChange = false;
        if (!string.IsNullOrWhiteSpace(message.Content))
        {
            // Prevent markdown bypassing inline, e.g. [](https://example.com)
            // This is because a direct image link is not proxied and can steal ip addresses
            message.Content = message.Content.Replace("[](", "(");
	        
            var inlineAttachments = await _proxyHandler.GetUrlAttachmentsFromContent(message.Content, _db);
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
        
        // Handle mentions
        var mentions = MentionParser.Parse(message.Content);
        if (mentions is not null)
        {
            foreach (var mention in mentions)
            {
                await _notificationService.HandleMentionAsync(mention, planet, message, member, user, channel);
            }

            // Serialize mentions to the message
            message.MentionsData = JsonSerializer.Serialize(mentions);
        }
        
        var memberModel = member?.ToModel();

        try
        {
            if (!await _automodService.ScanMessageAsync(message, memberModel))
                return TaskResult<Message>.FromFailure("Message blocked by automod.");
        } catch (Exception e)
        {
            _logger.LogError(e, "Failed to scan message with automod");
        }

        // Add to chat caches
        _chatCacheService.AddMessage(message);
        
        // Add member to recent chatters
        _chatCacheService.AddChatPlanetMember(message.ChannelId, memberModel);

        if (planet is null)
        {
            if (channel.ChannelType == ChannelTypeEnum.DirectChat)
            {
                var channelMembers = await _db.ChannelMembers
                    .AsNoTracking()
                    .Include(x => x.User)
                    .Where(x => x.ChannelId == channel.Id)
                    .Select(x => x.UserId)
                    .ToListAsync();

                await _db.Messages.AddAsync(message.ToDatabase());
                await _db.SaveChangesAsync();

                await _coreHubService.RelayDirectMessage(message, _nodeLifecycleService, channelMembers);
            }
            else
            {
                return TaskResult<Message>.FromFailure("Channel type not implemented!");
            }
        }
        else
        {
            PlanetMessageWorker.AddToQueue(message);
        }
        
        StatWorker.IncreaseMessageCount();
        
        // Update channel state
        await _db.Channels.Where(x => x.Id == channel.Id)
            .ExecuteUpdateAsync(x => x.SetProperty(c => c.LastUpdateTime, DateTime.UtcNow));
        
        // Update in cached hosted planet
        if (hostedPlanet is not null)
        {
            var cachedChannel = hostedPlanet.Channels.Get(channel.Id);
            cachedChannel.LastUpdateTime = DateTime.UtcNow;
        }


        if (channel.PlanetId is not null)
        {
            _coreHubService.NotifyChannelStateUpdate(channel.PlanetId.Value, channel.Id, message.TimeSent);
        }

        if (notification is not null)
        {
            switch (notification)
            {
                case NotificationSource.DirectReply:
                {
                    // For DM replies, still send the DM notification to the recipient
                    await _notificationService.HandleDirectMessageAsync(message, user, channel);
                    break;
                }
                case NotificationSource.PlanetMemberReply:
                {
                    if (replyTo is not null)
                    {
                        await _notificationService.HandleReplyAsync(replyTo, planet, message, member, user, channel);
                    }
                    break;
                }
                case NotificationSource.DirectMessage:
                {
                    await _notificationService.HandleDirectMessageAsync(message, user, channel);
                    break;
                }
            }
        }

        return TaskResult<Message>.FromData(message);
    }

    /// <summary>
    /// Used to update a message
    /// </summary>
    public async Task<TaskResult<Message>> EditMessageAsync(Message updated)
    {
        if (updated is null)
            return TaskResult<Message>.FromFailure("Include updated message");

        ISharedMessage old = null;
        Message stagedOld = null;
        
        var dbOld = await _db.Messages.FindAsync(updated.Id);
        if (dbOld is not null)
        {
            old = dbOld;
        }
        else
        {
            stagedOld = PlanetMessageWorker.GetStagedMessage(updated.Id);
            if (stagedOld is not null)
            {
                old = stagedOld;
            }
            else
            {
                return TaskResult<Message>.FromFailure("Message not found");
            }
        }
        
        // Sanity checks
        if (string.IsNullOrEmpty(updated.Content) &&
            string.IsNullOrEmpty(updated.EmbedData) &&
            string.IsNullOrEmpty(updated.AttachmentsData))
            return TaskResult<Message>.FromFailure("Updated message cannot be empty");
        
        if (updated.EmbedData != null && updated.EmbedData.Length > 65535)
            return TaskResult<Message>.FromFailure("EmbedData must be under 65535 chars");
        
        if (updated.Content != null && updated.Content.Length > 2048)
            return TaskResult<Message>.FromFailure("Content must be under 2048 chars");
        
        List<Valour.Sdk.Models.MessageAttachment> attachments = null;
        bool inlineChange = false;

        // Handle attachments
        if (!string.IsNullOrWhiteSpace(updated.AttachmentsData))
        {
            attachments = JsonSerializer.Deserialize<List<Valour.Sdk.Models.MessageAttachment>>(updated.AttachmentsData);
            if (attachments is not null)
            {
                foreach (var at in attachments)
                {
                    var result = MediaUriHelper.ScanMediaUri(at);
                    if (!result.Success)
                        return TaskResult<Message>.FromFailure(result.Message);
                }
                
                // Remove old inline attachments
                var n = attachments.RemoveAll(x => x.Inline);
                if (n > 0)
                    inlineChange = true;
            }
        }
        
        // Handle new inline attachments
        if (!string.IsNullOrWhiteSpace(updated.Content))
        {
            var inlineAttachments = await _proxyHandler.GetUrlAttachmentsFromContent(updated.Content, _db);
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
            updated.AttachmentsData = JsonSerializer.Serialize(attachments);
        }

        old.Content = updated.Content;
        old.AttachmentsData = updated.AttachmentsData;
        old.EmbedData = updated.EmbedData;
        old.EditedTime = DateTime.UtcNow;
        
        // In this case, the message has posted to the database so
        // we save changes there
        if (dbOld is not null)
        {
            try
            {
                _db.Messages.Update(dbOld);
                await _db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to update message");
                return TaskResult<Message>.FromFailure("Failed to update message in database.");
            }
        }
        
        _chatCacheService.ReplaceMessage(updated);
        
        // Handle events

        if (updated.PlanetId is not null)
        {
            _coreHubService.RelayMessageEdit(updated);
        }
        else
        {
            var channelUserIds = await _db.ChannelMembers
                .AsNoTracking()
                .Where(x => x.ChannelId == updated.ChannelId)
                .Select(x => x.UserId)
                .ToListAsync();
            
            await _coreHubService.RelayDirectMessageEdit(updated, _nodeLifecycleService, channelUserIds);
        }

        return TaskResult<Message>.FromData(updated);
    }

    /// <summary>
    /// Used to delete a message
    /// </summary>
    public async Task<TaskResult> DeleteMessageAsync(long messageId)
    {
        Message message = null;
        
        var dbMessage = await _db.Messages
            .Include(x => x.Reactions)
            .FirstOrDefaultAsync(x => x.Id == messageId);
        
        if (dbMessage is null)
        {
            // Check staging
            var staged = PlanetMessageWorker.GetStagedMessage(messageId);
            if (staged is not null)
            {
                message = staged;
                PlanetMessageWorker.RemoveFromQueue(staged);
            }
            else
            {
                return TaskResult.FromFailure("Message not found");
            }
        }
        else
        {
            message = dbMessage.ToModel();

            try
            {
                if (dbMessage.Reactions.Count > 0){
                    _db.MessageReactions.RemoveRange(dbMessage.Reactions);
                    await _db.SaveChangesAsync();
                }

                _db.Messages.Remove(dbMessage);
                await _db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to delete message");
                return TaskResult.FromFailure("Failed to delete message in database.");
            }
        }
        
        _chatCacheService.RemoveMessage(message.ChannelId, messageId);
        
        if (message.PlanetId is not null)
        {
            _coreHubService.NotifyMessageDeletion(message);
        }
        else
        {
            // TODO: Direct message deletion event
        }

        return TaskResult.SuccessResult;
    }
    
    public async Task<IEnumerable<Message>?> GetChannelMessagesAsync(long? planetId, long channelId, int count = 50, long index = long.MaxValue)
    {
        var channel = await _channelService.GetChannelAsync(planetId, channelId);
        if (channel is null)
            return null;
        
        if (!ISharedChannel.ChatChannelTypes.Contains(channel.ChannelType))
            return null;

        // Not sure why this request would even be made
        if (count < 1)
            return [];
        
        if (count > 50)
            count = 50;
        
        // For default latest messages, use the cache
        if (index == long.MaxValue && count == 50)
        {
            return await _chatCacheService.GetLastMessagesAsync(channelId);
        }

        List<Message>? staged = null;

        if (channel.ChannelType == ChannelTypeEnum.PlanetChat
            && index == long.MaxValue) // ONLY INCLUDE STAGED FOR LATEST
        {
            staged = PlanetMessageWorker.GetStagedMessages(channel.Id);
        }
        
        var messages = await _db.Messages
            .AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.Id < index)
            .Include(x => x.ReplyToMessage)
            .Include(x => x.Reactions)
            .OrderByDescending(x => x.Id)
            .Take(count)
            .Reverse()
            .Select(x => x.ToModel())
            .ToListAsync();

        // Not all channels actually stage messages
        if (staged is not null)
        {
            messages.AddRange(staged);
        }

        return messages;
    }

    public async Task<List<Message>> SearchChannelMessagesAsync(long? planetId, long channelId, string search, int count = 20)
    {
        var channel = await _channelService.GetChannelAsync(planetId, channelId);
        if (channel is null)
            return [];
        
        if (!ISharedChannel.ChatChannelTypes.Contains(channel.ChannelType))
            return [];
        
        // Use postgres functions to search for the search string
        var messages = await _db.Messages
            .AsNoTracking()
            .Where(x => x.ChannelId == channel.Id)
            .Where(x => EF.Functions.ILike(x.Content, $"%{search}%"))
            .Include(x => x.ReplyToMessage)
            .Include(x => x.Reactions)
            .OrderByDescending(x => x.Id)
            .Take(count)
            .Select(x => x.ToModel())
            .ToListAsync();
        
        return messages;
    }

    public async Task<TaskResult> AddReactionAsync(User user, PlanetMember? member, Message message, string emoji)
    {
        if (await _db.MessageReactions.AnyAsync(x => x.MessageId == message.Id && x.Emoji == emoji && x.AuthorUserId == user.Id))
            return TaskResult.FromFailure("Reaction already exists");

        var reaction = new Valour.Database.MessageReaction()
        {
            Id = Valour.Server.Database.IdManager.Generate(),
            Emoji = emoji,
            MessageId = message.Id,
            AuthorUserId = user.Id,
            AuthorMemberId = member?.Id,
            CreatedAt = DateTime.UtcNow,
        };
        
        bool staged = PlanetMessageWorker.GetStagedMessage(message.Id) is not null;

        if (!staged)
        {
            try
            {
                await _db.MessageReactions.AddAsync(reaction);
                await _db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to add reaction");
                return TaskResult.FromFailure("Failed to add reaction to database.");
            }
        }
        
        // Add to message
        if (message.Reactions is null)
            message.Reactions = [];
        
        message.Reactions.Add(reaction.ToModel());
        
        // Replace in message cache
        _chatCacheService.ReplaceMessage(message);
        
        _coreHubService.RelayMessageReactionAdded(message.ChannelId, reaction.ToModel());
        
        return TaskResult.SuccessResult;
    }
    
    public async Task<TaskResult> RemoveReactionAsync(long userId, long messageId, string emoji)
    {
        var reaction = await _db.MessageReactions
            .FirstOrDefaultAsync(x => x.MessageId == messageId && x.Emoji == emoji && x.AuthorUserId == userId);
        
        if (reaction is null)
            return TaskResult.FromFailure("Reaction not found");
        
        try
        {
            _db.MessageReactions.Remove(reaction);
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to remove reaction");
            return TaskResult.FromFailure("Failed to remove reaction from database.");
        }

        var message = await GetMessageAsync(messageId);
        if (message is not null)
        {
            message.Reactions.RemoveAll(x => x.Emoji == emoji && x.AuthorUserId == userId);
            _chatCacheService.ReplaceMessage(message);
            
            _coreHubService.RelayMessageReactionRemoved(message.ChannelId, reaction.ToModel());
        }
        
        return TaskResult.SuccessResult;
    }
}