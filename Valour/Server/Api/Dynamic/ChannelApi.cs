using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class ChannelApi
{
    [ValourRoute(HttpVerbs.Get, "api/channels/{id}")]
    [UserRequired]
    public static async Task<IResult> GetRouteAsync(
        long id,
        ChannelService channelService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentToken();
        
        var channel = await channelService.GetAsync(id);
        if (channel is null)
            return ValourResult.NotFound<Channel>();

        if (!await channelService.IsMemberAsync(channel, token.UserId))
            return ValourResult.Forbid("You are not a member of this channel");
        
        return Results.Json(channel);
    }
    
    [ValourRoute(HttpVerbs.Post, "api/channels/{channelId}/messages")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostMessageRouteAsync(
        [FromBody] Message message, 
        long channelId,
        ChannelService channelService,
        PlanetMemberService memberService,
        TokenService tokenService,
        PlanetRoleService roleService)
    {
        var token = await tokenService.GetCurrentToken();
        var userId = token.UserId;
        
        if (message is null)
            return ValourResult.BadRequest("Include message in body");
        
        var channel = await channelService.GetAsync(channelId);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");
        
        if (!await channelService.IsMemberAsync(channel, userId))
            return ValourResult.Forbid("You are not a member of this channel");

        if (channel.ChannelType == ChannelTypeEnum.DirectChat)
        {
            if (!token.HasScope(UserPermissions.DirectMessages))
            {
                return ValourResult.Forbid("Token lacks permission to post messages in direct chat channels");
            }
        }
        
        // For planet channels, planet roles and membership are used
        // to determine if the user can post messages and content
        if (channel.PlanetId is not null)
        {
            var member = await memberService.GetCurrentAsync(channel.PlanetId.Value);
            if (member is null)
                return ValourResult.Forbid("You are not a member of the planet this channel belongs to");
            
            // NOTE: We don't have to check View permission because lacking view will
            // cause every other permission check to fail
            
            // Check for message posting permissions
            if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.PostMessages))
                return ValourResult.Forbid("You lack permission to post messages in this channel");
            
            // If the message has attachments...
            if (!string.IsNullOrWhiteSpace(message.AttachmentsData))
            {
                if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.AttachContent))
                    return ValourResult.Forbid("You lack permission to attach content to messages in this channel");
            }

            // If the message has embed data...
            if (!string.IsNullOrWhiteSpace(message.EmbedData))
            {
                if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.Embed))
                    return ValourResult.Forbid("You lack permission to attach embeds to messages in this channel");
            }
            
            // Check mention permissions...
            if (!string.IsNullOrWhiteSpace(message.MentionsData))
            {
                var mentions = JsonSerializer.Deserialize<List<Mention>>(message.MentionsData);
                foreach (var mention in mentions)
                {
                    if (mention.Type == MentionType.Role)
                    {
                        var role = await roleService.GetAsync(mention.TargetId);
                        if (role is null)
                            return ValourResult.BadRequest("Invalid role mention");

                        if (role.AnyoneCanMention)
                            continue;
                        
                        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.MentionAll))
                            return ValourResult.Forbid($"You lack permission to mention the role {role.Name}");
                    }
                }
            }
        }
        
        var result = await channelService.PostMessageAsync(message);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/channels/{channelId}/messages/{messageId}")]
    public static async Task<IResult> EditMessageRouteAsync(
        [FromBody] Message message,
        long channelId,
        long messageId,
        ChannelService channelService,
        UserService userService)
    {
        if (message.Id != messageId)
            return ValourResult.BadRequest("Message id in body does not match message id in route");
        
        if (message.ChannelId != channelId)
            return ValourResult.BadRequest("Channel id in body does not match channel id in route");
        
        var userId = await userService.GetCurrentUserIdAsync();
        if (message.AuthorUserId != userId)
            return ValourResult.Forbid("You are not the author of this message");
        
        var result = await channelService.EditMessageAsync(message);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }

        return Results.Json(result.Data);
    }
}