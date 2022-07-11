using Valour.Server.Database.Items.Authorization;
using Valour.Server.Database.Items.Planets.Channels;

namespace Valour.Server.Requests;

public class CreatePlanetChatChannelRequest
{
    public PlanetChatChannel Channel { get; set; }
    public List<PermissionsNode> Nodes { get; set; }
}

