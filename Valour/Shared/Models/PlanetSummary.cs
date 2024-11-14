namespace Valour.Shared.Models;

/// <summary>
/// PlanetSummaries are used to give the client a summary of a planet that has likely
/// not yet been joined.
/// </summary>
public class PlanetSummary
{
    public long PlanetId { get; set; }
    public string Name { get; set; }
    public string Description { get; set; }
    public bool HasCustomIcon { get; set; }
    public bool HasAnimatedIcon { get; set; }
    public int MemberCount { get; set; }
}