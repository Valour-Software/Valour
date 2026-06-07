using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;

namespace Valour.Server.Api.Dynamic;

public class PlanetRuleApi
{
    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/rules")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAllAsync(
        long planetId,
        PlanetMemberService memberService,
        PlanetRuleService ruleService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var rules = await ruleService.GetAllAsync(planetId);
        return Results.Json(rules);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/rules/{ruleId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAsync(
        long planetId,
        long ruleId,
        PlanetMemberService memberService,
        PlanetRuleService ruleService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        var rule = await ruleService.GetAsync(planetId, ruleId);
        if (rule is null)
            return ValourResult.NotFound("Rule not found.");

        return Results.Json(rule);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/rules")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PostAsync(
        long planetId,
        [FromBody] PlanetRule rule,
        PlanetMemberService memberService,
        PlanetRuleService ruleService)
    {
        if (rule is null)
            return ValourResult.BadRequest("Include rule in body.");

        if (rule.PlanetId != planetId)
            return ValourResult.BadRequest("Rule planet id does not match route planet id.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var result = await ruleService.CreateAsync(rule);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Created(ISharedPlanetRule.GetIdRoute(planetId, result.Data.Id), result.Data);
    }

    [ValourRoute(HttpVerbs.Put, "api/planets/{planetId}/rules/{ruleId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> PutAsync(
        long planetId,
        long ruleId,
        [FromBody] PlanetRule rule,
        PlanetMemberService memberService,
        PlanetRuleService ruleService)
    {
        if (rule is null)
            return ValourResult.BadRequest("Include rule in body.");

        if (rule.Id != ruleId)
            return ValourResult.BadRequest("Rule id in body does not match route rule id.");

        if (rule.PlanetId != planetId)
            return ValourResult.BadRequest("Rule planet id does not match route planet id.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var result = await ruleService.UpdateAsync(rule);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Delete, "api/planets/{planetId}/rules/{ruleId}")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> DeleteAsync(
        long planetId,
        long ruleId,
        PlanetMemberService memberService,
        PlanetRuleService ruleService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var result = await ruleService.DeleteAsync(planetId, ruleId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/rules/order")]
    [UserRequired(UserPermissionsEnum.PlanetManagement)]
    public static async Task<IResult> SetOrderAsync(
        long planetId,
        [FromBody] long[] order,
        PlanetMemberService memberService,
        PlanetRuleService ruleService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Manage))
            return ValourResult.LacksPermission(PlanetPermissions.Manage);

        var result = await ruleService.SetRuleOrderAsync(planetId, order);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.NoContent();
    }
}
