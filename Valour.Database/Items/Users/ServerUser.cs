using System.ComponentModel.DataAnnotations.Schema;
using Valour.Database.Items.Planets;
using Valour.Shared.Users;

namespace Valour.Database.Items.Users;

public class ServerUser : User<ServerUser>
{
    [InverseProperty("User")]
    public virtual UserEmail Email { get; set; }

    [InverseProperty("User")]
    public virtual ICollection<ServerPlanetMember> Membership { get; set; }
}

