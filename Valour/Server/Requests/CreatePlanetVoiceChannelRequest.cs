namespace Valour.Server.Requests;

public class CreatePlanetVoiceChannelRequest
{
    public PlanetVoiceChannel Channel { get; set; }
    public List<PermissionsNode> Nodes { get; set; }
}