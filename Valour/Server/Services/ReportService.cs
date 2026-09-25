using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Server.Services;

public class ReportService
{
    private readonly ValourDb _db;
    private readonly E2eeMessageService _e2eeMessages;
    
    public ReportService(ValourDb db, E2eeMessageService e2eeMessages)
    {
        _db = db;
        _e2eeMessages = e2eeMessages;
    }
    
    public async ValueTask<TaskResult> CreateAsync(Report report)
    {
        // Messages the reporter reveals are checked against the author's
        // signed commitment before staff see them.
        var evidence = await _e2eeMessages.VerifyEvidenceAsync(report.Evidence, report.ReportingUserId);
        if (!evidence.Success)
            return new TaskResult(false, evidence.Message);

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

        // A deleted message can still name its author through its proof.
        if (dbReport.MessageId.HasValue && !dbReport.ReportedUserId.HasValue)
        {
            dbReport.ReportedUserId = evidence.Data
                .FirstOrDefault(x => x.MessageId == dbReport.MessageId.Value)?.AuthorUserId;
        }

        try
        {
            await _db.Reports.AddAsync(dbReport);
            await _db.SaveChangesAsync();
            await _e2eeMessages.SaveEvidenceAsync(evidence.Data, dbReport.Id, null);
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