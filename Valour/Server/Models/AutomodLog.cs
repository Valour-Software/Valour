using Valour.Shared.Models.Staff;

namespace Valour.Server.Models;

public class AutomodLog : ServerModel<Guid>, ISharedAutomodLog
{
    public long PlanetId { get; set; }
    public Guid TriggerId { get; set; }
    public long MemberId { get; set; }
    public long? MessageId { get; set; }
    public DateTime TimeTriggered { get; set; }
}

