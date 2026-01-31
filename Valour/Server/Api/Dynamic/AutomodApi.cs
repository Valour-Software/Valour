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

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/automod/triggers/{triggerId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteTriggerAsync(
        long planetId,
        Guid triggerId,
        PlanetMemberService memberService,
        AutomodService automodService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var trigger = await automodService.GetTriggerAsync(triggerId);
        if (trigger is null)
            return ValourResult.NotFound("Trigger not found.");

        if (trigger.PlanetId != planetId)
            return ValourResult.BadRequest("Trigger does not belong to this planet.");

        var result = await automodService.DeleteTriggerAsync(trigger);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok();
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/automod/triggers/{triggerId}/actions/{actionId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutActionAsync(
        long planetId,
        Guid triggerId,
        Guid actionId,
        [FromBody] AutomodAction action,
        PlanetMemberService memberService,
        AutomodService automodService)
    {
        if (action is null)
            return ValourResult.BadRequest("Include action in body.");

        if (action.Id != actionId)
            return ValourResult.BadRequest("Id mismatch with body.");

        if (action.TriggerId != triggerId)
            return ValourResult.BadRequest("TriggerId mismatch with route.");

        if (action.PlanetId != planetId)
            return ValourResult.BadRequest("PlanetId mismatch with route.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var result = await automodService.UpdateActionAsync(action);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/automod/triggers/{triggerId}/actions/{actionId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteActionAsync(
        long planetId,
        Guid triggerId,
        Guid actionId,
        PlanetMemberService memberService,
        AutomodService automodService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var action = await automodService.GetActionAsync(actionId);
        if (action is null)
            return ValourResult.NotFound("Action not found.");

        if (action.TriggerId != triggerId || action.PlanetId != planetId)
            return ValourResult.BadRequest("Action does not belong to this trigger.");

        var result = await automodService.DeleteActionAsync(action);
        if (!result.Success)
            return ValourResult.Problem(result.Message);

        return Results.Ok();
    }
}
