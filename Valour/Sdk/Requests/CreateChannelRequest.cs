namespace Valour.Sdk.Requests;

public class CreateChannelRequest
{
    public Channel Channel { get; set; }
    public List<PermissionsNode> Nodes { get; set; }
}