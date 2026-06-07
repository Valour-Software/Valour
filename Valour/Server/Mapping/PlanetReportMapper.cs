namespace Valour.Server.Mapping;

public static class PlanetReportMapper
{
    public static PlanetReport ToModel(this Valour.Database.PlanetReport report)
    {
        if (report is null)
            return null;

        return new PlanetReport
        {
            Id = report.Id,
            PlanetId = report.PlanetId,
            TimeCreated = report.TimeCreated,
            ReportingUserId = report.ReportingUserId,
            ReportedUserId = report.ReportedUserId,
            ReportedMemberId = report.ReportedMemberId,
            MessageId = report.MessageId,
            ChannelId = report.ChannelId,
            RuleId = report.RuleId,
            RuleTitleSnapshot = report.RuleTitleSnapshot,
            RuleDescriptionSnapshot = report.RuleDescriptionSnapshot,
            LongReason = report.LongReason,
            Reviewed = report.Reviewed,
            Resolution = report.Resolution,
            ResolvedById = report.ResolvedById,
            ResolvedAt = report.ResolvedAt,
            ModeratorNotes = report.ModeratorNotes
        };
    }

    public static Valour.Database.PlanetReport ToDatabase(this PlanetReport report)
    {
        if (report is null)
            return null;

        return new Valour.Database.PlanetReport
        {
            Id = report.Id,
            PlanetId = report.PlanetId,
            TimeCreated = report.TimeCreated,
            ReportingUserId = report.ReportingUserId,
            ReportedUserId = report.ReportedUserId,
            ReportedMemberId = report.ReportedMemberId,
            MessageId = report.MessageId,
            ChannelId = report.ChannelId,
            RuleId = report.RuleId,
            RuleTitleSnapshot = report.RuleTitleSnapshot,
            RuleDescriptionSnapshot = report.RuleDescriptionSnapshot,
            LongReason = report.LongReason,
            Reviewed = report.Reviewed,
            Resolution = report.Resolution,
            ResolvedById = report.ResolvedById,
            ResolvedAt = report.ResolvedAt,
            ModeratorNotes = report.ModeratorNotes
        };
    }
}
