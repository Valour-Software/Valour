using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Database.Items.Planets.Members;
using Valour.Shared.Items;
using Valour.Shared.Items.Users;

namespace Valour.Database.Items.Users;

public class User : UserBase
{
    [InverseProperty("User")]
    [JsonIgnore]
    public virtual UserEmail Email { get; set; }

    [InverseProperty("User")]
    [JsonIgnore]
    public virtual ICollection<PlanetMember> Membership { get; set; }
}

