using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Valour.Api.Models.Messages.Embeds;
using Valour.Api.Models.Messages.Embeds.Items;
using Valour.Server.Cdn;
using Valour.Server.Database;
using Valour.Server.Requests;
using Valour.Server.Workers;
using Valour.Shared;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class ChannelService
{
    private readonly ValourDB _db;
    private readonly CdnDb _cdnDb;
    private readonly HttpClient _http;
    private readonly PlanetMemberService _memberService;
    private readonly ILogger<ChannelService> _logger;
    private readonly CoreHubService _coreHub;
    private readonly PlanetRoleService _planetRoleService;
    private readonly NotificationService _notificationService;
    private readonly NodeService _nodeService;

    public ChannelService(
        ValourDB db,
        CdnDb cdnDb,
        HttpClient http,
        PlanetMemberService memberService,
        CoreHubService coreHubService,
        ILogger<ChannelService> logger,
        PlanetRoleService planetRoleService,
        NotificationService notificationService,
        NodeService nodeService)
    {
        _db = db;
        _cdnDb = cdnDb;
        _http = http;
        _memberService = memberService;
        _logger = logger;
        _coreHub = coreHubService;
        _planetRoleService = planetRoleService;
        _notificationService = notificationService;
        _nodeService = nodeService;
    }
    
    /// <summary>
    /// Returns the channel with the given id
    /// </summary>
    public async ValueTask<Channel> GetAsync(long id) =>
        (await _db.Channels.FindAsync(id)).ToModel();
    
    /// <summary>
    /// Soft deletes the given channel
    /// </summary>
    public async Task<TaskResult> DeleteAsync(Channel channel)
    {
        var dbChannel = await _db.Channels.FindAsync(channel.Id);
        if (dbChannel is null)
            return TaskResult.FromError( "Channel not found.");
        
        if (dbChannel.IsDefault == true)
            return TaskResult.FromError("You cannot delete the default channel.");
        
        dbChannel.IsDeleted = true;
        _db.Channels.Update(dbChannel);
        await _db.SaveChangesAsync();

        if (channel.PlanetId is not null)
            _coreHub.NotifyPlanetItemDelete(channel.PlanetId.Value, channel);
        
        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Creates the given channel
    /// </summary>
    public async Task<TaskResult<Channel>> CreateAsync(CreateChannelRequest request)
    {
        var channel = request.Channel;
        List<PermissionsNode> nodes = null;
        
        var baseValid = await ValidateChannel(channel);
        if (!baseValid.Success)
            return new(false, baseValid.Message);

        if (ISharedChannel.PlanetChannelTypes.Contains(channel.ChannelType))
        {
            if (channel.PlanetId is null)
            {
                return TaskResult<Channel>.FromError("PlanetId is required for planet channels.");
            }
        }
        
        // Only planet channels have permission nodes
        if (channel.PlanetId is not null)
        {
            var member = await _memberService.GetCurrentAsync(channel.PlanetId.Value);
            if (member is null)
                return TaskResult<Channel>.FromError("You are not a member of this planet.");

            var authority = await _memberService.GetAuthorityAsync(member);
            
            // Handle bundled permissions
            nodes = new();
            if (request.Nodes is not null)
            {
                foreach (var node in request.Nodes)
                {
                    node.TargetId = channel.Id;
                    node.PlanetId = channel.PlanetId.Value;
                    
                    var role = await _planetRoleService.GetAsync(node.RoleId);
                    if (role.GetAuthority() > authority)
                        return TaskResult<Channel>.FromError(
                            "You have a lower authority than the permission node you are trying to create.");

                    node.Id = IdManager.Generate();
                }
            }
        }

        channel.Id = IdManager.Generate();

        await using var tran = await _db.Database.BeginTransactionAsync();

        try
        {
            await _db.Channels.AddAsync(channel.ToDatabase());
            await _db.SaveChangesAsync();

            // Add fresh channel state
            var state = new Valour.Database.ChannelState()
            {
                ChannelId = channel.Id,
                PlanetId = channel.PlanetId,
                LastUpdateTime = DateTime.UtcNow,
            };

            await _db.ChannelStates.AddAsync(state);
            await _db.SaveChangesAsync();

            // Only add nodes if necessary
            if (nodes is not null)
            {
                await _db.PermissionsNodes.AddRangeAsync(nodes.Select(x => x.ToDatabase()));
                await _db.SaveChangesAsync();
            }

            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create planet chat channel");
            await tran.RollbackAsync();
            return TaskResult<Channel>.FromError("Failed to create channel");
        }

        if (channel.PlanetId is not null)
            _coreHub.NotifyPlanetItemChange(channel.PlanetId.Value, channel);

        return TaskResult<Channel>.FromData(channel);
    }
    
    /// <summary>
    /// Updates the given channel
    /// </summary>
    public async Task<TaskResult<Channel>> UpdateAsync(Channel updated)
    {
        var old = await _db.Channels.FindAsync(updated.Id);
        if (old is null) 
            return TaskResult<Channel>.FromError("Channel not found");
        
        // Update-specific validation
        if (old.Id != updated.Id)
            return TaskResult<Channel>.FromError("Cannot change Id.");
        
        if (old.PlanetId != updated.PlanetId)
            return TaskResult<Channel>.FromError("Cannot change PlanetId.");
        
        if (old.ChannelType != updated.ChannelType)
            return TaskResult<Channel>.FromError("Cannot change ChannelType.");

        // Channel parent is being changed
        if (old.ParentId != updated.ParentId)
        {
            return TaskResult<Channel>.FromError("Use the order endpoint in the parent category to update parent.");
        }
        // Channel is being moved
        if (old.Position != updated.Position)
        {
            return TaskResult<Channel>.FromError("Use the order endpoint in the parent category to change position.");
        }
        
        // Basic validation
        var baseValid = await ValidateChannel(updated);
        if (!baseValid.Success)
            return TaskResult<Channel>.FromError(baseValid.Message);

        // Update
        try
        {
            _db.Entry(old).CurrentValues.SetValues(updated);
            await _db.SaveChangesAsync();
        }
        catch (System.Exception e)
        {
            _logger.LogError("{Time}:{Error}", DateTime.UtcNow.ToShortTimeString(), e.Message);
            return new(false, e.Message);
        }

        if (updated.PlanetId is not null)
        {
            _coreHub.NotifyPlanetItemChange(updated.PlanetId.Value, updated);
        }

        // Response
        return TaskResult<Channel>.FromData(updated);
    }
    
    /// <summary>
    /// Sets the order of the children of a category. The order should contain all the children of the category.
    /// The list should contain the ids of the children in the order they should be displayed.
    /// </summary>
    public async Task<TaskResult> SetChildOrderAsync(long categoryId, List<long> order)
    {
        var category = await _db.Channels.FirstOrDefaultAsync(x =>
            x.Id == categoryId && x.ChannelType == ChannelTypeEnum.PlanetCategory);
        
        // Ensure that the category exists (and is actually a category)
        if (category is null)
            return TaskResult.FromError("Category not found.");
        
        // Prevent duplicates
        order = order.Distinct().ToList();
        
        var totalChildren = await _db.Channels.CountAsync(x => x.ParentId == categoryId);

        if (totalChildren != order.Count)
            return new(false, "Your order does not contain all the children.");

        // Use transaction so we can stop at any failure
        await using var tran = await _db.Database.BeginTransactionAsync();

        List<ChannelOrderData> newOrder = new();

        try
        {
            var pos = 0;
            foreach (var childId in order)
            {
                var child = await _db.Channels.FindAsync(childId);
                if (child is null)
                    return TaskResult.FromError($"Child with id {childId} does not exist!");

                if (child.ParentId != categoryId)
                    return new(false, $"Category {childId} is not a child of {categoryId}.");

                child.Position = pos;

                newOrder.Add(new(child.Id, child.ChannelType));

                pos++;
            }

            await _db.SaveChangesAsync();
            await tran.CommitAsync();
        }
        catch (Exception e)
        {
            await tran.RollbackAsync();
            _logger.LogError("{Time}:{Error}", DateTime.UtcNow.ToShortTimeString(), e.Message);
            return new(false, e.Message);
        }

        if (category.PlanetId is not null)
        {
            _coreHub.NotifyCategoryOrderChange(new()
            {
                PlanetId = category.PlanetId.Value,
                CategoryId = categoryId,
                Order = newOrder
            });
        }

        return new(true, "Success");
    }
    
    /// <summary>
    /// Returns the children of the given channel id
    /// </summary>
    public async Task<List<Channel>> GetChildrenAsync(long id) =>
        await _db.Channels.Where(x => x.ParentId == id)
            .OrderBy(x => x.Position)
            .Select(x => x.ToModel())
            .ToListAsync();

    /// <summary>
    /// Returns the number of children for the given channel id
    /// </summary>
    public async Task<int> GetChildCountAsync(long id) =>
        await _db.Channels.CountAsync(x => x.ParentId == id);

    /// <summary>
    /// Returns the ids of all of the children of the given channel id
    /// </summary>
    public async Task<List<long>> GetChildrenIdsAsync(long id) =>
        await _db.Channels.Where(x => x.ParentId == id)
            .Select(x => x.Id)
            .ToListAsync();
    
    /// <summary>
    /// Returns if the given category id is the last remaining category
    /// in its planet (used to prevent deletion of the last category)
    /// </summary>
    /// <param name="categoryId"></param>
    /// <returns></returns>
    public async Task<bool> IsLastCategory(long categoryId) =>
        await _db.Channels.CountAsync(x => x.PlanetId == categoryId && x.ChannelType == ChannelTypeEnum.PlanetCategory) < 2;
    
    
    
    #region Permissions
    
    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(Channel channel, PlanetMember member, CategoryPermission permission) =>
        await _memberService.HasPermissionAsync(member, channel, permission);
    
    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(Channel channel, PlanetMember member, ChatChannelPermission permission) =>
        await _memberService.HasPermissionAsync(member, channel, permission);
    
    /// <summary>
    /// Returns if a given member has a channel permission
    /// </summary>
    public async Task<bool> HasPermissionAsync(Channel channel, PlanetMember member, VoiceChannelPermission permission) =>
        await _memberService.HasPermissionAsync(member, channel, permission);
    
    #endregion
    
    //////////////
    // Messages //
    //////////////
    
    public async Task<List<Message>> GetMessagesAsync(long channelId, int count = 50, long index = long.MaxValue)
    {
        var channel = await _db.Channels.FindAsync(channelId);
        if (channel is null)
            return null;
        
        if (!ISharedChannel.MessageChannelTypes.Contains(channel.ChannelType))
            return null;

        // Not sure why this request would even be made
        if (count < 1)
            return new();

        List<Message> staged = null;

        if (channel.ChannelType == ChannelTypeEnum.PlanetChat)
        {
            staged = PlanetMessageWorker.GetStagedMessages(channel.Id);
        }
        
        var messages = await _db.Messages.AsNoTracking()
            .Where(x => x.ChannelId == channel.Id && x.Id < index)
            .Include(x => x.ReplyToMessage)
            .OrderByDescending(x => x.TimeSent)
            .Take(count)
            .Reverse()
            .Select(x => x.ToModel().AddReplyTo(x.ReplyToMessage.ToModel()))
            .ToListAsync();

        // Not all channels actually stage messages
        if (staged is not null)
        {
            messages.AddRange(staged);
        }

        return messages;
    }
    
    ///////////////
    // Messaging //
    ///////////////

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
    /// Returns the last (count) messages before the given index
    /// For non-chat channels, this will return an empty list
    /// </summary>
    public async Task<List<Message>> GetMessagesAsync(long channelId, long index = long.MaxValue, int count = 50)
    {
        if (count > 64)
            count = 64;

        return await _db.Messages.AsNoTracking()
            .Include(x => x.ReplyToMessage)
            .OrderBy(x => x.Id)
            .Where(x => x.Id < index && x.ChannelId == channelId)
            .Select(x => x.ToModel())
            .ToListAsync();
    }

    /// <summary>
    /// Used to post a message 
    /// </summary>
    public async Task<TaskResult<Message>> PostMessageAsync(Message message)
    {
        if (message is null)
            return TaskResult<Message>.FromError("Include message");
        
        var user = await _db.Users.FindAsync(message.AuthorUserId);
        if (user is null)
            return TaskResult<Message>.FromError("Author user not found.");
        
        var channel = await _db.Channels.FindAsync(message.ChannelId);
        if (channel is null)
            return TaskResult<Message>.FromError("Channel not found.");
        
        if (!ISharedChannel.MessageChannelTypes.Contains(channel.ChannelType))
            return TaskResult<Message>.FromError("Channel is not a message channel.");

        Valour.Database.Planet planet = null;
        Valour.Database.PlanetMember member = null;
        
        // Validation specifically for planet messages
        if (channel.PlanetId is not null)
        {
            if (channel.PlanetId != message.PlanetId)
                return TaskResult<Message>.FromError("Invalid planet id. Must match channel's planet id.");

            planet = await _db.Planets.FindAsync(channel.PlanetId);
            if (planet is null)
                return TaskResult<Message>.FromError("Planet not found.");
            
            if (!ISharedChannel.PlanetChannelTypes.Contains(channel.ChannelType))
                return TaskResult<Message>.FromError("Only planet channel messages can have a planet id.");
            
            if (message.AuthorMemberId is null)
                return TaskResult<Message>.FromError("AuthorMemberId is required for planet channel messages.");

            member = await _db.PlanetMembers.FindAsync(message.AuthorMemberId);
            if (member is null)
                return TaskResult<Message>.FromError("Member id does not exist or is invalid for this planet.");
            
            if (member.UserId != message.AuthorUserId)
                return TaskResult<Message>.FromError("Mismatch between member's user id and message author user id.");
        }

        // Handle replies
        if (message.ReplyToId is not null)
        {
            var replyTo = await _db.Messages.FindAsync(message.ReplyToId);
            if (replyTo is null)
                return TaskResult<Message>.FromError("ReplyToId does not exist.");

            // TODO: Technically we could support this in the future
            if (replyTo.ChannelId != channel.Id)
                return TaskResult<Message>.FromError("Cannot reply to a message from another channel.");
            
            message.ReplyTo = replyTo.ToModel();
        }
        
        if (string.IsNullOrEmpty(message.Content) &&
            string.IsNullOrEmpty(message.EmbedData) &&
            string.IsNullOrEmpty(message.AttachmentsData))
            return TaskResult<Message>.FromError("Message must contain content, embed data, or attachments.");
        
        if (message.Fingerprint is null)
            return TaskResult<Message>.FromError("Fingerprint is required. Generating a random UUID is suggested.");
        
        if (message.Content != null && message.Content.Length > 2048)
            return TaskResult<Message>.FromError("Content must be under 2048 chars");


        if (!string.IsNullOrWhiteSpace(message.EmbedData))
        {
            if (message.EmbedData.Length > 65535)
            {
                return TaskResult<Message>.FromError("EmbedData must be under 65535 chars");
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
                            return TaskResult<Message>.FromError($"Error scanning media URI in embed | Page {page.Id} | Item {item.Id}) | URI {at.Location}");
                    }
                }
            }
        }

        if (message.Content is null)
            message.Content = "";

        message.Id = IdManager.Generate();
        
        List<Valour.Api.Models.MessageAttachment> attachments = null;
        // Handle attachments
        if (!string.IsNullOrWhiteSpace(message.AttachmentsData))
        {
            attachments = JsonSerializer.Deserialize<List<Valour.Api.Models.MessageAttachment>>(message.AttachmentsData);
            if (attachments is not null)
            {
                foreach (var at in attachments)
                {
                    var result = MediaUriHelper.ScanMediaUri(at);
                    if (!result.Success)
                        return TaskResult<Message>.FromError($"Error scanning media URI in message attachments | {at.Location}");
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
	        
            var inlineAttachments = await ProxyHandler.GetUrlAttachmentsFromContent(message.Content, _cdnDb, _http);
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
        if (!string.IsNullOrWhiteSpace(message.MentionsData))
        {
            var mentions = JsonSerializer.Deserialize<List<Mention>>(message.MentionsData);
            if (mentions is not null)
            {
                foreach (var mention in mentions.DistinctBy(x => x.TargetId))
                {
                    if (mention.Type == MentionType.PlanetMember)
                    {
                        // Member mentions only work in planet channels
                        if (planet is null)
                            continue;
                        
                        var targetMember = await _db.PlanetMembers.FindAsync(mention.TargetId);
                        if (targetMember is null)
                            return TaskResult<Message>.FromError($"Mentioned member {mention.TargetId} not found.");

                        var content = message.Content.Replace($"«@m-{mention.TargetId}»", $"@{targetMember.Nickname}");

                        Notification notif = new()
                        {
	                        Title = member.Nickname + " in " + planet.Name,
	                        Body = content,
	                        ImageUrl = user.PfpUrl,
	                        UserId = targetMember.UserId,
	                        PlanetId = planet.Id,
	                        ChannelId = channel.Id,
	                        SourceId = message.Id,
	                        Source = NotificationSource.PlanetMemberMention,
	                        ClickUrl = $"/channels/{channel.Id}/{message.Id}"
                        };

                        await _notificationService.AddNotificationAsync(notif);
                    }
                    else if (mention.Type == MentionType.Role)
                    {
                        // Member mentions only work in planet channels
                        if (planet is null)
                            continue;
                        
	                    var targetRole = await _db.PlanetRoles.FindAsync(mention.TargetId);
	                    if (targetRole is null)
                            return TaskResult<Message>.FromError($"Mentioned role {mention.TargetId} not found.");

                        /* Handle in API; service is not for permissions!
	                    if (!targetRole.AnyoneCanMention)
	                    {
		                    if (!await memberService.HasPermissionAsync(member, channel, PlanetPermissions.MentionAll))
			                    return ValourResult.LacksPermission(PlanetPermissions.MentionAll);
	                    }
	                    */
	                    
	                    var content = message.Content.Replace($"«@r-{mention.TargetId}»", $"@{targetRole.Name}");

	                    Notification notif = new()
	                    {
		                    Title = member.Nickname + " in " + planet.Name,
		                    Body = content,
		                    ImageUrl = user.PfpUrl,
		                    PlanetId = planet.Id,
		                    ChannelId = channel.Id,
		                    SourceId = message.Id,
		                    ClickUrl = $"/channels/{channel.Id}/{message.Id}"
	                    };

	                    await _notificationService.AddRoleNotificationAsync(notif, targetRole.Id);
                    }
                    else if (mention.Type == MentionType.User)
                    {
                        // Ensure that the user is a member of the channel
                        if (await _db.ChannelMembers.AnyAsync(x => x.UserId == mention.TargetId && x.ChannelId == message.ChannelId))
                            return TaskResult<Message>.FromError($"Mentioned user {mention.TargetId} is not in this channel. If this a a planet channel, please use Member mentions");
                        
                        var mentionTargetUser = await _db.Users.FindAsync(mention.TargetId);

                        var content = message.Content.Replace($"«@u-{mention.TargetId}»", $"@{mentionTargetUser.Name}");

                        Notification notif = new()
                        {
                            Title = user.Name + " mentioned you in DMs",
                            Body = content,
                            ImageUrl = user.PfpUrl,
                            ClickUrl = $"/channels/{channel.Id}/{message.Id}",
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

                await _coreHub.RelayDirectMessage(message, _nodeService, channelMembers);
            }
            else
            {
                return TaskResult<Message>.FromError("Channel type not implemented!");
            }
        }
        else
        {
            PlanetMessageWorker.AddToQueue(message);
        }
        
        StatWorker.IncreaseMessageCount();

        return TaskResult<Message>.FromData(message);
    }

    /// <summary>
    /// Used to update a message
    /// </summary>
    public async Task<TaskResult<Message>> EditMessageAsync(Message updated)
    {
        if (updated is null)
            return TaskResult<Message>.FromError("Include updated message");

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
                return TaskResult<Message>.FromError("Message not found");
            }
        }
        
        // Things that CANNOT be changed (like my lack of sanity)
        if (old.PlanetId != updated.PlanetId)
            return TaskResult<Message>.FromError("Cannot change PlanetId.");
    
        if (old.ChannelId != updated.ChannelId)
            return TaskResult<Message>.FromError("Cannot change ChannelId.");
    
        if (old.AuthorUserId != updated.AuthorUserId)
            return TaskResult<Message>.FromError("Cannot change AuthorUserId.");
    
        if (old.AuthorMemberId != updated.AuthorMemberId)
            return TaskResult<Message>.FromError("Cannot change AuthorMemberId.");
    
        if (old.TimeSent != updated.TimeSent)
            return TaskResult<Message>.FromError("Cannot change TimeSent.");
        
        // Sanity checks
        if (string.IsNullOrEmpty(updated.Content) &&
            string.IsNullOrEmpty(updated.EmbedData) &&
            string.IsNullOrEmpty(updated.AttachmentsData))
            return TaskResult<Message>.FromError("Updated message cannot be empty");
        
        if (updated.EmbedData != null && updated.EmbedData.Length > 65535)
            return TaskResult<Message>.FromError("EmbedData must be under 65535 chars");
        
        if (updated.Content != null && updated.Content.Length > 2048)
            return TaskResult<Message>.FromError("Content must be under 2048 chars");
        
        List<Valour.Api.Models.MessageAttachment> attachments = null;
        bool inlineChange = false;

        // Handle attachments
        if (!string.IsNullOrWhiteSpace(updated.AttachmentsData))
        {
            attachments = JsonSerializer.Deserialize<List<Valour.Api.Models.MessageAttachment>>(updated.AttachmentsData);
            if (attachments is not null)
            {
                foreach (var at in attachments)
                {
                    var result = MediaUriHelper.ScanMediaUri(at);
                    if (!result.Success)
                        return TaskResult<Message>.FromError(result.Message);
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
            var inlineAttachments = await ProxyHandler.GetUrlAttachmentsFromContent(updated.Content, _cdnDb, _http);
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
        old.MentionsData = updated.MentionsData;
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
                return TaskResult<Message>.FromError("Failed to update message in database.");
            }
        }
        
        // Handle events

        if (updated.PlanetId is not null)
        {
            _coreHub.RelayMessageEdit(updated);
        }
        else
        {
            var channelUserIds = await _db.ChannelMembers
                .AsNoTracking()
                .Where(x => x.ChannelId == updated.ChannelId)
                .Select(x => x.UserId)
                .ToListAsync();
            
            await _coreHub.RelayDirectMessageEdit(updated, _nodeService, channelUserIds);
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
                return TaskResult.FromError("Message not found");
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
                return TaskResult.FromError("Failed to delete message in database.");
            }
        }
        
        if (message.PlanetId is not null)
        {
            _coreHub.NotifyMessageDeletion(message);
        }
        else
        {
            // TODO: Direct message deletion event
        }

        return TaskResult.SuccessResult;
    }
    
    ////////////////
    // Validation //
    ////////////////
    
    /// <summary>
    /// The regex used for name validation
    /// </summary>
    public static readonly Regex NameRegex = new Regex(@"^[a-zA-Z0-9 _-]+$");
    
    /// <summary>
    /// Validates that a given name is allowable
    /// </summary>
    private TaskResult ValidateName(string name)
    {
        if (name.Length > 32)
            return TaskResult.FromError("Channel names must be 32 characters or less.");

        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Validates that a given description is allowable
    /// </summary>
    private TaskResult ValidateDescription(string desc)
    {
        if (desc.Length > 500)
        {
            return TaskResult.FromError("Planet descriptions must be 500 characters or less.");
        }

        return TaskResult.SuccessResult;
    }
    
    
    /// <summary>
    /// Ensures the position is unique
    /// </summary>
    private async Task<bool> HasUniquePosition(Channel channel) =>
        // Ensure position is not already taken
        !await _db.Channels.AnyAsync(x => x.ParentId == channel.ParentId && // Same parent
                                                x.Position == channel.Position && // Same position
                                                x.Id != channel.Id); // Not self
    
    /// <summary>
    /// Ensures the parent and position are valid
    /// </summary>
    private async Task<TaskResult> ValidateParentAndPosition(Channel channel)
    {
        // Logic to check if parent is legitimate
        if (channel.ParentId is not null)
        {
            // Only planet channels can have a parent
            if (channel.PlanetId is null)
            {
                return TaskResult.FromError("Only planet channels can have a parent.");
            }
            
            var parent = await _db.Channels.FirstOrDefaultAsync
            (x => x.Id == channel.ParentId
                  && x.PlanetId == channel.PlanetId // This ensures the result has the same planet id
                  && x.ChannelType == ChannelTypeEnum.PlanetCategory); // Only categories can be parents 

            if (parent is null)
                return TaskResult.FromError( "Parent channel not found");
            
            if (parent.Id == channel.Id)
                return TaskResult.FromError( "A channel cannot be its own parent.");

            // Ensure that the channel is not a descendant of itself
            var loopScan = parent;
            
            while (loopScan.ParentId is not null)
            {
                if (loopScan.ParentId == channel.Id)
                    return TaskResult.FromError( "A channel cannot be a descendant of itself.");
                
                loopScan = await _db.Channels.FirstOrDefaultAsync(x => x.Id == loopScan.ParentId);
            }
        }

        // Auto determine position
        if (channel.Position < 0)
        {
            channel.Position = await _db.Channels.CountAsync(x => x.ParentId == channel.ParentId);
        }
        else
        {
            if (!await HasUniquePosition(channel))
                return TaskResult.FromError( "The position is already taken.");
        }

        return TaskResult.SuccessResult;
    }

    /// <summary>
    /// Validates the planet of a channel
    /// </summary>
    private async Task<TaskResult> ValidatePlanet(Channel channel)
    {
        if (ISharedChannel.PlanetChannelTypes.Contains(channel.ChannelType))
        {
            if (!await _db.Planets.AnyAsync(x => x.Id == channel.PlanetId))
            {
                return TaskResult.FromError("Planet not found.");
            }
        }
        else
        {
            if (channel.PlanetId is not null)
            {
                return TaskResult.FromError("Only planet channel types can have a planet id.");
            } 
        }

        return TaskResult.SuccessResult;
    }
    
    /// <summary>
    /// Common basic validation for channels
    /// </summary>
    private async Task<TaskResult> ValidateChannel(Channel channel)
    {
        var planetValid = await ValidatePlanet(channel);
        if (!planetValid.Success)
            return planetValid;
        
        var nameValid = ValidateName(channel.Name);
        if (!nameValid.Success)
            return nameValid;

        var descValid = ValidateDescription(channel.Description);
        if (!descValid.Success)
            return descValid;

        var positionValid = await ValidateParentAndPosition(channel);
        if (!positionValid.Success)
            return positionValid;

        return TaskResult.SuccessResult;
    }
}