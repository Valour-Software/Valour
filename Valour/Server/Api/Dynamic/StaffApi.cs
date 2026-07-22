using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;
using Valour.Shared.Models.Staff;
using Valour.Shared.Models;
using Valour.Shared.Queries;

namespace Valour.Server.Api.Dynamic;

public class StaffApi
{
    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Put, "api/staff/platform-banner")]
    public static async Task<IResult> SetPlatformBannerAsync(
        [FromBody] SetPlatformBannerRequest request,
        UserService userService,
        PlatformBannerService bannerService)
    {
        var staff = await userService.GetCurrentUserAsync();
        var result = await bannerService.SetAsync(request, staff.Id);
        return result.Success ? Results.Json(result.Data) : ValourResult.BadRequest(result.Message);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Delete, "api/staff/platform-banner")]
    public static async Task<IResult> ClearPlatformBannerAsync(
        UserService userService,
        PlatformBannerService bannerService)
    {
        var staff = await userService.GetCurrentUserAsync();
        var result = await bannerService.ClearAsync(staff.Id);
        return result.Success ? ValourResult.Ok() : ValourResult.BadRequest(result.Message);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Get, "api/staff/reports")]
    public static async Task<IResult> GetReportsAsync(StaffService staffService)
    {
        var reports = await staffService.GetReportsAsync();
        return Results.Json(reports);
    }
    
    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/reports/query")]
    public static async Task<IResult> QueryReportsAsync(
        [FromBody] QueryRequest queryRequest,
        StaffService staffService)
    {
        var result = await staffService.QueryReportsAsync(queryRequest);
        return Results.Json(result);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Get, "api/staff/reports/{reportId}")]
    public static async Task<IResult> GetReportAsync(
        StaffService staffService,
        string reportId)
    {
        var report = await staffService.GetReportAsync(reportId);
        if (report is null)
            return ValourResult.NotFound("Report not found");

        return Results.Json(report);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/reports/resolve")]
    public static async Task<IResult> ResolveReportAsync(
        UserService userService,
        StaffService staffService,
        [FromBody] ResolveReportRequest request)
    {
        var staffUser = await userService.GetCurrentUserAsync();

        var result = await staffService.ResolveReportAsync(
            request.ReportId,
            request.Resolution,
            request.StaffNotes,
            staffUser.Id);

        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok();
    }
    
    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Put, "api/staff/reports/{reportId}/reviewed/{value}")]
    public static async Task<IResult> SetReportReviewedAsync(StaffService staffService, [FromRoute] string reportId, [FromRoute] bool value)
    {
        var result = await staffService.SetReportReviewedAsync(reportId, value);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return ValourResult.Ok();
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/disable")]
    public static async Task<IResult> DisableUserAsync(UserService userService, StaffService staffService, [FromBody] DisableUserRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return ValourResult.BadRequest("A reason is required.");

        var requestor = await userService.GetCurrentUserAsync();

        var result = await staffService.DisableUserAsync(request.UserId, request.Value, requestor.Id, request.Reason);
        if (!result.Success)
        {
            if (result.Code == 404)
            {
                return ValourResult.NotFound(result.Message);
            }
            else
            {
                return ValourResult.BadRequest(result.Message);
            }
        }

        return ValourResult.Ok();
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/delete")]
    public static async Task<IResult> DeleteUserAsync(UserService userService, StaffService staffService, [FromBody] DeleteUserRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return ValourResult.BadRequest("A reason is required.");

        var requestor = await userService.GetCurrentUserAsync();

        var result = await staffService.DeleteUserAsync(request.UserId, requestor.Id, request.Reason);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok();
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/users/lookup")]
    public static async Task<IResult> LookupUserAsync(UserService userService, StaffService staffService, [FromBody] StaffUserLookupRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Identifier))
            return ValourResult.BadRequest("Please include an identifier.");
        if (string.IsNullOrWhiteSpace(request.Reason))
            return ValourResult.BadRequest("A reason is required for PII lookups.");

        var requestor = await userService.GetCurrentUserAsync();

        var result = await staffService.LookupUserAsync(request.Identifier, requestor.Id, request.Reason);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Get, "api/staff/users/{userId}/bots")]
    public static async Task<IResult> GetOwnedBotsAsync(UserService userService, StaffService staffService, long userId)
    {
        var requestor = await userService.GetCurrentUserAsync();

        var result = await staffService.GetOwnedBotsAsync(userId, requestor.Id, "Owned bot review");
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return Results.Json(result.Data);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/users/resetname")]
    public static async Task<IResult> ResetUsernameAsync(UserService userService, StaffService staffService, [FromBody] StaffResetUsernameRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return ValourResult.BadRequest("A reason is required.");

        var requestor = await userService.GetCurrentUserAsync();

        var result = await staffService.ResetUsernameAsync(request.UserId, requestor.Id, request.Reason);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok(result.Data);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/users/priorname")]
    public static async Task<IResult> SetPriorNameHiddenAsync(UserService userService, StaffService staffService, [FromBody] StaffSetPriorNameHiddenRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return ValourResult.BadRequest("A reason is required.");

        var requestor = await userService.GetCurrentUserAsync();

        var result = await staffService.SetPriorNameHiddenAsync(request.UserId, request.Hidden, requestor.Id, request.Reason);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok();
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/users/passwordreset")]
    public static async Task<IResult> TriggerPasswordResetAsync(HttpContext ctx, UserService userService, StaffService staffService, [FromBody] StaffPasswordResetRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return ValourResult.BadRequest("A reason is required.");

        var requestor = await userService.GetCurrentUserAsync();

        var result = await staffService.TriggerPasswordResetAsync(request.UserId, request.InvalidateSessions, requestor.Id, request.Reason, ctx);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok(result.Message);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/users/mfa/schedule")]
    public static async Task<IResult> ScheduleMfaRemovalAsync(UserService userService, StaffService staffService, [FromBody] StaffMfaRemovalRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return ValourResult.BadRequest("A reason is required.");

        var requestor = await userService.GetCurrentUserAsync();

        var result = await staffService.ScheduleMfaRemovalAsync(request.UserId, requestor.Id, request.Reason);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok(result.Message);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/users/mfa/cancel")]
    public static async Task<IResult> CancelMfaRemovalAsync(UserService userService, StaffService staffService, [FromBody] StaffMfaRemovalRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return ValourResult.BadRequest("A reason is required.");

        var requestor = await userService.GetCurrentUserAsync();

        var result = await staffService.CancelMfaRemovalAsync(request.UserId, requestor.Id, isStaff: true, request.Reason);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok(result.Message);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/audit/query")]
    public static async Task<IResult> QueryAuditLogAsync(
        [FromBody] QueryRequest queryRequest,
        StaffService staffService)
    {
        var result = await staffService.QueryAuditLogAsync(queryRequest);
        return Results.Json(result);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/users/verify")]
    public static async Task<IResult> VerifyUserAsync(
        StaffService staffService,
        [FromBody] VerifyUserRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Identifier))
            return ValourResult.BadRequest("Please include an identifier.");

        var result = await staffService.VerifyUserByIdentifierAsync(request.Identifier);
        if (result.Success)
            return ValourResult.Ok(result.Message);

        return result.Code switch
        {
            404 => ValourResult.NotFound(result.Message),
            409 => Results.Conflict(result.Message),
            _ => ValourResult.BadRequest(result.Message)
        };
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Post, "api/staff/email/send")]
    public static async Task<IResult> SendMassEmailAsync(
        HttpContext ctx,
        UserService userService,
        StaffService staffService,
        [FromBody] SendMassEmailRequest request)
    {
        var requestor = await userService.GetCurrentUserAsync();

        if (requestor.Id != 12200448886571008)
            return ValourResult.Forbid("SuperAdmins only.");

        if (string.IsNullOrWhiteSpace(request.Subject))
            return ValourResult.BadRequest("Subject is required.");

        if (string.IsNullOrWhiteSpace(request.HtmlBody))
            return ValourResult.BadRequest("Body is required.");

        var baseUrl = $"{ctx.Request.Scheme}://{ctx.Request.Host.ToUriComponent()}";

        var result = await staffService.SendMassEmailAsync(request.Subject, request.HtmlBody, baseUrl);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok(result.Message);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Get, "api/staff/messages/{messageId}")]
    public static async Task<IResult> GetMessageAsync(
        StaffService staffService,
        long messageId)
    {
        var msg = await staffService.GetMessageAsync(messageId);
        if (msg is null)
            return ValourResult.NotFound("Message not found");
        
        return Results.Json(msg);
    }
}
