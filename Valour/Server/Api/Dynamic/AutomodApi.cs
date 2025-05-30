using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models.Staff;
using Valour.Server.Models;
using Valour.Server.Services;
using Valour.Server.Requests;

namespace Valour.Server.Api.Dynamic;

public class AutomodApi
{
    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/automod/triggers/full")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostTriggerFullAsync(
        long planetId,
        [FromBody] CreateAutomodTriggerRequest request,
        PlanetMemberService memberService,
        AutomodService automodService)
    {
        if (request?.Trigger is null)
            return ValourResult.BadRequest("Include trigger in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var trigger = request.Trigger;
        trigger.PlanetId = planetId;
        trigger.MemberAddedBy = member.Id;

        if (request.Actions != null)
        {
            foreach (var act in request.Actions)
            {
                act.PlanetId = planetId;
                act.MemberAddedBy = member.Id;
            }
        }

        var result = await automodService.CreateTriggerWithActionsAsync(trigger, request.Actions ?? new());
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Created($"api/automod/triggers/{result.Data.Id}", result.Data);
    }
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

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/automod/triggers/{triggerId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutTriggerAsync(
        long planetId,
        Guid triggerId,
        [FromBody] AutomodTrigger trigger,
        PlanetMemberService memberService,
        AutomodService automodService)
    {
        if (trigger is null)
            return ValourResult.BadRequest("Include trigger in body.");

        if (trigger.Id != triggerId)
            return ValourResult.BadRequest("Id mismatch with body.");

        if (trigger.PlanetId != planetId)
            return ValourResult.BadRequest("PlanetId mismatch with route.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var result = await automodService.UpdateTriggerAsync(trigger);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }
}
