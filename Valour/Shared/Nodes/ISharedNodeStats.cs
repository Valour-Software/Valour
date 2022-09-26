namespace Valour.Shared.Nodes;

public interface ISharedNodeStats
{
    int ConnectionCount { get; set; }
    int ConnectionGroupCount { get; set; }
    int PlanetCount { get; set; }
    int ActiveMemberCount { get; set; }
}
