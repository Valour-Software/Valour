using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class ReportService
{
    private readonly ValourDb _db;
    
    public ReportService(ValourDb db)
    {
        _db = db;
    }
    
    public async ValueTask<TaskResult> CreateAsync(Report report)
    {
        var dbReport = report.ToDatabase();
        dbReport.Reviewed = false;
        dbReport.TimeCreated = DateTime.UtcNow;
        dbReport.Id = Guid.NewGuid().ToString();
        dbReport.Resolution = ReportResolution.None;

        // Auto-populate ReportedUserId from message author if applicable
        if (dbReport.MessageId.HasValue && !dbReport.ReportedUserId.HasValue)
        {
            var message = await _db.Messages.FindAsync(dbReport.MessageId.Value);
            if (message is not null)
            {
                dbReport.ReportedUserId = message.AuthorUserId;
            }
        }

        try
        {
            await _db.Reports.AddAsync(dbReport);
            await _db.SaveChangesAsync();
        }
        catch (Exception)
        {
            return new TaskResult(false, "Failed to create report. Try again?");
        }

        return TaskResult.SuccessResult;
    }
    
    public async Task<List<Report>> GetUnreviewedReportsAsync()
    {
        return await _db.Reports.Where(r => !r.Reviewed)
            .Select(x => x.ToModel()).ToListAsync();
    }
}