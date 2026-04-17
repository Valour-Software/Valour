namespace Valour.Shared.Nodes;

public class SavedCommunityNode
{
    public long Id { get; set; }
    public string NodeId { get; set; }
    public string Name { get; set; }
    public string CanonicalOrigin { get; set; }
    public string AuthorityOrigin { get; set; }
    public NodeMode Mode { get; set; } = NodeMode.Community;
    public DateTime TimeAdded { get; set; }
}
