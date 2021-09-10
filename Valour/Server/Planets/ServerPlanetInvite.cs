
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Planets;

namespace Valour.Server.Planets;
public class ServerPlanetInvite : PlanetInvite
{
    [ForeignKey("Planet_Id")]
    public virtual ServerPlanet Planet { get; set; }
}
