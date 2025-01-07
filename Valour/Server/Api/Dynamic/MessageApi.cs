using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class MessageApi
{
    [ValourRoute(HttpVerbs.Post, "api/messages")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> PostMessageRouteAsync(
        [FromBody] Message message, 
        long channelId,
        MessageService messageService,
        ChannelService channelService,
        PlanetMemberService memberService,
        TokenService tokenService,
        PlanetRoleService roleService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var userId = token.UserId;
        
        if (message is null)
            return ValourResult.BadRequest("Include message in body");
        
        var channel = await channelService.GetPlanetChannelAsync(channelId);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");
        
        if (!await channelService.HasAccessAsync(channel, userId))
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
                if (mentions is not null)
                {
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
                else
                {
                    message.MentionsData = null;
                }
            }
        }
        
        var result = await messageService.PostMessageAsync(message);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }

        return Results.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Put, "api/messages/{id}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> EditMessageRouteAsync(
        [FromBody] Message message,
        long id,
        MessageService messageService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        if (message.PlanetId is null)
        {
            if (!token.HasScope(UserPermissions.DirectMessages))
            {
                return ValourResult.Forbid("Token lacks permission to delete messages in direct chat channels");
            }
        }
        
        if (message.Id != id)
            return ValourResult.BadRequest("Message id in body does not match message id in route");
        
        if (message.AuthorUserId != token.UserId)
            return ValourResult.Forbid("You are not the author of this message");
        
        var result = await messageService.EditMessageAsync(message);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }

        return Results.Json(result.Data);
    }
    
    [ValourRoute(HttpVerbs.Delete, "api/messages/{id}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> DeleteMessageRouteAsync(
        long id,
        MessageService messageService,
        ChannelService channelService,
        TokenService tokenService,
        PlanetMemberService memberService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        var message = await messageService.GetMessageNoReplyAsync(id);
        
        // Direct messages require stronger token perms
        if (message.PlanetId is null)
        {
            if (!token.HasScope(UserPermissions.DirectMessages))
            {
                return ValourResult.Forbid("Token lacks permission to delete messages in direct chat channels");
            }
        }
        
        // Determine permissions

        // First off, if the user is the sender they can always delete
        if (message.AuthorUserId != token.UserId)
        {
            if (message.PlanetId is null)
                return ValourResult.Forbid("Only the sender can delete these messages");
            
            // Otherwise, we check planet channel permissions
            var member = await memberService.GetCurrentAsync(message.PlanetId.Value);
            if (member is null)
                return ValourResult.Forbid("You are not a member of the planet this channel belongs to");

            var channel = await channelService.GetPlanetChannelAsync(message.ChannelId);

            if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.ManageMessages))
            {
                return ValourResult.Forbid("You do not have permission to manage messages in this channel");
            }
        }
        
        var result = await messageService.DeleteMessageAsync(message.Id);
        if (!result.Success)
        {
            return ValourResult.BadRequest(result.Message);
        }
        
        return Results.Ok();
    }
    
    
    [ValourRoute(HttpVerbs.Get, "api/messages/{id}")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> GetMessageAsync(
        long id,
        MessageService messageService,
        ChannelService channelService,
        TokenService tokenService)
    {
        var token = await tokenService.GetCurrentTokenAsync();
        
        var message = await messageService.GetMessageAsync(id);
        if (message is null)
            return ValourResult.NotFound<Message>();
        
        var channel = await channelService.GetPlanetChannelAsync(message.ChannelId);
        if (!await channelService.HasAccessAsync(channel, token.UserId))
            return ValourResult.Forbid("You are not a member of this channel");
        
        if (message.PlanetId is null)
        {
            if (!token.HasScope(UserPermissions.DirectMessages))
            {
                return ValourResult.Forbid("Token lacks permission to delete messages in this channel");
            }
        }
        
        return Results.Json(message);
    }
}