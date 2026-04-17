namespace Valour.Shared.Nodes;

public class NodeManifest
{
    public string NodeId { get; set; }
    public string Name { get; set; }
    public string CanonicalOrigin { get; set; }
    public string AuthorityOrigin { get; set; }
    public string Version { get; set; }
    public NodeMode Mode { get; set; }
    public IEnumerable<long> PlanetIds { get; set; } = [];
}
