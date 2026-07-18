using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

/// <summary>
/// Bring-your-own-storage endpoints. Config management requires
/// PlanetPermissions.Manage; upload grants require AttachContent in the
/// target channel.
/// </summary>
public class PlanetStorageApi
{
    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/storage")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> GetRoute(
        long planetId,
        PlanetStorageService storageService,
        PlanetMemberService memberService)
    {
        var authResult = await AuthorizeManageAsync(planetId, memberService);
        if (authResult is not null)
            return authResult;

        var info = await storageService.GetInfoAsync(planetId);
        if (info is null)
            return ValourResult.NotFound("No storage config for this planet.");

        return Results.Json(info);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/storage")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRoute(
        long planetId,
        [FromBody] PlanetStorageConfigRequest request,
        PlanetStorageService storageService,
        PlanetMemberService memberService)
    {
        var authResult = await AuthorizeManageAsync(planetId, memberService);
        if (authResult is not null)
            return authResult;

        var result = await storageService.SetConfigAsync(planetId, request);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/storage")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteRoute(
        long planetId,
        PlanetStorageService storageService,
        PlanetMemberService memberService)
    {
        var authResult = await AuthorizeManageAsync(planetId, memberService);
        if (authResult is not null)
            return authResult;

        var result = await storageService.ClearAsync(planetId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Storage config removed.");
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/storage/probe")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> ProbeRoute(
        long planetId,
        PlanetStorageService storageService,
        PlanetMemberService memberService)
    {
        var authResult = await AuthorizeManageAsync(planetId, memberService);
        if (authResult is not null)
            return authResult;

        var result = await storageService.ProbeAsync(planetId);
        return Results.Json(result);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/storage/grants")]
    [UserRequired(UserPermissionsEnum.Messages)]
    public static async Task<IResult> CreateGrantRoute(
        long planetId,
        [FromBody] PlanetMediaUploadRequest request,
        PlanetStorageService storageService,
        PlanetMemberService memberService,
        ChannelService channelService,
        UserService userService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include request in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var channel = await channelService.GetChannelAsync(planetId, request.ChannelId);
        if (channel is null || channel.PlanetId != planetId)
            return ValourResult.NotFound("Channel not found in this planet.");

        if (!await memberService.HasPermissionAsync(member, channel, ChatChannelPermissions.AttachContent))
            return ValourResult.LacksPermission(ChatChannelPermissions.AttachContent);

        var user = await userService.GetCurrentUserAsync();
        var maxSize = UserSubscriptionTypes.GetMaxUploadBytes(user?.SubscriptionType);

        var result = await storageService.CreateUploadGrantAsync(planetId, member.UserId, request, maxSize);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    private static async Task<IResult> AuthorizeManageAsync(long planetId, PlanetMemberService memberService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        return null;
    }
}
