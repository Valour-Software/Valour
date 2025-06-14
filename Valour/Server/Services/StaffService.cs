using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Queries;

namespace Valour.Server.Services;

public class StaffService
{
    private readonly ValourDb _db;
    private readonly UserService _userService;
    
    public StaffService(ValourDb db, UserService userService)
    {
        _db = db;
        _userService = userService;
    }

    
    public async Task<List<Report>> GetReportsAsync() =>
        await _db.Reports.Select(x => x.ToModel()).ToListAsync();
    
    public async Task<QueryResponse<Report>> QueryReportsAsync(QueryRequest queryRequest)
    {
        var take = queryRequest.Take;
        if (take > 100)
            take = 100;
        
        var skip = queryRequest.Skip;
        
        var query = _db.Reports
            .AsQueryable()
            .AsNoTracking();

        var reason = queryRequest.Options?.Filters?.GetValueOrDefault("reason");
        if (int.TryParse(reason, out int reasonCode))
        {
            query = query.Where(x => (int)x.ReasonCode == reasonCode);
        }
        
        var sort = queryRequest.Options?.Sort?.Field;
        if (sort is not null)
        {
            // TODO: Sorting
        }
        else
        {
            // Sort by time created
            query = query.OrderByDescending(x => x.TimeCreated);
        }
        
        var reports = await query
            .Skip(skip)
            .Take(take)
            .Select(x => x.ToModel())
            .ToListAsync();
        
        var total = await query.CountAsync();
        
        return new QueryResponse<Report>()
        {
            Items = reports,
            TotalCount = total
        };
    }

    public async Task<TaskResult> SetReportReviewedAsync(string reportId, bool value)
    {
        var report = await _db.Reports.FindAsync(reportId);
        if (report is null)
            return TaskResult.FromFailure("Report not found");
        
        report.Reviewed = value;
        
        await _db.SaveChangesAsync();

        return TaskResult.SuccessResult;
    }
    
    public async Task<TaskResult> DisableUserAsync(long userId, bool value)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user is null)
            return TaskResult.FromFailure("Account not found", errorCode: 404);
        
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
                return TaskResult.FromFailure(result.Message);
        }
        catch (Exception e)
        {
            return TaskResult.FromFailure(e.Message);
        }
        
        return TaskResult.SuccessResult;
    }

    public async Task<Message> GetMessageAsync(long messageId)
    {
        var msg = await _db.Messages.FirstOrDefaultAsync(x => x.Id == messageId);
        return msg.ToModel();
    }
}