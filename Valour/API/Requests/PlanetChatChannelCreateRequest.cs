using Valour.Api.Items.Authorization;
using Valour.Api.Items.Planets.Channels;

namespace Valour.Api.Requests;

public class PlanetChatChannelCreateRequest
{
    public PlanetChatChannel Channel { get; set; }
    public List<PermissionsNode> Nodes { get; set; }
}


