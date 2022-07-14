using Valour.Shared.Items.Planets.Members;

namespace Valour.Api.Items.Planets.Members;

public class PlanetRoleMember : Item, ISharedPlanetRoleMember
{
    public long UserId { get; set; }
    public long RoleId { get; set; }
    public long PlanetId { get; set; }
    public long MemberId { get; set; }
}

