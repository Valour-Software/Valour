using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models.Staff;
using Valour.Server.Models;
using Valour.Server.Services;

namespace Valour.Server.Api.Dynamic;

public class AutomodApi
{
    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/automod/triggers")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostTriggerAsync(
        long planetId,
        [FromBody] AutomodTrigger trigger,
        PlanetMemberService memberService,
        AutomodService automodService)
    {
        if (trigger is null)
            return ValourResult.BadRequest("Include trigger in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        trigger.PlanetId = planetId;
        trigger.MemberAddedBy = member.Id;
        var result = await automodService.CreateTriggerAsync(trigger);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/automod/triggers/{result.Data.Id}", result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/automod/triggers/{triggerId}/actions")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostActionAsync(
        long planetId,
        Guid triggerId,
        [FromBody] AutomodAction action,
        PlanetMemberService memberService,
        AutomodService automodService)
    {
        if (action is null)
            return ValourResult.BadRequest("Include action in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        action.TriggerId = triggerId;
        action.PlanetId = planetId;
        action.MemberAddedBy = member.Id;

        var result = await automodService.CreateActionAsync(action);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/automod/actions/{result.Data.Id}", result.Data);
    }
}
