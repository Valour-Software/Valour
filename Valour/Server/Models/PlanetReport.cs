using Valour.Shared.Models;

namespace Valour.Server.Models;

public class PlanetReport : ServerModel<long>, ISharedPlanetReport
{
    public long PlanetId { get; set; }
    public DateTime TimeCreated { get; set; }
    public long ReportingUserId { get; set; }
    public long? ReportedUserId { get; set; }
    public long? ReportedMemberId { get; set; }
    public long? MessageId { get; set; }
    public long? ChannelId { get; set; }
    public long? RuleId { get; set; }
    public string RuleTitleSnapshot { get; set; }
    public string RuleDescriptionSnapshot { get; set; }
    public string LongReason { get; set; }
    public bool Reviewed { get; set; }
    public ReportResolution Resolution { get; set; }
    public long? ResolvedById { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string ModeratorNotes { get; set; }
}
