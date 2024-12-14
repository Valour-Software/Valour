using Valour.Sdk.ModelLogic;
using Valour.Shared.Models;

namespace Valour.Sdk.Models;

public class PlanetRoleMember : ClientPlanetModel<PlanetRoleMember, long>, ISharedPlanetRoleMember
{
    public long UserId { get; set; }
    public long RoleId { get; set; }
    public long PlanetId { get; set; }
    public long MemberId { get; set; }
    
    protected override long? GetPlanetId() => PlanetId;

    protected override void OnUpdated(ModelUpdateEvent<PlanetRoleMember> eventData)
    {
        // PlanetRoleMember is only updated if it's created, which means this is a new role member
        Planet.OnMemberRoleAdded(this);
    }

    protected override void OnDeleted()
    {
        Planet.OnMemberRoleRemoved(this);
    }
    
    public override PlanetRoleMember AddToCacheOrReturnExisting()
    {
        // not cached
        return this;
    }

    public override PlanetRoleMember TakeAndRemoveFromCache()
    {
        // not cached
        return this;
    }
}

