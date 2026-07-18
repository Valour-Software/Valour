using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

/// <summary>
/// Bring-your-own-voice endpoints. Config management requires
/// PlanetPermissions.Manage, mirroring <see cref="PlanetStorageApi"/>.
/// </summary>
public class PlanetVoiceApi
{
    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/voice")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> GetRoute(
        long planetId,
        PlanetVoiceService voiceService,
        PlanetMemberService memberService)
    {
        var authResult = await AuthorizeManageAsync(planetId, memberService);
        if (authResult is not null)
            return authResult;

        var info = await voiceService.GetInfoAsync(planetId);
        if (info is null)
            return ValourResult.NotFound("No voice config for this planet.");

        return Results.Json(info);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/voice")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutRoute(
        long planetId,
        [FromBody] PlanetVoiceConfigRequest request,
        PlanetVoiceService voiceService,
        PlanetMemberService memberService)
    {
        var authResult = await AuthorizeManageAsync(planetId, memberService);
        if (authResult is not null)
            return authResult;

        var result = await voiceService.SetConfigAsync(planetId, request);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/voice")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteRoute(
        long planetId,
        PlanetVoiceService voiceService,
        PlanetMemberService memberService)
    {
        var authResult = await AuthorizeManageAsync(planetId, memberService);
        if (authResult is not null)
            return authResult;

        var result = await voiceService.ClearAsync(planetId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok("Voice config removed.");
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/voice/probe")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> ProbeRoute(
        long planetId,
        PlanetVoiceService voiceService,
        PlanetMemberService memberService)
    {
        var authResult = await AuthorizeManageAsync(planetId, memberService);
        if (authResult is not null)
            return authResult;

        var result = await voiceService.ProbeAsync(planetId);
        return Results.Json(result);
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
