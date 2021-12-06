
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Planets;

namespace Valour.Database.Items.Planets;
public class ServerPlanetInvite : PlanetInvite<ServerPlanetInvite>
{
    [ForeignKey("Planet_Id")]
    public virtual ServerPlanet Planet { get; set; }
}
