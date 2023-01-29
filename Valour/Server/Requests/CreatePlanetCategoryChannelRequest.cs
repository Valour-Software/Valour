namespace Valour.Server.Requests;

public class CreatePlanetCategoryChannelRequest
{
    public PlanetCategory Category { get; set; }
    public List<PermissionsNode> Nodes { get; set; }
}
