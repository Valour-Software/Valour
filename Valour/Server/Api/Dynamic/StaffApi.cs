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
    [ValourRoute(HttpVerbs.Put, "api/staff/reports/{reportId}/reviewed/{value}")]
    public static async Task<IResult> SetReportReviewedAsync(StaffService staffService, string reportId, bool value)
    {
        var result = await staffService.SetReportReviewedAsync(reportId, value);
        if (!result.Success)
            return ValourResult.BadRequest(result.Message);
        
        return ValourResult.Ok();
    }
    
}