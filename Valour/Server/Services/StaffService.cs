using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;

namespace Valour.Server.Services;

public class StaffService
{
    private readonly ValourDB _db;
    private readonly UserService _userService;
    
    public StaffService(ValourDB db, UserService userService)
    {
        _db = db;
        _userService = userService;
    }

    
    public async Task<List<Report>> GetReportsAsync() =>
        await _db.Reports.Select(x => x.ToModel()).ToListAsync();
    
    public async Task<PagedResponse<Report>> QueryReportsAsync(ReportQueryModel model, int amount, int page)
    {
        var query = _db.Reports.AsQueryable();

        if (model.Filter is not null)
        {
            if (model.Filter.Reason.HasValue)
                query = query.Where(x => x.ReasonCode == model.Filter.Reason);
        }

        if (model.Sort is not null)
        {
            // TODO: Sorting
        }
        else
        {
            // Sort by time created
            query = query.OrderByDescending(x => x.TimeCreated);
        }
        
        var reports = await query.Skip(amount * page).Take(amount).Select(x => x.ToModel()).ToListAsync();
        var total = await query.CountAsync();
        
        return new PagedResponse<Report>()
        {
            Items = reports,
            TotalCount = total
        };
    }

    public async Task<TaskResult> SetReportReviewedAsync(string reportId, bool value)
    {
        var report = await _db.Reports.FindAsync(reportId);
        if (report is null)
            return TaskResult.FromError("Report not found");
        
        report.Reviewed = value;
        
        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }
    
    public async Task<TaskResult> DisableUserAsync(long userId, bool value)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult.FromError("Account not found", errorCode: 404);
        
        user.Disabled = value;
        
        await _db.SaveChangesAsync();
        
        // Also remove all tokens
        var tokens = _db.AuthTokens.Where(x => x.UserId == userId);
        _db.AuthTokens.RemoveRange(tokens);
        
        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }

    public async Task<TaskResult> DeleteUserAsync(long userId)
    {
        var user = await _db.Users.FindAsync(userId);

        try
        {
            var result = await _userService.HardDelete(user.ToModel());
            if (!result.Success)
                return TaskResult.FromError(result.Message);
        }
        catch (Exception e)
        {
            return TaskResult.FromError(e.Message);
        }
        
        return TaskResult.SuccessResult;
    }
}