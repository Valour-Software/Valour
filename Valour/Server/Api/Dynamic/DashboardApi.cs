using Microsoft.AspNetCore.Mvc;
using Valour.Shared.Authorization;

namespace Valour.Server.Api.Dynamic;

public class DashboardApi
{
    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Get, "api/staff/dashboard/snapshot")]
    public static async Task<IResult> GetSnapshotAsync(DashboardService dashboardService)
    {
        var snapshot = await dashboardService.BuildSnapshotAsync();
        return Results.Json(snapshot);
    }

    [StaffRequired]
    [UserRequired(UserPermissionsEnum.FullControl)]
    [ValourRoute(HttpVerbs.Get, "api/staff/dashboard/analytics")]
    public static async Task<IResult> GetAnalyticsAsync(
        DashboardService dashboardService,
        [FromQuery] int days = 30)
    {
        if (days < 7)
            days = 7;
        if (days > 365)
            days = 365;

        var analytics = await dashboardService.BuildAnalyticsAsync(days);
        return Results.Json(analytics);
    }
}
