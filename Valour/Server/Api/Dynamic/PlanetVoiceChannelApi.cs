using IdGen;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using Valour.Server.Database;
using Valour.Server.Requests;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class PlanetVoiceChannelApi
{
    [ValourRoute(HttpVerbs.Get, "api/voicechannels/{id}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetRouteAsync(
        long id,
        PlanetVoiceChannelService voiceChannelService,
        PlanetMemberService memberService)
    {
        // Get the channel
        var channel = await voiceChannelService.GetAsync(id);
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

    [ValourRoute(HttpVerbs.Post, "api/voicechannels")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteAsync(
        [FromBody] PlanetVoiceChannel channel,
        PlanetMemberService memberService,
        PlanetService planetService,
        PlanetVoiceChannelService voiceChannelService,
        PlanetCategoryService categoryService)
    {
        if (channel is null)
            return ValourResult.BadRequest("Include planetvoicechannel in body.");

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

        var result = await voiceChannelService.CreateAsync(channel);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/voicechannels/{result.Data.Id}", result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/voicechannels/detailed")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostRouteWithDetailsAsync(
        [FromBody] CreatePlanetVoiceChannelRequest request,
        PlanetMemberService memberService,
        PlanetService planetService,
        PlanetVoiceChannelService voiceChannelService,
        PlanetCategoryService categoryService)
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
            if (!await memberService.HasPermissionAsync(member, PlanetPermissions.CreateChannels))
                return ValourResult.LacksPermission(PlanetPermissions.CreateChannels);
        }

        var result = await voiceChannelService.CreateDetailedAsync(request, member);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/voicechannels/{result.Data.Id}", result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/voicechannels/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRouteAsync(
        [FromBody] PlanetVoiceChannel channel, 
        long id,
        PlanetMemberService memberService,
        PlanetVoiceChannelService voiceChannelService)
    {
        // Get the category
        var old = await voiceChannelService.GetAsync(id);
        if (old is null)
            return ValourResult.NotFound("Channel not found");

        // Get member
        var member = await memberService.GetCurrentAsync(old.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, old, VoiceChannelPermissions.ManageChannel))
            return ValourResult.LacksPermission(VoiceChannelPermissions.ManageChannel);

        var result = await voiceChannelService.PutAsync(channel);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/voicechannels/{id}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteRouteAsync(
        long id, 
        PlanetVoiceChannelService voiceChannelService,
        PlanetMemberService memberService)
    {
        // Get the channel
        var channel = await voiceChannelService.GetAsync(id);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");

        // Get member
        var member = await memberService.GetCurrentAsync(channel.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, channel, VoiceChannelPermissions.ManageChannel))
            return ValourResult.LacksPermission(VoiceChannelPermissions.ManageChannel);

        var result = await voiceChannelService.DeleteAsync(channel);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Get, "api/voicechannels/{id}/checkperm/{memberId}/{value}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> HasPermissionRouteAsync(
        long id, 
        long memberId, 
        long value,
        PlanetMemberService memberService,
        PlanetVoiceChannelService voiceChannelService)
    {
        // Get the channel
        var channel = await voiceChannelService.GetAsync(id);
        if (channel is null)
            return ValourResult.NotFound("Channel not found");

        // Get member
        var member = await memberService.GetCurrentAsync(channel.PlanetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        // Ensure member has permission to view this channel
        if (!await memberService.HasPermissionAsync(member, channel, VoiceChannelPermissions.View))
            return ValourResult.LacksPermission(VoiceChannelPermissions.View);

        var targetMember = await memberService.GetAsync(memberId);
        if (targetMember is null)
            return ValourResult.NotFound<PlanetMember>();

        var hasPerm = await memberService.HasPermissionAsync(targetMember, channel, new VoiceChannelPermission(value, "", ""));

        return Results.Json(hasPerm);
    }

    [ValourRoute(HttpVerbs.Get, "api/voicechannels/{id}/nodes")]
    [UserRequired]
    public static async Task<IResult> GetNodesRouteAsync(long id, PlanetChannelService service)
    {
        return Results.Json(await service.GetPermNodesAsync(id));
    }
}