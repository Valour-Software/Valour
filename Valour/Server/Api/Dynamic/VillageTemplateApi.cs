using Microsoft.AspNetCore.Mvc;
using Valour.Server.Services.Villages;
using Valour.Shared.Authorization;
using Valour.Shared.Villages;

namespace Valour.Server.Api.Dynamic;

public class VillageTemplateApi
{
    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Get, "api/staff/village-template")]
    public static async Task<IResult> GetAsync(VillageTemplateService templates) => Results.Json(await templates.GetStatusAsync());

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Put, "api/staff/village-template/draft")]
    public static async Task<IResult> SelectDraftAsync([FromBody] VillageTemplateDraftRequest request,
        VillageTemplateService templates, UserService users, PlanetService planets, PlanetMemberService members)
    {
        if (!await CanManageAsync(request.PlanetId, planets, members))
            return ValourResult.Forbid("Choose an enabled village you have permission to manage.");
        var staff = await users.GetCurrentUserAsync();
        var result = await templates.SelectDraftAsync(request, staff.Id);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/village-template/publish")]
    public static async Task<IResult> PublishAsync([FromBody] VillageTemplatePublishRequest request,
        VillageTemplateService templates, UserService users, PlanetService planets, PlanetMemberService members)
    {
        if (!await CanManageAsync(request.DraftPlanetId, planets, members))
            return ValourResult.Forbid("You need permission to manage the draft village before publishing it.");
        var staff = await users.GetCurrentUserAsync();
        try
        {
            var result = await templates.PublishAsync(request, staff.Id);
            return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
        }
        catch (DbUpdateConcurrencyException)
        {
            return ValourResult.BadRequest("Another staff member published first. Refresh and review the latest revision.");
        }
        catch (Npgsql.PostgresException ex) when (ex.SqlState == "40001")
        {
            return ValourResult.BadRequest("The draft changed during publication. Refresh and try again.");
        }
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/village-template/reset/{planetId}")]
    public static async Task<IResult> ResetAsync(long planetId, VillageTemplateService templates,
        VillageWorldService worlds, UserService users, PlanetService planets, PlanetMemberService members)
    {
        if (!await CanManageAsync(planetId, planets, members))
            return ValourResult.Forbid("Choose an enabled village you have permission to manage.");
        if ((await templates.GetStatusAsync()).DraftPlanetId == planetId)
            return ValourResult.BadRequest("Choose another draft before resetting the template workshop.");
        var staff = await users.GetCurrentUserAsync();
        await worlds.ResetToTemplateAsync(await planets.GetAsync(planetId), await planets.GetAllChannelsAsync(planetId), staff.Id);
        return ValourResult.Ok();
    }

    private static async Task<bool> CanManageAsync(long planetId, PlanetService planets, PlanetMemberService members)
    {
        var member = await members.GetCurrentAsync(planetId);
        return member is not null && (await planets.GetAsync(planetId))?.EnableVillage == true &&
               await members.HasPermissionAsync(member, PlanetPermissions.ManageVillage);
    }
}
