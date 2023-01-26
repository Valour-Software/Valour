using Valour.Api.Models;

namespace Valour.Api.Requests;

public class CreatePlanetVoiceChannelRequest
{
    public PlanetVoiceChannel Channel { get; set; }
    public List<PermissionsNode> Nodes { get; set; }
}


