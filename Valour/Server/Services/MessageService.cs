using System.Text.Json;
using Valour.Sdk.Models.Messages.Embeds;
using Valour.Sdk.Models.Messages.Embeds.Items;
using Valour.Server.Cdn;
using Valour.Server.Database;
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
    private readonly ChannelStateService _stateService;
    private readonly CoreHubService _coreHubService;
    private readonly HttpClient _http;

    public MessageService(
        ILogger<MessageService> logger,
        ValourDb db, 
        NodeLifecycleService nodeLifecycleService, 
        NotificationService notificationService, 
        ChannelStateService stateService,
        IHttpClientFactory http, 
        CoreHubService coreHubService, 
        ChannelService channelService)
    {
        _logger = logger;
        _db = db;
        _nodeLifecycleService = nodeLifecycleService;
        _notificationService = notificationService;
        _stateService = stateService;
        _http = http.CreateClient();
        _coreHubService = coreHubService;
        _channelService = channelService;
    }
    
    /// <summary>
    /// Returns the message with the given id
    /// Will include the reply to message if it exists
    /// </summary>
    public async Task<Message> GetMessageAsync(long id)
    {
        var message = await _db.Messages.AsNoTracking()
            .Include(x => x.ReplyToMessage)
            .FirstOrDefaultAsync(x => x.Id == id);
        
        return message?.ToModel();
    }
    
    /// <summary>
    /// Returns the message with the given id (no reply!)
    /// </summary>
    public async Task<Message> GetMessageNoReplyAsync(long id) =>
        (await _db.Messages.FindAsync(id)).ToModel();
    
    /// <summary>
    /// Used to post a message 
    /// </summary>
    public async Task<TaskResult<Message>> PostMessageAsync(Message message)
    {
        if (message is null)
            return TaskResult<Message>.FromFailure("Include message");

        // Handle node planet ownership
        if (message.PlanetId is not null)
        {
            if (!await _nodeLifecycleService.IsHostingPlanet(message.PlanetId.Value))
            {
                return TaskResult<Message>.FromFailure("Planet belongs to another node.");
            }
        }
        
        var user = await _db.Users.FindAsync(message.AuthorUserId);
        if (user is null)
            return TaskResult<Message>.FromFailure("Author user not found.");
        
        var channel = await _db.Channels.FindAsync(message.ChannelId);
        if (channel is null)
            return TaskResult<Message>.FromFailure("Channel not found.");

        if (channel.PlanetId != message.PlanetId)
            return TaskResult<Message>.FromFailure("Invalid planet id. Must match channel's planet id.");
        
        if (!ISharedChannel.ChatChannelTypes.Contains(channel.ChannelType))
            return TaskResult<Message>.FromFailure("Channel is not a message channel.");

        Valour.Database.Planet planet = null;
        Valour.Database.PlanetMember member = null;
        
        // Validation specifically for planet messages
        if (channel.PlanetId is not null)
        {
            if (channel.PlanetId != message.PlanetId)
                return TaskResult<Message>.FromFailure("Invalid planet id. Must match channel's planet id.");

            planet = await _db.Planets.FindAsync(channel.PlanetId);
            if (planet is null)
                return TaskResult<Message>.FromFailure("Planet not found.");
            
            if (!ISharedChannel.PlanetChannelTypes.Contains(channel.ChannelType))
                return TaskResult<Message>.FromFailure("Only planet channel messages can have a planet id.");
            
            if (message.AuthorMemberId is null)
                return TaskResult<Message>.FromFailure("AuthorMemberId is required for planet channel messages.");

            member = await _db.PlanetMembers.FindAsync(message.AuthorMemberId);
            if (member is null)
                return TaskResult<Message>.FromFailure("Member id does not exist or is invalid for this planet.");
            
            if (member.UserId != message.AuthorUserId)
                return TaskResult<Message>.FromFailure("Mismatch between member's user id and message author user id.");
        }

        // Handle replies
        if (message.ReplyToId is not null)
        {
            var replyTo = (await _db.Messages.FindAsync(message.ReplyToId)).ToModel();
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

        message.Id = IdManager.Generate();
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
	        
            var inlineAttachments = await ProxyHandler.GetUrlAttachmentsFromContent(message.Content, _db, _http);
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
        _stateService.SetChannelStateTime(channel.Id, message.TimeSent, channel.PlanetId);
        if (channel.PlanetId is not null)
        {
            _coreHubService.NotifyChannelStateUpdate(channel.PlanetId.Value, channel.Id, message.TimeSent);
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
            var inlineAttachments = await ProxyHandler.GetUrlAttachmentsFromContent(updated.Content, _db, _http);
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
        
        var dbMessage = await _db.Messages.FindAsync(messageId);
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
                _db.Messages.Remove(dbMessage);
                await _db.SaveChangesAsync();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to delete message");
                return TaskResult.FromFailure("Failed to delete message in database.");
            }
        }
        
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
    
    public async Task<List<Message>> GetChannelMessagesAsync(long channelId, int count = 50, long index = long.MaxValue)
    {
        var channel = await _channelService.GetChannelAsync(channelId);
        if (channel is null)
            return null;
        
        if (!ISharedChannel.ChatChannelTypes.Contains(channel.ChannelType))
            return null;

        // Not sure why this request would even be made
        if (count < 1)
            return new();

        List<Message> staged = null;

        if (channel.ChannelType == ChannelTypeEnum.PlanetChat
            && index == long.MaxValue) // ONLY INCLUDE STAGED FOR LATEST
        {
            staged = PlanetMessageWorker.GetStagedMessages(channel.Id);
        }
        
        var messages = await _db.Messages
            .AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.Id < index)
            .Include(x => x.ReplyToMessage)
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
}