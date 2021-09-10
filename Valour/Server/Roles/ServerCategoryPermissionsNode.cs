
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Valour.Server.Categories;
using Valour.Server.Planets;
using Valour.Shared.Roles;

namespace Valour.Server.Roles;
public class ServerCategoryPermissionsNode : CategoryPermissionsNode
{
    [ForeignKey("Category_Id")]
    [JsonIgnore]
    public virtual ServerPlanetCategory Category { get; set; }

    [ForeignKey("Planet_Id")]
    [JsonIgnore]
    public virtual ServerPlanet Planet { get; set; }

    [ForeignKey("Role_Id")]
    [JsonIgnore]
    public virtual ServerPlanetRole Role { get; set; }
}
