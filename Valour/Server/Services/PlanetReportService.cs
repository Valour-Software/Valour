using Valour.Server.Database;
using Valour.Shared;
using Valour.Shared.Models;
using Valour.Shared.Models.Staff;
using Valour.Shared.Queries;

namespace Valour.Server.Services;

public class PlanetReportService
{
    private readonly ValourDb _db;
    private readonly CoreHubService _coreHub;
    private readonly PlanetMemberService _memberService;
    private readonly PlanetBanService _banService;
    private readonly ModerationAuditService _moderationAuditService;
    private readonly ILogger<PlanetReportService> _logger;

    public PlanetReportService(
        ValourDb db,
        CoreHubService coreHub,
        PlanetMemberService memberService,
        PlanetBanService banService,
        ModerationAuditService moderationAuditService,
        ILogger<PlanetReportService> logger)
    {
        _db = db;
        _coreHub = coreHub;
        _memberService = memberService;
        _banService = banService;
        _moderationAuditService = moderationAuditService;
        _logger = logger;
    }

    public async Task<PlanetReport> GetAsync(long planetId, long reportId)
    {
        var report = await _db.PlanetReports
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.Id == reportId);

        return report.ToModel();
    }

    public async Task<TaskResult<PlanetReport>> CreateAsync(PlanetReport report)
    {
        var validation = ValidateCreate(report);
        if (!validation.Success)
            return TaskResult<PlanetReport>.FromFailure(validation.Message);

        var rule = await _db.PlanetRules
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlanetId == report.PlanetId && x.Id == report.RuleId);

        if (rule is null)
            return TaskResult<PlanetReport>.FromFailure("Rule not found.");

        report.Id = IdManager.Generate();
        report.TimeCreated = DateTime.UtcNow;
        report.Reviewed = false;
        report.Resolution = ReportResolution.None;
        report.ResolvedById = null;
        report.ResolvedAt = null;
        report.ModeratorNotes = string.Empty;
        report.RuleTitleSnapshot = rule.Title;
        report.RuleDescriptionSnapshot = rule.Description;

        if (report.MessageId.HasValue)
            await PopulateFromMessageAsync(report);

        if (!report.ReportedMemberId.HasValue && report.ReportedUserId.HasValue)
        {
            var member = await _db.PlanetMembers
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.PlanetId == report.PlanetId && x.UserId == report.ReportedUserId.Value);

            report.ReportedMemberId = member?.Id;
        }

        try
        {
            await _db.PlanetReports.AddAsync(report.ToDatabase());
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create planet report for planet {PlanetId}", report.PlanetId);
            return TaskResult<PlanetReport>.FromFailure("Failed to create report. Try again?");
        }

        _coreHub.NotifyPlanetItemChange(report);

        return TaskResult<PlanetReport>.FromData(report);
    }

    public async Task<QueryResponse<PlanetReport>> QueryPlanetReportsAsync(long planetId, QueryRequest request)
    {
        var take = request.Take;
        if (take > 50)
            take = 50;

        var skip = request.Skip;

        var query = _db.PlanetReports
            .AsNoTracking()
            .Where(x => x.PlanetId == planetId);

        var filters = request.Options?.Filters;

        if (filters?.GetValueOrDefault("unresolved") == "true")
            query = query.Where(x => x.Resolution == ReportResolution.None);

        if (filters?.GetValueOrDefault("resolved") == "true")
            query = query.Where(x => x.Resolution != ReportResolution.None);

        if (filters?.TryGetValue("rule", out var ruleFilter) == true &&
            long.TryParse(ruleFilter, out var ruleId))
        {
            query = query.Where(x => x.RuleId == ruleId);
        }

        if (filters?.TryGetValue("search", out var search) == true &&
            !string.IsNullOrWhiteSpace(search))
        {
            var lowered = search.ToLowerInvariant();
            query = query.Where(x =>
                EF.Functions.ILike(x.RuleTitleSnapshot.ToLower(), $"%{lowered}%") ||
                EF.Functions.ILike(x.LongReason.ToLower(), $"%{lowered}%"));
        }

        var sortField = request.Options?.Sort?.Field;
        var sortDesc = request.Options?.Sort?.Descending ?? true;
        if (string.IsNullOrWhiteSpace(sortField))
            sortDesc = true;

        query = sortField switch
        {
            "rule" => sortDesc
                ? query.OrderByDescending(x => x.RuleTitleSnapshot)
                : query.OrderBy(x => x.RuleTitleSnapshot),
            "status" => sortDesc
                ? query.OrderByDescending(x => x.Resolution)
                : query.OrderBy(x => x.Resolution),
            _ => sortDesc
                ? query.OrderByDescending(x => x.TimeCreated)
                : query.OrderBy(x => x.TimeCreated)
        };

        var total = await query.CountAsync();
        var items = await query
            .Skip(skip)
            .Take(take)
            .Select(x => x.ToModel())
            .ToListAsync();

        return new QueryResponse<PlanetReport>
        {
            Items = items,
            TotalCount = total
        };
    }

    public async Task<TaskResult<PlanetReport>> ResolveAsync(
        long planetId,
        long reportId,
        PlanetMember actor,
        ReportResolution resolution,
        string notes)
    {
        if (resolution == ReportResolution.None)
            return TaskResult<PlanetReport>.FromFailure("Choose a resolution.");

        notes = NormalizeNotes(notes);

        var dbReport = await _db.PlanetReports
            .FirstOrDefaultAsync(x => x.PlanetId == planetId && x.Id == reportId);

        if (dbReport is null)
            return TaskResult<PlanetReport>.FromFailure("Report not found.");

        if (dbReport.Resolution != ReportResolution.None)
            return TaskResult<PlanetReport>.FromFailure("Report is already resolved.");

        dbReport.Resolution = resolution;
        dbReport.Reviewed = true;
        dbReport.ResolvedById = actor.UserId;
        dbReport.ResolvedAt = DateTime.UtcNow;
        dbReport.ModeratorNotes = notes;

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to resolve planet report {ReportId} on planet {PlanetId}", reportId, planetId);
            return TaskResult<PlanetReport>.FromFailure("Failed to resolve report.");
        }

        var report = dbReport.ToModel();
        _coreHub.NotifyPlanetItemChange(report);

        await LogReportResolutionAsync(report, actor.UserId, resolution, notes);

        return TaskResult<PlanetReport>.FromData(report);
    }

    public async Task<TaskResult<PlanetReport>> KickReportedMemberAsync(
        long planetId,
        long reportId,
        PlanetMember actor,
        string notes)
    {
        var report = await GetAsync(planetId, reportId);
        if (report is null)
            return TaskResult<PlanetReport>.FromFailure("Report not found.");

        if (report.Resolution != ReportResolution.None)
            return TaskResult<PlanetReport>.FromFailure("Report is already resolved.");

        var target = await GetTargetMemberAsync(report);
        if (target is null)
            return TaskResult<PlanetReport>.FromFailure("Reported member is no longer in this planet.");

        if (target.UserId == actor.UserId)
            return TaskResult<PlanetReport>.FromFailure("You cannot kick yourself.");

        if (await _memberService.GetAuthorityAsync(actor) <= await _memberService.GetAuthorityAsync(target))
            return TaskResult<PlanetReport>.FromFailure("The target has equal or higher authority than you.");

        var kickResult = await _memberService.DeleteAsync(target.Id);
        if (!kickResult.Success)
            return TaskResult<PlanetReport>.FromFailure(kickResult.Message);

        await _moderationAuditService.LogAsync(
            report.PlanetId,
            ModerationActionSource.Manual,
            ModerationActionType.Kick,
            actorUserId: actor.UserId,
            targetUserId: target.UserId,
            targetMemberId: target.Id,
            messageId: report.MessageId,
            details: BuildActionDetails(report, notes));

        return await ResolveAsync(planetId, reportId, actor, ReportResolution.Kicked, notes);
    }

    public async Task<TaskResult<PlanetReport>> BanReportedUserAsync(
        long planetId,
        long reportId,
        PlanetMember actor,
        string reason,
        string notes,
        DateTime? timeExpires)
    {
        var report = await GetAsync(planetId, reportId);
        if (report is null)
            return TaskResult<PlanetReport>.FromFailure("Report not found.");

        if (report.Resolution != ReportResolution.None)
            return TaskResult<PlanetReport>.FromFailure("Report is already resolved.");

        var target = await GetTargetMemberAsync(report);
        if (target is null)
            return TaskResult<PlanetReport>.FromFailure("Reported member is no longer in this planet.");

        if (target.UserId == actor.UserId)
            return TaskResult<PlanetReport>.FromFailure("You cannot ban yourself.");

        if (await _memberService.GetAuthorityAsync(target) >= await _memberService.GetAuthorityAsync(actor))
            return TaskResult<PlanetReport>.FromFailure("The target has equal or higher authority than you.");

        reason = string.IsNullOrWhiteSpace(reason)
            ? BuildActionDetails(report, notes)
            : reason.Trim();

        var ban = new PlanetBan
        {
            PlanetId = planetId,
            TargetId = target.UserId,
            IssuerId = actor.UserId,
            Reason = reason,
            TimeCreated = DateTime.UtcNow,
            TimeExpires = timeExpires
        };

        var banResult = await _banService.CreateAsync(ban, actor);
        if (!banResult.Success)
            return TaskResult<PlanetReport>.FromFailure(banResult.Message);

        return await ResolveAsync(planetId, reportId, actor, ReportResolution.Banned, notes);
    }

    private async Task PopulateFromMessageAsync(PlanetReport report)
    {
        var message = await _db.Messages
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == report.MessageId.Value && x.PlanetId == report.PlanetId);

        if (message is null)
            return;

        report.ChannelId ??= message.ChannelId;
        report.ReportedUserId ??= message.AuthorUserId;

        var member = await _db.PlanetMembers
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.PlanetId == report.PlanetId && x.UserId == message.AuthorUserId);

        report.ReportedMemberId ??= member?.Id;
    }

    private async Task<PlanetMember> GetTargetMemberAsync(PlanetReport report)
    {
        if (report.ReportedMemberId.HasValue)
        {
            var member = await _memberService.GetAsync(report.ReportedMemberId.Value);
            if (member is not null && member.PlanetId == report.PlanetId)
                return member;
        }

        if (report.ReportedUserId.HasValue)
            return await _memberService.GetByUserAsync(report.ReportedUserId.Value, report.PlanetId);

        return null;
    }

    private async Task LogReportResolutionAsync(
        PlanetReport report,
        long actorUserId,
        ReportResolution resolution,
        string notes)
    {
        var actionType = resolution is ReportResolution.NoAction or ReportResolution.Duplicate
            ? ModerationActionType.DismissReport
            : ModerationActionType.ResolveReport;

        await _moderationAuditService.LogAsync(
            report.PlanetId,
            ModerationActionSource.Manual,
            actionType,
            actorUserId: actorUserId,
            targetUserId: report.ReportedUserId,
            targetMemberId: report.ReportedMemberId,
            messageId: report.MessageId,
            details: BuildResolutionDetails(report, resolution, notes));
    }

    private static string BuildResolutionDetails(PlanetReport report, ReportResolution resolution, string notes)
    {
        var details = $"Report {report.Id} resolved as {resolution}: {report.RuleTitleSnapshot}";
        if (!string.IsNullOrWhiteSpace(notes))
            details += $" | Notes: {notes}";

        return details;
    }

    private static string BuildActionDetails(PlanetReport report, string notes)
    {
        var details = $"Planet report {report.Id}: {report.RuleTitleSnapshot}";
        if (!string.IsNullOrWhiteSpace(notes))
            details += $" | Notes: {notes}";

        return details;
    }

    private static string NormalizeNotes(string notes)
    {
        notes = notes?.Trim() ?? string.Empty;
        if (notes.Length > ISharedPlanetReport.MaxModeratorNotesLength)
            notes = notes[..ISharedPlanetReport.MaxModeratorNotesLength];

        return notes;
    }

    private static TaskResult ValidateCreate(PlanetReport report)
    {
        if (report is null)
            return TaskResult.FromFailure("Report is required.");

        if (report.PlanetId == 0)
            return TaskResult.FromFailure("Planet is required.");

        if (!report.RuleId.HasValue)
            return TaskResult.FromFailure("Select a planet rule.");

        report.LongReason = report.LongReason?.Trim() ?? string.Empty;

        if (report.LongReason.Length > ISharedPlanetReport.MaxReasonLength)
            return TaskResult.FromFailure($"Report details must be {ISharedPlanetReport.MaxReasonLength} characters or less.");

        return TaskResult.SuccessResult;
    }
}
