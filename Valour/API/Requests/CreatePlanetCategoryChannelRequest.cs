using Valour.Api.Models;

namespace Valour.Api.Requests;

public class CreatePlanetCategoryChannelRequest
{
    public PlanetCategory Category { get; set; }
    public List<PermissionsNode> Nodes { get; set; }
}

