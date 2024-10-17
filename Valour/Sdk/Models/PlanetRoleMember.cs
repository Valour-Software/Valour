using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PlanetRoleMember : ClientPlanetModel<PlanetRoleMember, long>, ISharedPlanetRoleMember
{
    public long UserId { get; set; }
    public long RoleId { get; set; }
    public long PlanetId { get; set; }
    public long MemberId { get; set; }
    
    public override long? GetPlanetId() => PlanetId;
}

