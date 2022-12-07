using Valour.Api.Items.Authorization;
using Valour.Api.Items.Channels.Planets;

namespace Valour.Api.Requests;

public class CreatePlanetVoiceChannelRequest
{
    public PlanetVoiceChannel Channel { get; set; }
    public List<PermissionsNode> Nodes { get; set; }
}


