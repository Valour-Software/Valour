using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Models.Staff;
using Valour.Shared.Queries;

namespace Valour.Server.Api.Dynamic;

public class StaffApi
{
    [StaffRequired]
    [ValourRoute(HttpVerbs.Get, "api/staff/reports")]
    public static async Task<IResult> GetReportsAsync(StaffService staffService)
    {
        var reports = await staffService.GetReportsAsync();
        return Results.Json(reports);
    }
    
    [StaffRequired]
    [ValourRoute(HttpVerbs.Post, "api/staff/reports/query")]
    public static async Task<IResult> QueryReportsAsync(
        [FromBody] QueryRequest queryRequest,
        StaffService staffService)
    {
        var result = await staffService.QueryReportsAsync(queryRequest);
        return Results.Json(result);
    }

    [StaffRequired]
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
    [ValourRoute(HttpVerbs.Put, "api/staff/reports/{reportId}/reviewed/{value}")]
    public static async Task<IResult> SetReportReviewedAsync(StaffService staffService, [FromQuery] string reportId, [FromQuery] bool value)
    {
        var result = await staffService.SetReportReviewedAsync(reportId, value);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return ValourResult.Ok();
    }

    [StaffRequired]
    [ValourRoute(HttpVerbs.Post, "api/staff/disable")]
    public static async Task<IResult> DisableUserAsync(StaffService staffService, [FromBody] DisableUserRequest request)
    {
        var result = await staffService.DisableUserAsync(request.UserId, request.Value);
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
    [ValourRoute(HttpVerbs.Post, "api/staff/delete")]
    public static async Task<IResult> DeleteUserAsync(UserService userService, StaffService staffService, [FromBody] DeleteUserRequest request)
    {
        var requestor = await userService.GetCurrentUserAsync();
        
        // For now, only Spike can do this
        if (requestor.Id != 12200448886571008)
        {
            return ValourResult.Forbid("SuperAdmins only.");
        }
        
        var result = await staffService.DeleteUserAsync(request.UserId);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);

        return ValourResult.Ok();
    }

    [StaffRequired]
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
