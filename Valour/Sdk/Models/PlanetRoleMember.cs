using Valour.Api.Models;
using Valour.Shared.Models;

namespace Valour.Api.Models;

public class PlanetRoleMember : LiveModel, ISharedPlanetRoleMember
{
    public long UserId { get; set; }
    public long RoleId { get; set; }
    public long PlanetId { get; set; }
    public long MemberId { get; set; }
}

