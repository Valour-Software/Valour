namespace Valour.Server.Models;

/// <summary>
/// For getting data from the server.  Must match!
/// </summary>
public class PlanetMemberInfo
{
    public int TotalCount { get; set; }
    public List<PlanetMember> Members { get; set; }
}
