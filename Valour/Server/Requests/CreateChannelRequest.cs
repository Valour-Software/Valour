namespace Valour.Server.Requests;

public class CreateChannelRequest
{
    public Channel Channel { get; set; }
    public List<PermissionsNode> Nodes { get; set; }
    
    /// <summary>
    /// This is the id of the target for the channel placement.
    /// If it's null, it will be placed at the top of parent's children.
    /// If it's a valid channel id, it will be placed after the channel.
    /// </summary>
    public long? TargetId { get; set; }
}