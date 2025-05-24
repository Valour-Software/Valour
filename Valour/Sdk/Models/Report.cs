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

    public override Report AddToCache(ModelInsertFlags flags = ModelInsertFlags.None)
    {
        return this;
    }
    
    public override Report RemoveFromCache(bool skipEvents = false)
    {
        return this;
    }
}