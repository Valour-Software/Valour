using Valour.Shared;

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