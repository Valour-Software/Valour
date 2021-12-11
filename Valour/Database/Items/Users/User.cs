using System.ComponentModel.DataAnnotations.Schema;
using Valour.Database.Items.Planets.Members;

namespace Valour.Database.Items.Users;

public class User : Valour.Shared.Items.Users.User<User>
{
    [InverseProperty("User")]
    public virtual UserEmail Email { get; set; }

    [InverseProperty("User")]
    public virtual ICollection<PlanetMember> Membership { get; set; }
}

