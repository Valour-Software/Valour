namespace Valour.Server.Models;

/// <summary>
/// For getting data from the server.  Must match!
/// </summary>
public class PlanetMemberInfo
{
    public int TotalCount { get; set; }
    public List<PlanetMemberData> Members { get; set; }
}

public class PlanetMemberData
{
    public PlanetMember Member { get; set; }
    public List<long> RoleIds { get; set; }
}

