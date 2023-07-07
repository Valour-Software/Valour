using System.Web.Http;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class ReportApi
{
    [ValourRoute(HttpVerbs.Get, "api/reports/unreviewed")]
    [UserRequired(UserPermissionsEnum.FullControl)] // Only allow full access
    public static async Task<IResult> GetUnreviewedReportsAsync(UserService userService, ReportService reportService)
    {
        // ONLY ALLOW STAFF TO GET REPORTS
        var user = await userService.GetCurrentUserAsync();
        if (!user.ValourStaff)
            return ValourResult.Forbid("You are not a staff member");
        
        return Results.Json(await reportService.GetUnreviewedReportsAsync());
    }
    
    [ValourRoute(HttpVerbs.Post, "api/reports")]
    [UserRequired(UserPermissionsEnum.FullControl)] // Only allow full access
    public static async Task<IResult> CreateReportAsync(UserService userService, ReportService reportService, [FromBody] Report report)
    {
        var user = await userService.GetCurrentUserAsync();
        if (user.Bot) // Bots cannot send reports
            return ValourResult.Forbid("Bots cannot send reports");

        report.ReportingUserId = user.Id;
        
        var result = await reportService.CreateAsync(report);
        if (!result.Success)
            return ValourResult.Problem(result.Message);
        
        return Results.Ok("Created report successfully.");
    }
}