using Valour.Shared.Authorization;
using Microsoft.AspNetCore.Mvc;
using Valour.Server.Database;
using Valour.Server.Workers;
using Valour.Server.Cdn;
using System.Text.Json;
using Valour.Server.Config;
using Valour.Shared.Models;
using Valour.Server.Requests;
using Valour.Api.Models.Messages.Embeds.Items;
using Valour.Api.Models.Messages.Embeds;

namespace Valour.Server.Api.Dynamic;

public class PlanetChatChannelApi
{
	[ValourRoute(HttpVerbs.Get, "api/chatchannels/{id}")]
	[UserRequired(UserPermissionsEnum.Membership)]
	public static async Task<IResult> GetRouteAsync(
		long id,
		PlanetChatChannelService service,
		PlanetMemberService memberService)
	{
		// Get the channel
		var channel = await service.GetAsync(id);
		if (channel is null)
			return ValourResult.NotFound("Channel not found");

		// Get member
		var member = await memberService.GetCurrentAsync(channel.PlanetId);
		if (member is null)
			return ValourResult.NotPlanetMember();

		// Ensure member has permission to view this channel
		if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View))
			return ValourResult.LacksPermission(ChatChannelPermissions.View);

		// Return json
		return Results.Json(channel);
	}

	[ValourRoute(HttpVerbs.Post, "api/chatchannels")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetChatChannel channel,
		PlanetChatChannelService service,
		PlanetCategoryService categoryService,
		PlanetMemberService memberService,
        PlanetService planetService)
    {
        if (channel is null)
            return ValourResult.BadRequest("Include planetchatchannel in body.");

		// Get member
		var member = await memberService.GetCurrentAsync(channel.PlanetId);
		if (member is null)
			return ValourResult.NotPlanetMember();

        if (channel.ParentId is not null)
        {
			// Ensure user has permission for parent category management
			var parent = await categoryService.GetAsync((long)channel.ParentId);
			if (!await memberService.HasPermissionAsync(member, parent, CategoryPermissions.ManageCategory))
				return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
		}
		else
        {
			if (!await memberService.HasPermissionAsync(member, PlanetPermissions.CreateChannels))
				return ValourResult.LacksPermission(PlanetPermissions.CreateChannels);
		}

		var result = await service.CreateAsync(channel);
		if (!result.Success)
			return ValourResult.Problem(result.Message);

		return Results.Created($"api/chatchannels/{result.Data.Id}", result.Data);
	}

    [ValourRoute(HttpVerbs.Post, "api/chatchannels/detailed")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteWithDetailsAsync(
        [FromBody] CreatePlanetChatChannelRequest request,
        PlanetChatChannelService channelService,
        PlanetCategoryService categoryService,
        PlanetMemberService memberService,
        PlanetService planetService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include CreatePlanetChatChannelRequest in body.");

        if (request.Channel is null)
            return ValourResult.BadRequest("Include Channel in CreatePlanetChatChannelRequest.");

        // Get member
        var member = await memberService.GetCurrentAsync(request.Channel.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var planet = await planetService.GetAsync(request.Channel.PlanetId);

        if (request.Channel.ParentId is not null)
        {
            // Ensure user has permission for parent category management
            var parent = await categoryService.GetAsync((long)request.Channel.ParentId);
            if (!await memberService.HasPermissionAsync(member, parent, CategoryPermissions.ManageCategory))
                return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
        }
        else
        {
            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.CreateChannels))
                return ValourResult.LacksPermission(PlanetPermissions.CreateChannels);
        }

        var result = await channelService.CreateDetailedAsync(request, member);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/chatchannels/{result.Data.Id}", result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/chatchannels/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetChatChannel channel,
        long id,
        PlanetMemberService memberService,
        PlanetChatChannelService channelService,
        PlanetCategoryService categoryService)
    {
        // Get the channel
        var old = await channelService.GetAsync(id);
        if (old is null)
            return ValourResult.NotFound("Channel not found");

        // Get member
        var member = await memberService.GetCurrentAsync(old.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, old, ChatChannelPermissions.ManageChannel))
            return ValourResult.LacksPermission(ChatChannelPermissions.ManageChannel);
        
        // Channel parent is being changed
        if (old.ParentId != channel.ParentId)
        {
	        return ValourResult.BadRequest("Use the order endpoint in the parent category to update parent.");
        }
        // Channel is being moved
        else if (old.Position != channel.Position)
        {
	        return ValourResult.BadRequest("Use the order endpoint in the parent category to change position.");
        }

        var result = await channelService.UpdateAsync(channel);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/chatchannels/{id}")]
	[UserRequired(UserPermissionsEnum.PlanetManagement)]
	public static async Task<IResult> DeleteRouteAsync(
        long id,
        PlanetChatChannelService channelService,
		PlanetMemberService memberService)
    {
        // Get the channel
        var channel = await channelService.GetAsync(id);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");

        // Get member
        var member = await memberService.GetCurrentAsync(channel.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.ManageChannel))
			return ValourResult.LacksPermission(ChatChannelPermissions.ManageChannel);

		var result = await channelService.DeleteAsync(channel);
		if (!result.Success)
			return ValourResult.BadRequest(result.Message);
		
		return Results.NoContent();
	}

    [ValourRoute(HttpVerbs.Get, "api/chatchannels/{id}/checkperm/{memberId}/{value}")]
    [UserRequired(UserPermissionsEnum.View)]
    public static async Task<IResult> HasPermissionRouteAsync(
        long id, 
        long memberId, 
        long value,
        PlanetMemberService memberService,
        PlanetChatChannelService channelService)
    {
        // Get the channel
        var channel = await channelService.GetAsync(id);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");

        // Get member
        var member = await memberService.GetCurrentAsync(channel.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        // Ensure member has permission to view this channel
        if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View))
            return ValourResult.LacksPermission(ChatChannelPermissions.View);

        var targetMember = await memberService.GetAsync(memberId);
        if (targetMember is null)
            return ValourResult.NotFound<PlanetMember>();

        var hasPerm = await memberService.HasPermissionAsync(targetMember, channel, new ChatChannelPermission(value, "", ""));

        return Results.Json(hasPerm);
    }

    // Message routes

    [ValourRoute(HttpVerbs.Get, "api/chatchannels/{id}/message/{messageId}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetMessageRouteAsync(
        long id, 
        long messageId,
		PlanetChatChannelService channelService,
		PlanetMemberService memberService,
        PlanetMessageService messageService)
    {
		// Get the channel
		var channel = await channelService.GetAsync(id);
		if (channel is null)
			return ValourResult.NotFound("Channel not found");

		// Get member
		var member = await memberService.GetCurrentAsync(channel.PlanetId);
		if (member is null)
			return ValourResult.NotPlanetMember();

		if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View))
			return ValourResult.LacksPermission(ChatChannelPermissions.View);

		if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.ViewMessages))
			return ValourResult.LacksPermission(ChatChannelPermissions.ViewMessages);

        var message = await messageService.GetAsync(messageId);

        if (message is null)
            return ValourResult.NotFound("Message not found.");

        return Results.Json(message);
    }

    [ValourRoute(HttpVerbs.Get, "api/chatchannels/{id}/messages")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetMessagesRouteAsync(
        long id,
		PlanetChatChannelService channelService,
		PlanetMemberService memberService,
		[FromQuery] long index = long.MaxValue, 
        [FromQuery] int count = 10)
	{
		// Get the channel
		var channel = await channelService.GetAsync(id);
		if (channel is null)
			return ValourResult.NotFound("Channel not found");

		// Get member
		var member = await memberService.GetCurrentAsync(channel.PlanetId);
		if (member is null)
			return ValourResult.NotPlanetMember();

		if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View))
			return ValourResult.LacksPermission(ChatChannelPermissions.View);

        if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.ViewMessages))
            return ValourResult.LacksPermission(ChatChannelPermissions.ViewMessages);

        if (count > 64)
            return Results.BadRequest("Maximum count is 64.");

        return Results.Json(await channelService.GetMessagesAsync(channel, count, index));
    }

    [ValourRoute(HttpVerbs.Post, "api/chatchannels/{id}/messages")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostMessageRouteAsync(
        [FromBody] Message message,
        long id,
        HttpClient client, 
        ValourDB valourDb, 
        CdnDb db,
		PlanetChatChannelService channelService,
		PlanetMemberService memberService,
        PlanetRoleService roleService,
		UserService userService,
        PlanetService planetService,
        NodeService nodeService,
        NotificationService notificationService)
    {
	    if (NodeConfig.Instance.LogInfo)
		    Console.WriteLine($"Message posted for channel {id}");
	    
	    if (!await nodeService.IsPlanetHostedLocally(message.PlanetId))
		    return ValourResult.BadRequest("Planet belongs to another node.");
	    
		// Get the channel
		var channel = await channelService.GetAsync(id);
		if (channel is null)
			return ValourResult.NotFound("Channel not found");

		// Get member
		var member = await memberService.GetCurrentAsync(channel.PlanetId);
		if (member is null)
			return ValourResult.NotPlanetMember();

		if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View))
			return ValourResult.LacksPermission(ChatChannelPermissions.View);

		if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.PostMessages))
			return ValourResult.LacksPermission(ChatChannelPermissions.PostMessages);

		if (message is null)
            return Results.BadRequest("Include message in body.");

		if (message.ReplyToId is not null)
		{
			var replyTo = await valourDb.PlanetMessages.FindAsync(message.ReplyToId);
			if (replyTo is null)
				return ValourResult.NotFound("ReplyTo message not found.");
			
			if (replyTo.ChannelId != message.ChannelId)
				return ValourResult.BadRequest("Cannot reply to message in another channel.");
		}

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

        if (message.EmbedData is not null)
        {
            // load embed to check for anti-valour propaganda (incorrect media URIs)
            var embed = JsonSerializer.Deserialize<Embed>(message.EmbedData);
            foreach (var page in embed.Pages)
            {
                foreach (var item in page.GetAllItems())
                {
                    if (item.ItemType == Valour.Api.Models.Messages.Embeds.Items.EmbedItemType.Media)
                    {
                        var at = ((EmbedMediaItem)item).Attachment;
                        var result = MediaUriHelper.ScanMediaUri(at);
                        if (!result.Success)
                            return Results.BadRequest(result.Message);
                    }
                }
            }
        }

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
                        return Results.BadRequest(result.Message);
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
	        
	        var inlineAttachments = await ProxyHandler.GetUrlAttachmentsFromContent(message.Content, db, client);
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
                foreach (var mention in mentions.DistinctBy(x => x.TargetId))
                {
	                var sendingUser = await userService.GetAsync(member.UserId);
	                var planet = await planetService.GetAsync(message.PlanetId);
	                
                    if (mention.Type == MentionType.Member)
                    {
                        var targetMember = await memberService.GetAsync(mention.TargetId);
                        if (targetMember is null)
	                        return ValourResult.NotFound($"Mentioned user {mention.TargetId} not found.");

                        var content = message.Content.Replace($"«@m-{mention.TargetId}»", $"@{targetMember.Nickname}");

                        Notification notif = new()
                        {
	                        Title = member.Nickname + " in " + planet.Name,
	                        Body = content,
	                        ImageUrl = sendingUser.PfpUrl,
	                        UserId = targetMember.UserId,
	                        PlanetId = planet.Id,
	                        ChannelId = channel.Id,
	                        SourceId = message.Id,
	                        Source = NotificationSource.PlanetMemberMention,
	                        ClickUrl = $"/planetchannels/{channel.PlanetId}/{channel.Id}/{message.Id}"
                        };

                        await notificationService.AddNotificationAsync(notif);
                    }
                    else if (mention.Type == MentionType.Role)
                    {
	                    var targetRole = await roleService.GetAsync(mention.TargetId);
	                    if (targetRole is null)
		                    return ValourResult.NotFound($"Mentioned role {mention.TargetId} not found.");

	                    if (!targetRole.AnyoneCanMention)
	                    {
		                    if (!await memberService.HasPermissionAsync(member, channel, PlanetPermissions.MentionAll))
			                    return ValourResult.LacksPermission(PlanetPermissions.MentionAll);
	                    }
	                    
	                    var content = message.Content.Replace($"«@r-{mention.TargetId}»", $"@{targetRole.Name}");

	                    Notification notif = new()
	                    {
		                    Title = member.Nickname + " in " + planet.Name,
		                    Body = content,
		                    ImageUrl = sendingUser.PfpUrl,
		                    PlanetId = planet.Id,
		                    ChannelId = channel.Id,
		                    SourceId = message.Id,
		                    ClickUrl = $"/planetchannels/{channel.PlanetId}/{channel.Id}/{message.Id}"
	                    };

	                    await notificationService.AddRoleNotificationAsync(notif, targetRole.Id);
                    }
                }
            }
        }

        PlanetMessageWorker.AddToQueue(message);

        StatWorker.IncreaseMessageCount();

        return Results.Ok();
    }
    
    [ValourRoute(HttpVerbs.Put, "api/chatchannels/{id}/messages")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> EditMessageRouteAsync(
        [FromBody] Message editedMessage,
        long id,
        HttpClient client, 
        ValourDB valourDb, 
        CdnDb db,
		PlanetChatChannelService channelService,
		PlanetMemberService memberService,
		UserService userService,
        NodeService nodeService,
        CoreHubService coreHub)
    {
	    if (editedMessage is null)
		    return Results.BadRequest("Include message in body.");
	    
	    if (NodeConfig.Instance.LogInfo)
		    Console.WriteLine($"Message edit request for channel {id}");
	    
	    var currentUser = await userService.GetCurrentUserAsync();

	    if (!await nodeService.IsPlanetHostedLocally(editedMessage.PlanetId))
		    return ValourResult.BadRequest("Planet belongs to another node.");
	    
	    // Sanity checks
	    if (string.IsNullOrEmpty(editedMessage.Content) &&
	        string.IsNullOrEmpty(editedMessage.EmbedData) &&
	        string.IsNullOrEmpty(editedMessage.AttachmentsData))
		    return Results.BadRequest("Message content cannot be null");

        if (editedMessage.EmbedData != null && editedMessage.EmbedData.Length > 65535)
            return Results.BadRequest("EmbedData must be under 65535 chars");
        
	    if (editedMessage.Content != null && editedMessage.Content.Length > 2048)
		    return Results.BadRequest("Content must be under 2048 chars");
	    
	    // Get the channel
	    var channel = await channelService.GetAsync(id);
	    if (channel is null)
		    return ValourResult.NotFound("Channel not found");

	    // Get member
	    var member = await memberService.GetCurrentAsync(channel.PlanetId);
	    if (member is null)
		    return ValourResult.NotPlanetMember();

	    if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View))
		    return ValourResult.LacksPermission(ChatChannelPermissions.View);

	    if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.PostMessages))
		    return ValourResult.LacksPermission(ChatChannelPermissions.PostMessages);
	    
	    List<Valour.Api.Models.MessageAttachment> attachments = null;
	    bool inlineChange = false;

	    // Handle attachments
        if (editedMessage.AttachmentsData is not null)
        {
            attachments = JsonSerializer.Deserialize<List<Valour.Api.Models.MessageAttachment>>(editedMessage.AttachmentsData);
            if (attachments is not null)
            {
                foreach (var at in attachments)
                {
	                var result = MediaUriHelper.ScanMediaUri(at);
	                if (!result.Success)
		                return Results.BadRequest(result.Message);
                }
                
                // Remove old inline attachments
                var n = attachments.RemoveAll(x => x.Inline);
                if (n > 0)
	                inlineChange = true;
            }
        }
        
        // Handle new inline attachments
        if (!string.IsNullOrWhiteSpace(editedMessage.Content))
        {
	        var inlineAttachments = await ProxyHandler.GetUrlAttachmentsFromContent(editedMessage.Content, db, client);
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
        
        // yeah ok so there's a chance the message has not yet hit the database which makes this painful
        Message stagedMessage = PlanetMessageWorker.GetStagedMessage(editedMessage.Id);
        if (stagedMessage is null)
        {
	        Valour.Database.PlanetMessage dbMessage = await valourDb.PlanetMessages.FindAsync(editedMessage.Id);
	        if (dbMessage is null)
		        return ValourResult.NotFound("Message not found");

	        if (currentUser.Id != dbMessage.AuthorUserId)
		        return ValourResult.Forbid("Only message author can edit a message");

	        dbMessage.Content = editedMessage.Content;
	        dbMessage.AttachmentsData = editedMessage.AttachmentsData;
	        dbMessage.MentionsData = editedMessage.MentionsData;
	        dbMessage.EditedTime = DateTime.UtcNow;
            dbMessage.EmbedData = editedMessage.EmbedData;

	        try
	        {
		        await valourDb.SaveChangesAsync();
	        }
	        catch (Exception)
	        {
		        return ValourResult.Problem("Failed to save edited message");
	        }
	        
	        coreHub.RelayMessageEdit(dbMessage.ToModel());
        }
        else
        {
	        if (currentUser.Id != stagedMessage.AuthorUserId)
		        return ValourResult.Forbid("Only message author can edit a message");
			
	        // this is effective immediately so we can just edit the staged message before
	        // it hits the database
	        stagedMessage.Content = editedMessage.Content;
	        stagedMessage.AttachmentsData = editedMessage.AttachmentsData;
	        stagedMessage.MentionsData = editedMessage.MentionsData;
	        stagedMessage.EditedTime = DateTime.UtcNow;
            stagedMessage.EmbedData = editedMessage.EmbedData;

	        coreHub.RelayMessageEdit(stagedMessage);
        }
        
        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Delete, "api/chatchannels/{id}/messages/{message_id}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> DeleteMessageRouteAsync(
        long id, 
        long message_id,
		PlanetChatChannelService channelService,
		PlanetMemberService memberService,
        PlanetMessageService messageService)
    {
		// Get the channel
		var channel = await channelService.GetAsync(id);
		if (channel is null)
			return ValourResult.NotFound("Channel not found");

		// Get member
		var member = await memberService.GetCurrentAsync(channel.PlanetId);
		if (member is null)
			return ValourResult.NotPlanetMember();

		if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View))
			return ValourResult.LacksPermission(ChatChannelPermissions.View);

		if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.ViewMessages))
			return ValourResult.LacksPermission(ChatChannelPermissions.ViewMessages);

        return await messageService.DeleteAsync(channel, member, message_id);
    }

    [ValourRoute(HttpVerbs.Post, "api/chatchannels/{id}/typing")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostTypingAsync(
        long id, 
        CurrentlyTypingService typingService,
		PlanetChatChannelService channelService,
		PlanetMemberService memberService)
	{
		// Get the channel
		var channel = await channelService.GetAsync(id);
		if (channel is null)
			return ValourResult.NotFound("Channel not found");

		// Get member
		var member = await memberService.GetCurrentAsync(channel.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.View))
			return ValourResult.LacksPermission(ChatChannelPermissions.View);

		if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.PostMessages))
			return ValourResult.LacksPermission(ChatChannelPermissions.PostMessages);

		typingService.AddCurrentlyTyping(id, member.UserId);
        
        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Get, "api/chatchannels/{id}/nodes")]
    [UserRequired]
    public static async Task<IResult> GetNodesRouteAsync(long id, PlanetChannelService service)
    {
        return Results.Json(await service.GetPermNodesAsync(id));
    }
}