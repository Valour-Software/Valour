namespace Valour.Server.Mapping;

public static class ReportMapper
{
    public static Report ToModel(this Valour.Database.Report report)
    {
        if (report is null)
            return null;

        return new Report()
        {
            Id = report.Id,
            TimeCreated = report.TimeCreated,
            ReportingUserId = report.ReportingUserId,
            MessageId = report.MessageId,
            ChannelId = report.ChannelId,
            PlanetId = report.PlanetId,
            ReasonCode = report.ReasonCode,
            LongReason = report.LongReason,
            Reviewed = report.Reviewed,
        };
    }
    
    public static Valour.Database.Report ToDatabase(this Report report)
    {
        if (report is null)
            return null;

        return new Valour.Database.Report()
        {
            Id = report.Id,
            TimeCreated = report.TimeCreated,
            ReportingUserId = report.ReportingUserId,
            MessageId = report.MessageId,
            ChannelId = report.ChannelId,
            PlanetId = report.PlanetId,
            ReasonCode = report.ReasonCode,
            LongReason = report.LongReason,
            Reviewed = report.Reviewed,
        };
    }
}