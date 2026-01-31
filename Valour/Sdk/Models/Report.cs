using Valour.Sdk.Client;
using Valour.Sdk.ModelLogic;
using Valour.Shared;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class Report : ClientModel<Report, string>, ISharedReport
{
    /// <summary>
    /// Guid Id of the report
    /// </summary>
    public string Id { get; set; }
    
    /// <summary>
    /// The time the report was created
    /// </summary>
    public DateTime TimeCreated { get; set; }
    
    /// <summary>
    /// The user who sent the report
    /// </summary>
    public long ReportingUserId { get; set; }
    
    /// <summary>
    /// The message id (if any) the report applies to
    /// </summary>
    public long? MessageId { get; set; }
    
    /// <summary>
    /// The channel id (if any) the report applies to
    /// </summary>
    public long? ChannelId { get; set; }
    
    /// <summary>
    /// The planet id (if any) the report applies to
    /// </summary>ß
    public long? PlanetId { get; set; }
    
    /// <summary>
    /// The category-code of the reason of the report
    /// </summary>
    public ReportReasonCode ReasonCode { get; set; }
    
    /// <summary>
    /// The user-written reason for the report
    /// </summary>
    public string LongReason { get; set; }
    
    /// <summary>
    /// If the report has been reviewed by a moderator
    /// </summary>
    public bool Reviewed { get; set; }

    /// <summary>
    /// The user who was reported (if applicable)
    /// </summary>
    public long? ReportedUserId { get; set; }

    /// <summary>
    /// The resolution status of the report
    /// </summary>
    public ReportResolution Resolution { get; set; }

    /// <summary>
    /// The staff member who resolved the report
    /// </summary>
    public long? ResolvedById { get; set; }

    /// <summary>
    /// When the report was resolved
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

    /// <summary>
    /// Internal staff notes about the report
    /// </summary>
    public string StaffNotes { get; set; }

    /// <summary>
    /// Whether the report has been resolved
    /// </summary>
    public bool IsResolved => Resolution != ReportResolution.None;

    /// <summary>
    /// Gets the display name for the resolution
    /// </summary>
    public string ResolutionDisplayName => Resolution switch
    {
        ReportResolution.None => "Unresolved",
        ReportResolution.NoAction => "No Action Needed",
        ReportResolution.Warning => "Warning Issued",
        ReportResolution.UserDisabled => "User Disabled",
        ReportResolution.ContentRemoved => "Content Removed",
        ReportResolution.UserDeleted => "User Deleted",
        ReportResolution.Duplicate => "Duplicate",
        _ => "Unknown"
    };

    public override Report AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }

    public override Report RemoveFromCache(bool skipEvents = false)
    {
        return this;
    }
}