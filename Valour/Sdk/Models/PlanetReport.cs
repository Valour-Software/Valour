using System.Text.Json.Serialization;
using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PlanetReport : ClientPlanetModel<PlanetReport, long>, ISharedPlanetReport
{
    public override string BaseRoute => ISharedPlanetReport.GetBaseRoute(PlanetId);
    public override string IdRoute => ISharedPlanetReport.GetIdRoute(PlanetId, Id);

    public long PlanetId { get; set; }
    public DateTime TimeCreated { get; set; }
    public long ReportingUserId { get; set; }
    public long? ReportedUserId { get; set; }
    public long? ReportedMemberId { get; set; }
    public long? MessageId { get; set; }
    public long? ChannelId { get; set; }
    public long? ThreadId { get; set; }
    public long? ThreadCommentId { get; set; }
    public long? RuleId { get; set; }
    public string RuleTitleSnapshot { get; set; }
    public string RuleDescriptionSnapshot { get; set; }
    public string LongReason { get; set; }
    public bool Reviewed { get; set; }
    public ReportResolution Resolution { get; set; }
    public long? ResolvedById { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public string ModeratorNotes { get; set; }

    public bool IsResolved => Resolution != ReportResolution.None;

    public string ResolutionDisplayName => Resolution switch
    {
        ReportResolution.None => "Unresolved",
        ReportResolution.NoAction => "No Action",
        ReportResolution.Warning => "Warning",
        ReportResolution.UserDisabled => "Disabled",
        ReportResolution.ContentRemoved => "Removed",
        ReportResolution.UserDeleted => "User Deleted",
        ReportResolution.Duplicate => "Duplicate",
        ReportResolution.Kicked => "Kicked",
        ReportResolution.Banned => "Banned",
        _ => "Unknown"
    };

    protected override long? GetPlanetId() => PlanetId;

    [JsonConstructor]
    private PlanetReport() : base()
    {
    }

    public PlanetReport(ValourClient client) : base(client)
    {
    }

    public override PlanetReport AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return Planet.Reports.Put(this, flags);
    }

    public override PlanetReport RemoveFromCache(bool skipEvents = false)
    {
        return Planet.Reports.Remove(this, skipEvents);
    }
}
