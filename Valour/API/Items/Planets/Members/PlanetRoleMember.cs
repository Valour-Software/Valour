using Valour.Shared.Items.Planets.Members;

namespace Valour.Api.Items.Planets.Members;

public class PlanetRoleMember : Item, ISharedPlanetRoleMember
{
    public ulong UserId { get; set; }
    public ulong RoleId { get; set; }
    public ulong PlanetId { get; set; }
    public ulong MemberId { get; set; }
}

