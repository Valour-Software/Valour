using Valour.Shared.Authorization;
using Microsoft.AspNetCore.Mvc;
using IdGen;
using Valour.Server.Database;
using Pipelines.Sockets.Unofficial.Arenas;
using Valour.Server.Workers;
using Valour.Server.Cdn;
using System.Text.Json;
using Valour.Shared.Models;
using Valour.Server.Notifications;
using Valour.Server.Services;
using Valour.Server.Requests;

namespace Valour.Server.Api.Dynamic;

public class PlanetChatChannelApi
{
	[ValourRoute(HttpVerbs.Get, "api/planetchatchannels/{id}")]
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

	[ValourRoute(HttpVerbs.Post, "api/planetchatchannels")]
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

		channel.Id = IdManager.Generate();

		// Get member
		var member = await memberService.GetCurrentAsync(channel.PlanetId);
		if (member is null)
			return ValourResult.NotPlanetMember();

        var planet = await planetService.GetAsync(channel.PlanetId);

        if (channel.ParentId is not null)
        {
			// Ensure user has permission for parent category management
			var parent = await categoryService.GetAsync((long)channel.ParentId);
			if (!await memberService.HasPermissionAsync(member, parent, CategoryPermissions.ManageCategory))
				return ValourResult.LacksPermission(CategoryPermissions.ManageCategory);
		}
		else
        {
			if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageCategories))
				return ValourResult.LacksPermission(PlanetPermissions.ManageCategories);
		}

		var result = await service.CreateAsync(channel);
		if (!result.Success)
			return ValourResult.Problem(result.Message);

		return Results.Created($"api/planetchatchannels/{channel.Id}", channel);
	}

    [ValourRoute(HttpVerbs.Post, "api/planetchatchannels/detailed")]
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

        request.Channel.Id = IdManager.Generate();

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
            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageCategories))
                return ValourResult.LacksPermission(PlanetPermissions.ManageCategories);
        }

        return await channelService.CreateDetailedAsync(request, member);
    }

    [ValourRoute(HttpVerbs.Put, "api/planetchatchannels/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetChatChannel channel,
        long id,
        PlanetMemberService memberService,
        PlanetChatChannelService channelService)
    {
        // Get the category
        var old = await channelService.GetAsync(id);
        if (old is null)
            return ValourResult.NotFound("Category not found");

        // Get member
        var member = await memberService.GetCurrentAsync(old.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageCategories))
            return ValourResult.LacksPermission(PlanetPermissions.ManageCategories);

        if (!await memberService.HasPermissionAsync(member, old, ChatChannelPermissions.ManageChannel))
            return ValourResult.LacksPermission(ChatChannelPermissions.ManageChannel);

        return await channelService.PutAsync(old, channel);
    }

	[ValourRoute(HttpVerbs.Delete, "api/planetchatchannels/{id}")]
	[UserRequired(UserPermissionsEnum.PlanetManagement)]
	public static async Task<IResult> DeleteRouteAsync(
        long id,
        PlanetChatChannelService channelService,
		PlanetMemberService memberService,
		PlanetService planetService,
		CoreHubService hubService)
    {
        // Get the channel
        var channel = await channelService.GetAsync(id);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");

        // Get member
        var member = await memberService.GetCurrentAsync(channel.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

		if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ManageCategories))
			return ValourResult.LacksPermission(PlanetPermissions.ManageCategories);

        if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.ManageChannel))
			return ValourResult.LacksPermission(ChatChannelPermissions.ManageChannel);

		await channelService.DeleteAsync(channel);

		return Results.NoContent();
	}

    [ValourRoute(HttpVerbs.Get, "api/planetchatchannels/{id}/checkperm/{memberId}/{value}")]
    [UserRequired(UserPermissionsEnum.View)]
    public static async Task<IResult> HasPermissionRouteAsync(
        long id, 
        long memberId, 
        long value,
        PlanetMemberService memberService,
        PlanetChatChannelService channelService,
        ValourDB db)
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

    [ValourRoute(HttpVerbs.Get, "api/planetchatchannels/{id}/message/{messageId}")]
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

    [ValourRoute(HttpVerbs.Get, "api/planetchatchannels/{id}/messages")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetMessagesRouteAsync(
        long id,
        ValourDB db,
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

    [ValourRoute(HttpVerbs.Post, "api/planetchatchannels/{id}/messages")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostMessageRouteAsync(
        [FromBody] PlanetMessage message,
        long id,
        HttpClient client, 
        ValourDB valourDb, 
        CdnDb db,
        UserOnlineService onlineService,
		PlanetChatChannelService channelService,
		PlanetMemberService memberService,
		UserService userService,
        PlanetService planetService)
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
                        var targetMember = await memberService.GetAsync(mention.TargetId);
                        var sendingUser = await userService.GetAsync(member.UserId);
                        var planet = await planetService.GetAsync(message.PlanetId);

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

    [ValourRoute(HttpVerbs.Delete, "api/planetchatchannels/{id}/messages/{message_id}")]
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

    [ValourRoute(HttpVerbs.Post, "api/planetchatchannels/{id}/typing")]
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
}