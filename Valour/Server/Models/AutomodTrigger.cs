using Valour.Shared.Models.Staff;
using Valour.Shared.Models;

namespace Valour.Server.Models;

public class AutomodTrigger : ServerModel<Guid>, ISharedAutomodTrigger, ISharedPlanetModel
{
    public long PlanetId { get; set; }
    public long MemberAddedBy { get; set; }
    public AutomodTriggerType Type { get; set; }
    public string Name { get; set; }
    public string? TriggerWords { get; set; }
}
