
using System.ComponentModel.DataAnnotations.Schema;
using Valour.Shared.Planets;

namespace Valour.Database.Items.Planets;
public class Invite : Valour.Shared.Items.Planets.Invite<Invite>
{
    [ForeignKey("Planet_Id")]
    public virtual Planet Planet { get; set; }
}
