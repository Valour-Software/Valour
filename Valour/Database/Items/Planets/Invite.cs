
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Shared.Items;
using Valour.Shared.Items.Planets;

namespace Valour.Database.Items.Planets;
public class Invite : InviteBase
{
    [ForeignKey("Planet_Id")]
    public virtual Planet Planet { get; set; }
}
