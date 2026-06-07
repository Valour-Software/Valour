using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models;
using Valour.Shared.Queries;

namespace Valour.Server.Api.Dynamic;

public class PlanetReportApi
{
    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/reports/{reportId}")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetAsync(
        long planetId,
        long reportId,
        PlanetMemberService memberService,
        PlanetReportService reportService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ViewReports))
            return ValourResult.LacksPermission(PlanetPermissions.ViewReports);

        var report = await reportService.GetAsync(planetId, reportId);
        if (report is null)
            return ValourResult.NotFound("Report not found.");

        return Results.Json(report);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/reports")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> PostAsync(
        long planetId,
        [FromBody] PlanetReport report,
        UserService userService,
        PlanetMemberService memberService,
        PlanetReportService reportService)
    {
        if (report is null)
            return ValourResult.BadRequest("Include report in body.");

        if (report.PlanetId != planetId)
            return ValourResult.BadRequest("Report planet id does not match route planet id.");

        var user = await userService.GetCurrentUserAsync();
        if (user.Bot)
            return ValourResult.Forbid("Bots cannot send reports.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        report.ReportingUserId = user.Id;

        var result = await reportService.CreateAsync(report);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Created(ISharedPlanetReport.GetIdRoute(planetId, result.Data.Id), result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/reports/query")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> QueryAsync(
        long planetId,
        [FromBody] QueryRequest queryRequest,
        PlanetMemberService memberService,
        PlanetReportService reportService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ViewReports))
            return ValourResult.LacksPermission(PlanetPermissions.ViewReports);

        var reports = await reportService.QueryPlanetReportsAsync(planetId, queryRequest);
        return Results.Json(reports);
    }

    [ValourRoute(HttpVerbs.Get, "api/planets/{planetId}/reports/{reportId}/message")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> GetReportedMessageAsync(
        long planetId,
        long reportId,
        PlanetMemberService memberService,
        PlanetReportService reportService,
        MessageService messageService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ViewReports))
            return ValourResult.LacksPermission(PlanetPermissions.ViewReports);

        var report = await reportService.GetAsync(planetId, reportId);
        if (report is null)
            return ValourResult.NotFound("Report not found.");

        if (!report.MessageId.HasValue)
            return ValourResult.NotFound("Report has no message.");

        var message = await messageService.GetMessageAsync(report.MessageId.Value);
        if (message is null || message.PlanetId != planetId)
            return ValourResult.NotFound("Message not found.");

        return Results.Json(message);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/reports/{reportId}/resolve")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> ResolveAsync(
        long planetId,
        long reportId,
        [FromBody] ResolvePlanetReportRequest request,
        PlanetMemberService memberService,
        PlanetReportService reportService)
    {
        if (request is null)
            return ValourResult.BadRequest("Include resolution in body.");

        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ViewReports))
            return ValourResult.LacksPermission(PlanetPermissions.ViewReports);

        var result = await reportService.ResolveAsync(
            planetId,
            reportId,
            member,
            request.Resolution,
            request.Notes);

        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/reports/{reportId}/kick")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> KickAsync(
        long planetId,
        long reportId,
        [FromBody] PlanetReportActionRequest request,
        PlanetMemberService memberService,
        PlanetReportService reportService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ViewReports))
            return ValourResult.LacksPermission(PlanetPermissions.ViewReports);

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Kick))
            return ValourResult.LacksPermission(PlanetPermissions.Kick);

        var result = await reportService.KickReportedMemberAsync(
            planetId,
            reportId,
            member,
            request?.Notes);

        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [ValourRoute(HttpVerbs.Post, "api/planets/{planetId}/reports/{reportId}/ban")]
    [UserRequired(UserPermissionsEnum.Membership)]
    public static async Task<IResult> BanAsync(
        long planetId,
        long reportId,
        [FromBody] PlanetReportActionRequest request,
        PlanetMemberService memberService,
        PlanetReportService reportService)
    {
        var member = await memberService.GetCurrentAsync(planetId);
        if (member is null)
            return ValourResult.NotPlanetMember();

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.ViewReports))
            return ValourResult.LacksPermission(PlanetPermissions.ViewReports);

        if (!await memberService.HasPermissionAsync(member, PlanetPermissions.Ban))
            return ValourResult.LacksPermission(PlanetPermissions.Ban);

        var result = await reportService.BanReportedUserAsync(
            planetId,
            reportId,
            member,
            request?.Reason,
            request?.Notes,
            request?.TimeExpires);

        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }
}
