namespace Valour.Shared.Models;

public interface ISharedPlanetReport : ISharedPlanetModel<long>
{
    public static string GetBaseRoute(long planetId) => $"api/planets/{planetId}/reports";
    public static string GetIdRoute(long planetId, long id) => $"{GetBaseRoute(planetId)}/{id}";
    public static string GetResolveRoute(long planetId, long id) => $"{GetIdRoute(planetId, id)}/resolve";
    public static string GetKickRoute(long planetId, long id) => $"{GetIdRoute(planetId, id)}/kick";
    public static string GetBanRoute(long planetId, long id) => $"{GetIdRoute(planetId, id)}/ban";

    public const int MaxReasonLength = 4000;
    public const int MaxRuleTitleSnapshotLength = ISharedPlanetRule.MaxTitleLength;
    public const int MaxRuleDescriptionSnapshotLength = ISharedPlanetRule.MaxDescriptionLength;
    public const int MaxModeratorNotesLength = 2000;

    DateTime TimeCreated { get; set; }
    long ReportingUserId { get; set; }
    long? ReportedUserId { get; set; }
    long? ReportedMemberId { get; set; }
    long? MessageId { get; set; }
    long? ChannelId { get; set; }
    long? RuleId { get; set; }
    string RuleTitleSnapshot { get; set; }
    string RuleDescriptionSnapshot { get; set; }
    string LongReason { get; set; }
    bool Reviewed { get; set; }
    ReportResolution Resolution { get; set; }
    long? ResolvedById { get; set; }
    DateTime? ResolvedAt { get; set; }
    string ModeratorNotes { get; set; }
}

public class ResolvePlanetReportRequest
{
    public ReportResolution Resolution { get; set; }
    public string Notes { get; set; }
}

public class PlanetReportActionRequest
{
    public string Reason { get; set; }
    public string Notes { get; set; }
    public DateTime? TimeExpires { get; set; }
}
