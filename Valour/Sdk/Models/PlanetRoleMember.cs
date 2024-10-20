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

    protected override void OnUpdated(ModelUpdateEvent<PlanetRoleMember> eventData)
    {
        // PlanetRoleMember is only updated if it's created, which means this is a new role member
        
        // Get the member
        if (!PlanetMember.Cache.TryGet(MemberId, out var member))
            return;
        
        // Get the role
        if (!PlanetRole.Cache.TryGet(RoleId, out var role))
            return;

        // Add the role to the member
        member.OnRoleAdded(role);
    }

    protected override void OnDeleted()
    {
        // Get the member
        if (!PlanetMember.Cache.TryGet(MemberId, out var member))
            return;
        
        // Get the role
        if (!PlanetRole.Cache.TryGet(RoleId, out var role))
            return;
        
        // Remove the role from the member
        member.OnRoleRemoved(role);
    }
}

